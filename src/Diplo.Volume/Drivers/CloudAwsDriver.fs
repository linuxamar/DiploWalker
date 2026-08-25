namespace Diplo.Volume.Drivers

open System
open Grpc.Core
open Diplo.Abstractions
open Diplo.Abstractions.Interfaces

type CloudAwsDriver(dataRoot: string) =

    let store = RemoteVolumeStore(dataRoot, "aws")

    let buildEfsPath (driverOpts: Map<string, string>) =
        match driverOpts |> Map.tryFind "fsId", driverOpts |> Map.tryFind "region" with
        | Some fsId, Some region -> sprintf "%s.efs.%s.amazonaws.com:/" fsId region
        | _ ->
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        "Les options 'fsId' et 'region' sont requises pour le driver AWS"
                    )
                )
            )

    let mountEfs (remotePath: string) (targetPath: string) (_opts: Map<string, string>) =
        ProcessExec.runUnit
            "mount"
            [ "-o"; "nfsvers=4.1"; remotePath; targetPath ]
            (Some ProcessExec.MountTimeoutMs)
            None

    interface IVolumeDriver with
        member _.CreateVolume(name, driverOpts, labels) =
            let remotePath = buildEfsPath driverOpts
            store.CreateVolume(name, remotePath, labels, driverOpts)

        member _.RemoveVolume(id, _force) = store.RemoveVolume(id)
        member _.InspectVolume(id) = store.InspectVolume(id)
        member _.ListVolumes(_filters) = store.ListVolumes()
        member _.GetVolumeSize(_id) = 0L

        member _.MountVolume(id, targetPath, options) =
            RemoteDriverHelpers.mountVolume store id targetPath options mountEfs

        member _.UnmountVolume(id, targetPath) =
            RemoteDriverHelpers.unmountVolume id targetPath RemoteDriverHelpers.unmountNfsLike

        member _.PruneVolumes() =
            RemoteDriverHelpers.pruneCloudVolumes store
