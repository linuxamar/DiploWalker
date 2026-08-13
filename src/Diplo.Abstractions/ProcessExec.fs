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

    let private timeoutMsOr (timeoutMs: int option) = defaultArg timeoutMs defaultTimeoutMs

    /// Exécute `fileName` avec `args` et renvoie `(code, stdout, stderr)`.
    /// `input` (Some texte) alimente l'entrée standard ; `timeoutMs` borne la
    /// durée totale (None = 60 s) et déclenche un `Kill` en cas de dépassement.
    let runWithResult (fileName: string) (args: seq<string>) (timeoutMs: int option) (input: string option) : int * string * string =
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
        let exited = proc.WaitForExitAsync()
        let completed = Task.WhenAny(exited, Task.Delay(timeout)).GetAwaiter().GetResult()
        if completed <> exited then
            try proc.Kill(true) with _ -> ()
            // Lectures interrompues par le Kill : on les laisse se terminer pour
            // éviter une exception de disposition sur le Process.
            try
                stdoutRead.GetAwaiter().GetResult() |> ignore
                stderrRead.GetAwaiter().GetResult() |> ignore
            with _ -> ()
            raise (TimeoutException(sprintf "Délai d'attente dépassé pour '%s' (%dms)" fileName timeout))
        let stdout = stdoutRead.GetAwaiter().GetResult()
        let stderr = stderrRead.GetAwaiter().GetResult()
        (proc.ExitCode, stdout, stderr)

    /// Exécute `fileName` avec `args` et renvoie la sortie standard. Lève si le
    /// délai d'attente est dépassé ou si le processus échoue (la sortie d'erreur
    /// est jointe au message). Paramètres comme `runWithResult`.
    let run (fileName: string) (args: seq<string>) (timeoutMs: int option) (input: string option) : string =
        let code, stdout, stderr = runWithResult fileName args timeoutMs input
        if code <> 0 then
            let detail = if String.IsNullOrWhiteSpace stderr then "" else " " + stderr.Trim()
            raise (InvalidOperationException(sprintf "La commande '%s' a échoué (code %d):%s" fileName code detail))
        stdout
