namespace DiploWalker.Volume.Drivers

open System
open System.IO
open System.Text.Json
open Grpc.Core
open Serilog
open DiploWalker.Abstractions
open DiploWalker.Abstractions.Interfaces

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

    // Synchronisation pour Ã©viter les conditions de course entre Mount/Unmount/Remove
    let lockObj = obj ()

    let metaPath (id: string) =
        Path.Combine(volumesDir, id, "meta.json")

    let dataPath (id: string) = Path.Combine(volumesDir, id, "_data")
    let mountPath (id: string) (target: string) = Path.Combine(mountsDir, id, target)

    let generateId () = Guid.NewGuid().ToString("N")

    // Si le volume pointe directement sur un rÃ©pertoire externe (option "path"),
    // renvoie ce rÃ©pertoire ; sinon le rÃ©pertoire _data interne au volume.
    let resolveDataPath (id: string) =
        let file = metaPath id

        if File.Exists(file) then
            try
                let elem =
                    JsonSerializer.Deserialize<JsonElement>(File.ReadAllText file, JsonSerializerOptions(MaxDepth = 32))

                match elem.TryGetProperty "driverOpts" with
                | true, opts when opts.ValueKind = JsonValueKind.Object ->
                    match opts.TryGetProperty "path" with
                    | true, p when p.ValueKind = JsonValueKind.String ->
                        let p = p.GetString()
                        if String.IsNullOrWhiteSpace p then dataPath id else p
                    | _ -> dataPath id
                | _ -> dataPath id
            with _ ->
                dataPath id
        else
            dataPath id

    /// Copie rÃ©cursivement le contenu de `source` vers `target`, en crÃ©ant les
    /// sous-rÃ©pertoires et en Ã©crasant les fichiers existants. UtilisÃ© Ã  la fois
    /// pour matÃ©rialiser un volume lors du montage et pour resynchroniser le
    /// montage vers le volume au dÃ©montage.
    /// Les points de reparse (junctions, liens symboliques) sont ignorÃ©s : en
    /// recopier le contenu pourrait crÃ©er des cycles ou dupliquer des donnÃ©es
    /// hors du volume. Un ensemble `visited` garde la trace des rÃ©pertoires dÃ©jÃ
    /// copiÃ©s : toute boucle restante est interrompue sans rÃ©cursion infinie.
    let rec copyDirectoryTree (source: string) (target: string) (visited: System.Collections.Generic.HashSet<string>) =
        if visited.Add(Path.GetFullPath source) then
            Directory.CreateDirectory(target) |> ignore

            for file in Directory.GetFiles(source) do
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true)

            for subdir in Directory.GetDirectories(source) do
                let attrs = File.GetAttributes(subdir)

                if (attrs &&& FileAttributes.ReparsePoint) <> enum 0 then
                    () // junction ou lien symbolique : on ne recopie pas derriÃ¨re
                else
                    copyDirectoryTree subdir (Path.Combine(target, Path.GetFileName(subdir))) visited

    member _.CreateVolume(name: string, driverOpts: Map<string, string>, labels: Map<string, string>) =
        let id = generateId ()
        let dir = Path.Combine(volumesDir, id)
        Directory.CreateDirectory(dir) |> ignore

        let dataDir =
            match driverOpts.TryFind "path" with
            | Some p when not (String.IsNullOrWhiteSpace p) ->
                // RÃ©fÃ©rence directe : le volume pointe sur le rÃ©pertoire fourni,
                // sans le copier. Le rÃ©pertoire est crÃ©Ã© s'il n'existe pas encore.
                SecurityValidation.validateVolumePath p "Le chemin du volume"
                let full = Path.GetFullPath(p)
                Directory.CreateDirectory(full) |> ignore
                full
            | _ ->
                let d = Path.Combine(dir, "_data")
                Directory.CreateDirectory(d) |> ignore
                d

        let meta =
            {| id = id
               name = name
               driver = "local"
               mountpoint = dataDir
               labels = labels
               driverOpts = driverOpts
               createdAt = DateTime.UtcNow |}

        AtomicFile.write (metaPath id) (JsonSerializer.Serialize(meta))
        (id, meta.mountpoint)

    member _.RemoveVolume(id: string, force: bool) =
        SecurityValidation.validateId id "L'identifiant du volume"

        lock lockObj (fun () ->
            let dir = Path.Combine(volumesDir, id)
            let mountFile = Path.Combine(mountsDir, id)

            if Directory.Exists(mountFile) && not force then
                raise (
                    RpcException(
                        Status(
                            StatusCode.FailedPrecondition,
                            "Le volume est montÃ©. Utilisez force=true pour forcer la suppression."
                        )
                    )
                )

            let dirExisted = Directory.Exists(dir)

            try
                if dirExisted then
                    Directory.Delete(dir, true)
            with :? System.IO.DirectoryNotFoundException ->
                Log.Warning("RÃ©pertoire de volume dÃ©jÃ  supprimÃ©: {VolumeId}", id)

            try
                if Directory.Exists(mountFile) then
                    Directory.Delete(mountFile, true)
            with :? System.IO.DirectoryNotFoundException ->
                Log.Warning("RÃ©pertoire de montage dÃ©jÃ  supprimÃ©: {VolumeId}", id)

            dirExisted)

    member _.InspectVolume(id: string) =
        SecurityValidation.validateId id "L'identifiant du volume"
        let file = metaPath id

        if File.Exists(file) then
            let content = File.ReadAllText(file)

            JsonSerializer.Deserialize<JsonElement>(content, JsonSerializerOptions(MaxDepth = 32))
            |> Some
        else
            None

    member _.ListVolumes(filters: Map<string, string>) =
        if not (Directory.Exists(volumesDir)) then
            []
        else
            Directory.GetDirectories(volumesDir)
            |> Array.choose (fun dir ->
                let metaFile = Path.Combine(dir, "meta.json")

                if File.Exists(metaFile) then
                    let content = File.ReadAllText(metaFile)

                    let elem =
                        JsonSerializer.Deserialize<JsonElement>(content, JsonSerializerOptions(MaxDepth = 32))

                    Some elem
                else
                    None)
            |> Array.toList
            // Appliquer au minimum les filtres name et label : ignorer la
            // requÃªte du client renverrait des volumes hors pÃ©rimÃ¨tre.
            |> List.filter (fun elem ->
                filters
                |> Map.forall (fun key expected ->
                    match key.ToLowerInvariant() with
                    | "name" ->
                        (JsonHelpers.tryGetString elem "name")
                            .IndexOf(expected, StringComparison.OrdinalIgnoreCase)
                        >= 0
                    | "label" ->
                        match expected.Split('=', 2) with
                        | [| k |] ->
                            (JsonHelpers.tryGetElement elem "labels")
                            |> Option.map (fun labels ->
                                let mutable e = Unchecked.defaultof<System.Text.Json.JsonElement>

                                labels.TryGetProperty(k, &e))
                            |> Option.defaultValue false
                        | [| k; v |] ->
                            (JsonHelpers.tryGetElement elem "labels")
                            |> Option.bind (fun labels ->
                                let mutable e = Unchecked.defaultof<System.Text.Json.JsonElement>

                                if labels.TryGetProperty(k, &e) then
                                    Some(e.GetString())
                                else
                                    None)
                            |> Option.map (fun actual -> actual = v)
                            |> Option.defaultValue false
                        | _ -> true
                    | _ -> true))

    member _.MountVolume(id: string, targetPath: string, options: string) =
        SecurityValidation.validateId id "L'identifiant du volume"
        SecurityValidation.validateVolumePath targetPath "Le chemin cible"

        lock lockObj (fun () ->
            let src = resolveDataPath id

            if not (Directory.Exists(src)) then
                raise (RpcException(Status(StatusCode.NotFound, sprintf "Volume '%s' introuvable" id)))

            let mountDir = mountPath id targetPath
            SecurityValidation.validatePath mountDir mountsDir "Le chemin de montage"

            try
                Directory.CreateDirectory(mountDir) |> ignore

                if Directory.Exists(src) then
                    copyDirectoryTree src mountDir (System.Collections.Generic.HashSet<string>())

                (true, mountDir)
            with ex ->
                try
                    Directory.Delete(mountDir, true)
                with _ ->
                    ()

                raise (
                    RpcException(
                        Status(StatusCode.Internal, sprintf "Erreur lors du montage du volume '%s': %s" id ex.Message)
                    )
                ))

    member _.UnmountVolume(id: string, targetPath: string) =
        SecurityValidation.validateId id "L'identifiant du volume"
        SecurityValidation.validateVolumePath targetPath "Le chemin cible"
        let mountDir = mountPath id targetPath
        SecurityValidation.validatePath mountDir mountsDir "Le chemin de montage"

        lock lockObj (fun () ->
            if Directory.Exists(mountDir) then
                let src = resolveDataPath id

                // Resynchroniser les modifications du montage vers le volume :
                // supprimer le rÃ©pertoire sans rÃ©Ã©criture dÃ©truirait TOUT ce
                // que le conteneur a Ã©crit entre Mount et Unmount.
                if Directory.Exists(src) then
                    try
                        copyDirectoryTree mountDir src (System.Collections.Generic.HashSet<string>())
                    with ex ->
                        // Synchronisation Ã©chouÃ©e : conserver le montage pour ne
                        // pas perdre les donnÃ©es, et signaler l'Ã©chec.
                        Log.Error(
                            ex,
                            "Synchronisation retour impossible de {MountDir} vers {DataPath} â€” contenu conservÃ©",
                            mountDir,
                            src
                        )

                        raise (
                            RpcException(
                                Status(
                                    StatusCode.Internal,
                                    sprintf "DÃ©montage de '%s' annulÃ© : synchronisation retour Ã©chouÃ©e" id
                                )
                            )
                        )

                Directory.Delete(mountDir, true))

        (true, "Démonté")

    member _.GetVolumeSize(id: string) =
        SecurityValidation.validateId id "L'identifiant du volume"

        lock lockObj (fun () ->
            let dir = resolveDataPath id
            let dirFull = Path.GetFullPath(dir)

            if Directory.Exists(dir) then
                let mutable totalBytes = 0L

                let mutable visited =
                    System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)

                let queue = System.Collections.Generic.Queue<string>()
                queue.Enqueue(dirFull) |> ignore

                while queue.Count > 0 do
                    let current = queue.Dequeue()

                    if visited.Add(current) then
                        try
                            for file in Directory.GetFiles(current) do
                                try
                                    totalBytes <- totalBytes + FileInfo(file).Length
                                with _ ->
                                    ()

                            for subdir in Directory.GetDirectories(current) do
                                let subFull = Path.GetFullPath(subdir)

                                // DÃ©tecter les boucles de symlinks â€” comparer avec
                                // le sÃ©parateur final, sinon un rÃ©pertoire frÃ¨re
                                // (vol-abc vs vol-abcd) serait inclus Ã  tort.
                                let dirFullWithSep =
                                    if dirFull.EndsWith(Path.DirectorySeparatorChar) then
                                        dirFull
                                    else
                                        dirFull + string Path.DirectorySeparatorChar

                                if subFull.StartsWith(dirFullWithSep, StringComparison.OrdinalIgnoreCase) then
                                    queue.Enqueue(subFull)
                        with _ ->
                            ()

                totalBytes
            else
                0L)

    member _.PruneVolumes() =
        lock lockObj (fun () ->
            if not (Directory.Exists(volumesDir)) then
                []
            else
                let removed = ResizeArray<string>()

                for dir in Directory.GetDirectories(volumesDir) do
                    let mountFile = Path.Combine(mountsDir, Path.GetFileName(dir))

                    if Directory.Exists(mountFile) then
                        ()
                    else
                        let metaFile = Path.Combine(dir, "meta.json")

                        if File.Exists(metaFile) then
                            try
                                Directory.Delete(dir, true)
                                removed.Add(Path.GetFileName(dir))
                            with ex ->
                                Log.Warning(ex, "Erreur lors du nettoyage du volume {VolumeId}", Path.GetFileName(dir))

                removed |> Seq.toList)

    interface IVolumeDriver with
        member this.CreateVolume(name, driverOpts, labels) =
            this.CreateVolume(name, driverOpts, labels)

        member this.RemoveVolume(id, force) = this.RemoveVolume(id, force)
        member this.InspectVolume(id) = this.InspectVolume(id)
        member this.ListVolumes(filters) = this.ListVolumes(filters)

        member this.MountVolume(id, targetPath, options) =
            this.MountVolume(id, targetPath, options)

        member this.UnmountVolume(id, targetPath) = this.UnmountVolume(id, targetPath)
        member this.GetVolumeSize(id) = this.GetVolumeSize(id)
        member this.PruneVolumes() = this.PruneVolumes()

