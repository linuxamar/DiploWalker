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
        if info.TryGetProperty("remotePath", &v) then v.GetString()
        else raise (RpcException(Status(StatusCode.NotFound, sprintf "Aucun chemin distant pour le volume '%s'" id)))

    let parseMergedOpts (info: JsonElement) (options: string) =
        let optsMap =
            if String.IsNullOrEmpty(options) then Map.empty
            else
                options.Split(';')
                |> Array.choose (fun part ->
                    let idx = part.IndexOf('=')
                    if idx > 0 then Some(part.Substring(0, idx), part.Substring(idx + 1))
                    else None)
                |> Map.ofSeq
        let mutable ov = Unchecked.defaultof<JsonElement>
        if info.TryGetProperty("driverOpts", &ov) then
            let mutable existing = optsMap
            for prop in ov.EnumerateObject() do
                if not (existing.ContainsKey(prop.Name)) then
                    existing <- existing |> Map.add prop.Name (prop.Value.GetString())
            existing
        else optsMap

    /// Montage partagé : validate → inspect → extract remotePath → call mountFn.
    let mountVolume (store: RemoteVolumeStore) (id: string) (targetPath: string) (options: string) (mountFn: string -> string -> Map<string, string> -> unit) =
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

    /// Démontage NFS-like : umount → fallback mount -u → silencieux.
    let unmountNfsLike (targetPath: string) =
        try ProcessExec.runUnit "umount" [ targetPath ] (Some 30_000) None
        with _ -> try ProcessExec.runUnit "mount" [ "-u"; targetPath ] (Some 30_000) None
                  with _ -> ()

    /// Prune cloud partagé : itère sur le store et supprime tous les volumes.
    let pruneCloudVolumes (store: RemoteVolumeStore) =
        let vols = store.ListVolumes()
        if not (vols.IsEmpty) then
            let removed = ResizeArray<string>()
            for vol in vols do
                let mutable v = Unchecked.defaultof<JsonElement>
                let id = if vol.TryGetProperty("id", &v) then v.GetString() else null
                if not (String.IsNullOrEmpty(id)) then
                    if store.RemoveVolume(id) then
                        removed.Add(id)
            removed |> Seq.toList
        else []
