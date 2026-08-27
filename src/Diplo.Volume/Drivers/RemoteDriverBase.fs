namespace Diplo.Volume.Drivers

open System
open System.Text.Json
open Grpc.Core
open Diplo.Abstractions
open Diplo.Abstractions.Interfaces

/// Helpers partagés pour les drivers de volume distants.
module RemoteDriverHelpers =

    let extractRemotePathFromInfo (info: JsonElement) (id: string) =
        let mutable v = Unchecked.defaultof<JsonElement>

        if info.TryGetProperty("remotePath", &v) then
            v.GetString()
        else
            raise (RpcException(Status(StatusCode.NotFound, sprintf "Aucun chemin distant pour le volume '%s'" id)))

    let parseMergedOpts (info: JsonElement) (options: string) =
        let optsMap =
            if String.IsNullOrEmpty(options) then
                Map.empty
            else
                options.Split(';')
                |> Array.choose (fun part ->
                    let idx = part.IndexOf('=')

                    if idx > 0 then
                        Some(part.Substring(0, idx), part.Substring(idx + 1))
                    else
                        None)
                |> Map.ofSeq

        let mutable ov = Unchecked.defaultof<JsonElement>

        if info.TryGetProperty("driverOpts", &ov) then
            let mutable existing = optsMap

            for prop in ov.EnumerateObject() do
                if not (existing.ContainsKey(prop.Name)) then
                    existing <- existing |> Map.add prop.Name (prop.Value.GetString())

            existing
        else
            optsMap

    /// Montage partagé : validate → inspect → extract remotePath → call mountFn.
    let mountVolume
        (store: RemoteVolumeStore)
        (id: string)
        (targetPath: string)
        (options: string)
        (mountFn: string -> string -> Map<string, string> -> unit)
        =
        SecurityValidation.validateId id "L'identifiant du volume"
        SecurityValidation.validateVolumePath targetPath "Le chemin cible"

        match store.InspectVolume(id) with
        | None -> raise (RpcException(Status(StatusCode.NotFound, sprintf "Volume '%s' introuvable" id)))
        | Some info ->
            let remotePath = extractRemotePathFromInfo info id
            let mergedOpts = parseMergedOpts info options
            mountFn remotePath targetPath mergedOpts
            (true, targetPath)

    /// Démontage partagé : validate → call unmountFn.
    let unmountVolume (id: string) (targetPath: string) (unmountFn: string -> unit) =
        SecurityValidation.validateId id "L'identifiant du volume"
        SecurityValidation.validateVolumePath targetPath "Le chemin cible"
        unmountFn targetPath
        (true, "Démonté")

    /// Démontage NFS-like : umount → fallback mount -u → error.
    let unmountNfsLike (targetPath: string) =
        try
            ProcessExec.runUnit "umount" [ targetPath ] (Some ProcessExec.MountTimeoutMs) None
        with _ ->
            try
                ProcessExec.runUnit "mount" [ "-u"; targetPath ] (Some ProcessExec.MountTimeoutMs) None
            with ex ->
                raise (
                    RpcException(
                        Status(StatusCode.Internal, sprintf "Impossible de démonter '%s': %s" targetPath ex.Message)
                    )
                )

/// Base commune aux drivers de volume distants. Mutualise le store, la
/// création d'un volume (construction du chemin distant), le montage/démontage
/// et la suppression de masse — le reste (chemin, monteur) reste spécifique.
[<AbstractClass>]
type RemoteVolumeDriver(dataRoot: string, driverName: string) as this =

    let store = RemoteVolumeStore(dataRoot, driverName)

    /// Construit le chemin distant (partage / export / maquette) à partir des
    /// options du driver. Lève RpcException InvalidArgument si un champ manque.
    abstract RemotePath: Map<string, string> -> string

    /// Monte la ressource distante `remotePath` sur `targetPath`.
    abstract Mount: string -> string -> Map<string, string> -> unit

    /// Démonte la ressource montée sur `targetPath`. Défaut : démontage de
    /// type NFS (umount → fallback mount -u) ; Azure/SMB surchargent.
    abstract Unmount: string -> unit
    default this.Unmount targetPath = RemoteDriverHelpers.unmountNfsLike targetPath

    interface IVolumeDriver with
        member _.CreateVolume(name, driverOpts, labels) =
            let remotePath = this.RemotePath driverOpts
            store.CreateVolume(name, remotePath, labels, driverOpts)

        member _.RemoveVolume(id, _force) = store.RemoveVolume(id)
        member _.InspectVolume(id) = store.InspectVolume(id)
        member _.ListVolumes(_filters) = store.ListVolumes()
        member _.GetVolumeSize(_id) = 0L

        member _.MountVolume(id, targetPath, options) =
            RemoteDriverHelpers.mountVolume store id targetPath options this.Mount

        member _.UnmountVolume(id, targetPath) =
            RemoteDriverHelpers.unmountVolume id targetPath this.Unmount

        member _.PruneVolumes() = store.PruneAll()
