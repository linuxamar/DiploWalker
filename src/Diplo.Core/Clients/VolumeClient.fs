namespace Diplo.Core.Clients

open System
open System.Collections.Generic
open System.Threading
open Diplo.Abstractions
open Diplo.Core.Connection
open Diplo.Grpc
open Diplo.Grpc.Volume
open Grpc.Net.Client
open ProtoBuf.Grpc.Client

type VolumeClient(channel: GrpcChannel, ownsChannel: bool) =
    inherit GrpcClientBase(channel, ownsChannel)

    let client = channel.CreateGrpcService<IVolumeService>()

    new(port: int) =
        let ch = DiploChannel.forVolume port
        new VolumeClient(ch, true)

    new() = new VolumeClient(5002)

    member _.CreateAsync(name: string, ?driver: StorageDriverType, ?driverOpts: IDictionary<string, string>, ?labels: IDictionary<string, string>, ?ct: CancellationToken) =
        task {
            if String.IsNullOrEmpty(name) then invalidArg (nameof name) "Le nom est requis"
            let d = defaultArg driver StorageDriverType.Local
            let ct = defaultArg ct CancellationToken.None
            let request = { Name = name; Driver = d; DriverOpts = Dictionary<string, string>(); Labels = Dictionary<string, string>() }
            driverOpts |> Option.iter (fun o -> for kv in o do request.DriverOpts.[kv.Key] <- kv.Value)
            labels |> Option.iter (fun l -> for kv in l do request.Labels.[kv.Key] <- kv.Value)
            let! response = client.CreateVolume(request, ct)
            return response
        }

    member _.RemoveAsync(id: string, ?force: bool, ?ct: CancellationToken) =
        task {
            if String.IsNullOrEmpty(id) then invalidArg (nameof id) "L'identifiant est requis"
            let f = defaultArg force false
            let ct = defaultArg ct CancellationToken.None
            let! response = client.RemoveVolume({ Id = id; Force = f }, ct)
            return response
        }

    member _.InspectAsync(id: string, ?ct: CancellationToken) =
        task {
            if String.IsNullOrEmpty(id) then invalidArg (nameof id) "L'identifiant est requis"
            let ct = defaultArg ct CancellationToken.None
            let! response = client.InspectVolume({ Id = id }, ct)
            return response
        }

    member _.ListAsync(?filters: IDictionary<string, string>, ?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let request = { Filters = Dictionary<string, string>() }
            filters |> Option.iter (fun f -> for kv in f do request.Filters.[kv.Key] <- kv.Value)
            let! response = client.ListVolumes(request, ct)
            return response
        }

    member _.MountAsync(id: string, targetPath: string, ?options: IDictionary<string, string>, ?ct: CancellationToken) =
        task {
            if String.IsNullOrEmpty(id) then invalidArg (nameof id) "L'identifiant est requis"
            if String.IsNullOrEmpty(targetPath) then invalidArg (nameof targetPath) "Le chemin cible est requis"
            let ct = defaultArg ct CancellationToken.None
            let request = { Id = id; TargetPath = targetPath; Options = Dictionary<string, string>() }
            options |> Option.iter (fun o -> for kv in o do request.Options.[kv.Key] <- kv.Value)
            let! response = client.MountVolume(request, ct)
            return response
        }

    member _.UnmountAsync(id: string, targetPath: string, ?ct: CancellationToken) =
        task {
            if String.IsNullOrEmpty(id) then invalidArg (nameof id) "L'identifiant est requis"
            if String.IsNullOrEmpty(targetPath) then invalidArg (nameof targetPath) "Le chemin cible est requis"
            let ct = defaultArg ct CancellationToken.None
            let! response = client.UnmountVolume({ Id = id; TargetPath = targetPath }, ct)
            return response
        }

    member _.PruneVolumesAsync(?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let! response = client.PruneVolumes({ Placeholder = false }, ct)
            return response
        }
