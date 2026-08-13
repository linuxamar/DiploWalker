namespace Diplo.Volume.Drivers

open System
open System.Text.Json
open Diplo.Abstractions
open Diplo.Abstractions.Interfaces

type CloudAzureDriver(dataRoot: string) =

    let store = RemoteVolumeStore(dataRoot, "azure")

    let buildAzureSharePath (driverOpts: Map<string, string>) =
        match driverOpts |> Map.tryFind "storageAccount", driverOpts |> Map.tryFind "shareName" with
        | Some account, Some share ->
            sprintf "\\\\%s.file.core.windows.net\\%s" account share
        | _ -> failwith "Les options 'storageAccount' et 'shareName' sont requises pour le driver Azure"

    let buildNetUseArgs (sharePath: string) (targetPath: string) (driverOpts: Map<string, string>) =
        let args = ResizeArray<string>()
        args.Add(targetPath)
        args.Add(sharePath)
        match driverOpts |> Map.tryFind "storageAccount", driverOpts |> Map.tryFind "storageKey" with
        | Some account, Some key ->
            args.Add("/user:AZURE\\" + account)
            args.Add(key)
        | _ -> failwith "Les options 'storageAccount' et 'storageKey' sont requises pour le montage Azure"
        args.Add("/persistent:no")
        args |> Seq.toList

    interface IVolumeDriver with
        member _.CreateVolume(name, driverOpts, labels) =
            let sharePath = buildAzureSharePath driverOpts
            let (id, mountpoint) = store.CreateVolume(name, sharePath, labels, driverOpts)
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
            | None -> failwithf "Volume %s introuvable" id
            | Some info ->
                let mutable v = Unchecked.defaultof<JsonElement>
                let sharePath =
                    if info.TryGetProperty("remotePath", &v) then v.GetString()
                    else failwithf "Aucun chemin distant pour le volume %s" id
                let mutable ov = Unchecked.defaultof<JsonElement>
                let opts =
                    if info.TryGetProperty("driverOpts", &ov) then
                        [ for prop in ov.EnumerateObject() -> prop.Name, prop.Value.GetString() ] |> Map.ofSeq
                    else Map.empty
                let args = buildNetUseArgs sharePath targetPath opts
                ProcessExec.run "net" ("use" :: args) (Some 30_000) None |> ignore
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
