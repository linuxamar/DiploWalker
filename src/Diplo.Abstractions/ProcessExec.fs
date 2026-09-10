namespace Diplo.Abstractions

open System
open System.Diagnostics
open System.Threading.Tasks

/// Exécution de processus externes de manière sûre : lectures asynchrones
/// concurrentes de stdout/stderr (pas de blocage quand un tampon de pipe est
/// plein), délai d'attente couvrant la durée totale, et arrêt (Kill) en cas de
/// dépassement.
[<RequireQualifiedAccess>]
module ProcessExec =

    let private defaultTimeoutMs = 60_000

    /// Délai d'attente pour les opérations de montage/démontage (30 secondes).
    [<Literal>]
    let MountTimeoutMs = 30_000

    let private timeoutMsOr (timeoutMs: int option) = defaultArg timeoutMs defaultTimeoutMs

    /// Exécute `fileName` avec `args` et renvoie `(code, stdout, stderr)`.
    /// `input` (Some texte) alimente l'entrée standard ; `timeoutMs` borne la
    /// durée totale (None = 60 s) et déclenche un `Kill` en cas de dépassement ;
    /// `ct` annule également l'exécution (arrêt du processus).
    ///
    /// L'attente utilise Process.WaitForExit(millisecondes) (attente synchrone
    /// sur le handle du processus) pendant que stdout/stderr sont purgés en
    /// arrière-plan par ReadToEndAsync : aucune attente async-sur-sync ni blocage
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
            failwithf "Impossible de démarrer le processus '%s'" fileName

        let stdoutRead = proc.StandardOutput.ReadToEndAsync()
        let stderrRead = proc.StandardError.ReadToEndAsync()

        match input with
        | Some text ->
            proc.StandardInput.Write(text)
            proc.StandardInput.Close()
        | None -> ()

        match ct with
        | Some token when token.CanBeCanceled ->
            // Attente découpée pour réagir à l'annulation, sans bloquer les
            // lectures pipes (purgées en arrière-plan).
            let mutable exited = false

            while not exited && not token.IsCancellationRequested do
                exited <- proc.WaitForExit(250)

            if token.IsCancellationRequested then
                try
                    proc.Kill(true)
                with _ ->
                    ()
                raise (OperationCanceledException(sprintf "Exécution annulée pour '%s'" fileName, token))
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

                raise (TimeoutException(sprintf "Délai d'attente dépassé pour '%s' (%dms)" fileName timeout))

        let stdout = stdoutRead.GetAwaiter().GetResult()
        let stderr = stderrRead.GetAwaiter().GetResult()
        (proc.ExitCode, stdout, stderr)

    /// Exécute `fileName` avec `args` et renvoie la sortie standard. Lève si le
    /// délai d'attente est dépassé ou si le processus échoue (la sortie d'erreur
    /// est jointe au message). Paramètres comme `runWithResult`.
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

            raise (InvalidOperationException(sprintf "La commande '%s' a échoué (code %d):%s" fileName code detail))

        stdout

    /// Exécute sans retourner la sortie (usage montage/démontage).
    let runUnit
        (fileName: string)
        (args: seq<string>)
        (timeoutMs: int option)
        (input: string option)
        (ct: System.Threading.CancellationToken option)
        : unit =
        run fileName args timeoutMs input ct |> ignore

    /// Encode un script PowerShell en Base64 UTF-16LE pour -EncodedCommand.
    /// Évite l'injection via la ligne de commande (les valeurs sont dans le script,
    /// pas dans argv).
    let private encodeScript (script: string) =
        let bytes = System.Text.Encoding.Unicode.GetBytes(script)
        Convert.ToBase64String(bytes)

    /// Retire le préfixe « - » éventuel d'un nom de paramètre PowerShell.
    let private stripParamPrefix (name: string) =
        if name.Length > 0 && name.[0] = '-' then name.Substring(1) else name

    /// Allowlist stricte des noms de paramètres interpolés dans le script
    /// PowerShell : `[A-Za-z_][A-Za-z0-9_]*` (avec préfixe « - » optionnel).
    /// Refuse tout nom qui s'écarte de ce jeu pour empêcher l'injection.
    let private validateParameterNames (api: string) (parameters: (string * string) list) =
        for name, _ in parameters do
            let bare = stripParamPrefix name

            let ok =
                bare.Length > 0
                && (Char.IsAsciiLetter bare.[0] || bare.[0] = '_')
                && bare |> Seq.forall (fun c -> Char.IsAsciiLetterOrDigit c || c = '_')

            if not ok then
                invalidArg "parameters" (sprintf "%s : nom de paramètre PowerShell invalide '%s'" api name)

    /// Exécute un script PowerShell encodé avec les paramètres nommés.
    /// Les VALEURS sont injectées dans le script lui-même en Base64 UTF-8 :
    /// `-EncodedCommand` n'offre aucun canal de liaison pour un bloc param(),
    /// et stdin n'est pas consommé par le script — l'injection Base64 garantit
    /// le binding réel des variables $pN sans risque d'injection.
    /// Partagé par runPowerShell et runPowerShellScript.
    /// Attention : API bloquante — ne jamais appeler depuis le thread UI.
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

            raise (InvalidOperationException(sprintf "PowerShell a échoué (code %d):%s" code detail))

        stdout

    /// Exécute une commande PowerShell avec les paramètres spécifiés.
    /// Utilise -EncodedCommand pour un binding sûr des paramètres (pas de concaténation dans argv).
    /// Les NOMS de paramètres sont interpolés dans le script : ils sont donc
    /// jaugés par une allowlist stricte ([-,]?[A-Za-z_][A-Za-z0-9_]*) pour
    /// empêcher toute injection depuis un nom contrôlé par un appelant.
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

    /// Exécute un script PowerShell brut avec des paramètres nommés.
    /// Utilise -EncodedCommand pour un binding sûr des paramètres.
    let runPowerShellScript
        (scriptBody: string)
        (parameters: (string * string) list)
        (timeoutMs: int option)
        (ct: System.Threading.CancellationToken option)
        : string =
        runEncodedScript scriptBody parameters timeoutMs ct
