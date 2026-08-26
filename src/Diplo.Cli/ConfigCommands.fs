namespace Diplo.Cli

open System
open System.IO
open System.Threading
open Spectre.Console.Cli
open Diplo.Core.Output
open Diplo.Abstractions

type InitConfigSettings() =
    inherit CommandSettings()

    [<CommandOption("--path")>]
    member val Path = "" with get, set

    [<CommandOption("--transport")>]
    member val Transport = "tcp" with get, set

type InitConfigCommand(output: IOutputPort) =
    inherit Command<InitConfigSettings>()

    // Défauts alignés sur DiploPorts (Debug 5001-5003, Release 6001-6003).
    let defaultConfig =
        sprintf
            """{
  "container": {
    "address": "localhost:%d",
    "namespace": "default"
  },
  "volume": {
    "address": "localhost:%d"
  },
  "network": {
    "address": "localhost:%d"
  },
  "logLevel": "Information"
}"""
            DiploPorts.Container
            DiploPorts.Volume
            DiploPorts.Network

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

            try
                File.WriteAllText(filePath, content)
                output.WriteSuccess(sprintf "Configuration écrite dans %s" filePath)
                0
            with ex ->
                output.WriteError(sprintf "Erreur lors de l'écriture de %s : %s" filePath ex.Message)
                1
