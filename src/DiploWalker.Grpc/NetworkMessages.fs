namespace DiploWalker.Grpc.Network

open System
open ProtoBuf

// ═══════════════════════════════════════════════
// Enums
// ═══════════════════════════════════════════════

[<ProtoContract>]
type NetworkDriver =
    | [<ProtoEnum>] Bridge = 0
    | [<ProtoEnum>] CustomCni = 1
    | [<ProtoEnum>] ``None`` = 2
    | [<ProtoEnum>] Pod = 3

// Convention protobuf : 0 = valeur neutre/inconnue. Un champ State absent à
// la désérialisation vaut désormais Unknown (et non un état « actif »
// trompeur). Rupture wire assumée : client et serveur sont versionnés ensemble.
[<ProtoContract>]
type EndpointState =
    | [<ProtoEnum>] Unknown = 0
    | [<ProtoEnum>] Active = 1
    | [<ProtoEnum>] Inactive = 2

// ═══════════════════════════════════════════════
// CreateNetwork
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type CreateNetworkRequest =
    { [<ProtoMember(1)>]
      mutable Name: string
      [<ProtoMember(2)>]
      mutable Driver: NetworkDriver
      [<ProtoMember(3)>]
      mutable Subnet: string
      [<ProtoMember(4)>]
      mutable Gateway: string
      [<ProtoMember(5)>]
      mutable IpRange: string
      [<ProtoMember(6)>]
      mutable Options: System.Collections.Generic.Dictionary<string, string>
      [<ProtoMember(7)>]
      mutable Labels: System.Collections.Generic.Dictionary<string, string>
      [<ProtoMember(8)>]
      mutable CniPluginPath: string }

    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Options then
            this.Options <- System.Collections.Generic.Dictionary<string, string>()

        if isNull this.Labels then
            this.Labels <- System.Collections.Generic.Dictionary<string, string>()

[<ProtoContract>]
[<CLIMutable>]
type CreateNetworkResponse =
    { [<ProtoMember(1)>]
      mutable Id: string
      [<ProtoMember(2)>]
      mutable Name: string
      [<ProtoMember(3)>]
      mutable Driver: NetworkDriver
      [<ProtoMember(4)>]
      mutable Subnet: string
      [<ProtoMember(5)>]
      mutable Gateway: string
      [<ProtoMember(6)>]
      mutable CreatedAt: string }

// ═══════════════════════════════════════════════
// RemoveNetwork
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type RemoveNetworkRequest =
    { [<ProtoMember(1)>]
      mutable Id: string
      [<ProtoMember(2)>]
      mutable Force: bool }

[<ProtoContract>]
[<CLIMutable>]
type RemoveNetworkResponse =
    { [<ProtoMember(1)>]
      mutable Success: bool
      [<ProtoMember(2)>]
      mutable Message: string }

// ═══════════════════════════════════════════════
// EndpointInfo
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type EndpointInfo =
    { [<ProtoMember(1)>]
      mutable EndpointId: string
      [<ProtoMember(2)>]
      mutable ContainerId: string
      [<ProtoMember(3)>]
      mutable Ipv4Address: string
      [<ProtoMember(4)>]
      mutable MacAddress: string
      [<ProtoMember(5)>]
      mutable State: EndpointState }

// ═══════════════════════════════════════════════
// InspectNetwork
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type InspectNetworkRequest =
    { [<ProtoMember(1)>]
      mutable Id: string }

[<ProtoContract>]
[<CLIMutable>]
type InspectNetworkResponse =
    { [<ProtoMember(1)>]
      mutable Id: string
      [<ProtoMember(2)>]
      mutable Name: string
      [<ProtoMember(3)>]
      mutable Driver: NetworkDriver
      [<ProtoMember(4)>]
      mutable Subnet: string
      [<ProtoMember(5)>]
      mutable Gateway: string
      [<ProtoMember(6)>]
      mutable IpRange: string
      [<ProtoMember(7)>]
      mutable Options: System.Collections.Generic.Dictionary<string, string>
      [<ProtoMember(8)>]
      mutable Labels: System.Collections.Generic.Dictionary<string, string>
      [<ProtoMember(9)>]
      mutable Endpoints: System.Collections.Generic.List<EndpointInfo>
      [<ProtoMember(10)>]
      mutable CreatedAt: string }

    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Options then
            this.Options <- System.Collections.Generic.Dictionary<string, string>()

        if isNull this.Labels then
            this.Labels <- System.Collections.Generic.Dictionary<string, string>()

        if isNull this.Endpoints then
            this.Endpoints <- System.Collections.Generic.List<EndpointInfo>()

// ═══════════════════════════════════════════════
// ListNetworks
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type ListNetworksRequest =
    { [<ProtoMember(1)>]
      mutable Filters: System.Collections.Generic.Dictionary<string, string> }

    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Filters then
            this.Filters <- System.Collections.Generic.Dictionary<string, string>()

