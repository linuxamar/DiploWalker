namespace DiploWalker.Core.Compose

open System
open System.Collections.Generic

type PortMapping =
    { ContainerPort: int
      HostPort: int option
      Protocol: string }

type VolumeMapping =
    { Source: string
      Target: string
      ReadOnly: bool }

type EnvironmentVariable = { Key: string; Value: string }

type ServiceDefinition =
    { Name: string
      Image: string
      Build: string option
      Command: string list option
      Args: string list option
      Environment: EnvironmentVariable list
      Ports: PortMapping list
      Volumes: VolumeMapping list
      Labels: IDictionary<string, string>
      RestartPolicy: string option
      CpuShares: int option
      MemoryLimit: int64 option
      PidLimit: int option }

type ComposeFile =
    { Version: string
      ProjectName: string
      Services: ServiceDefinition list }

type ComposeContainerInfo =
    { ServiceName: string
      ContainerName: string
      ContainerId: string
      Image: string
      State: string
      Project: string
      Ports: string }

type ComposeProjectInfo =
    { Name: string
      File: string
      Services: ComposeContainerInfo list }

[<AutoOpen>]
module ComposeModels =

    let composeProjectLabel = "com.DiploWalker.compose.project"
    let composeServiceLabel = "com.DiploWalker.compose.service"

    let buildContainerName (projectName: string) (serviceName: string) (index: int) =
        sprintf "%s_%s_%d" projectName serviceName index

    let buildServiceLabels (projectName: string) (serviceName: string) =
        dict [ composeProjectLabel, projectName; composeServiceLabel, serviceName ]

    /// Analyse une entrée « ports ». Gère les formes conteneur, hôte:conteneur
    /// et IP:hôte:conteneur. Retourne None pour une entrée inexploitable.
    let private tryParsePortSpec (s: string) : PortMapping option =
        let parts = s.Split('/', 2)
        let protocol = if parts.Length > 1 then parts.[1] else "tcp"
        let spec = parts.[0].Trim()

        // Découper sur tous les ':' : gère "8080:80" et "127.0.0.1:8080:80".
        match spec.Split(':') with
        | [| container |] ->
            match Int32.TryParse(container) with
            | true, c when c > 0 && c <= 65535 ->
                Some { ContainerPort = c; HostPort = None; Protocol = protocol }
            | _ -> None
        | [| host; container |] when not (host.Contains(".")) ->
            match Int32.TryParse(host), Int32.TryParse(container) with
            | (true, h), (true, c) when h > 0 && h <= 65535 && c > 0 && c <= 65535 ->
                Some { ContainerPort = c; HostPort = Some h; Protocol = protocol }
            | _ -> None
        | [| _ip; host; container |] ->
            match Int32.TryParse(host), Int32.TryParse(container) with
            | (true, h), (true, c) when h > 0 && h <= 65535 && c > 0 && c <= 65535 ->
                Some { ContainerPort = c; HostPort = Some h; Protocol = protocol }
            | _ -> None
        | _ -> None

    /// Retourne (mappings valides, entrées rejetées). Le rejet doit être signalé
    /// par l'appelant : une configuration réseau perdue silencieusement est un bug.
    let parsePorts (portStrings: string list) : PortMapping list * string list =
        portStrings
        |> List.partition (fun s -> tryParsePortSpec s |> Option.isSome)
        |> fun (valid, invalid) ->
            (valid |> List.choose tryParsePortSpec, invalid)

    let parseVolumes (volumeStrings: string list) : VolumeMapping list =
        volumeStrings
        |> List.choose (fun s ->
            let readOnly = s.EndsWith(":ro")
            let path = if readOnly then s.Substring(0, s.Length - 3) else s

            // Chemin Windows avec lettre de lecteur ("C:\data:/app") : le second
            // ':' est le séparateur source/cible, pas une coupure du chemin.
            let drivePrefixLen =
                if path.Length >= 2 && Char.IsAsciiLetter path.[0] && path.[1] = ':' then
                    2
                else
                    0

            let rest = path.Substring(drivePrefixLen)

            match rest.IndexOf(':') with
            | -1 -> None
            | idx ->
                let source = path.Substring(0, drivePrefixLen + idx).Trim()
                let target = path.Substring(drivePrefixLen + idx + 1).Trim()

                if String.IsNullOrWhiteSpace source || String.IsNullOrWhiteSpace target then
                    None
                else
                    Some { Source = source; Target = target; ReadOnly = readOnly })

    let parseEnvironment (envStrings: string list) : EnvironmentVariable list =
        envStrings
        |> List.choose (fun s ->
            match s.Split('=', 2) with
            | [| key; value |] -> Some { Key = key; Value = value }
            | _ -> None)

    let parseLabels (labelStrings: string list) : IDictionary<string, string> =
        labelStrings
        |> List.choose (fun s ->
            match s.Split('=', 2) with
            | [| key; value |] -> Some(key, value)
            | _ -> None)
        |> dict

    let mapPortsToEnv (ports: PortMapping list) : (string * string) list =
        ports
        |> List.mapi (fun i p ->
            let hostPart =
                match p.HostPort with
                | Some h -> string h
                | None -> string p.ContainerPort

            sprintf "DIPLO_PORT_%d" i, sprintf "%s:%d" hostPart p.ContainerPort)


