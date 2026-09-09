namespace Diplo.Grpc.Container

open System
open ProtoBuf

// ═══════════════════════════════════════════════
// Enums
// ═══════════════════════════════════════════════

[<ProtoContract>]
type ContainerState =
    | [<ProtoEnum>] Unknown = 0
    | [<ProtoEnum>] Created = 1
    | [<ProtoEnum>] Running = 2
    | [<ProtoEnum>] Paused = 3
    | [<ProtoEnum>] Stopped = 4
    | [<ProtoEnum>] Failed = 5

// ═══════════════════════════════════════════════
// CreateContainer
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type ContainerMount =
    { [<ProtoMember(1)>]
      mutable Source: string
      [<ProtoMember(2)>]
      mutable Destination: string
      [<ProtoMember(3)>]
      mutable ReadOnly: bool }

[<ProtoContract>]
[<CLIMutable>]
type PortMapping =
    { [<ProtoMember(1)>]
      mutable HostPort: int
      [<ProtoMember(2)>]
      mutable ContainerPort: int
      [<ProtoMember(3)>]
      mutable Protocol: string }

[<ProtoContract>]
[<CLIMutable>]
type HealthCheckConfig =
    { [<ProtoMember(1)>]
      mutable Command: string
      [<ProtoMember(2)>]
      mutable IntervalSeconds: int
      [<ProtoMember(3)>]
      mutable TimeoutSeconds: int
      [<ProtoMember(4)>]
      mutable Retries: int }

[<ProtoContract>]
[<CLIMutable>]
type CreateContainerRequest =
    {
        [<ProtoMember(1)>]
        mutable Name: string
        [<ProtoMember(2)>]
        mutable Image: string
        [<ProtoMember(3)>]
        mutable Env: System.Collections.Generic.Dictionary<string, string>
        [<ProtoMember(4)>]
        mutable Command: System.Collections.Generic.List<string>
        [<ProtoMember(5)>]
        mutable Args: System.Collections.Generic.List<string>
        [<ProtoMember(6)>]
        mutable Labels: System.Collections.Generic.Dictionary<string, string>
        [<ProtoMember(7)>]
        mutable PidLimit: int
        [<ProtoMember(8)>]
        mutable MemoryLimit: int64
        [<ProtoMember(9)>]
        mutable CpuShares: int
        [<ProtoMember(10)>]
        mutable Mounts: System.Collections.Generic.List<ContainerMount>
        /// Politique de redémarrage : "" (aucun), "always", "on-failure".
        [<ProtoMember(11)>]
        mutable RestartPolicy: string
        /// Nombre max de redémarrages pour on-failure (0 = illimité).
        [<ProtoMember(12)>]
        mutable RestartMaxCount: int
        [<ProtoMember(13)>]
        mutable Ports: System.Collections.Generic.List<PortMapping>
        [<ProtoMember(14)>]
        mutable HealthCheck: HealthCheckConfig
    }

    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Env then
            this.Env <- System.Collections.Generic.Dictionary<string, string>()

        if isNull this.Command then
            this.Command <- System.Collections.Generic.List<string>()

        if isNull this.Args then
            this.Args <- System.Collections.Generic.List<string>()

        if isNull this.Labels then
            this.Labels <- System.Collections.Generic.Dictionary<string, string>()

        if isNull this.Mounts then
            this.Mounts <- System.Collections.Generic.List<ContainerMount>()

        if isNull this.Ports then
            this.Ports <- System.Collections.Generic.List<PortMapping>()

[<ProtoContract>]
[<CLIMutable>]
type CreateContainerResponse =
    { [<ProtoMember(1)>]
      mutable Id: string
      [<ProtoMember(2)>]
      mutable Name: string
      [<ProtoMember(3)>]
      mutable State: ContainerState
      [<ProtoMember(4)>]
      mutable CreatedAt: string }

// ═══════════════════════════════════════════════
// StartContainer
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type StartContainerRequest =
    {
        [<ProtoMember(1)>]
        mutable Id: string
        /// true : la console du client reste attachée au conteneur ;
        /// false : le démarrage est non interactif (détaché).
        [<ProtoMember(2)>]
        mutable Attach: bool
    }

[<ProtoContract>]
[<CLIMutable>]
type StartContainerResponse =
    { [<ProtoMember(1)>]
      mutable State: ContainerState
      [<ProtoMember(2)>]
      mutable Message: string }

