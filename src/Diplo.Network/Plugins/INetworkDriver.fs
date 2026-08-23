namespace Diplo.Network.Plugins

open Diplo.Grpc.Network

type NetworkDriverInfo = {
    Id: string
    Name: string
    Driver: NetworkDriver
    Subnet: string
    Gateway: string
    Options: Map<string, string>
    Labels: Map<string, string>
    CreatedAt: string
}

type EndpointInfo = {
    EndpointId: string
    ContainerId: string
    Ipv4Address: string
    MacAddress: string
    Message: string
}

type INetworkDriver =
    abstract member DriverType: NetworkDriver
    abstract member Create: name: string * subnet: string * gateway: string * ipRange: string * options: Map<string, string> * labels: Map<string, string> -> Result<NetworkDriverInfo, string>
    abstract member Remove: id: string * force: bool -> Result<unit, string>
    abstract member Inspect: id: string -> Result<NetworkDriverInfo, string>
    abstract member List: unit -> Result<NetworkDriverInfo list, string>
    abstract member Connect: networkId: string * containerId: string * endpointId: string * ipv4Address: string option * options: Map<string, string> -> Result<EndpointInfo, string>
    abstract member Disconnect: networkId: string * containerId: string * endpointId: string * force: bool -> Result<unit, string>
    abstract member Prune: unit -> Result<string list, string>
