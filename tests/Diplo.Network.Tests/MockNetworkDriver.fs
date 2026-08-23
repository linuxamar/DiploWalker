namespace Diplo.Network.Tests

open Diplo.Grpc.Network
open Diplo.Network.Plugins

type MockNetworkDriver() =

    let mutable networks = Map.empty<string, NetworkDriverInfo>
    let mutable endpoints = Map.empty<string, EndpointInfo>

    interface INetworkDriver with
        member _.DriverType = NetworkDriver.Bridge

        member _.Create(name, subnet, gateway, ipRange, _options, _labels) =
            let id = System.Guid.NewGuid().ToString("N")
            let info = {
                Id = id
                Name = name
                Driver = NetworkDriver.Bridge
                Subnet = subnet
                Gateway = gateway
                Options = Map.empty
                Labels = Map.empty
                CreatedAt = System.DateTime.UtcNow.ToString("o")
            }
            networks <- networks |> Map.add id info
            Ok info

        member _.Remove(id, _force) =
            if networks |> Map.containsKey id then
                networks <- networks |> Map.remove id
                Ok ()
            else Error (sprintf "Réseau '%s' introuvable" id)

        member _.Inspect(id) =
            match networks |> Map.tryFind id with
            | Some info -> Ok info
            | None -> Error (sprintf "Réseau '%s' introuvable" id)

        member _.List() =
            networks |> Map.toList |> List.map snd |> Ok

        member _.Connect(networkId, containerId, endpointId, ipv4Address, _options) =
            if networks |> Map.containsKey networkId |> not then
                Error (sprintf "Réseau '%s' introuvable" networkId)
            else
                let epInfo = {
                    EndpointId = endpointId
                    ContainerId = containerId
                    Ipv4Address = ipv4Address |> Option.defaultValue "172.17.0.2"
                    MacAddress = "02:42:ac:11:00:02"
                    Message = sprintf "Connecté à %s" containerId
                }
                endpoints <- endpoints |> Map.add endpointId epInfo
                Ok epInfo

        member _.Disconnect(networkId, containerId, endpointId, _force) =
            if networks |> Map.containsKey networkId |> not then
                Error (sprintf "Réseau '%s' introuvable" networkId)
            else
                endpoints <- endpoints |> Map.remove endpointId
                Ok ()

        member _.Prune() =
            Ok (networks |> Map.toList |> List.map fst)

    member this.Mock : INetworkDriver = this :> INetworkDriver
    member _.Networks = networks
    member _.Endpoints = endpoints