// ═══════════════════════════════════════════════
// StopContainer
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type StopContainerRequest =
    { [<ProtoMember(1)>]
      mutable Id: string
      [<ProtoMember(2)>]
      mutable TimeoutSeconds: int }

[<ProtoContract>]
[<CLIMutable>]
type StopContainerResponse =
    { [<ProtoMember(1)>]
      mutable State: ContainerState
      [<ProtoMember(2)>]
      mutable Message: string }

// ═══════════════════════════════════════════════
// DeleteContainer
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type DeleteContainerRequest =
    { [<ProtoMember(1)>]
      mutable Id: string
      [<ProtoMember(2)>]
      mutable Force: bool }

[<ProtoContract>]
[<CLIMutable>]
type DeleteContainerResponse =
    { [<ProtoMember(1)>]
      mutable Success: bool
      [<ProtoMember(2)>]
      mutable Message: string }

// ═══════════════════════════════════════════════
// InspectContainer
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type InspectContainerRequest =
    { [<ProtoMember(1)>]
      mutable Id: string }

[<ProtoContract>]
[<CLIMutable>]
type InspectContainerResponse =
    {
        [<ProtoMember(1)>]
        mutable Id: string
        [<ProtoMember(2)>]
        mutable Name: string
        [<ProtoMember(3)>]
        mutable Image: string
        [<ProtoMember(4)>]
        mutable State: ContainerState
        [<ProtoMember(5)>]
        mutable CreatedAt: string
        [<ProtoMember(6)>]
        mutable StartedAt: string
        [<ProtoMember(7)>]
        mutable FinishedAt: string
        [<ProtoMember(8)>]
        mutable Labels: System.Collections.Generic.Dictionary<string, string>
        [<ProtoMember(9)>]
        mutable Env: System.Collections.Generic.Dictionary<string, string>
        [<ProtoMember(10)>]
        mutable Pid: int
        [<ProtoMember(11)>]
        mutable ExitCode: int
        [<ProtoMember(12)>]
        mutable RestartPolicy: string
        [<ProtoMember(13)>]
        mutable Ports: System.Collections.Generic.List<PortMapping>
        /// Santé : "", "starting", "healthy", "unhealthy".
        [<ProtoMember(14)>]
        mutable Health: string
        [<ProtoMember(15)>]
        mutable Mounts: System.Collections.Generic.List<string>
    }

    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Labels then
            this.Labels <- System.Collections.Generic.Dictionary<string, string>()

        if isNull this.Env then
            this.Env <- System.Collections.Generic.Dictionary<string, string>()

        if isNull this.Ports then
            this.Ports <- System.Collections.Generic.List<PortMapping>()

        if isNull this.Mounts then
            this.Mounts <- System.Collections.Generic.List<string>()

// ═══════════════════════════════════════════════
// ListContainers
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type ListContainersRequest =
    { [<ProtoMember(1)>]
      mutable All: bool
      [<ProtoMember(2)>]
      mutable Filters: System.Collections.Generic.Dictionary<string, string> }

    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Filters then
            this.Filters <- System.Collections.Generic.Dictionary<string, string>()

[<ProtoContract>]
[<CLIMutable>]
type ContainerInfo =
    { [<ProtoMember(1)>]
      mutable Id: string
      [<ProtoMember(2)>]
      mutable Name: string
      [<ProtoMember(3)>]
      mutable Image: string
      [<ProtoMember(4)>]
      mutable State: ContainerState
      [<ProtoMember(5)>]
      mutable CreatedAt: string
      [<ProtoMember(6)>]
      mutable Labels: System.Collections.Generic.Dictionary<string, string> }

    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Labels then
            this.Labels <- System.Collections.Generic.Dictionary<string, string>()

[<ProtoContract>]
[<CLIMutable>]
type ListContainersResponse =
    { [<ProtoMember(1)>]
      mutable Containers: System.Collections.Generic.List<ContainerInfo> }

    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Containers then
            this.Containers <- System.Collections.Generic.List<ContainerInfo>()

// ═══════════════════════════════════════════════
// GetContainerLogs (server streaming)
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type GetContainerLogsRequest =
    { [<ProtoMember(1)>]
      mutable Id: string
      [<ProtoMember(2)>]
      mutable Follow: bool
      [<ProtoMember(3)>]
      mutable Tail: int
      [<ProtoMember(4)>]
      mutable Since: string }

