namespace Diplo.Volume.Drivers

open System
open System.Text.Json
open Grpc.Core
open Diplo.Abstractions
open Diplo.Abstractions.Interfaces

type NfsDriver(dataRoot: string) =

    let store = RemoteVolumeStore(dataRoot, "nfs")

    let extractRemotePath (driverOpts: Map<string, string>) =
        match driverOpts |> Map.tryFind "server", driverOpts |> Map.tryFind "export" with
        | Some server, Some export -> sprintf "%s:/%s" server export
        | _ -> raise (RpcException(Status(StatusCode.InvalidArgument, "Les options 'server' et 'export' sont requises pour le driver NFS")))

    interface IVolumeDriver with
        member _.CreateVolume(name, driverOpts, labels) =
            let remotePath = extractRemotePath driverOpts
            let (id, mountpoint) = store.CreateVolume(name, remotePath, labels, driverOpts)
            (id, mountpoint)

        member _.RemoveVolume(id, _force) =
            store.RemoveVolume(id)

        member _.InspectVolume(id) =
            store.InspectVolume(id)

        member _.ListVolumes(_filters) =
            store.ListVolumes()

        member _.MountVolume(id, targetPath, _options) =
            SecurityValidation.validateId id "L'identifiant du volume"
            SecurityValidation.validateVolumePath targetPath "Le chemin cible"
            match store.InspectVolume(id) with
            | None -> raise (RpcException(Status(StatusCode.NotFound, sprintf "Volume '%s' introuvable" id)))
            | Some info ->
                let mutable v = Unchecked.defaultof<JsonElement>
                let remotePath =
                    if info.TryGetProperty("remotePath", &v) then v.GetString()
                    else raise (RpcException(Status(StatusCode.NotFound, sprintf "Aucun chemin distant pour le volume '%s'" id)))
                ProcessExec.run "mount" [ "-o"; "nolock"; remotePath; targetPath ] (Some 30_000) None |> ignore
                (true, targetPath)

        member _.UnmountVolume(id, targetPath) =
            SecurityValidation.validateId id "L'identifiant du volume"
            SecurityValidation.validateVolumePath targetPath "Le chemin cible"
            try
                ProcessExec.run "umount" [ targetPath ] (Some 30_000) None |> ignore
            with _ ->
                try ProcessExec.run "mount" [ "-u"; targetPath ] (Some 30_000) None |> ignore
                with _ -> ()
            (true, "Démonté")

        member _.PruneVolumes() = store.PruneAll()

        member _.GetVolumeSize(_id) = 0L
