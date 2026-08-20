namespace Diplo.Disk

open System.IO

/// Adaptateur Hawkynt.FileFormats.FileSystems pour l'extraction et la
/// réécriture de systèmes de fichiers Btrfs, XFS et HFS+.
module HawkyntFs =

    let private maxInMemoryBytes = 2L * 1024L * 1024L * 1024L

    // ── Extraction Btrfs ──────────────────────────────────────────────

    let private extractBtrfs (stream: Stream) (targetDir: string) =
        let reader = new FileSystem.Btrfs.BtrfsReader(stream, true)
        let mutable count = 0
        for entry in reader.Entries do
            if not (System.String.IsNullOrEmpty(entry.Name)) then
                let relPath = entry.Name.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar)
                let fullPath = Path.Combine(targetDir, relPath)
                if entry.IsDirectory then
                    Directory.CreateDirectory(fullPath) |> ignore
                else
                    let dir = Path.GetDirectoryName(fullPath)
                    if not (System.String.IsNullOrEmpty(dir)) then
                        Directory.CreateDirectory(dir) |> ignore
                    File.WriteAllBytes(fullPath, reader.Extract(entry))
                    count <- count + 1
        reader.Dispose()
        count

    // ── Extraction XFS ────────────────────────────────────────────────

    let private extractXfs (stream: Stream) (targetDir: string) =
        let reader = new FileSystem.Xfs.XfsReader(stream, true)
        let mutable count = 0
        for entry in reader.Entries do
            if not (System.String.IsNullOrEmpty(entry.Name)) then
                let relPath = entry.Name.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar)
                let fullPath = Path.Combine(targetDir, relPath)
                if entry.IsDirectory then
                    Directory.CreateDirectory(fullPath) |> ignore
                else
                    let dir = Path.GetDirectoryName(fullPath)
                    if not (System.String.IsNullOrEmpty(dir)) then
                        Directory.CreateDirectory(dir) |> ignore
                    File.WriteAllBytes(fullPath, reader.Extract(entry))
                    count <- count + 1
        reader.Dispose()
        count

    // ── Extraction HFS+ ───────────────────────────────────────────────

    let private extractHfsPlus (stream: Stream) (targetDir: string) =
        let reader = new FileSystem.HfsPlus.HfsPlusReader(stream, true)
        let mutable count = 0
        for entry in reader.Entries do
            let name = if System.String.IsNullOrEmpty(entry.FullPath) then entry.Name else entry.FullPath
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
        reader.Dispose()
        count

    /// Tente d'extraire une image disque via Hawkynt.
    /// Retourne Some(nombreFichiers) si le format est géré, None sinon.
    let tryExtract (sourcePath: string) (targetDir: string) : int option =
        if not (File.Exists(sourcePath)) then None
        else
            let fileInfo = FileInfo(sourcePath)
            if fileInfo.Length > maxInMemoryBytes then None
            else
            try
                let format = DiskFormat.detect sourcePath
                match format with
                | DiskFormat.Qcow2 -> None
                | _ when DiskFormat.isDiskImage format ->
                    use stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read)
                    try Some (extractBtrfs stream targetDir)
                    with _ ->
                        stream.Position <- 0L
                        try Some (extractXfs stream targetDir)
                        with _ ->
                            stream.Position <- 0L
                            try Some (extractHfsPlus stream targetDir)
                            with _ -> None
                | _ -> None
            with _ -> None

    // ── Réécriture Btrfs ──────────────────────────────────────────────

    let private writeBackBtrfs (sourcePath: string) (sourceDir: string) =
        let imageBytes = File.ReadAllBytes(sourcePath)
        let stream = new MemoryStream(imageBytes)
        let reader = new FileSystem.Btrfs.BtrfsReader(stream, false)
        let writer = new FileSystem.Btrfs.BtrfsWriter()
        for entry in reader.Entries do
            if not entry.IsDirectory then
                writer.AddFile(entry.Name, reader.Extract(entry))
        for file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories) do
            let relPath = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/')
            writer.AddFile(relPath, File.ReadAllBytes(file))
        reader.Dispose()
        use output = new FileStream(sourcePath, FileMode.Create, FileAccess.Write)
        writer.WriteTo(output)

    // ── Réécriture XFS ────────────────────────────────────────────────

    let private writeBackXfs (sourcePath: string) (sourceDir: string) =
        let imageBytes = File.ReadAllBytes(sourcePath)
        let stream = new MemoryStream(imageBytes)
        let reader = new FileSystem.Xfs.XfsReader(stream, false)
        let writer = new FileSystem.Xfs.XfsWriter()
        for entry in reader.Entries do
            if not entry.IsDirectory then
                writer.AddFile(entry.Name, reader.Extract(entry))
        for file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories) do
            let relPath = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/')
            writer.AddFile(relPath, File.ReadAllBytes(file))
        reader.Dispose()
        use output = new FileStream(sourcePath, FileMode.Create, FileAccess.Write)
        writer.WriteTo(output)

    // ── Réécriture HFS+ ───────────────────────────────────────────────

    let private writeBackHfsPlus (sourcePath: string) (sourceDir: string) =
        let imageBytes = File.ReadAllBytes(sourcePath)
        let stream = new MemoryStream(imageBytes)
        let reader = new FileSystem.HfsPlus.HfsPlusReader(stream, false)
        let writer = new FileSystem.HfsPlus.HfsPlusWriter(true, true, 8192, "")
        for entry in reader.Entries do
            if not entry.IsDirectory then
                let path = if System.String.IsNullOrEmpty(entry.FullPath) then entry.Name else entry.FullPath
                writer.AddFile(path, reader.Extract(entry))
        for file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories) do
            let relPath = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/')
            writer.AddFile(relPath, File.ReadAllBytes(file))
        reader.Dispose()
        use output = new FileStream(sourcePath, FileMode.Create, FileAccess.Write)
        writer.BuildToStreamingAutoSized(output, 2048)

    /// Réécrit le contenu de `sourceDir` dans l'image disque via Hawkynt.
    /// Retourne true si réussi, false si le format n'est pas géré.
    let tryWriteBack (sourcePath: string) (sourceDir: string) : bool =
        if not (File.Exists(sourcePath)) then false
        else
            let fileInfo = FileInfo(sourcePath)
            if fileInfo.Length > maxInMemoryBytes then false
            else
            try
                let format = DiskFormat.detect sourcePath
                match format with
                | DiskFormat.Qcow2 -> false
                | _ when DiskFormat.isDiskImage format ->
                    try writeBackBtrfs sourcePath sourceDir; true
                    with _ ->
                        try writeBackXfs sourcePath sourceDir; true
                        with _ ->
                            try writeBackHfsPlus sourcePath sourceDir; true
                            with _ -> false
                | _ -> false
            with _ -> false