[<ProtoContract>]
[<CLIMutable>]
type ContainerLogEntry =
    { [<ProtoMember(1)>]
      mutable Timestamp: string
      [<ProtoMember(2)>]
      mutable Stream: string
      [<ProtoMember(3)>]
      mutable Log: string }

// ═══════════════════════════════════════════════
// ExecInContainer (server streaming)
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type ExecInContainerRequest =
    { [<ProtoMember(1)>]
      mutable Id: string
      [<ProtoMember(2)>]
      mutable Command: System.Collections.Generic.List<string>
      [<ProtoMember(3)>]
      mutable AttachStdin: bool
      [<ProtoMember(4)>]
      mutable AttachStdout: bool
      [<ProtoMember(5)>]
      mutable AttachStderr: bool }

    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Command then
            this.Command <- System.Collections.Generic.List<string>()

[<ProtoContract>]
[<CLIMutable>]
type ExecOutput =
    { [<ProtoMember(1)>]
      mutable Stream: string
      [<ProtoMember(2)>]
      mutable Data: byte[] }

// ═══════════════════════════════════════════════
// PullImage
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type PullImageRequest =
    { [<ProtoMember(1)>]
      mutable Image: string
      [<ProtoMember(2)>]
      mutable User: string }

[<ProtoContract>]
[<CLIMutable>]
type PullImageResponse =
    { [<ProtoMember(1)>]
      mutable Image: string
      [<ProtoMember(2)>]
      mutable Message: string }

// ═══════════════════════════════════════════════
// GetVersion
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type GetVersionRequest =
    { [<ProtoMember(1)>]
      mutable Placeholder: bool }

[<ProtoContract>]
[<CLIMutable>]
type GetVersionResponse =
    { [<ProtoMember(1)>]
      mutable Version: string
      [<ProtoMember(2)>]
      mutable Revision: string
      [<ProtoMember(3)>]
      mutable GoVersion: string
      [<ProtoMember(4)>]
      mutable Os: string
      [<ProtoMember(5)>]
      mutable Arch: string }

// ═══════════════════════════════════════════════
// ListNamespaces
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type ListNamespacesRequest =
    { [<ProtoMember(1)>]
      mutable Placeholder: bool }

[<ProtoContract>]
[<CLIMutable>]
type ListNamespacesResponse =
    { [<ProtoMember(1)>]
      mutable Namespaces: System.Collections.Generic.List<string> }

    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Namespaces then
            this.Namespaces <- System.Collections.Generic.List<string>()

// ═══════════════════════════════════════════════
// RenameContainer
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type RenameContainerRequest =
    { [<ProtoMember(1)>]
      mutable Id: string
      [<ProtoMember(2)>]
      mutable NewName: string }

[<ProtoContract>]
[<CLIMutable>]
type RenameContainerResponse =
    { [<ProtoMember(1)>]
      mutable Success: bool
      [<ProtoMember(2)>]
      mutable Message: string }

// ═══════════════════════════════════════════════
// TopContainer
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type TopContainerRequest =
    { [<ProtoMember(1)>]
      mutable Id: string }

[<ProtoContract>]
[<CLIMutable>]
type ProcessInfo =
    { [<ProtoMember(1)>]
      mutable Pid: int64
      [<ProtoMember(2)>]
      mutable User: string
      [<ProtoMember(3)>]
      mutable Command: string
      [<ProtoMember(4)>]
      mutable CpuPercent: float
      [<ProtoMember(5)>]
      mutable MemPercent: float
      [<ProtoMember(6)>]
      mutable Rss: int64 }

[<ProtoContract>]
[<CLIMutable>]
type TopContainerResponse =
    { [<ProtoMember(1)>]
      mutable Processes: System.Collections.Generic.List<ProcessInfo> }

    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Processes then
            this.Processes <- System.Collections.Generic.List<ProcessInfo>()

// ═══════════════════════════════════════════════
// GetContainerStats
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type GetContainerStatsRequest =
    { [<ProtoMember(1)>]
      mutable Id: string }

