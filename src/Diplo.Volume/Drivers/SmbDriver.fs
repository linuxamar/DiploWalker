namespace Diplo.Volume.Drivers

open System
open System.Text.Json
open Diplo.Abstractions
open Diplo.Abstractions.Interfaces

type SmbDriver(dataRoot: string) =

    let store = RemoteVolumeStore(dataRoot, "smb")

    let extractSharePath (driverOpts: Map<string, string>) =
        match driverOpts |> Map.tryFind "server", driverOpts |> Map.tryFind "share" with
        | Some server, Some share -> sprintf "\\\\%s\\%s" server share
        | _ -> failwith "Les options 'server' et 'share' sont requises pour le driver SMB"

    /// Monte le partage : l'identifiant est enregistré dans le gestionnaire
    /// d'identifiants Windows (cmdkey) puis retiré, afin que le mot de passe
    /// n'apparaisse jamais sur la ligne de commande de `net use` (visible dans
    /// le gestionnaire de tâches et les journaux d'audit).
    let mountShare (remotePath: string) (targetPath: string) (user: string option) (password: string option) =
        match user, password with
        | Some user, Some password ->
            let server = remotePath.TrimStart('\\') |> fun p -> p.Split('\\').[0]
            try
                ProcessExec.run "cmdkey" [ "/add:" + server; "/user:" + user; "/pass:" + password ] (Some 30_000) None |> ignore
                try
                    ProcessExec.run "net" [ "use"; targetPath; remotePath; "/user:" + user; "/persistent:no" ] (Some 30_000) None |> ignore
                finally
                    try ProcessExec.run "cmdkey" [ "/delete:" + server ] (Some 30_000) None |> ignore
                    with _ -> ()
            with _ -> reraise ()
        | Some user, None ->
            ProcessExec.run "net" [ "use"; targetPath; remotePath; "/user:" + user; "/persistent:no" ] (Some 30_000) None |> ignore
        | None, _ ->
            ProcessExec.run "net" [ "use"; targetPath; remotePath; "/persistent:no" ] (Some 30_000) None |> ignore

    interface IVolumeDriver with
        member _.CreateVolume(name, driverOpts, labels) =
            let sharePath = extractSharePath driverOpts
            let (id, mountpoint) = store.CreateVolume(name, sharePath, labels, driverOpts)
            (id, mountpoint)

        member _.RemoveVolume(id, _force) =
            store.RemoveVolume(id)

        member _.InspectVolume(id) =
            store.InspectVolume(id)

        member _.ListVolumes(_filters) =
            store.ListVolumes()

        member _.MountVolume(id, targetPath, options) =
            SecurityValidation.validateId id "L'identifiant du volume"
            SecurityValidation.validateVolumePath targetPath "Le chemin cible"
            match store.InspectVolume(id) with
            | None -> failwithf "Volume %s introuvable" id
            | Some info ->
                let mutable v = Unchecked.defaultof<JsonElement>
                let remotePath =
                    if info.TryGetProperty("remotePath", &v) then v.GetString()
                    else failwithf "Aucun chemin distant pour le volume %s" id
                let optsMap =
                    if String.IsNullOrEmpty(options) then Map.empty
                    else
                        options.Split(';')
                        |> Array.choose (fun part ->
                            let idx = part.IndexOf('=')
                            if idx > 0 then Some(part.Substring(0, idx), part.Substring(idx + 1))
                            else None)
                        |> Map.ofSeq
                let mergedOpts =
                    let mutable ov = Unchecked.defaultof<JsonElement>
                    if info.TryGetProperty("driverOpts", &ov) then
                        let mutable existing = optsMap
                        for prop in ov.EnumerateObject() do
                            if not (existing.ContainsKey(prop.Name)) then
                                existing <- existing |> Map.add prop.Name (prop.Value.GetString())
                        existing
                    else optsMap
                mountShare remotePath targetPath
                    (mergedOpts |> Map.tryFind "username")
                    (mergedOpts |> Map.tryFind "password")
                (true, targetPath)

        member _.UnmountVolume(id, targetPath) =
            SecurityValidation.validateId id "L'identifiant du volume"
            SecurityValidation.validateVolumePath targetPath "Le chemin cible"
            ProcessExec.run "net" [ "use"; targetPath; "/delete"; "/y" ] (Some 30_000) None |> ignore
            (true, "Démonté")

        member _.PruneVolumes() =
            if not (store.ListVolumes().IsEmpty) then
                let removed = ResizeArray<string>()
                for vol in store.ListVolumes() do
                    let mutable v = Unchecked.defaultof<JsonElement>
                    let id = if vol.TryGetProperty("id", &v) then v.GetString() else null
                    if not (String.IsNullOrEmpty(id)) then
                        store.RemoveVolume(id) |> ignore
                        removed.Add(id)
                removed |> Seq.toList
            else []

        member _.GetVolumeSize(_id) = 0L
