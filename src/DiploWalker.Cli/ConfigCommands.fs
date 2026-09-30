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
            """{\r\n  \"container\": {\r\n    \"address\": \"localhost:%d\",\r\n    \"namespace\": \"default\"\r\n  },\r\n  \"volume\": {\r\n    \"address\": \"localhost:%d\"\r\n  },\r\n  \"network\": {\r\n    \"address\": \"localhost:%d\"\r\n  },\r\n  \"logLevel\": \"Information\"\r\n}\r\n"""
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
            """{\r\n  \"container\": {\r\n    \"address\": \"https://pipe:/%s\",\r\n    \"namespace\": \"default\"\r\n  },\r\n  \"volume\": {\r\n    \"address\": \"https://pipe:/%s\"\r\n  },\r\n  \"network\": {\r\n    \"address\": \"https://pipe:/%s\"\r\n  },\r\n  \"logLevel\": \"Information\"\r\n}\r\n"""
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