[<ProtoContract>]
[<CLIMutable>]
type GetContainerStatsResponse =
    { [<ProtoMember(1)>]
      mutable CpuUsage: float
      [<ProtoMember(2)>]
      mutable MemoryUsage: int64
      [<ProtoMember(3)>]
      mutable MemoryLimit: int64
      [<ProtoMember(4)>]
      mutable NetworkRx: int64
      [<ProtoMember(5)>]
      mutable NetworkTx: int64
      [<ProtoMember(6)>]
      mutable DiskRead: int64
      [<ProtoMember(7)>]
      mutable DiskWrite: int64
      [<ProtoMember(8)>]
      mutable Pids: int }

// ═══════════════════════════════════════════════
// ListImages
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type ListImagesRequest =
    { [<ProtoMember(1)>]
      mutable NamespaceName: string }

[<ProtoContract>]
[<CLIMutable>]
type ImageInfo =
    { [<ProtoMember(1)>]
      mutable Ref: string
      [<ProtoMember(2)>]
      mutable Id: string
      [<ProtoMember(3)>]
      mutable Repository: string
      [<ProtoMember(4)>]
      mutable Tag: string
      [<ProtoMember(5)>]
      mutable Size: int64
      [<ProtoMember(6)>]
      mutable CreatedAt: string }

[<ProtoContract>]
[<CLIMutable>]
type ListImagesResponse =
    { [<ProtoMember(1)>]
      mutable Images: System.Collections.Generic.List<ImageInfo> }

    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Images then
            this.Images <- System.Collections.Generic.List<ImageInfo>()

// ═══════════════════════════════════════════════
// InspectImage
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type InspectImageRequest =
    { [<ProtoMember(1)>]
      mutable Ref: string
      [<ProtoMember(2)>]
      mutable NamespaceName: string }

[<ProtoContract>]
[<CLIMutable>]
type InspectImageResponse =
    { [<ProtoMember(1)>]
      mutable Ref: string
      [<ProtoMember(2)>]
      mutable Id: string
      [<ProtoMember(3)>]
      mutable Repository: string
      [<ProtoMember(4)>]
      mutable Tag: string
      [<ProtoMember(5)>]
      mutable Size: int64
      [<ProtoMember(6)>]
      mutable CreatedAt: string
      [<ProtoMember(7)>]
      mutable Labels: System.Collections.Generic.Dictionary<string, string> }

    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Labels then
            this.Labels <- System.Collections.Generic.Dictionary<string, string>()

// ═══════════════════════════════════════════════
// RemoveImage
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type RemoveImageRequest =
    { [<ProtoMember(1)>]
      mutable Ref: string
      [<ProtoMember(2)>]
      mutable NamespaceName: string }

[<ProtoContract>]
[<CLIMutable>]
type RemoveImageResponse =
    { [<ProtoMember(1)>]
      mutable Success: bool
      [<ProtoMember(2)>]
      mutable Message: string }

// ═══════════════════════════════════════════════
// TagImage
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type TagImageRequest =
    { [<ProtoMember(1)>]
      mutable Source: string
      [<ProtoMember(2)>]
      mutable Target: string
      [<ProtoMember(3)>]
      mutable NamespaceName: string }

[<ProtoContract>]
[<CLIMutable>]
type TagImageResponse =
    { [<ProtoMember(1)>]
      mutable Source: string
      [<ProtoMember(2)>]
      mutable Target: string
      [<ProtoMember(3)>]
      mutable Message: string }

// ═══════════════════════════════════════════════
// SearchRegistry
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type SearchRegistryRequest =
    { [<ProtoMember(1)>]
      mutable Query: string
      [<ProtoMember(2)>]
      mutable Registry: string
      [<ProtoMember(3)>]
      mutable Limit: int }

[<ProtoContract>]
[<CLIMutable>]
type RegistrySearchResult =
    { [<ProtoMember(1)>]
      mutable Registry: string
      [<ProtoMember(2)>]
      mutable Ref: string
      [<ProtoMember(3)>]
      mutable Description: string
      [<ProtoMember(4)>]
      mutable Stars: int }

[<ProtoContract>]
[<CLIMutable>]
type SearchRegistryResponse =
    { [<ProtoMember(1)>]
      mutable Results: System.Collections.Generic.List<RegistrySearchResult>
      [<ProtoMember(2)>]
      mutable Message: string }

    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Results then
            this.Results <- System.Collections.Generic.List<RegistrySearchResult>()

// ═══════════════════════════════════════════════
// Pause / UnpauseContainer
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type PauseContainerRequest =
    { [<ProtoMember(1)>]
      mutable Id: string }

