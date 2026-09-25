namespace DiploWalker.Abstractions

open System
open System.Diagnostics
open System.Threading.Tasks

/// ExÃ©cution de processus externes de maniÃ¨re sÃ»re : lectures asynchrones
/// concurrentes de stdout/stderr (pas de blocage quand un tampon de pipe est
/// plein), dÃ©lai d'attente couvrant la durÃ©e totale, et arrÃªt (Kill) en cas de
/// dÃ©passement.
[<RequireQualifiedAccess>]
module ProcessExec =

    let private defaultTimeoutMs = 60_000

    /// DÃ©lai d'attente pour les opÃ©rations de montage/dÃ©montage (30 secondes).
    [<Literal>]
    let MountTimeoutMs = 30_000

    let private timeoutMsOr (timeoutMs: int option) = defaultArg timeoutMs defaultTimeoutMs

    /// ExÃ©cute `fileName` avec `args` et renvoie `(code, stdout, stderr)`.
    /// `input` (Some texte) alimente l'entrÃ©e standard ; `timeoutMs` borne la
    /// durÃ©e totale (None = 60 s) et dÃ©clenche un `Kill` en cas de dÃ©passement ;
    /// `ct` annule Ã©galement l'exÃ©cution (arrÃªt du processus).
    ///
    /// L'attente utilise Process.WaitForExit(millisecondes) (attente synchrone
    /// sur le handle du processus) pendant que stdout/stderr sont purgÃ©s en
    /// arriÃ¨re-plan par ReadToEndAsync : aucune attente async-sur-sync ni blocage
    /// de pipe.
    let runWithResult
        (fileName: string)
        (args: seq<string>)
        (timeoutMs: int option)
        (input: string option)
        (ct: System.Threading.CancellationToken option)
        : int * string * string =
        let timeout = timeoutMsOr timeoutMs
        let psi = ProcessStartInfo()
        psi.FileName <- fileName
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- true

        match input with
        | Some _ -> psi.RedirectStandardInput <- true
        | None -> ()

        for arg in args do
            psi.ArgumentList.Add(arg) |> ignore

        use proc = Process.Start(psi)

        if isNull proc then
            failwithf "Impossible de dÃ©marrer le processus '%s'" fileName

        let stdoutRead = proc.StandardOutput.ReadToEndAsync()
        let stderrRead = proc.StandardError.ReadToEndAsync()

        match input with
        | Some text ->
            proc.StandardInput.Write(text)
            proc.StandardInput.Close()
        | None -> ()

        match ct with
        | Some token when token.CanBeCanceled ->
            // Attente dÃ©coupÃ©e pour rÃ©agir Ã  l'annulation, sans bloquer les
            // lectures pipes (purgÃ©es en arriÃ¨re-plan).
            let mutable exited = false

            while not exited && not token.IsCancellationRequested do
                exited <- proc.WaitForExit(250)

            if token.IsCancellationRequested then
                try
                    proc.Kill(true)
                with _ ->
                    ()
                raise (OperationCanceledException(sprintf "ExÃ©cution annulÃ©e pour '%s'" fileName, token))
        | _ ->
            if not (proc.WaitForExit(timeout)) then
                try
                    proc.Kill(true)
                with _ ->
                    ()

                try
                    Task.WaitAll([| stdoutRead :> Task; stderrRead :> Task |], 5_000) |> ignore
                with _ ->
                    ()

                raise (TimeoutException(sprintf "DÃ©lai d'attente dÃ©passÃ© pour '%s' (%dms)" fileName timeout))

        let stdout = stdoutRead.GetAwaiter().GetResult()
        let stderr = stderrRead.GetAwaiter().GetResult()
        (proc.ExitCode, stdout, stderr)

    /// ExÃ©cute `fileName` avec `args` et renvoie la sortie standard. LÃ¨ve si le
    /// dÃ©lai d'attente est dÃ©passÃ© ou si le processus Ã©choue (la sortie d'erreur
    /// est jointe au message). ParamÃ¨tres comme `runWithResult`.
    let run
        (fileName: string)
        (args: seq<string>)
        (timeoutMs: int option)
        (input: string option)
        (ct: System.Threading.CancellationToken option)
        : string =
        let code, stdout, stderr = runWithResult fileName args timeoutMs input ct

        if code <> 0 then
            let detail =
                if String.IsNullOrWhiteSpace stderr then
                    ""
                else
                    " " + stderr.Trim()

            raise (InvalidOperationException(sprintf "La commande '%s' a Ã©chouÃ© (code %d):%s" fileName code detail))

        stdout

    /// ExÃ©cute sans retourner la sortie (usage montage/dÃ©montage).
    let runUnit
        (fileName: string)
        (args: seq<string>)
        (timeoutMs: int option)
        (input: string option)
        (ct: System.Threading.CancellationToken option)
        : unit =
        run fileName args timeoutMs input ct |> ignore

    /// Encode un script PowerShell en Base64 UTF-16LE pour -EncodedCommand.
    /// Ã‰vite l'injection via la ligne de commande (les valeurs sont dans le script,
    /// pas dans argv).
    let private encodeScript (script: string) =
        let bytes = System.Text.Encoding.Unicode.GetBytes(script)
        Convert.ToBase64String(bytes)

    /// Retire le prÃ©fixe Â« - Â» Ã©ventuel d'un nom de paramÃ¨tre PowerShell.
    let private stripParamPrefix (name: string) =
        if name.Length > 0 && name.[0] = '-' then name.Substring(1) else name

    /// Allowlist stricte des noms de paramÃ¨tres interpolÃ©s dans le script
    /// PowerShell : `[A-Za-z_][A-Za-z0-9_]*` (avec prÃ©fixe Â« - Â» optionnel).
    /// Refuse tout nom qui s'Ã©carte de ce jeu pour empÃªcher l'injection.
    let private validateParameterNames (api: string) (parameters: (string * string) list) =
        for name, _ in parameters do
            let bare = stripParamPrefix name

            let ok =
                bare.Length > 0
                && (Char.IsAsciiLetter bare.[0] || bare.[0] = '_')
                && bare |> Seq.forall (fun c -> Char.IsAsciiLetterOrDigit c || c = '_')

            if not ok then
                invalidArg "parameters" (sprintf "%s : nom de paramÃ¨tre PowerShell invalide '%s'" api name)

    /// ExÃ©cute un script PowerShell encodÃ© avec les paramÃ¨tres nommÃ©s.
    /// Les VALEURS sont injectÃ©es dans le script lui-mÃªme en Base64 UTF-8 :
    /// `-EncodedCommand` n'offre aucun canal de liaison pour un bloc param(),
    /// et stdin n'est pas consommÃ© par le script â€” l'injection Base64 garantit
    /// le binding rÃ©el des variables $pN sans risque d'injection.
    /// PartagÃ© par runPowerShell et runPowerShellScript.
    /// Attention : API bloquante â€” ne jamais appeler depuis le thread UI.
    let private runEncodedScript
        (scriptBody: string)
        (parameters: (string * string) list)
        (timeoutMs: int option)
        (ct: System.Threading.CancellationToken option)
        : string =
        let timeout = timeoutMsOr timeoutMs

        let script =
            let assignments =
                parameters
                |> List.mapi (fun i (_, value) ->
                    let b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes value)

                    sprintf "$p%d = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('%s'))" i b64)
                |> String.concat "; "

            if String.IsNullOrEmpty assignments then
                scriptBody
            else
                sprintf "%s; %s" assignments scriptBody

        let encoded = encodeScript script

        let args =
            [ "-NoProfile"
              "-NonInteractive"
              "-EncodedCommand"
              encoded ]

        let code, stdout, stderr = runWithResult "powershell" args (Some timeout) None ct

        if code <> 0 then
            let detail =
                if String.IsNullOrWhiteSpace stderr then
                    ""
                else
                    " " + stderr.Trim()

            raise (InvalidOperationException(sprintf "PowerShell a Ã©chouÃ© (code %d):%s" code detail))

        stdout

    /// ExÃ©cute une commande PowerShell avec les paramÃ¨tres spÃ©cifiÃ©s.
    /// Utilise -EncodedCommand pour un binding sÃ»r des paramÃ¨tres (pas de concatÃ©nation dans argv).
    /// Les NOMS de paramÃ¨tres sont interpolÃ©s dans le script : ils sont donc
    /// jaugÃ©s par une allowlist stricte ([-,]?[A-Za-z_][A-Za-z0-9_]*) pour
    /// empÃªcher toute injection depuis un nom contrÃ´lÃ© par un appelant.
    let runPowerShell
        (command: string)
        (parameters: (string * string) list)
        (timeoutMs: int option)
        (ct: System.Threading.CancellationToken option)
        : string =
        validateParameterNames "runPowerShell" parameters

        let scriptBody =
            let paramNames = parameters |> List.mapi (fun i _ -> sprintf "$p%d" i)

            sprintf
                "%s -%s"
                command
                (parameters
                 |> List.mapi (fun i (name, _) -> sprintf "%s %s" (stripParamPrefix name) paramNames.[i])
                 |> String.concat " -")

        runEncodedScript scriptBody parameters timeoutMs ct

    /// ExÃ©cute un script PowerShell brut avec des paramÃ¨tres nommÃ©s.
    /// Utilise -EncodedCommand pour un binding sÃ»r des paramÃ¨tres.
    let runPowerShellScript
        (scriptBody: string)
        (parameters: (string * string) list)
        (timeoutMs: int option)
        (ct: System.Threading.CancellationToken option)
        : string =
        runEncodedScript scriptBody parameters timeoutMs ct

