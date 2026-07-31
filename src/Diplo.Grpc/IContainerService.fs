namespace Diplo.Grpc

open System
open System.Collections.Generic
open System.ServiceModel
open System.Threading
open System.Threading.Tasks
open ProtoBuf.Grpc
open Diplo.Grpc.Container

[<ServiceContract>]
type IContainerService =

    abstract member CreateContainer: request: CreateContainerRequest * ct: CancellationToken -> Task<CreateContainerResponse>

    abstract member StartContainer: request: StartContainerRequest * ct: CancellationToken -> Task<StartContainerResponse>

    abstract member StopContainer: request: StopContainerRequest * ct: CancellationToken -> Task<StopContainerResponse>

    abstract member DeleteContainer: request: DeleteContainerRequest * ct: CancellationToken -> Task<DeleteContainerResponse>

    abstract member InspectContainer: request: InspectContainerRequest * ct: CancellationToken -> Task<InspectContainerResponse>

    abstract member ListContainers: request: ListContainersRequest * ct: CancellationToken -> Task<ListContainersResponse>

    abstract member GetContainerLogs: request: GetContainerLogsRequest * ct: CancellationToken -> IAsyncEnumerable<ContainerLogEntry>

    abstract member ExecInContainer: request: ExecInContainerRequest * ct: CancellationToken -> IAsyncEnumerable<ExecOutput>

    abstract member PullImage: request: PullImageRequest * ct: CancellationToken -> Task<PullImageResponse>

    abstract member GetVersion: request: GetVersionRequest * ct: CancellationToken -> Task<GetVersionResponse>

    abstract member ListNamespaces: request: ListNamespacesRequest * ct: CancellationToken -> Task<ListNamespacesResponse>

    abstract member RenameContainer: request: RenameContainerRequest * ct: CancellationToken -> Task<RenameContainerResponse>

    abstract member TopContainer: request: TopContainerRequest * ct: CancellationToken -> Task<TopContainerResponse>

    abstract member GetContainerStats: request: GetContainerStatsRequest * ct: CancellationToken -> Task<GetContainerStatsResponse>

    abstract member ListImages: request: ListImagesRequest * ct: CancellationToken -> Task<ListImagesResponse>

    abstract member InspectImage: request: InspectImageRequest * ct: CancellationToken -> Task<InspectImageResponse>

    abstract member RemoveImage: request: RemoveImageRequest * ct: CancellationToken -> Task<RemoveImageResponse>

    abstract member TagImage: request: TagImageRequest * ct: CancellationToken -> Task<TagImageResponse>
