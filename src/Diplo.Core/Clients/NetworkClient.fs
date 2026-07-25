namespace Diplo.Core.Clients

open System
open System.Collections.Generic
open System.Threading
open Diplo.Abstractions
open Diplo.Core.Connection
open Diplo.Grpc.Network
open Grpc.Net.Client

type NetworkClient(channel: GrpcChannel, ownsChannel: bool) =
    inherit GrpcClientBase(channel, ownsChannel)

    let client = NetworkService.NetworkServiceClient(channel)

    new(port: int) =
        let ch = DiploChannel.forNetwork port
        new NetworkClient(ch, true)

    new() = new NetworkClient(5003)

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
            if String.IsNullOrEmpty(name) then invalidArg (nameof name) "Le nom du réseau est requis"
            let d = defaultArg driver NetworkDriver.Bridge
            let s = defaultArg subnet ""
            let g = defaultArg gateway ""
            let r = defaultArg ipRange ""
            let p = defaultArg cniPluginPath ""
            let ct = defaultArg ct CancellationToken.None
            let request = CreateNetworkRequest(Name = name, Driver = d, Subnet = s, Gateway = g, IpRange = r, CniPluginPath = p)
            options |> Option.iter (fun o -> request.Options.Add(o))
            labels |> Option.iter (fun l -> request.Labels.Add(l))
            let! response = client.CreateNetworkAsync(request, cancellationToken = ct).ResponseAsync
            return response
        }

    member _.RemoveAsync(id: string, ?force: bool, ?ct: CancellationToken) =
        task {
            if String.IsNullOrEmpty(id) then invalidArg (nameof id) "L'identifiant du réseau est requis"
            let f = defaultArg force false
            let ct = defaultArg ct CancellationToken.None
            let! response = client.RemoveNetworkAsync(RemoveNetworkRequest(Id = id, Force = f), cancellationToken = ct).ResponseAsync
            return response
        }

    member _.InspectAsync(id: string, ?ct: CancellationToken) =
        task {
            if String.IsNullOrEmpty(id) then invalidArg (nameof id) "L'identifiant du réseau est requis"
            let ct = defaultArg ct CancellationToken.None
            let! response = client.InspectNetworkAsync(InspectNetworkRequest(Id = id), cancellationToken = ct).ResponseAsync
            return response
        }

    member _.ListAsync(?filters: IDictionary<string, string>, ?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let request = ListNetworksRequest()
            filters |> Option.iter (fun f -> request.Filters.Add(f))
            let! response = client.ListNetworksAsync(request, cancellationToken = ct).ResponseAsync
            return response
        }

    member _.ConnectAsync(networkId: string, containerId: string, ?endpointId: string, ?ipv4Address: string, ?options: IDictionary<string, string>, ?ct: CancellationToken) =
        task {
            if String.IsNullOrEmpty(networkId) then invalidArg (nameof networkId) "L'identifiant du réseau est requis"
            if String.IsNullOrEmpty(containerId) then invalidArg (nameof containerId) "L'identifiant du conteneur est requis"
            let eId = defaultArg endpointId ""
            let ip = defaultArg ipv4Address ""
            let ct = defaultArg ct CancellationToken.None
            let request = ConnectContainerRequest(NetworkId = networkId, ContainerId = containerId, EndpointId = eId, Ipv4Address = ip)
            options |> Option.iter (fun o -> request.Options.Add(o))
            let! response = client.ConnectContainerAsync(request, cancellationToken = ct).ResponseAsync
            return response
        }

    member _.DisconnectAsync(networkId: string, containerId: string, ?endpointId: string, ?force: bool, ?ct: CancellationToken) =
        task {
            if String.IsNullOrEmpty(networkId) then invalidArg (nameof networkId) "L'identifiant du réseau est requis"
            if String.IsNullOrEmpty(containerId) then invalidArg (nameof containerId) "L'identifiant du conteneur est requis"
            let eId = defaultArg endpointId ""
            let f = defaultArg force false
            let ct = defaultArg ct CancellationToken.None
            let! response = client.DisconnectContainerAsync(DisconnectContainerRequest(NetworkId = networkId, ContainerId = containerId, EndpointId = eId, Force = f), cancellationToken = ct).ResponseAsync
            return response
        }

    member _.RunCniPluginAsync(pluginPath: string, command: string, containerId: string, netnsPath: string, ?config: CniConfiguration, ?ct: CancellationToken) =
        task {
            if String.IsNullOrEmpty(pluginPath) then invalidArg (nameof pluginPath) "Le chemin du plugin est requis"
            if String.IsNullOrEmpty(command) then invalidArg (nameof command) "La commande est requise"
            if String.IsNullOrEmpty(containerId) then invalidArg (nameof containerId) "L'identifiant du conteneur est requis"
            if String.IsNullOrEmpty(netnsPath) then invalidArg (nameof netnsPath) "Le chemin netns est requis"
            let ct = defaultArg ct CancellationToken.None
            let request = RunCniPluginRequest(PluginPath = pluginPath, Command = command, ContainerId = containerId, NetnsPath = netnsPath)
            config |> Option.iter (fun c -> request.Config <- c)
            let! response = client.RunCniPluginAsync(request, cancellationToken = ct).ResponseAsync
            return response
        }

    member _.PruneNetworksAsync(?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let! response = client.PruneNetworksAsync(PruneNetworksRequest(), cancellationToken = ct).ResponseAsync
            return response
        }