[<ProtoContract>]
[<CLIMutable>]
type PauseContainerResponse =
    { [<ProtoMember(1)>]
      mutable State: ContainerState
      [<ProtoMember(2)>]
      mutable Message: string }

[<ProtoContract>]
[<CLIMutable>]
type UnpauseContainerRequest =
    { [<ProtoMember(1)>]
      mutable Id: string }

[<ProtoContract>]
[<CLIMutable>]
type UnpauseContainerResponse =
    { [<ProtoMember(1)>]
      mutable State: ContainerState
      [<ProtoMember(2)>]
      mutable Message: string }

// ═══════════════════════════════════════════════
// WaitContainer
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type WaitContainerRequest =
    {
        [<ProtoMember(1)>]
        mutable Id: string
        /// 0 = attente illimitée.
        [<ProtoMember(2)>]
        mutable TimeoutSeconds: int
    }

[<ProtoContract>]
[<CLIMutable>]
type WaitContainerResponse =
    { [<ProtoMember(1)>]
      mutable ExitCode: int
      [<ProtoMember(2)>]
      mutable State: ContainerState
      [<ProtoMember(3)>]
      mutable Message: string }

// ═══════════════════════════════════════════════
// UpdateContainer
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type UpdateContainerRequest =
    {
        [<ProtoMember(1)>]
        mutable Id: string
        /// 0 = inchangé.
        [<ProtoMember(2)>]
        mutable MemoryLimit: int64
        /// 0 = inchangé.
        [<ProtoMember(3)>]
        mutable CpuShares: int
        /// 0 = inchangé.
        [<ProtoMember(4)>]
        mutable PidLimit: int
        /// Nouvelle politique de redémarrage ("" = inchangée).
        [<ProtoMember(5)>]
        mutable RestartPolicy: string
    }

[<ProtoContract>]
[<CLIMutable>]
type UpdateContainerResponse =
    { [<ProtoMember(1)>]
      mutable Success: bool
      [<ProtoMember(2)>]
      mutable Message: string }

// ═══════════════════════════════════════════════
// PruneContainers / PruneImages
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type PruneContainersRequest =
    { [<ProtoMember(1)>]
      mutable Placeholder: bool }

[<ProtoContract>]
[<CLIMutable>]
type PruneContainersResponse =
    { [<ProtoMember(1)>]
      mutable Deleted: System.Collections.Generic.List<string> }

    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Deleted then
            this.Deleted <- System.Collections.Generic.List<string>()

[<ProtoContract>]
[<CLIMutable>]
type PruneImagesRequest =
    { [<ProtoMember(1)>]
      mutable Placeholder: bool }

[<ProtoContract>]
[<CLIMutable>]
type PruneImagesResponse =
    { [<ProtoMember(1)>]
      mutable Deleted: System.Collections.Generic.List<string> }

    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Deleted then
            this.Deleted <- System.Collections.Generic.List<string>()

// ═══════════════════════════════════════════════
// GetContainerStatsStream (server streaming)
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type GetContainerStatsStreamRequest =
    {
        [<ProtoMember(1)>]
        mutable Id: string
        /// 0 = défaut (2 s).
        [<ProtoMember(2)>]
        mutable IntervalSeconds: int
    }

// ═══════════════════════════════════════════════
// WatchEvents (server streaming)
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type WatchEventsRequest =
    { [<ProtoMember(1)>]
      mutable Placeholder: bool }

[<ProtoContract>]
[<CLIMutable>]
type ContainerEvent =
    {
        [<ProtoMember(1)>]
        mutable Timestamp: string
        /// create, start, stop, exit, pause, unpause, delete.
        [<ProtoMember(2)>]
        mutable EventType: string
        [<ProtoMember(3)>]
        mutable Id: string
        [<ProtoMember(4)>]
        mutable Status: string
        [<ProtoMember(5)>]
        mutable ExitCode: int
    }

// ═══════════════════════════════════════════════
// ExecContainerStream (bidirectionnel)
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type ExecMessage =
    {
        [<ProtoMember(1)>]
        mutable Id: string
        [<ProtoMember(2)>]
        mutable Command: System.Collections.Generic.List<string>
        /// Données envoyées vers l'entrée standard du processus.
        [<ProtoMember(3)>]
        mutable Data: byte[]
        /// true : fermeture du flux d'entrée.
        [<ProtoMember(4)>]
        mutable Eof: bool
    }

    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Command then
            this.Command <- System.Collections.Generic.List<string>()

