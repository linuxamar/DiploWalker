namespace Diplo.Container.Clients

open System.Diagnostics
open Diplo.Abstractions.Interfaces

/// Exécuteur réel de processus (ctr CLI)
type ProcessRunner() =

    let runProcess (psi: ProcessStartInfo) =
        use proc = Process.Start(psi)
        if proc |> isNull then failwith "Impossible de démarrer le processus"
        let stdout = proc.StandardOutput.ReadToEnd()
        let stderr = proc.StandardError.ReadToEnd()
        if not (proc.WaitForExit(60_000)) then
            try proc.Kill(true) with _ -> ()
            failwith "Délai d'attente dépassé pour le processus (60s)"
        if proc.ExitCode <> 0 then
            failwithf "Le processus a échoué (code %d)" proc.ExitCode
        stdout

    interface IProcessRunner with
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
