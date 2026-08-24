namespace Diplo.Volume.Drivers

open System
open Grpc.Core
open Diplo.Abstractions
open Diplo.Abstractions.Interfaces

type CloudGcpDriver(dataRoot: string) =

    let store = RemoteVolumeStore(dataRoot, "gcp")

    let buildFilestorePath (driverOpts: Map<string, string>) =
        match driverOpts |> Map.tryFind "ipAddress", driverOpts |> Map.tryFind "volumeName" with
        | Some ip, Some volName -> sprintf "%s:/%s" ip volName
        | _ -> raise (RpcException(Status(StatusCode.InvalidArgument, "Les options 'ipAddress' et 'volumeName' sont requises pour le driver GCP")))

    let mountNfs (remotePath: string) (targetPath: string) (_opts: Map<string, string>) =
        ProcessExec.runUnit "mount" [ "-o"; "nolock"; remotePath; targetPath ] (Some ProcessExec.MountTimeoutMs) None

    interface IVolumeDriver with
        member _.CreateVolume(name, driverOpts, labels) =
            let remotePath = buildFilestorePath driverOpts
            store.CreateVolume(name, remotePath, labels, driverOpts)

        member _.RemoveVolume(id, _force) = store.RemoveVolume(id)
        member _.InspectVolume(id) = store.InspectVolume(id)
        member _.ListVolumes(_filters) = store.ListVolumes()
        member _.GetVolumeSize(_id) = 0L

        member _.MountVolume(id, targetPath, options) =
            RemoteDriverHelpers.mountVolume store id targetPath options mountNfs

        member _.UnmountVolume(id, targetPath) =
            RemoteDriverHelpers.unmountVolume id targetPath RemoteDriverHelpers.unmountNfsLike

        member _.PruneVolumes() = RemoteDriverHelpers.pruneCloudVolumes store