// ═══════════════════════════════════════════════
// ReadFile / WriteFile (copie de fichiers)
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type ReadFileRequest =
    { [<ProtoMember(1)>]
      mutable Id: string
      [<ProtoMember(2)>]
      mutable Path: string }

[<ProtoContract>]
[<CLIMutable>]
type ReadFileResponse =
    { [<ProtoMember(1)>]
      mutable Data: byte[]
      [<ProtoMember(2)>]
      mutable Success: bool
      [<ProtoMember(3)>]
      mutable Message: string }

[<ProtoContract>]
[<CLIMutable>]
type WriteFileRequest =
    { [<ProtoMember(1)>]
      mutable Id: string
      [<ProtoMember(2)>]
      mutable Path: string
      [<ProtoMember(3)>]
      mutable Data: byte[] }

[<ProtoContract>]
[<CLIMutable>]
type WriteFileResponse =
    { [<ProtoMember(1)>]
      mutable Success: bool
      [<ProtoMember(2)>]
      mutable Message: string }

// ═══════════════════════════════════════════════
// CommitImage
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type CommitImageRequest =
    { [<ProtoMember(1)>]
      mutable ContainerId: string
      [<ProtoMember(2)>]
      mutable ImageRef: string
      [<ProtoMember(3)>]
      mutable Message: string
      [<ProtoMember(4)>]
      mutable Author: string }

[<ProtoContract>]
[<CLIMutable>]
type CommitImageResponse =
    { [<ProtoMember(1)>]
      mutable ImageRef: string
      [<ProtoMember(2)>]
      mutable Success: bool
      [<ProtoMember(3)>]
      mutable Message: string }

// ═══════════════════════════════════════════════
// ExportImage / ImportImage
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type ExportImageRequest =
    { [<ProtoMember(1)>]
      mutable ImageRef: string
      [<ProtoMember(2)>]
      mutable NamespaceName: string }

[<ProtoContract>]
[<CLIMutable>]
type ImageChunk =
    { [<ProtoMember(1)>]
      mutable Data: byte[] }

[<ProtoContract>]
[<CLIMutable>]
type ImportImageRequest =
    { [<ProtoMember(1)>]
      mutable NamespaceName: string }

[<ProtoContract>]
[<CLIMutable>]
type ImportImageResponse =
    { [<ProtoMember(1)>]
      mutable ImageRefs: System.Collections.Generic.List<string>
      [<ProtoMember(2)>]
      mutable Message: string }

    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.ImageRefs then
            this.ImageRefs <- System.Collections.Generic.List<string>()

// ═══════════════════════════════════════════════
// LoginRegistry / LogoutRegistry
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type LoginRegistryRequest =
    { [<ProtoMember(1)>]
      mutable Registry: string
      [<ProtoMember(2)>]
      mutable Username: string
      [<ProtoMember(3)>]
      mutable Password: string }

[<ProtoContract>]
[<CLIMutable>]
type LoginRegistryResponse =
    { [<ProtoMember(1)>]
      mutable Success: bool
      [<ProtoMember(2)>]
      mutable Message: string }

[<ProtoContract>]
[<CLIMutable>]
type LogoutRegistryRequest =
    { [<ProtoMember(1)>]
      mutable Registry: string }

[<ProtoContract>]
[<CLIMutable>]
type LogoutRegistryResponse =
    { [<ProtoMember(1)>]
      mutable Success: bool
      [<ProtoMember(2)>]
      mutable Message: string }

// ═══════════════════════════════════════════════
// CreateNamespace / DeleteNamespace
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type CreateNamespaceRequest =
    { [<ProtoMember(1)>]
      mutable Name: string }

[<ProtoContract>]
[<CLIMutable>]
type CreateNamespaceResponse =
    { [<ProtoMember(1)>]
      mutable Success: bool
      [<ProtoMember(2)>]
      mutable Message: string }

[<ProtoContract>]
[<CLIMutable>]
type DeleteNamespaceRequest =
    { [<ProtoMember(1)>]
      mutable Name: string }

[<ProtoContract>]
[<CLIMutable>]
type DeleteNamespaceResponse =
    { [<ProtoMember(1)>]
      mutable Success: bool
      [<ProtoMember(2)>]
      mutable Message: string }
