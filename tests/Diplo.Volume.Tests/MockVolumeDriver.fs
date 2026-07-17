namespace Diplo.Volume.Tests

open System
open System.Text.Json
open Diplo.Abstractions.Interfaces

type MockVolumeDriver() =

    let mutable volumes = Map.empty<string, string * string * Map<string, string> * Map<string, string>>
    let mutable mountedVolumes = Set.empty<string>
    let ownedDocs = System.Collections.Generic.List<JsonDocument>()

    let keepDoc (doc: JsonDocument) =
        ownedDocs.Add(doc)
        doc

    let createVolumeJson (id: string) (name: string) (mountpoint: string) (labels: Map<string, string>) =
        let labelsStr =
            labels
            |> Map.toList
            |> List.map (fun (k, v) -> sprintf "\"%s\":\"%s\"" k v)
            |> String.concat ","
        let escapedMountpoint = mountpoint.Replace("\\", "\\\\")
        let json = sprintf """{"id":"%s","name":"%s","mountpoint":"%s","labels":{%s},"createdAt":"2025-01-15T10:30:00Z"}""" id name escapedMountpoint labelsStr
        let doc = JsonDocument.Parse(json) |> keepDoc
        doc.RootElement

    interface IVolumeDriver with
        member _.CreateVolume(name, _driverOpts, labels) =
            let id = Guid.NewGuid().ToString("N")
            let mountpoint = sprintf "C:\\volumes\\%s\\_data" id
            volumes <- volumes |> Map.add id (id, name, Map.empty, labels)
            (id, mountpoint)

        member _.RemoveVolume(id, _force) =
            if volumes |> Map.containsKey id then
                volumes <- volumes |> Map.remove id
                true
            else false

        member _.InspectVolume(id) =
            match volumes |> Map.tryFind id with
            | Some (_, name, _, labels) ->
                Some (createVolumeJson id name (sprintf "C:\\volumes\\%s\\_data" id) labels)
            | None -> None

        member _.ListVolumes(_filters) =
            volumes
            |> Map.toList
            |> List.map (fun (id, (_, name, _, labels)) -> createVolumeJson id name (sprintf "C:\\volumes\\%s\\_data" id) labels)

        member _.MountVolume(id, _targetPath, _options) =
            if not (volumes |> Map.containsKey id) then
                failwithf "Volume %s introuvable" id
            mountedVolumes <- mountedVolumes |> Set.add id
            let mountDir = sprintf "C:\\mounts\\%s" id
            (true, mountDir)

        member _.UnmountVolume(id, _targetPath) =
            mountedVolumes <- mountedVolumes |> Set.remove id
            (true, "Démonté")

        member _.GetVolumeSize(id) =
            if volumes |> Map.containsKey id then 1024L else 0L

    member this.Mock : IVolumeDriver = this :> IVolumeDriver
    member _.Volumes = volumes
    member _.MountedVolumes = mountedVolumes
