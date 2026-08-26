namespace Diplo.Disk

open System.IO
open Serilog

/// Adaptateur Hawkynt.FileFormats.FileSystems pour l'extraction et la
/// réécriture de systèmes de fichiers Btrfs, XFS et HFS+.
module HawkyntFs =

    let private maxInMemoryBytes = 2L * 1024L * 1024L * 1024L

    // ── Extraction Btrfs ──────────────────────────────────────────────

    let private extractBtrfs (stream: Stream) (targetDir: string) =
        use reader = new FileSystem.Btrfs.BtrfsReader(stream, true)
        let mutable count = 0

        for entry in reader.Entries do
            if not (System.String.IsNullOrEmpty(entry.Name)) then
                let relPath =
                    entry.Name.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar)

                let fullPath = Path.Combine(targetDir, relPath)

                if entry.IsDirectory then
                    Directory.CreateDirectory(fullPath) |> ignore
                else
                    let dir = Path.GetDirectoryName(fullPath)

                    if not (System.String.IsNullOrEmpty(dir)) then
                        Directory.CreateDirectory(dir) |> ignore

                    File.WriteAllBytes(fullPath, reader.Extract(entry))
                    count <- count + 1

        count

    // ── Extraction XFS ────────────────────────────────────────────────

    let private extractXfs (stream: Stream) (targetDir: string) =
        use reader = new FileSystem.Xfs.XfsReader(stream, true)
        let mutable count = 0

        for entry in reader.Entries do
            if not (System.String.IsNullOrEmpty(entry.Name)) then
                let relPath =
                    entry.Name.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar)

                let fullPath = Path.Combine(targetDir, relPath)

                if entry.IsDirectory then
                    Directory.CreateDirectory(fullPath) |> ignore
                else
                    let dir = Path.GetDirectoryName(fullPath)

                    if not (System.String.IsNullOrEmpty(dir)) then
                        Directory.CreateDirectory(dir) |> ignore

                    File.WriteAllBytes(fullPath, reader.Extract(entry))
                    count <- count + 1

        count

    // ── Extraction HFS+ ───────────────────────────────────────────────

    let private extractHfsPlus (stream: Stream) (targetDir: string) =
        use reader = new FileSystem.HfsPlus.HfsPlusReader(stream, true)
        let mutable count = 0

        for entry in reader.Entries do
            let name =
                if System.String.IsNullOrEmpty(entry.FullPath) then
                    entry.Name
                else
                    entry.FullPath

            if not (System.String.IsNullOrEmpty(name)) then
                let relPath = name.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar)
                let fullPath = Path.Combine(targetDir, relPath)

                if entry.IsDirectory then
                    Directory.CreateDirectory(fullPath) |> ignore
                else
                    let dir = Path.GetDirectoryName(fullPath)

                    if not (System.String.IsNullOrEmpty(dir)) then
                        Directory.CreateDirectory(dir) |> ignore

                    File.WriteAllBytes(fullPath, reader.Extract(entry))
                    count <- count + 1

        count

    /// Tente d'extraire une image disque via Hawkynt.
    /// Retourne Some(nombreFichiers) si le format est géré, None sinon.
    /// Chaque tentative extrait dans un sous-répertoire dédié et n'est promue
    /// que si elle produit des fichiers : sinon un Btrfs partiel suivi d'un XFS
    /// réussi mélangerait deux interprétations dans le même staging.
    let private extractInAttemptDir
        (extract: Stream -> string -> int)
        (stream: Stream)
        (targetDir: string)
        : int option =
        let attemptDir =
            Path.Combine(targetDir, "hawkynt-" + System.Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory(attemptDir) |> ignore
        let mutable promoted = false

        try
            let count = extract stream attemptDir

            if count > 0 then
                for entry in Directory.GetFileSystemEntries(attemptDir) do
                    let dest = Path.Combine(targetDir, Path.GetFileName(entry))

                    if Directory.Exists(entry) then
                        Directory.Move(entry, dest)
                    else
                        File.Move(entry, dest)

                promoted <- true

            (if promoted then Some count else None)
        finally
            if not promoted then
                try
                    Directory.Delete(attemptDir, true)
                with ex ->
                    Log.Warning(ex, "Impossible de supprimer le répertoire de tentative {Dir}", attemptDir)

    let tryExtract (sourcePath: string) (targetDir: string) : int option =
        if not (File.Exists(sourcePath)) then
            None
        else
            let fileInfo = FileInfo(sourcePath)

            if fileInfo.Length > maxInMemoryBytes then
                None
            else
                try
                    let format = DiskFormat.detect sourcePath

                    match format with
                    | DiskFormat.Qcow2 -> None
                    | _ when DiskFormat.isDiskImage format ->
                        use stream =
                            new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read)

                        try
                            extractInAttemptDir extractBtrfs stream targetDir
                        with ex ->
                            Log.Warning(ex, "Échec extraction Btrfs, tentative XFS")
                            stream.Position <- 0L

                            match extractInAttemptDir extractXfs stream targetDir with
                            | Some c -> Some c
                            | None ->
                                stream.Position <- 0L

                                match extractInAttemptDir extractHfsPlus stream targetDir with
                                | Some c ->
                                    Log.Information("Extraction HFS+ réussie après échecs Btrfs/XFS")
                                    Some c
                                | None ->
                                    Log.Warning("Échec extraction HFS+ : aucun système de fichiers reconnu")
                                    None
                    | _ -> None
                with ex ->
                    Log.Warning(ex, "Échec détection format Hawkynt pour {Path}", sourcePath)
                    None

    // ── Réécriture (schéma commun) ────────────────────────────────────

    /// Sérialise dans un fichier temporaire puis remplace l'image source d'un
    /// bloc : FileMode.Create tronquerait l'image AVANT tout succès de
    /// sérialisation — un échec (disque plein) détruirait l'image et le
    /// fallback relirait ensuite un fichier déjà corrompu.
    let private replaceAfterSerialize (sourcePath: string) (serialize: FileStream -> unit) =
        let tmpPath = sourcePath + "." + System.Guid.NewGuid().ToString("N") + ".tmp"

        try
            do
                use output = new FileStream(tmpPath, FileMode.Create, FileAccess.Write)
                serialize output

            try
                File.Replace(tmpPath, sourcePath, null)
            with :? FileNotFoundException ->
                File.Move(tmpPath, sourcePath)
        with
        | _ ->
            try
                File.Delete(tmpPath)
            with _ -> ()
            reraise ()

    // ── Réécriture Btrfs ──────────────────────────────────────────────

    let private writeBackBtrfs (sourcePath: string) (sourceDir: string) =
        let imageBytes = File.ReadAllBytes(sourcePath)

        replaceAfterSerialize sourcePath (fun output ->
            use stream = new MemoryStream(imageBytes)
            use reader = new FileSystem.Btrfs.BtrfsReader(stream, false)
            let writer = new FileSystem.Btrfs.BtrfsWriter()

            for entry in reader.Entries do
                if not entry.IsDirectory then
                    writer.AddFile(entry.Name, reader.Extract(entry))

            for file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories) do
                let relPath =
                    file
                        .Substring(sourceDir.Length)
                        .TrimStart(Path.DirectorySeparatorChar)
                        .Replace(Path.DirectorySeparatorChar, '/')

                writer.AddFile(relPath, File.ReadAllBytes(file))

            writer.WriteTo(output))

    // ── Réécriture XFS ────────────────────────────────────────────────

    let private writeBackXfs (sourcePath: string) (sourceDir: string) =
        let imageBytes = File.ReadAllBytes(sourcePath)

        replaceAfterSerialize sourcePath (fun output ->
            use stream = new MemoryStream(imageBytes)
            use reader = new FileSystem.Xfs.XfsReader(stream, false)
            let writer = new FileSystem.Xfs.XfsWriter()

            for entry in reader.Entries do
                if not entry.IsDirectory then
                    writer.AddFile(entry.Name, reader.Extract(entry))

            for file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories) do
                let relPath =
                    file
                        .Substring(sourceDir.Length)
                        .TrimStart(Path.DirectorySeparatorChar)
                        .Replace(Path.DirectorySeparatorChar, '/')

                writer.AddFile(relPath, File.ReadAllBytes(file))

            writer.WriteTo(output))

    // ── Réécriture HFS+ ───────────────────────────────────────────────

    let private writeBackHfsPlus (sourcePath: string) (sourceDir: string) =
        let imageBytes = File.ReadAllBytes(sourcePath)

        replaceAfterSerialize sourcePath (fun output ->
            use stream = new MemoryStream(imageBytes)
            use reader = new FileSystem.HfsPlus.HfsPlusReader(stream, false)
            let writer = new FileSystem.HfsPlus.HfsPlusWriter(true, true, 8192, "")

            for entry in reader.Entries do
                if not entry.IsDirectory then
                    let path =
                        if System.String.IsNullOrEmpty(entry.FullPath) then
                            entry.Name
                        else
                            entry.FullPath

                    writer.AddFile(path, reader.Extract(entry))

            for file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories) do
                let relPath =
                    file
                        .Substring(sourceDir.Length)
                        .TrimStart(Path.DirectorySeparatorChar)
                        .Replace(Path.DirectorySeparatorChar, '/')

                writer.AddFile(relPath, File.ReadAllBytes(file))

            writer.BuildToStreamingAutoSized(output, 2048))

    /// Réécrit le contenu de `sourceDir` dans l'image disque via Hawkynt.
    /// Retourne true si réussi, false si le format n'est pas géré.
    let tryWriteBack (sourcePath: string) (sourceDir: string) : bool =
        if not (File.Exists(sourcePath)) then
            false
        else
            let fileInfo = FileInfo(sourcePath)

            if fileInfo.Length > maxInMemoryBytes then
                false
            else
                try
                    let format = DiskFormat.detect sourcePath

                    match format with
                    | DiskFormat.Qcow2 -> false
                    | _ when DiskFormat.isDiskImage format ->
                        try
                            writeBackBtrfs sourcePath sourceDir
                            true
                        with ex ->
                            Log.Warning(ex, "Échec réécriture Btrfs, tentative XFS")

                            try
                                writeBackXfs sourcePath sourceDir
                                true
                            with ex ->
                                Log.Warning(ex, "Échec réécriture XFS, tentative HFS+")

                                try
                                    writeBackHfsPlus sourcePath sourceDir
                                    true
                                with ex ->
                                    Log.Warning(ex, "Échec réécriture HFS+")
                                    false
                    | _ -> false
                with ex ->
                    Log.Warning(ex, "Échec détection format Hawkynt pour réécriture {Path}", sourcePath)
                    false
