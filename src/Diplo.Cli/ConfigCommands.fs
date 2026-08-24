namespace Diplo.Cli

open System
open System.IO
open System.Threading
open Spectre.Console.Cli
open Diplo.Core.Output

type InitConfigSettings() =
    inherit CommandSettings()

    [<CommandOption("--path")>]
    member val Path = "" with get, set

    [<CommandOption("--transport")>]
    member val Transport = "tcp" with get, set

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

    let pipeConfig =
        """{
  "container": {
    "address": "http://pipe:/diplo-container",
    "namespace": "default"
  },
  "volume": {
    "address": "http://pipe:/diplo-volume"
  },
  "network": {
    "address": "http://pipe:/diplo-network"
  },
  "logLevel": "Information"
}"""

    override _.Execute(_ctx, settings, _ct: CancellationToken) =
        let content =
            match settings.Transport.ToLowerInvariant() with
            | "tcp" -> Some defaultConfig
            | "pipe" -> Some pipeConfig
            | other ->
                output.WriteError(sprintf "Transport inconnu : '%s'. Valeurs acceptées : tcp, pipe" other)
                None

        match content with
        | None -> 1
        | Some content ->
            let filePath =
                if String.IsNullOrWhiteSpace(settings.Path) then
                    Path.Combine(Directory.GetCurrentDirectory(), "diplo.json")
                else
                    settings.Path

            let dir = Path.GetDirectoryName(filePath)
            if not (String.IsNullOrWhiteSpace(dir)) && not (Directory.Exists(dir)) then
                Directory.CreateDirectory(dir) |> ignore

            File.WriteAllText(filePath, content)
            output.WriteSuccess(sprintf "Configuration écrite dans %s" filePath)
            0
