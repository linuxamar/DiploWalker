namespace Diplo.Network.Plugins

open System
open Diplo.Abstractions

[<AutoOpen>]
module PowerShellHelper =

    let runPowershellWithArgs (cmdlet: string) (parameters: (string * string) list) =
        ProcessExec.runPowerShell cmdlet parameters None None

    let runPowershellScript (scriptBody: string) (parameters: (string * string) list) =
        ProcessExec.runPowerShellScript scriptBody parameters None None
