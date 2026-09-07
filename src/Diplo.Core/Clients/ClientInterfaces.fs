namespace Diplo.Core.Clients

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Diplo.Abstractions
open Diplo.Core
open Diplo.Grpc
open Diplo.Grpc.Container
open Diplo.Grpc.Network
open Diplo.Grpc.Volume

/// Contrat des appels gRPC conteneurs consommés par les commandes CLI.
/// Injecté via IDiploClients pour permettre les tests unitaires sans serveur.
type IContainerClient =
    inherit IDisposable

    abstract member CreateAsync:
        name: string *
        image: string *
        ?env: IDictionary<string, string> *
        ?command: string list *
        ?args: string list *
        ?labels: IDictionary<string, string> *
        ?pidLimit: int *
        ?memoryLimit: int64 *
        ?cpuShares: int *
        ?mounts: (string * string * bool) list *
        ?ports: (int * int * string) list *
        ?ct: CancellationToken ->
            Task<CreateContainerResponse>

    abstract member StartAsync: id: string * ?attach: bool * ?ct: CancellationToken -> Task<StartContainerResponse>

    abstract member StopAsync: id: string * ?timeoutSeconds: int * ?ct: CancellationToken -> Task<StopContainerResponse>

    abstract member DeleteAsync: id: string * ?force: bool * ?ct: CancellationToken -> Task<DeleteContainerResponse>

    abstract member InspectAsync: id: string * ?ct: CancellationToken -> Task<InspectContainerResponse>

    abstract member ListAsync:
        ?all: bool * ?filters: IDictionary<string, string> * ?ct: CancellationToken -> Task<ListContainersResponse>

    abstract member GetLogsStream:
        id: string * ?follow: bool * ?tail: int * ?since: string * ?ct: CancellationToken ->
            IAsyncEnumerable<ContainerLogEntry>

    abstract member GetLogs:
        id: string * ?follow: bool * ?tail: int * ?since: string * ?ct: CancellationToken ->
            Task<seq<ContainerLogEntry>>

    abstract member Exec:
        id: string * command: IEnumerable<string> * ?attachStdout: bool * ?attachStderr: bool * ?ct: CancellationToken ->
            Task<seq<ExecOutput>>

    abstract member PullImageAsync: image: string * ?user: string * ?ct: CancellationToken -> Task<PullImageResponse>

    abstract member LoginRegistryAsync:
        registry: string * username: string * password: string * ?ct: CancellationToken -> Task<LoginRegistryResponse>

    abstract member LogoutRegistryAsync: registry: string * ?ct: CancellationToken -> Task<LogoutRegistryResponse>

    abstract member GetVersionAsync: ?ct: CancellationToken -> Task<GetVersionResponse>

    abstract member ListNamespacesAsync: ?ct: CancellationToken -> Task<ListNamespacesResponse>

    abstract member RenameContainerAsync:
        id: string * newName: string * ?ct: CancellationToken -> Task<RenameContainerResponse>

    abstract member TopContainerAsync: id: string * ?ct: CancellationToken -> Task<TopContainerResponse>

    abstract member GetContainerStatsAsync: id: string * ?ct: CancellationToken -> Task<GetContainerStatsResponse>

    abstract member ListImagesAsync: ?namespaceName: string * ?ct: CancellationToken -> Task<ListImagesResponse>

    abstract member InspectImageAsync:
        ref: string * ?namespaceName: string * ?ct: CancellationToken -> Task<InspectImageResponse>

    abstract member RemoveImageAsync:
        ref: string * ?namespaceName: string * ?ct: CancellationToken -> Task<RemoveImageResponse>

    abstract member TagImageAsync:
        source: string * target: string * ?namespaceName: string * ?ct: CancellationToken -> Task<TagImageResponse>

/// Contrat des appels gRPC réseaux consommés par les commandes CLI.
type INetworkClient =
    inherit IDisposable

    abstract member CreateAsync:
        name: string *
        ?driver: NetworkDriver *
        ?subnet: string *
        ?gateway: string *
        ?ipRange: string *
        ?options: IDictionary<string, string> *
        ?labels: IDictionary<string, string> *
        ?cniPluginPath: string *
        ?ct: CancellationToken ->
            Task<CreateNetworkResponse>

    abstract member RemoveAsync: id: string * ?force: bool * ?ct: CancellationToken -> Task<RemoveNetworkResponse>

    abstract member InspectAsync: id: string * ?ct: CancellationToken -> Task<InspectNetworkResponse>

    abstract member ListAsync:
        ?filters: IDictionary<string, string> * ?ct: CancellationToken -> Task<ListNetworksResponse>

    abstract member ConnectAsync:
        networkId: string *
        containerId: string *
        ?endpointId: string *
        ?ipv4Address: string *
        ?options: IDictionary<string, string> *
        ?ct: CancellationToken ->
            Task<ConnectContainerResponse>

    abstract member DisconnectAsync:
        networkId: string * containerId: string * ?endpointId: string * ?force: bool * ?ct: CancellationToken ->
            Task<DisconnectContainerResponse>

    abstract member RunCniPluginAsync:
        pluginPath: string *
        command: string *
        containerId: string *
        netnsPath: string *
        ?config: CniConfiguration *
        ?ct: CancellationToken ->
            Task<RunCniPluginResponse>

    abstract member PruneNetworksAsync: ?ct: CancellationToken -> Task<PruneNetworksResponse>

/// Contrat des appels gRPC volumes consommés par les commandes CLI.
type IVolumeClient =
    inherit IDisposable

    abstract member CreateAsync:
        name: string *
        ?driver: StorageDriverType *
        ?driverOpts: IDictionary<string, string> *
        ?labels: IDictionary<string, string> *
        ?ct: CancellationToken ->
            Task<CreateVolumeResponse>

    abstract member RemoveAsync: id: string * ?force: bool * ?ct: CancellationToken -> Task<RemoveVolumeResponse>

    abstract member InspectAsync: id: string * ?ct: CancellationToken -> Task<InspectVolumeResponse>

    abstract member ListAsync:
        ?filters: IDictionary<string, string> * ?ct: CancellationToken -> Task<ListVolumesResponse>

    abstract member MountAsync:
        id: string * targetPath: string * ?options: IDictionary<string, string> * ?ct: CancellationToken ->
            Task<MountVolumeResponse>

    abstract member UnmountAsync:
        id: string * targetPath: string * ?ct: CancellationToken -> Task<UnmountVolumeResponse>

    abstract member PruneVolumesAsync: ?ct: CancellationToken -> Task<PruneVolumesResponse>

/// Fabrique des clients gRPC, injectable dans les commandes pour les tests.
type IDiploClients =

    abstract member CreateContainerClient: unit -> IContainerClient

    abstract member CreateNetworkClient: unit -> INetworkClient

    abstract member CreateVolumeClient: unit -> IVolumeClient
