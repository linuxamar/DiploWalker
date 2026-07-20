namespace Diplo.Container.Clients

open System.Diagnostics
open Diplo.Abstractions.Interfaces

/// Exécuteur réel de processus (ctr CLI)
type ProcessRunner() =

    let runProcess (psi: ProcessStartInfo) =
        use proc = Process.Start(psi)
        if proc |> isNull then failwithf "Impossible de démarrer %s" psi.FileName
        let stdout = proc.StandardOutput.ReadToEnd()
        let stderr = proc.StandardError.ReadToEnd()
        proc.WaitForExit()
        if proc.ExitCode <> 0 then
            failwithf "%s a échoué (code %d): %s" psi.FileName proc.ExitCode stderr
        stdout

    interface IProcessRunner with
        member _.Run(fileName, arguments) =
            let psi = ProcessStartInfo()
            psi.FileName <- fileName
            psi.Arguments <- arguments
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            psi.UseShellExecute <- false
            psi.CreateNoWindow <- true
            runProcess psi

        member _.RunWithArgs(fileName, args) =
            let psi = ProcessStartInfo()
            psi.FileName <- fileName
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            psi.UseShellExecute <- false
            psi.CreateNoWindow <- true
            for arg in args do
                psi.ArgumentList.Add(arg) |> ignore
            runProcess psi
