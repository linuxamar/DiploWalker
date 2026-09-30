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

    [<CommandOption("--transport")>]
    member val Transport = "tcp" with get, set

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

    let pipeConfig =
        // TLS par défaut : le client épingle l'empreinte de
        // `certificates\leaf-tls-server` (cf. PipeTls). `http://pipe:/` reste
        // accepté pour les déploiements existants, mais il transmet le token
        // d'authentification en clair.
        // Les noms de pipes sont suffixés par "-debug" en configuration Debug
        // pour permettre l'exécution simultanée avec une installation Release.
        sprintf
            """{
  ""container"": {
    ""address"": ""https://pipe:/%s"",
    ""namespace"": ""default""
  },
  ""volume"": {
    ""address"": ""https://pipe:/%s""
  },
  ""network"": {
    ""address"": ""https://pipe:/%s""
  },
  ""logLevel"": ""Information""
}"""
            DiploWalkerPorts.ContainerPipe
            DiploWalkerPorts.VolumePipe
            DiploWalkerPorts.NetworkPipe

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
                    Path.Combine(Directory.GetCurrentDirectory(), "DiploWalker.json")
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

