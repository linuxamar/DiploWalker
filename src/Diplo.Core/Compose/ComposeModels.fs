namespace Diplo.Core.Compose

open System
open System.Collections.Generic

type PortMapping = {
    ContainerPort: int
    HostPort: int option
    Protocol: string
}

type VolumeMapping = {
    Source: string
    Target: string
    ReadOnly: bool
}

type EnvironmentVariable = {
    Key: string
    Value: string
}

type ServiceDefinition = {
    Name: string
    Image: string
    Command: string list option
    Args: string list option
    Environment: EnvironmentVariable list
    Ports: PortMapping list
    Volumes: VolumeMapping list
    Labels: IDictionary<string, string>
    RestartPolicy: string option
    CpuShares: int option
    MemoryLimit: int64 option
    PidLimit: int option
}

type ComposeFile = {
    Version: string
    ProjectName: string
    Services: ServiceDefinition list
}

type ComposeContainerInfo = {
    ServiceName: string
    ContainerName: string
    ContainerId: string
    Image: string
    State: string
    Project: string
    Ports: string
}

type ComposeProjectInfo = {
    Name: string
    File: string
    Services: ComposeContainerInfo list
}

[<AutoOpen>]
module ComposeModels =

    let composeProjectLabel = "com.diplo.compose.project"
    let composeServiceLabel = "com.diplo.compose.service"

    let buildContainerName (projectName: string) (serviceName: string) (index: int) =
        sprintf "%s_%s_%d" projectName serviceName index

    let buildServiceLabels (projectName: string) (serviceName: string) =
        dict [
            composeProjectLabel, projectName
            composeServiceLabel, serviceName
        ]

    let parsePorts (portStrings: string list) : PortMapping list =
        portStrings
        |> List.choose (fun s ->
            let parts = s.Split('/', 2)
            match parts.[0].Split(':', 2) with
            | [| host; container |] ->
                match Int32.TryParse host, Int32.TryParse container with
                | (true, h), (true, c) ->
                    Some { ContainerPort = c; HostPort = Some h; Protocol = if parts.Length > 1 then parts.[1] else "tcp" }
                | _ -> None
            | [| container |] ->
                match Int32.TryParse container with
                | (true, c) ->
                    Some { ContainerPort = c; HostPort = None; Protocol = if parts.Length > 1 then parts.[1] else "tcp" }
                | _ -> None
            | _ -> None)

    let parseVolumes (volumeStrings: string list) : VolumeMapping list =
        volumeStrings
        |> List.choose (fun s ->
            let readOnly = s.EndsWith(":ro")
            let path = if readOnly then s.Substring(0, s.Length - 3) else s
            let parts = path.Split(':', 2)
            match parts with
            | [| source; target |] ->
                Some { Source = source; Target = target; ReadOnly = readOnly }
            | _ -> None)

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
            | [| key; value |] -> Some (key, value)
            | _ -> None)
        |> dict

    let mapPortsToEnv (ports: PortMapping list) : (string * string) list =
        ports
        |> List.mapi (fun i p ->
            let hostPart = match p.HostPort with Some h -> string h | None -> string p.ContainerPort
            sprintf "DIPLO_PORT_%d" i, sprintf "%s:%d" hostPart p.ContainerPort)
