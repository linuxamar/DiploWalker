namespace Diplo.Grpc.Volume

open System
open ProtoBuf

// ═══════════════════════════════════════════════
// Enums
// ═══════════════════════════════════════════════

[<ProtoContract>]
type StorageDriverType =
    | [<ProtoEnum>] Local = 0
    | [<ProtoEnum>] Nfs = 1
    | [<ProtoEnum>] Smb = 2
    | [<ProtoEnum>] CloudAzure = 3
    | [<ProtoEnum>] CloudAws = 4
    | [<ProtoEnum>] CloudGcp = 5

[<ProtoContract>]
type MountState =
    | [<ProtoEnum>] Unmounted = 0
    | [<ProtoEnum>] Mounted = 1
    | [<ProtoEnum>] Error = 2

// ═══════════════════════════════════════════════
// CreateVolume
// ═══════════════════════════════════════════════

[<ProtoContract>]
type CreateVolumeRequest =
    { [<ProtoMember(1)>] mutable Name : string
      [<ProtoMember(2)>] mutable Driver : StorageDriverType
      [<ProtoMember(3)>] mutable DriverOpts : System.Collections.Generic.Dictionary<string, string>
      [<ProtoMember(4)>] mutable Labels : System.Collections.Generic.Dictionary<string, string> }

[<ProtoContract>]
type CreateVolumeResponse =
    { [<ProtoMember(1)>] mutable Id : string
      [<ProtoMember(2)>] mutable Name : string
      [<ProtoMember(3)>] mutable Driver : StorageDriverType
      [<ProtoMember(4)>] mutable Mountpoint : string
      [<ProtoMember(5)>] mutable CreatedAt : string }

// ═══════════════════════════════════════════════
// RemoveVolume
// ═══════════════════════════════════════════════

[<ProtoContract>]
type RemoveVolumeRequest =
    { [<ProtoMember(1)>] mutable Id : string
      [<ProtoMember(2)>] mutable Force : bool }

[<ProtoContract>]
type RemoveVolumeResponse =
    { [<ProtoMember(1)>] mutable Success : bool
      [<ProtoMember(2)>] mutable Message : string }

// ═══════════════════════════════════════════════
// InspectVolume
// ═══════════════════════════════════════════════

[<ProtoContract>]
type InspectVolumeRequest =
    { [<ProtoMember(1)>] mutable Id : string }

[<ProtoContract>]
type InspectVolumeResponse =
    { [<ProtoMember(1)>] mutable Id : string
      [<ProtoMember(2)>] mutable Name : string
      [<ProtoMember(3)>] mutable Driver : StorageDriverType
      [<ProtoMember(4)>] mutable Mountpoint : string
      [<ProtoMember(5)>] mutable State : MountState
      [<ProtoMember(6)>] mutable Labels : System.Collections.Generic.Dictionary<string, string>
      [<ProtoMember(7)>] mutable DriverOpts : System.Collections.Generic.Dictionary<string, string>
      [<ProtoMember(8)>] mutable SizeBytes : int64
      [<ProtoMember(9)>] mutable CreatedAt : string }

// ═══════════════════════════════════════════════
// ListVolumes
// ═══════════════════════════════════════════════

[<ProtoContract>]
type ListVolumesRequest =
    { [<ProtoMember(1)>] mutable Filters : System.Collections.Generic.Dictionary<string, string> }

[<ProtoContract>]
type VolumeInfo =
    { [<ProtoMember(1)>] mutable Id : string
      [<ProtoMember(2)>] mutable Name : string
      [<ProtoMember(3)>] mutable Driver : StorageDriverType
      [<ProtoMember(4)>] mutable Mountpoint : string
      [<ProtoMember(5)>] mutable State : MountState
      [<ProtoMember(6)>] mutable Labels : System.Collections.Generic.Dictionary<string, string>
      [<ProtoMember(7)>] mutable SizeBytes : int64 }

[<ProtoContract>]
type ListVolumesResponse =
    { [<ProtoMember(1)>] mutable Volumes : System.Collections.Generic.List<VolumeInfo> }

// ═══════════════════════════════════════════════
// MountVolume
// ═══════════════════════════════════════════════

[<ProtoContract>]
type MountVolumeRequest =
    { [<ProtoMember(1)>] mutable Id : string
      [<ProtoMember(2)>] mutable TargetPath : string
      [<ProtoMember(3)>] mutable Options : System.Collections.Generic.Dictionary<string, string> }

[<ProtoContract>]
type MountVolumeResponse =
    { [<ProtoMember(1)>] mutable State : MountState
      [<ProtoMember(2)>] mutable Mountpoint : string
      [<ProtoMember(3)>] mutable Message : string }

// ═══════════════════════════════════════════════
// UnmountVolume
// ═══════════════════════════════════════════════

[<ProtoContract>]
type UnmountVolumeRequest =
    { [<ProtoMember(1)>] mutable Id : string
      [<ProtoMember(2)>] mutable TargetPath : string }

[<ProtoContract>]
type UnmountVolumeResponse =
    { [<ProtoMember(1)>] mutable State : MountState
      [<ProtoMember(2)>] mutable Message : string }

// ═══════════════════════════════════════════════
// PruneVolumes
// ═══════════════════════════════════════════════

[<ProtoContract>]
type PruneVolumesRequest = { [<ProtoMember(1)>] mutable Placeholder : bool }

[<ProtoContract>]
type PruneVolumesResponse =
    { [<ProtoMember(1)>] mutable VolumesDeleted : System.Collections.Generic.List<string>
      [<ProtoMember(2)>] mutable Count : int
      [<ProtoMember(3)>] mutable Message : string }
