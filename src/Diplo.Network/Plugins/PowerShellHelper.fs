namespace Diplo.Network.Plugins

open System
open Diplo.Abstractions

[<AutoOpen>]
module PowerShellHelper =

    let private runPowershell cmdletOrScript parameters isScript (timeoutMs: int option) =
        let timeout = defaultArg timeoutMs 60_000
        let script =
            let paramNames = parameters |> List.mapi (fun i _ -> sprintf "$p%d" i)
            let paramDecl = paramNames |> String.concat ", "
            let body =
                if isScript then cmdletOrScript
                else sprintf "%s -%s" cmdletOrScript (parameters |> List.mapi (fun i (name, _) -> sprintf "%s %s" name paramNames.[i]) |> String.concat " -")
            sprintf "{ param(%s) %s }" paramDecl body
        let args =
            [ yield "-NoProfile"
              yield "-NonInteractive"
              yield "-Command"
              yield script
              yield! parameters |> List.map snd ]
        let code, stdout, stderr = ProcessExec.runWithResult "powershell" args (Some timeout) None
        if code <> 0 then
            let detail = if String.IsNullOrWhiteSpace stderr then "" else " " + stderr.Trim()
            raise (InvalidOperationException(sprintf "PowerShell a échoué (code %d):%s" code detail))
        stdout

    let runPowershellWithArgs (cmdlet: string) (parameters: (string * string) list) =
        runPowershell cmdlet parameters false None

    let runPowershellScript (scriptBody: string) (parameters: (string * string) list) =
        runPowershell scriptBody parameters true None
