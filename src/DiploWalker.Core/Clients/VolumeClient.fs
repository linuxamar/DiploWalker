namespace DiploWalker.Core.Clients

open System
open System.Collections.Generic
open System.Threading
open DiploWalker.Core
open DiploWalker.Abstractions
open DiploWalker.Core.Connection
open DiploWalker.Grpc
open DiploWalker.Grpc.Volume
open Grpc.Net.Client
open ProtoBuf.Grpc.Client

type VolumeClient(channel: GrpcChannel, ownsChannel: bool) as this =
    inherit GrpcClientBase(channel, ownsChannel)

    do
        if isNull channel then
            nullArg (nameof channel)

        try
            channel.State |> ignore
        with :? ObjectDisposedException ->
            raise (ObjectDisposedException(nameof channel, "Le canal gRPC a été disposé avant son utilisation. Recréez un client via la factory partagée."))

    let client = channel.CreateGrpcService<IVolumeService>()

    new(port: int) =
        let ch = DiploWalkerChannel.forVolume port
        // Canal mutualisé (M1) : non possédé, sa disposition ne ferme pas le canal partagé.
        new VolumeClient(ch, false)

    new() =
        match DiploWalkerConfig.volumeAddress () with
        | Some address -> new VolumeClient(DiploWalkerChannel.forAddress address, false)
        | None -> new VolumeClient(DiploWalkerPorts.Volume)

    member _.CreateAsync
        (
            name: string,
            ?driver: StorageDriverType,
            ?driverOpts: IDictionary<string, string>,
            ?labels: IDictionary<string, string>,
            ?ct: CancellationToken
        ) =
        task {
            if String.IsNullOrWhiteSpace(name) then
                invalidArg (nameof name) "Le nom est requis"

            let d = defaultArg driver StorageDriverType.Local
            let ct = defaultArg ct CancellationToken.None

            let request =
                { Name = name
                  Driver = d
                  DriverOpts = Dictionary<string, string>()
                  Labels = Dictionary<string, string>() }

            driverOpts
            |> Option.iter (fun o ->
                for kv in o do
                    request.DriverOpts.[kv.Key] <- kv.Value)

            labels
            |> Option.iter (fun l ->
                for kv in l do
                    request.Labels.[kv.Key] <- kv.Value)

            let! response = client.CreateVolume(request, ct)
            return response
        }

    member _.RemoveAsync(id: string, ?force: bool, ?ct: CancellationToken) =
        task {
            if String.IsNullOrWhiteSpace(id) then
                invalidArg (nameof id) "L'identifiant est requis"

            let f = defaultArg force false
            let ct = defaultArg ct CancellationToken.None
            let! response = client.RemoveVolume({ Id = id; Force = f }, ct)
            return response
        }

    member _.InspectAsync(id: string, ?ct: CancellationToken) =
        task {
            if String.IsNullOrWhiteSpace(id) then
                invalidArg (nameof id) "L'identifiant est requis"

            let ct = defaultArg ct CancellationToken.None
            let! response = client.InspectVolume({ Id = id }, ct)
            return response
        }

    member _.ListAsync(?filters: IDictionary<string, string>, ?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let request = { Filters = Dictionary<string, string>() }

            filters
            |> Option.iter (fun f ->
                for kv in f do
                    request.Filters.[kv.Key] <- kv.Value)

            let! response = client.ListVolumes(request, ct)
            return response
        }

    member _.MountAsync(id: string, targetPath: string, ?options: IDictionary<string, string>, ?ct: CancellationToken) =
        task {
            if String.IsNullOrWhiteSpace(id) then
                invalidArg (nameof id) "L'identifiant est requis"

            if String.IsNullOrWhiteSpace(targetPath) then
                invalidArg (nameof targetPath) "Le chemin cible est requis"

            let ct = defaultArg ct CancellationToken.None

            let request =
                { Id = id
                  TargetPath = targetPath
                  Options = Dictionary<string, string>() }

            options
            |> Option.iter (fun o ->
                for kv in o do
                    request.Options.[kv.Key] <- kv.Value)

            let! response = client.MountVolume(request, ct)
            return response
        }

    member _.UnmountAsync(id: string, targetPath: string, ?ct: CancellationToken) =
        task {
            if String.IsNullOrWhiteSpace(id) then
                invalidArg (nameof id) "L'identifiant est requis"

            if String.IsNullOrWhiteSpace(targetPath) then
                invalidArg (nameof targetPath) "Le chemin cible est requis"

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

    interface IVolumeClient with
        member _.CreateAsync(name, ?driver, ?driverOpts, ?labels, ?ct) =
            this.CreateAsync(name, ?driver = driver, ?driverOpts = driverOpts, ?labels = labels, ?ct = ct)

        member _.RemoveAsync(id, ?force, ?ct) =
            this.RemoveAsync(id, ?force = force, ?ct = ct)

        member _.InspectAsync(id, ?ct) = this.InspectAsync(id, ?ct = ct)

        member _.ListAsync(?filters, ?ct) =
            this.ListAsync(?filters = filters, ?ct = ct)

        member _.MountAsync(id, targetPath, ?options, ?ct) =
            this.MountAsync(id, targetPath, ?options = options, ?ct = ct)

        member _.UnmountAsync(id, targetPath, ?ct) =
            this.UnmountAsync(id, targetPath, ?ct = ct)

        member _.PruneVolumesAsync(?ct) = this.PruneVolumesAsync(?ct = ct)


