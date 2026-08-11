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

    abstract member PauseContainer: request: PauseContainerRequest * ct: CancellationToken -> Task<PauseContainerResponse>

    abstract member UnpauseContainer: request: UnpauseContainerRequest * ct: CancellationToken -> Task<UnpauseContainerResponse>

    abstract member WaitContainer: request: WaitContainerRequest * ct: CancellationToken -> Task<WaitContainerResponse>

    abstract member UpdateContainer: request: UpdateContainerRequest * ct: CancellationToken -> Task<UpdateContainerResponse>

    abstract member PruneContainers: request: PruneContainersRequest * ct: CancellationToken -> Task<PruneContainersResponse>

    abstract member PruneImages: request: PruneImagesRequest * ct: CancellationToken -> Task<PruneImagesResponse>

    abstract member GetContainerStatsStream: request: GetContainerStatsStreamRequest * ct: CancellationToken -> IAsyncEnumerable<GetContainerStatsResponse>

    abstract member WatchEvents: request: WatchEventsRequest * ct: CancellationToken -> IAsyncEnumerable<ContainerEvent>

    abstract member ExecContainerStream: request: IAsyncEnumerable<ExecMessage> * ct: CancellationToken -> IAsyncEnumerable<ExecOutput>

    abstract member ReadFile: request: ReadFileRequest * ct: CancellationToken -> Task<ReadFileResponse>

    abstract member WriteFile: request: WriteFileRequest * ct: CancellationToken -> Task<WriteFileResponse>

    abstract member CommitImage: request: CommitImageRequest * ct: CancellationToken -> Task<CommitImageResponse>

    abstract member ExportImage: request: ExportImageRequest * ct: CancellationToken -> IAsyncEnumerable<ImageChunk>

    abstract member ImportImage: request: IAsyncEnumerable<ImageChunk> * ct: CancellationToken -> Task<ImportImageResponse>

    abstract member LoginRegistry: request: LoginRegistryRequest * ct: CancellationToken -> Task<LoginRegistryResponse>

    abstract member LogoutRegistry: request: LogoutRegistryRequest * ct: CancellationToken -> Task<LogoutRegistryResponse>

    abstract member CreateNamespace: request: CreateNamespaceRequest * ct: CancellationToken -> Task<CreateNamespaceResponse>

    abstract member DeleteNamespace: request: DeleteNamespaceRequest * ct: CancellationToken -> Task<DeleteNamespaceResponse>
