namespace Diplo.Grpc

open System
open System.Collections.Generic
open System.ServiceModel
open System.Threading.Tasks
open ProtoBuf.Grpc
open Diplo.Grpc.Container

[<ServiceContract>]
type IContainerService =

    abstract member CreateContainer: request: CreateContainerRequest * ?context: CallContext -> Task<CreateContainerResponse>

    abstract member StartContainer: request: StartContainerRequest * ?context: CallContext -> Task<StartContainerResponse>

    abstract member StopContainer: request: StopContainerRequest * ?context: CallContext -> Task<StopContainerResponse>

    abstract member DeleteContainer: request: DeleteContainerRequest * ?context: CallContext -> Task<DeleteContainerResponse>

    abstract member InspectContainer: request: InspectContainerRequest * ?context: CallContext -> Task<InspectContainerResponse>

    abstract member ListContainers: request: ListContainersRequest * ?context: CallContext -> Task<ListContainersResponse>

    abstract member GetContainerLogs: request: GetContainerLogsRequest * ?context: CallContext -> IAsyncEnumerable<ContainerLogEntry>

    abstract member ExecInContainer: request: ExecInContainerRequest * ?context: CallContext -> IAsyncEnumerable<ExecOutput>

    abstract member PullImage: request: PullImageRequest * ?context: CallContext -> Task<PullImageResponse>

    abstract member GetVersion: request: GetVersionRequest * ?context: CallContext -> Task<GetVersionResponse>

    abstract member ListNamespaces: request: ListNamespacesRequest * ?context: CallContext -> Task<ListNamespacesResponse>

    abstract member RenameContainer: request: RenameContainerRequest * ?context: CallContext -> Task<RenameContainerResponse>

    abstract member TopContainer: request: TopContainerRequest * ?context: CallContext -> Task<TopContainerResponse>

    abstract member GetContainerStats: request: GetContainerStatsRequest * ?context: CallContext -> Task<GetContainerStatsResponse>

    abstract member ListImages: request: ListImagesRequest * ?context: CallContext -> Task<ListImagesResponse>

    abstract member InspectImage: request: InspectImageRequest * ?context: CallContext -> Task<InspectImageResponse>

    abstract member RemoveImage: request: RemoveImageRequest * ?context: CallContext -> Task<RemoveImageResponse>

    abstract member TagImage: request: TagImageRequest * ?context: CallContext -> Task<TagImageResponse>
