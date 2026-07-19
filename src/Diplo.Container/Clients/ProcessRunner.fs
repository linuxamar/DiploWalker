namespace Diplo.Container.Clients

open System.Diagnostics
open Diplo.Abstractions.Interfaces

/// Exécuteur réel de processus (ctr CLI)
type ProcessRunner() =

    interface IProcessRunner with
        member _.Run(fileName, arguments) =
            let psi = ProcessStartInfo()
            psi.FileName <- fileName
            psi.Arguments <- arguments
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            psi.UseShellExecute <- false
            psi.CreateNoWindow <- true
            use proc = Process.Start(psi)
            if proc |> isNull then failwithf "Impossible de démarrer %s" fileName
            let stdout = proc.StandardOutput.ReadToEnd()
            let stderr = proc.StandardError.ReadToEnd()
            proc.WaitForExit()
            if proc.ExitCode <> 0 then
                failwithf "%s a échoué (code %d): %s" fileName proc.ExitCode stderr
            stdout
