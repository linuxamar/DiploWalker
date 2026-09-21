namespace DiploWalker.Volume.Drivers

open System
open System.IO
open System.Text.Json
open Grpc.Core
open Serilog
open DiploWalker.Abstractions
open DiploWalker.Abstractions.Interfaces

/// Pilote de volume permettant de monter un fichier ISO9660 ou UDF (DVD) en
/// extrayant son contenu (lecture seule) dans le répertoire cible.
///
/// Façade : la détection (ISO9660/UDF) et l'extraction sont déléguées au
/// parseur mutualisé `DiploWalker.Disk.IsoFs` (regroupement unique dans DiploWalker.Disk).
type IsoDriver(dataRoot: string) =

    let store = RemoteVolumeStore(dataRoot, "iso")

    let validateIso (driverOpts: Map<string, string>) =
        match driverOpts.TryFind("iso") with
        | None ->
            raise (RpcException(Status(StatusCode.InvalidArgument, "L'option 'iso' est requise pour le driver ISO")))
        | Some isoPath when String.IsNullOrWhiteSpace(isoPath) ->
            raise (RpcException(Status(StatusCode.InvalidArgument, "L'option 'iso' est requise pour le driver ISO")))
        | Some isoPath ->
            if not (File.Exists(isoPath)) then
                raise (RpcException(Status(StatusCode.NotFound, sprintf "Le fichier ISO '%s' est introuvable" isoPath)))

            isoPath

    interface IVolumeDriver with

        member _.CreateVolume(name, driverOpts, labels) =
            let isoPath = validateIso driverOpts
            store.CreateVolume(name, isoPath, labels, driverOpts)

        member _.RemoveVolume(id, force) =
            let isoPath =
                match store.InspectVolume(id) with
                | Some info ->
                    let mutable v = Unchecked.defaultof<JsonElement>

                    if info.TryGetProperty("remotePath", &v) then
                        v.GetString()
                    else
                        null
                | None -> null

            store.RemoveVolume(id) |> ignore

            if force && not (String.IsNullOrEmpty(isoPath)) && File.Exists(isoPath) then
                SecurityValidation.validateVolumePath isoPath "Le chemin du fichier ISO"
                File.Delete(isoPath)

            true

        member _.InspectVolume(id) = store.InspectVolume(id)

        member _.ListVolumes(_filters) = store.ListVolumes()

        member _.MountVolume(id, targetPath, _options) =
            SecurityValidation.validateId id "L'identifiant du volume"
            SecurityValidation.validateVolumePath targetPath "Le chemin cible"

            match store.InspectVolume(id) with
            | None -> raise (RpcException(Status(StatusCode.NotFound, sprintf "Volume '%s' introuvable" id)))
            | Some info ->
                let mutable v = Unchecked.defaultof<JsonElement>

                if not (info.TryGetProperty("remotePath", &v)) then
                    raise (
                        RpcException(
                            Status(StatusCode.InvalidArgument, sprintf "Volume '%s' invalide (chemin ISO manquant)" id)
                        )
                    )

                let isoPath = v.GetString()

                if not (File.Exists(isoPath)) then
                    raise (
                        RpcException(Status(StatusCode.NotFound, sprintf "Le fichier ISO '%s' est introuvable" isoPath))
                    )

                match DiploWalker.Disk.IsoFs.tryExtract isoPath targetPath with
                | Some _ -> (true, targetPath)
                | None ->
                    raise (
                        RpcException(
                            Status(StatusCode.Internal, sprintf "Échec de l'extraction du fichier ISO '%s'" isoPath)
                        )
                    )

        member _.UnmountVolume(id, targetPath) =
            SecurityValidation.validateId id "L'identifiant du volume"
            SecurityValidation.validateVolumePath targetPath "Le chemin cible"

            if Directory.Exists(targetPath) then
                Directory.Delete(targetPath, true)

            (true, "Démonté")

        member _.GetVolumeSize(_id) = 0L

        member _.PruneVolumes() =
            let removed =
                store.ListVolumes()
                |> List.choose (fun vol ->
                    let mutable v = Unchecked.defaultof<JsonElement>

                    if vol.TryGetProperty("id", &v) then
                        let id = v.GetString()

                        try
                            store.RemoveVolume(id) |> ignore
                            Some id
                        with ex ->
                            Log.Warning(ex, "Erreur lors du nettoyage du volume ISO {VolumeId}", id)
                            None
                    else
                        None)

            removed

    member this.CreateVolume(name, driverOpts, labels) =
        (this :> IVolumeDriver).CreateVolume(name, driverOpts, labels)

    member this.RemoveVolume(id, force) =
        (this :> IVolumeDriver).RemoveVolume(id, force)

    member this.InspectVolume(id) =
        (this :> IVolumeDriver).InspectVolume(id)

    member this.ListVolumes(filters) =
        (this :> IVolumeDriver).ListVolumes(filters)

    member this.MountVolume(id, targetPath, options) =
        (this :> IVolumeDriver).MountVolume(id, targetPath, options)

    member this.UnmountVolume(id, targetPath) =
        (this :> IVolumeDriver).UnmountVolume(id, targetPath)

    member this.GetVolumeSize(id) =
        (this :> IVolumeDriver).GetVolumeSize(id)

    member this.PruneVolumes() = (this :> IVolumeDriver).PruneVolumes()


