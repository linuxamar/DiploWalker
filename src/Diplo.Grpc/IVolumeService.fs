namespace Diplo.Grpc

open System.ServiceModel
open System.Threading.Tasks
open ProtoBuf.Grpc
open Diplo.Grpc.Volume

[<ServiceContract>]
type IVolumeService =

    abstract member CreateVolume: request: CreateVolumeRequest * ?context: CallContext -> Task<CreateVolumeResponse>

    abstract member RemoveVolume: request: RemoveVolumeRequest * ?context: CallContext -> Task<RemoveVolumeResponse>

    abstract member InspectVolume: request: InspectVolumeRequest * ?context: CallContext -> Task<InspectVolumeResponse>

    abstract member ListVolumes: request: ListVolumesRequest * ?context: CallContext -> Task<ListVolumesResponse>

    abstract member MountVolume: request: MountVolumeRequest * ?context: CallContext -> Task<MountVolumeResponse>

    abstract member UnmountVolume: request: UnmountVolumeRequest * ?context: CallContext -> Task<UnmountVolumeResponse>

    abstract member PruneVolumes: request: PruneVolumesRequest * ?context: CallContext -> Task<PruneVolumesResponse>
