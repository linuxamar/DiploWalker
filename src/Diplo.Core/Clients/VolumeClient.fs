namespace Diplo.Core.Clients

open System
open System.Collections.Generic
open System.Threading
open Diplo.Core.Connection
open Diplo.Grpc.Volume
open Grpc.Net.Client

[<Sealed>]
type VolumeClient(channel: GrpcChannel, ownsChannel: bool) =

    let client = VolumeService.VolumeServiceClient(channel)

    new(port: int) =
        let ch = DiploChannel.forVolume port
        new VolumeClient(ch, true)

    new() = new VolumeClient(5002)

    member _.CreateAsync(name: string, ?driver: StorageDriverType, ?driverOpts: IDictionary<string, string>, ?labels: IDictionary<string, string>, ?ct: CancellationToken) =
        task {
            let d = defaultArg driver StorageDriverType.Local
            let ct = defaultArg ct CancellationToken.None
            let request = CreateVolumeRequest(Name = name, Driver = d)
            driverOpts |> Option.iter (fun o -> request.DriverOpts.Add(o))
            labels |> Option.iter (fun l -> request.Labels.Add(l))
            let! response = client.CreateVolumeAsync(request, cancellationToken = ct).ResponseAsync
            return response
        }

    member _.RemoveAsync(id: string, ?force: bool, ?ct: CancellationToken) =
        task {
            let f = defaultArg force false
            let ct = defaultArg ct CancellationToken.None
            let! response = client.RemoveVolumeAsync(RemoveVolumeRequest(Id = id, Force = f), cancellationToken = ct).ResponseAsync
            return response
        }

    member _.InspectAsync(id: string, ?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let! response = client.InspectVolumeAsync(InspectVolumeRequest(Id = id), cancellationToken = ct).ResponseAsync
            return response
        }

    member _.ListAsync(?filters: IDictionary<string, string>, ?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let request = ListVolumesRequest()
            filters |> Option.iter (fun f -> request.Filters.Add(f))
            let! response = client.ListVolumesAsync(request, cancellationToken = ct).ResponseAsync
            return response
        }

    member _.MountAsync(id: string, targetPath: string, ?options: IDictionary<string, string>, ?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let request = MountVolumeRequest(Id = id, TargetPath = targetPath)
            options |> Option.iter (fun o -> request.Options.Add(o))
            let! response = client.MountVolumeAsync(request, cancellationToken = ct).ResponseAsync
            return response
        }

    member _.UnmountAsync(id: string, targetPath: string, ?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let! response = client.UnmountVolumeAsync(UnmountVolumeRequest(Id = id, TargetPath = targetPath), cancellationToken = ct).ResponseAsync
            return response
        }

    interface IDisposable with
        member _.Dispose() =
            if ownsChannel then channel.Dispose()
