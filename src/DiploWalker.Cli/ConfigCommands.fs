namespace DiploWalker.Cli

open System
open System.IO
open System.Threading
open Spectre.Console.Cli
open DiploWalker.Core.Output
open DiploWalker.Abstractions

type InitConfigSettings() =
    inherit CommandSettings()

    [<CommandOption("--path")>]
    member val Path = "" with get, set

type InitConfigCommand(output: IOutputPort) =
    inherit Command<InitConfigSettings>()

    // Défauts alignés sur DiploWalkerPorts (Debug 5001-5003, Release 6001-6003).
    let defaultConfig =
        sprintf
            """{
  ""container"": {
    ""address"": ""localhost:%d"",
    ""namespace"": ""default""
  },
  ""volume"": {
    ""address"": ""localhost:%d""
  },
  ""network"": {
    ""address"": ""localhost:%d""
  },
  ""logLevel"": ""Information""
}"""
            DiploWalkerPorts.Container
            DiploWalkerPorts.Volume
            DiploWalkerPorts.Network

    override _.Execute(_ctx, settings, _ct: CancellationToken) =
        let filePath =
            if String.IsNullOrWhiteSpace(settings.Path) then
                Path.Combine(Directory.GetCurrentDirectory(), "DiploWalker.json")
            else
                settings.Path

        let dir = Path.GetDirectoryName(filePath)

        if not (String.IsNullOrWhiteSpace(dir)) && not (Directory.Exists(dir)) then
            Directory.CreateDirectory(dir) |> ignore

        try
            File.WriteAllText(filePath, defaultConfig)
            output.WriteSuccess(sprintf "Configuration écrite dans %s" filePath)
            0
        with ex ->
            output.WriteError(sprintf "Erreur lors de l'écriture de %s : %s" filePath ex.Message)
            1
