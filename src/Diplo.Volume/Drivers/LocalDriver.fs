namespace Diplo.Volume.Drivers

open System
open System.IO
open System.Text.Json

type LocalVolumeDriver(dataRoot: string) =

    do
        if not (Directory.Exists(dataRoot)) then
            Directory.CreateDirectory(dataRoot) |> ignore

    let volumesDir = Path.Combine(dataRoot, "volumes")
    let mountsDir = Path.Combine(dataRoot, "mounts")

    do
        if not (Directory.Exists(volumesDir)) then
            Directory.CreateDirectory(volumesDir) |> ignore
        if not (Directory.Exists(mountsDir)) then
            Directory.CreateDirectory(mountsDir) |> ignore

    let metaPath (id: string) = Path.Combine(volumesDir, id, "meta.json")
    let dataPath (id: string) = Path.Combine(volumesDir, id, "_data")
    let mountPath (id: string) (target: string) = Path.Combine(mountsDir, id, target)

    let generateId () = Guid.NewGuid().ToString("N")

    member _.CreateVolume(name: string, driverOpts: Map<string, string>, labels: Map<string, string>) =
        let id = generateId()
        let dir = Path.Combine(volumesDir, id)
        Directory.CreateDirectory(dir) |> ignore
        Directory.CreateDirectory(Path.Combine(dir, "_data")) |> ignore
        let meta = {|
            id = id
            name = name
            driver = "local"
            mountpoint = Path.Combine(dir, "_data")
            labels = labels
            driverOpts = driverOpts
            createdAt = DateTime.UtcNow
        |}
        File.WriteAllText(metaPath id, JsonSerializer.Serialize(meta))
        (id, meta.mountpoint)

    member _.RemoveVolume(id: string, force: bool) =
        let dir = Path.Combine(volumesDir, id)
        if Directory.Exists(dir) then
            let mountFile = Path.Combine(mountsDir, id)
            if Directory.Exists(mountFile) && not force then
                failwith "Le volume est monté. Utilisez force=true pour forcer la suppression."
            Directory.Delete(dir, true)
            if Directory.Exists(mountFile) then Directory.Delete(mountFile, true)
            true
        else false

    member _.InspectVolume(id: string) =
        let file = metaPath id
        if File.Exists(file) then
            let content = File.ReadAllText(file)
            JsonSerializer.Deserialize<JsonElement>(content)
            |> Some
        else None

    member _.ListVolumes(filters: Map<string, string>) =
        if not (Directory.Exists(volumesDir)) then []
        else
            Directory.GetDirectories(volumesDir)
            |> Array.choose (fun dir ->
                let metaFile = Path.Combine(dir, "meta.json")
                if File.Exists(metaFile) then
                    let content = File.ReadAllText(metaFile)
                    let elem = JsonSerializer.Deserialize<JsonElement>(content)
                    Some elem
                else None)
            |> Array.toList

    member _.MountVolume(id: string, targetPath: string, options: string) =
        let src = dataPath id
        if not (Directory.Exists(src)) then
            failwithf "Volume %s introuvable" id
        let mountDir = mountPath id targetPath
        Directory.CreateDirectory(mountDir) |> ignore
        // Sur Windows, on simule le mount en copiant les fichiers
        if Directory.Exists(src) then
            for file in Directory.GetFiles(src) do
                let destFile = Path.Combine(mountDir, Path.GetFileName(file))
                File.Copy(file, destFile, true)
        (true, mountDir)

    member _.UnmountVolume(id: string, targetPath: string) =
        let mountDir = mountPath id targetPath
        if Directory.Exists(mountDir) then
            Directory.Delete(mountDir, true)
        (true, "Démonté")

    member _.GetVolumeSize(id: string) =
        let dir = dataPath id
        if Directory.Exists(dir) then
            Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            |> Array.sumBy (fun f -> FileInfo(f).Length)
        else 0L
