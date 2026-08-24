namespace Diplo.Volume.Drivers

open System
open System.IO
open System.Text.Json
open Serilog
open Diplo.Abstractions

type RemoteVolumeStore(dataRoot: string, driverName: string) =

    let volumesDir = Path.Combine(dataRoot, driverName)

    do
        if not (Directory.Exists(volumesDir)) then
            Directory.CreateDirectory(volumesDir) |> ignore

    let metaPath (id: string) = Path.Combine(volumesDir, id, "meta.json")

    let generateId () = Guid.NewGuid().ToString("N")

    /// Clés sensibles à supprimer avant persistance (credentials en clair interdit).
    let sensitiveKeys = set [ "password"; "secret"; "token"; "apikey"; "api_key" ]

    member _.CreateVolume(name: string, remotePath: string, labels: Map<string, string>, driverOpts: Map<string, string>) =
        let id = generateId()
        let dir = Path.Combine(volumesDir, id)
        Directory.CreateDirectory(dir) |> ignore
        let safeOpts =
            driverOpts
            |> Map.filter (fun k _ -> not (sensitiveKeys.Contains(k.ToLowerInvariant())))
        let meta = {|
            id = id
            name = name
            driver = driverName
            remotePath = remotePath
            labels = labels
            driverOpts = safeOpts
            createdAt = DateTime.UtcNow
        |}
        AtomicFile.write (metaPath id) (JsonSerializer.Serialize(meta))
        (id, remotePath)

    member _.RemoveVolume(id: string) =
        SecurityValidation.validateId id "L'identifiant du volume"
        let dir = Path.Combine(volumesDir, id)
        if Directory.Exists(dir) then
            try Directory.Delete(dir, true)
            with :? DirectoryNotFoundException ->
                Log.Warning("Répertoire déjà supprimé: {VolumeId}", id)
            true
        else false

    member _.InspectVolume(id: string) =
        SecurityValidation.validateId id "L'identifiant du volume"
        let file = metaPath id
        if File.Exists(file) then
            let content = File.ReadAllText(file)
            JsonSerializer.Deserialize<JsonElement>(content, JsonSerializerOptions(MaxDepth = 32))
            |> Some
        else None

    member _.ListVolumes() =
        if not (Directory.Exists(volumesDir)) then []
        else
            Directory.GetDirectories(volumesDir)
            |> Array.choose (fun dir ->
                let metaFile = Path.Combine(dir, "meta.json")
                if File.Exists(metaFile) then
                    let content = File.ReadAllText(metaFile)
                    JsonSerializer.Deserialize<JsonElement>(content, JsonSerializerOptions(MaxDepth = 32))
                    |> Some
                else None)
            |> Array.toList

    member _.VolumeExists(id: string) =
        File.Exists(metaPath id)

    /// Supprime tous les volumes enregistrés et retourne la liste des identifiants supprimés.
    member this.PruneAll() =
        let volumes = this.ListVolumes()
        if volumes.IsEmpty then []
        else
            let removed = ResizeArray<string>()
            for vol in volumes do
                let id = JsonHelpers.tryGetString vol "id"
                if not (String.IsNullOrEmpty(id)) then
                    this.RemoveVolume(id) |> ignore
                    removed.Add(id)
            removed |> Seq.toList
