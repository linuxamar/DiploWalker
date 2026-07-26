namespace Diplo.Grpc

open System.ServiceModel
open System.Threading.Tasks
open ProtoBuf.Grpc
open Diplo.Grpc.Network

[<ServiceContract>]
type INetworkService =

    abstract member CreateNetwork: request: CreateNetworkRequest * ?context: CallContext -> Task<CreateNetworkResponse>

    abstract member RemoveNetwork: request: RemoveNetworkRequest * ?context: CallContext -> Task<RemoveNetworkResponse>

    abstract member InspectNetwork: request: InspectNetworkRequest * ?context: CallContext -> Task<InspectNetworkResponse>

    abstract member ListNetworks: request: ListNetworksRequest * ?context: CallContext -> Task<ListNetworksResponse>

    abstract member ConnectContainer: request: ConnectContainerRequest * ?context: CallContext -> Task<ConnectContainerResponse>

    abstract member DisconnectContainer: request: DisconnectContainerRequest * ?context: CallContext -> Task<DisconnectContainerResponse>

    abstract member RunCniPlugin: request: RunCniPluginRequest * ?context: CallContext -> Task<RunCniPluginResponse>

    abstract member PruneNetworks: request: PruneNetworksRequest * ?context: CallContext -> Task<PruneNetworksResponse>
