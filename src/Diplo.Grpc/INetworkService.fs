namespace Diplo.Grpc

open System.ServiceModel
open System.Threading
open System.Threading.Tasks
open Diplo.Grpc.Network

[<ServiceContract>]
type INetworkService =

    abstract member CreateNetwork: request: CreateNetworkRequest * ct: CancellationToken -> Task<CreateNetworkResponse>

    abstract member RemoveNetwork: request: RemoveNetworkRequest * ct: CancellationToken -> Task<RemoveNetworkResponse>

    abstract member InspectNetwork:
        request: InspectNetworkRequest * ct: CancellationToken -> Task<InspectNetworkResponse>

    abstract member ListNetworks: request: ListNetworksRequest * ct: CancellationToken -> Task<ListNetworksResponse>

    abstract member ConnectContainer:
        request: ConnectContainerRequest * ct: CancellationToken -> Task<ConnectContainerResponse>

    abstract member DisconnectContainer:
        request: DisconnectContainerRequest * ct: CancellationToken -> Task<DisconnectContainerResponse>

    abstract member RunCniPlugin: request: RunCniPluginRequest * ct: CancellationToken -> Task<RunCniPluginResponse>

    abstract member PruneNetworks: request: PruneNetworksRequest * ct: CancellationToken -> Task<PruneNetworksResponse>