[<ProtoContract>]
[<CLIMutable>]
type NetworkInfo =
    { [<ProtoMember(1)>]
      mutable Id: string
      [<ProtoMember(2)>]
      mutable Name: string
      [<ProtoMember(3)>]
      mutable Driver: NetworkDriver
      [<ProtoMember(4)>]
      mutable Subnet: string
      [<ProtoMember(5)>]
      mutable Gateway: string
      [<ProtoMember(6)>]
      mutable EndpointCount: int
      [<ProtoMember(7)>]
      mutable CreatedAt: string }

[<ProtoContract>]
[<CLIMutable>]
type ListNetworksResponse =
    { [<ProtoMember(1)>]
      mutable Networks: System.Collections.Generic.List<NetworkInfo> }

    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Networks then
            this.Networks <- System.Collections.Generic.List<NetworkInfo>()

// ═══════════════════════════════════════════════
// ConnectContainer
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type ConnectContainerRequest =
    { [<ProtoMember(1)>]
      mutable NetworkId: string
      [<ProtoMember(2)>]
      mutable ContainerId: string
      [<ProtoMember(3)>]
      mutable EndpointId: string
      [<ProtoMember(4)>]
      mutable Ipv4Address: string
      [<ProtoMember(5)>]
      mutable Options: System.Collections.Generic.Dictionary<string, string> }

    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Options then
            this.Options <- System.Collections.Generic.Dictionary<string, string>()

[<ProtoContract>]
[<CLIMutable>]
type ConnectContainerResponse =
    { [<ProtoMember(1)>]
      mutable EndpointId: string
      [<ProtoMember(2)>]
      mutable Ipv4Address: string
      [<ProtoMember(3)>]
      mutable MacAddress: string
      [<ProtoMember(4)>]
      mutable Message: string }

// ═══════════════════════════════════════════════
// DisconnectContainer
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type DisconnectContainerRequest =
    { [<ProtoMember(1)>]
      mutable NetworkId: string
      [<ProtoMember(2)>]
      mutable ContainerId: string
      [<ProtoMember(3)>]
      mutable EndpointId: string
      [<ProtoMember(4)>]
      mutable Force: bool }

[<ProtoContract>]
[<CLIMutable>]
type DisconnectContainerResponse =
    { [<ProtoMember(1)>]
      mutable Success: bool
      [<ProtoMember(2)>]
      mutable Message: string }

// ═══════════════════════════════════════════════
// RunCniPlugin
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type CniConfiguration =
    { [<ProtoMember(1)>]
      mutable Name: string
      [<ProtoMember(2)>]
      mutable Type: string
      [<ProtoMember(3)>]
      mutable Subnet: string
      [<ProtoMember(4)>]
      mutable Gateway: string
      [<ProtoMember(5)>]
      mutable IpRange: string
      [<ProtoMember(6)>]
      mutable HairpinMode: bool
      [<ProtoMember(7)>]
      mutable IsDefaultGateway: bool
      [<ProtoMember(8)>]
      mutable Dns: System.Collections.Generic.Dictionary<string, string> }

    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.Dns then
            this.Dns <- System.Collections.Generic.Dictionary<string, string>()

[<ProtoContract>]
[<CLIMutable>]
type RunCniPluginRequest =
    { [<ProtoMember(1)>]
      mutable PluginPath: string
      [<ProtoMember(2)>]
      mutable Command: string
      [<ProtoMember(3)>]
      mutable ContainerId: string
      [<ProtoMember(4)>]
      mutable NetnsPath: string
      [<ProtoMember(5)>]
      mutable Config: CniConfiguration }

[<ProtoContract>]
[<CLIMutable>]
type RunCniPluginResponse =
    { [<ProtoMember(1)>]
      mutable Success: bool
      [<ProtoMember(2)>]
      mutable Ifname: string
      [<ProtoMember(3)>]
      mutable Ipv4Address: string
      [<ProtoMember(4)>]
      mutable Gateway: string
      [<ProtoMember(5)>]
      mutable Message: string }

// ═══════════════════════════════════════════════
// PruneNetworks
// ═══════════════════════════════════════════════

[<ProtoContract>]
[<CLIMutable>]
type PruneNetworksRequest =
    { [<ProtoMember(1)>]
      mutable Placeholder: bool }

[<ProtoContract>]
[<CLIMutable>]
type PruneNetworksResponse =
    { [<ProtoMember(1)>]
      mutable NetworksDeleted: System.Collections.Generic.List<string>
      [<ProtoMember(2)>]
      mutable Count: int
      [<ProtoMember(3)>]
      mutable Message: string }

    [<ProtoAfterDeserialization>]
    member this.EnsureCollections() =
        if isNull this.NetworksDeleted then
            this.NetworksDeleted <- System.Collections.Generic.List<string>()

