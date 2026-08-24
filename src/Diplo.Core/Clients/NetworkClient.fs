namespace Diplo.Core.Clients

open System
open System.Collections.Generic
open System.Threading
open Diplo.Core
open Diplo.Abstractions
open Diplo.Core.Connection
open Diplo.Grpc
open Diplo.Grpc.Network
open Grpc.Net.Client
open ProtoBuf.Grpc.Client

type NetworkClient(channel: GrpcChannel, ownsChannel: bool) as this =
    inherit GrpcClientBase(channel, ownsChannel)

    let client = channel.CreateGrpcService<INetworkService>()

    new(port: int) =
        let ch = DiploChannel.forNetwork port
        new NetworkClient(ch, true)

    new() =
        match DiploConfig.networkAddress() with
        | Some address -> new NetworkClient(DiploChannel.forAddress address, true)
        | None -> new NetworkClient(5003)

    member _.CreateAsync
        ( name: string,
          ?driver: NetworkDriver,
          ?subnet: string,
          ?gateway: string,
          ?ipRange: string,
          ?options: IDictionary<string, string>,
          ?labels: IDictionary<string, string>,
          ?cniPluginPath: string,
          ?ct: CancellationToken ) =
        task {
            if String.IsNullOrWhiteSpace(name) then invalidArg (nameof name) "Le nom du réseau est requis"
            let d = defaultArg driver NetworkDriver.Bridge
            let s = defaultArg subnet ""
            let g = defaultArg gateway ""
            let r = defaultArg ipRange ""
            let p = defaultArg cniPluginPath ""
            let ct = defaultArg ct CancellationToken.None
            let request = { Name = name; Driver = d; Subnet = s; Gateway = g; IpRange = r; Options = Dictionary<string, string>(); Labels = Dictionary<string, string>(); CniPluginPath = p }
            options |> Option.iter (fun o -> for kv in o do request.Options.[kv.Key] <- kv.Value)
            labels |> Option.iter (fun l -> for kv in l do request.Labels.[kv.Key] <- kv.Value)
            let! response = client.CreateNetwork(request, ct)
            return response
        }

    member _.RemoveAsync(id: string, ?force: bool, ?ct: CancellationToken) =
        task {
            if String.IsNullOrWhiteSpace(id) then invalidArg (nameof id) "L'identifiant du réseau est requis"
            let f = defaultArg force false
            let ct = defaultArg ct CancellationToken.None
            let! response = client.RemoveNetwork({ Id = id; Force = f }, ct)
            return response
        }

    member _.InspectAsync(id: string, ?ct: CancellationToken) =
        task {
            if String.IsNullOrWhiteSpace(id) then invalidArg (nameof id) "L'identifiant du réseau est requis"
            let ct = defaultArg ct CancellationToken.None
            let! response = client.InspectNetwork({ Id = id }, ct)
            return response
        }

    member _.ListAsync(?filters: IDictionary<string, string>, ?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let request = { Filters = Dictionary<string, string>() }
            filters |> Option.iter (fun f -> for kv in f do request.Filters.[kv.Key] <- kv.Value)
            let! response = client.ListNetworks(request, ct)
            return response
        }

    member _.ConnectAsync(networkId: string, containerId: string, ?endpointId: string, ?ipv4Address: string, ?options: IDictionary<string, string>, ?ct: CancellationToken) =
        task {
            if String.IsNullOrWhiteSpace(networkId) then invalidArg (nameof networkId) "L'identifiant du réseau est requis"
            if String.IsNullOrWhiteSpace(containerId) then invalidArg (nameof containerId) ServiceGuards.ContainerIdRequired
            let eId = defaultArg endpointId ""
            let ip = defaultArg ipv4Address ""
            let ct = defaultArg ct CancellationToken.None
            let request = { NetworkId = networkId; ContainerId = containerId; EndpointId = eId; Ipv4Address = ip; Options = Dictionary<string, string>() }
            options |> Option.iter (fun o -> for kv in o do request.Options.[kv.Key] <- kv.Value)
            let! response = client.ConnectContainer(request, ct)
            return response
        }

    member _.DisconnectAsync(networkId: string, containerId: string, ?endpointId: string, ?force: bool, ?ct: CancellationToken) =
        task {
            if String.IsNullOrWhiteSpace(networkId) then invalidArg (nameof networkId) "L'identifiant du réseau est requis"
            if String.IsNullOrWhiteSpace(containerId) then invalidArg (nameof containerId) ServiceGuards.ContainerIdRequired
            let eId = defaultArg endpointId ""
            let f = defaultArg force false
            let ct = defaultArg ct CancellationToken.None
            let! response = client.DisconnectContainer({ NetworkId = networkId; ContainerId = containerId; EndpointId = eId; Force = f }, ct)
            return response
        }

    member _.RunCniPluginAsync(pluginPath: string, command: string, containerId: string, netnsPath: string, ?config: CniConfiguration, ?ct: CancellationToken) =
        task {
            if String.IsNullOrWhiteSpace(pluginPath) then invalidArg (nameof pluginPath) "Le chemin du plugin est requis"
            if String.IsNullOrWhiteSpace(command) then invalidArg (nameof command) "La commande est requise"
            if String.IsNullOrWhiteSpace(containerId) then invalidArg (nameof containerId) ServiceGuards.ContainerIdRequired
            if String.IsNullOrWhiteSpace(netnsPath) then invalidArg (nameof netnsPath) "Le chemin netns est requis"
            let ct = defaultArg ct CancellationToken.None
            let request = { PluginPath = pluginPath; Command = command; ContainerId = containerId; NetnsPath = netnsPath; Config = Unchecked.defaultof<CniConfiguration> }
            config |> Option.iter (fun c -> request.Config <- c)
            let! response = client.RunCniPlugin(request, ct)
            return response
        }

    member _.PruneNetworksAsync(?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let! response = client.PruneNetworks({ Placeholder = false }, ct)
            return response
        }

    interface INetworkClient with
        member _.CreateAsync(name, ?driver, ?subnet, ?gateway, ?ipRange, ?options, ?labels, ?cniPluginPath, ?ct) =
            this.CreateAsync(name, ?driver = driver, ?subnet = subnet, ?gateway = gateway, ?ipRange = ipRange, ?options = options, ?labels = labels, ?cniPluginPath = cniPluginPath, ?ct = ct)

        member _.RemoveAsync(id, ?force, ?ct) =
            this.RemoveAsync(id, ?force = force, ?ct = ct)

        member _.InspectAsync(id, ?ct) =
            this.InspectAsync(id, ?ct = ct)

        member _.ListAsync(?filters, ?ct) =
            this.ListAsync(?filters = filters, ?ct = ct)

        member _.ConnectAsync(networkId, containerId, ?endpointId, ?ipv4Address, ?options, ?ct) =
            this.ConnectAsync(networkId, containerId, ?endpointId = endpointId, ?ipv4Address = ipv4Address, ?options = options, ?ct = ct)

        member _.DisconnectAsync(networkId, containerId, ?endpointId, ?force, ?ct) =
            this.DisconnectAsync(networkId, containerId, ?endpointId = endpointId, ?force = force, ?ct = ct)

        member _.RunCniPluginAsync(pluginPath, command, containerId, netnsPath, ?config, ?ct) =
            this.RunCniPluginAsync(pluginPath, command, containerId, netnsPath, ?config = config, ?ct = ct)

        member _.PruneNetworksAsync(?ct) =
            this.PruneNetworksAsync(?ct = ct)
