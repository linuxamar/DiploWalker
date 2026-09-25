namespace DiploWalker.Grpc

open System.ServiceModel
open System.Threading
open System.Threading.Tasks
open DiploWalker.Grpc.Volume

[<ServiceContract>]
type IVolumeService =

    abstract member CreateVolume: request: CreateVolumeRequest * ct: CancellationToken -> Task<CreateVolumeResponse>

    abstract member RemoveVolume: request: RemoveVolumeRequest * ct: CancellationToken -> Task<RemoveVolumeResponse>

    abstract member InspectVolume: request: InspectVolumeRequest * ct: CancellationToken -> Task<InspectVolumeResponse>

    abstract member ListVolumes: request: ListVolumesRequest * ct: CancellationToken -> Task<ListVolumesResponse>

    abstract member MountVolume: request: MountVolumeRequest * ct: CancellationToken -> Task<MountVolumeResponse>

    abstract member UnmountVolume: request: UnmountVolumeRequest * ct: CancellationToken -> Task<UnmountVolumeResponse>

    abstract member PruneVolumes: request: PruneVolumesRequest * ct: CancellationToken -> Task<PruneVolumesResponse>

