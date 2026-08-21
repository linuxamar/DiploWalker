namespace Diplo.Volume.Drivers

open System
open Diplo.Abstractions
open Diplo.Abstractions.Interfaces

type NfsDriver(dataRoot: string) =

    let store = RemoteVolumeStore(dataRoot, "nfs")

    let extractRemotePath (driverOpts: Map<string, string>) =
        match driverOpts |> Map.tryFind "server", driverOpts |> Map.tryFind "export" with
        | Some server, Some export -> sprintf "%s:/%s" server export
        | _ -> failwith "Les options 'server' et 'export' sont requises pour le driver NFS"

    let mountNfs (remotePath: string) (targetPath: string) (_opts: Map<string, string>) =
        ProcessExec.run "mount" [ "-o"; "nolock"; remotePath; targetPath ] (Some 30_000) None |> ignore

    let unmountNfs (targetPath: string) =
        try
            ProcessExec.run "umount" [ targetPath ] (Some 30_000) None |> ignore
        with _ ->
            try ProcessExec.run "mount" [ "-u"; targetPath ] (Some 30_000) None |> ignore
            with _ -> ()

    interface IVolumeDriver with
        member _.CreateVolume(name, driverOpts, labels) =
            let remotePath = extractRemotePath driverOpts
            store.CreateVolume(name, remotePath, labels, driverOpts)

        member _.RemoveVolume(id, _force) = store.RemoveVolume(id)
        member _.InspectVolume(id) = store.InspectVolume(id)
        member _.ListVolumes(_filters) = store.ListVolumes()
        member _.GetVolumeSize(_id) = 0L

        member _.MountVolume(id, targetPath, options) =
            RemoteDriverHelpers.mountVolume store id targetPath options mountNfs

        member _.UnmountVolume(id, targetPath) =
            RemoteDriverHelpers.unmountVolume id targetPath unmountNfs

        member _.PruneVolumes() = store.PruneAll()
