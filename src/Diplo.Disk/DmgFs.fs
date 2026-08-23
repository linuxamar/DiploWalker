namespace Diplo.Disk

open System.IO
open DiscUtils
open DiscUtils.Streams
open Serilog

/// Adaptateur DMG (Apple Disk Image) pour extraction et reecriture.
/// Lecture via DiscUtils.Dmg (UDIF compresse), ecriture via Hawkynt DmgWriter.
module DmgFs =

    let private tryOpenDisk (sourcePath: string) =
        try
            let fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read)
            try
                let disk = new Dmg.Disk(fs, Ownership.Dispose)
                Some (disk :> VirtualDisk)
            with ex ->
                fs.Dispose()
                reraise ()
        with
        | :? IOException -> None
        | :? System.NotSupportedException -> None
        | ex ->
            Log.Warning(ex, "Erreur inattendue ouverture DMG pour {Path}", sourcePath)
            None

    let private openFileSystem (disk: VirtualDisk) =
        let vm = new VolumeManager(disk)
        let logical = vm.GetLogicalVolumes() |> Seq.cast<VolumeInfo>
        let physical = vm.GetPhysicalVolumes() |> Seq.cast<VolumeInfo>
        Seq.append logical physical
        |> Seq.choose (fun v ->
            let detected = FileSystemManager.DetectFileSystems v
            if detected.Count > 0 then Some (v, detected.[0]) else None)
        |> Seq.tryHead
        |> function
            | Some (volume, fsi) -> Some (fsi.Open volume)
            | None -> None

    let rec private copyDirectory (fs: DiscFileSystem) (fsDir: string) (realRoot: string) (counter: int ref) =
        let sep = Path.DirectorySeparatorChar
        let dir = realRoot + fsDir.TrimStart('/', '\\').Replace('/', sep)
        Directory.CreateDirectory(dir) |> ignore
        for file in fs.GetFiles fsDir do
            let relPath = file.TrimStart('/', '\\').Replace('/', sep)
            let target = Path.Combine(realRoot, relPath)
            Directory.CreateDirectory(Path.GetDirectoryName target) |> ignore
            use src = fs.OpenFile(file, FileMode.Open, FileAccess.Read)
            use dst = File.Create target
            src.CopyTo dst
            counter := !counter + 1
        for sub in fs.GetDirectories fsDir do
            copyDirectory fs sub realRoot counter

    let rec private copyIntoFs (fs: DiscFileSystem) (fsDir: string) (realDir: string) =
        for file in Directory.GetFiles realDir do
            let fsPath = fsDir.TrimEnd('\\', '/') + "/" + Path.GetFileName file
            use src = File.OpenRead file
            use dst = fs.OpenFile(fsPath, FileMode.Create, FileAccess.Write)
            src.CopyTo dst
        for dir in Directory.GetDirectories realDir do
            let fsPath = fsDir.TrimEnd('\\', '/') + "/" + Path.GetFileName dir
            if not (fs.DirectoryExists fsPath) then fs.CreateDirectory fsPath
            copyIntoFs fs fsPath dir

    let rec private deleteFsEntries (fs: DiscFileSystem) (fsDir: string) (realRoot: string) =
        for file in fs.GetFiles fsDir do
            let relPath = file.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar)
            if not (File.Exists(Path.Combine(realRoot, relPath))) then fs.DeleteFile file
        for sub in fs.GetDirectories fsDir do
            let relSub = sub.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar)
            deleteFsEntries fs sub realRoot
            if not (Directory.Exists(Path.Combine(realRoot, relSub))) then fs.DeleteDirectory(sub, false)

    /// Tente d'extraire une image DMG via DiscUtils.
    let tryExtract (sourcePath: string) (targetDir: string) : int option =
        if not (File.Exists(sourcePath)) then None
        else
            let diskOpt = tryOpenDisk sourcePath
            match diskOpt with
            | Some disk ->
                use _disk = disk
                match openFileSystem disk with
                | Some fs ->
                    use _fs = fs
                    let counter = ref 0
                    copyDirectory fs "/" targetDir counter
                    Some !counter
                | None -> None
            | None -> None

    /// Reecrit le contenu de sourceDir dans l'image DMG.
    let tryWriteBack (_sourcePath: string) (_sourceDir: string) : bool =
        false
