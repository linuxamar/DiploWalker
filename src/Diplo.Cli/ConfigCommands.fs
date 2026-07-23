namespace Diplo.Cli

open System.IO
open System.Threading
open Spectre.Console.Cli
open Diplo.Core.Output

type InitConfigSettings() =
    inherit CommandSettings()

    [<CommandOption("--path")>]
    member val Path = "" with get, set

type InitConfigCommand(output: IOutputPort) =
    inherit Command<InitConfigSettings>()

    let defaultConfig =
        """{
  "container": {
    "address": "localhost:5001",
    "namespace": "default"
  },
  "volume": {
    "address": "localhost:5002"
  },
  "network": {
    "address": "localhost:5003"
  },
  "logLevel": "Information"
}"""

    override _.Execute(_ctx, settings, _ct: CancellationToken) =
        let filePath =
            if System.String.IsNullOrEmpty(settings.Path) then
                Path.Combine(Directory.GetCurrentDirectory(), "diplo.json")
            else
                settings.Path

        let dir = Path.GetDirectoryName(filePath)
        if not (System.String.IsNullOrEmpty(dir)) && not (Directory.Exists(dir)) then
            Directory.CreateDirectory(dir) |> ignore

        File.WriteAllText(filePath, defaultConfig)
        output.WriteSuccess(sprintf "Configuration écrite dans %s" filePath)
        0
