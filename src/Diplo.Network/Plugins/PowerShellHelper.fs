namespace Diplo.Network.Plugins

open System
open System.Diagnostics

[<AutoOpen>]
module PowerShellHelper =

    let private runPowershell cmdletOrScript parameters isScript =
        let psi = ProcessStartInfo()
        psi.FileName <- "powershell"
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- true
        psi.ArgumentList.Add("-NoProfile") |> ignore
        psi.ArgumentList.Add("-NonInteractive") |> ignore
        psi.ArgumentList.Add("-Command") |> ignore
        let paramNames = parameters |> List.mapi (fun i _ -> sprintf "$p%d" i)
        let paramValues = parameters |> List.map snd
        let paramDecl = paramNames |> String.concat ", "
        let body =
            if isScript then cmdletOrScript
            else sprintf "%s -%s" cmdletOrScript (parameters |> List.mapi (fun i (name, _) -> sprintf "%s %s" name paramNames.[i]) |> String.concat " -")
        let script = sprintf "{ param(%s) %s }" paramDecl body
        psi.ArgumentList.Add(script) |> ignore
        for value in paramValues do
            psi.ArgumentList.Add(value) |> ignore
        use proc = Process.Start(psi)
        let stdout = proc.StandardOutput.ReadToEnd()
        let stderr = proc.StandardError.ReadToEnd()
        if not (proc.WaitForExit(60_000)) then
            try proc.Kill(true) with _ -> ()
            failwith "Délai d'attente dépassé pour PowerShell (60s)"
        if proc.ExitCode <> 0 then
            failwithf "PowerShell a échoué (code %d)" proc.ExitCode
        stdout

    let runPowershellWithArgs (cmdlet: string) (parameters: (string * string) list) =
        runPowershell cmdlet parameters false

    let runPowershellScript (scriptBody: string) (parameters: (string * string) list) =
        runPowershell scriptBody parameters true