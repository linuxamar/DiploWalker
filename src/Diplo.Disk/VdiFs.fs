namespace Diplo.Disk

open System.IO
open DiscUtils
open DiscUtils.Streams

/// Adaptateur DiscUtils.Vdi pour l'extraction et la réécriture
/// de systèmes de fichiers contenus dans des images VDI (VirtualBox).
///
/// DiscUtils.Vdi.Disk gère nativement la lecture et l'écriture VDI
/// (fixe et dynamique). L'approche est identique à qcow2 : on passe
/// un Stream au disque DiscUtils, puis on extrait le FS contenu.
module VdiFs =

    let private maxInMemoryBytes = 2L * 1024L * 1024L * 1024L

    let private tryOpen (sourcePath: string) (readOnly: bool) =
        let access = if readOnly then FileAccess.Read else FileAccess.ReadWrite
        let fs = new FileStream(sourcePath, FileMode.Open, access, FileShare.Read)
        try
            let disk = new Vdi.Disk(fs, Ownership.Dispose)
            disk :> VirtualDisk
        with ex ->
            fs.Dispose()
            raise ex

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
        System.IO.Directory.CreateDirectory(
            realRoot + fsDir.TrimStart('/','\\').Replace('/', System.IO.Path.DirectorySeparatorChar))
            |> ignore
        for file in fs.GetFiles fsDir do
            let relPath = file.TrimStart('/','\\').Replace('/', System.IO.Path.DirectorySeparatorChar)
            let target = System.IO.Path.Combine(realRoot, relPath)
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName target) |> ignore
            use src = fs.OpenFile(file, FileMode.Open, FileAccess.Read)
            use dst = File.Create target
            src.CopyTo dst
            counter := !counter + 1
        for sub in fs.GetDirectories fsDir do
            copyDirectory fs sub realRoot counter

    let rec private copyIntoFs (fs: DiscFileSystem) (fsDir: string) (realDir: string) =
        for file in System.IO.Directory.GetFiles realDir do
            let fsPath = fsDir.TrimEnd('\\','/') + "/" + System.IO.Path.GetFileName file
            use src = File.OpenRead file
            use dst = fs.OpenFile(fsPath, FileMode.Create, FileAccess.Write)
            src.CopyTo dst
        for dir in System.IO.Directory.GetDirectories realDir do
            let fsPath = fsDir.TrimEnd('\\','/') + "/" + System.IO.Path.GetFileName dir
            if not (fs.DirectoryExists fsPath) then fs.CreateDirectory fsPath
            copyIntoFs fs fsPath dir

    let rec private deleteFsEntries (fs: DiscFileSystem) (fsDir: string) (realRoot: string) =
        for file in fs.GetFiles fsDir do
            let relPath = file.TrimStart('/','\\').Replace('/', System.IO.Path.DirectorySeparatorChar)
            if not (File.Exists(System.IO.Path.Combine(realRoot, relPath))) then fs.DeleteFile file
        for sub in fs.GetDirectories fsDir do
            let relSub = sub.TrimStart('/','\\').Replace('/', System.IO.Path.DirectorySeparatorChar)
            deleteFsEntries fs sub realRoot
            if not (System.IO.Directory.Exists(System.IO.Path.Combine(realRoot, relSub))) then
                fs.DeleteDirectory(sub, false)

    /// Tente d'extraire une image VDI via DiscUtils.Vdi.
    /// Retourne Some(nombreFichiers) si le format est géré, None sinon.
    let tryExtract (sourcePath: string) (targetDir: string) : int option =
        if not (File.Exists(sourcePath)) then None
        else
            try
                use disk = tryOpen sourcePath true
                match openFileSystem disk with
                | Some fs ->
                    let counter = ref 0
                    copyDirectory fs "/" targetDir counter
                    fs.Dispose()
                    Some !counter
                | None -> None
            with _ -> None

    /// Réécrit le contenu de `sourceDir` dans l'image VDI via DiscUtils.Vdi.
    /// Retourne true si réussi, false sinon.
    let tryWriteBack (sourcePath: string) (sourceDir: string) : bool =
        if not (File.Exists(sourcePath)) then false
        else
            try
                use disk = tryOpen sourcePath false
                match openFileSystem disk with
                | Some fs ->
                    copyIntoFs fs "/" sourceDir
                    deleteFsEntries fs "/" sourceDir
                    fs.Dispose()
                    true
                | None -> false
            with _ -> false
