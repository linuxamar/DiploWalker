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
[<CLIMutable>]
type CreateVolumeRequest =
    { [<ProtoMember(1)>] mutable Name : string
      [<ProtoMember(2)>] mutable Driver : StorageDriverType
      [<ProtoMember(3)>] mutable DriverOpts : System.Collections.Generic.Dictionary<string, string>
      [<ProtoMember(4)>] mutable Labels : System.Collections.Generic.Dictionary<string, string> }
    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.DriverOpts then this.DriverOpts <- System.Collections.Generic.Dictionary<string, string>()
        if isNull this.Labels then this.Labels <- System.Collections.Generic.Dictionary<string, string>()

[<ProtoContract>]
[<CLIMutable>]
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
[<CLIMutable>]
type RemoveVolumeRequest =
    { [<ProtoMember(1)>] mutable Id : string
      [<ProtoMember(2)>] mutable Force : bool }

[<ProtoContract>]
[<CLIMutable>]
type RemoveVolumeResponse =
    { [<ProtoMember(1)>] mutable Success : bool
      [<ProtoMember(2)>] mutable Message : string }

// ═══════════════════════════════════════════════
// InspectVolume
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type InspectVolumeRequest =
    { [<ProtoMember(1)>] mutable Id : string }

[<ProtoContract>]
[<CLIMutable>]
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
    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Labels then this.Labels <- System.Collections.Generic.Dictionary<string, string>()
        if isNull this.DriverOpts then this.DriverOpts <- System.Collections.Generic.Dictionary<string, string>()

// ═══════════════════════════════════════════════
// ListVolumes
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type ListVolumesRequest =
    { [<ProtoMember(1)>] mutable Filters : System.Collections.Generic.Dictionary<string, string> }
    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Filters then this.Filters <- System.Collections.Generic.Dictionary<string, string>()

[<ProtoContract>]
[<CLIMutable>]
type VolumeInfo =
    { [<ProtoMember(1)>] mutable Id : string
      [<ProtoMember(2)>] mutable Name : string
      [<ProtoMember(3)>] mutable Driver : StorageDriverType
      [<ProtoMember(4)>] mutable Mountpoint : string
      [<ProtoMember(5)>] mutable State : MountState
      [<ProtoMember(6)>] mutable Labels : System.Collections.Generic.Dictionary<string, string>
      [<ProtoMember(7)>] mutable SizeBytes : int64 }
    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Labels then this.Labels <- System.Collections.Generic.Dictionary<string, string>()

[<ProtoContract>]
[<CLIMutable>]
type ListVolumesResponse =
    { [<ProtoMember(1)>] mutable Volumes : System.Collections.Generic.List<VolumeInfo> }
    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Volumes then this.Volumes <- System.Collections.Generic.List<VolumeInfo>()

// ═══════════════════════════════════════════════
// MountVolume
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type MountVolumeRequest =
    { [<ProtoMember(1)>] mutable Id : string
      [<ProtoMember(2)>] mutable TargetPath : string
      [<ProtoMember(3)>] mutable Options : System.Collections.Generic.Dictionary<string, string> }
    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Options then this.Options <- System.Collections.Generic.Dictionary<string, string>()

[<ProtoContract>]
[<CLIMutable>]
type MountVolumeResponse =
    { [<ProtoMember(1)>] mutable State : MountState
      [<ProtoMember(2)>] mutable Mountpoint : string
      [<ProtoMember(3)>] mutable Message : string }

// ═══════════════════════════════════════════════
// UnmountVolume
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type UnmountVolumeRequest =
    { [<ProtoMember(1)>] mutable Id : string
      [<ProtoMember(2)>] mutable TargetPath : string }

[<ProtoContract>]
[<CLIMutable>]
type UnmountVolumeResponse =
    { [<ProtoMember(1)>] mutable State : MountState
      [<ProtoMember(2)>] mutable Message : string }

// ═══════════════════════════════════════════════
// PruneVolumes
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type PruneVolumesRequest = { [<ProtoMember(1)>] mutable Placeholder : bool }

[<ProtoContract>]
[<CLIMutable>]
type PruneVolumesResponse =
    { [<ProtoMember(1)>] mutable VolumesDeleted : System.Collections.Generic.List<string>
      [<ProtoMember(2)>] mutable Count : int
      [<ProtoMember(3)>] mutable Message : string }
    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.VolumesDeleted then this.VolumesDeleted <- System.Collections.Generic.List<string>()
