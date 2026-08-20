namespace Diplo.Disk

open System.IO
open DiscUtils
open DiscUtils.Streams

/// Couche d'accès au système de fichiers contenu dans une image disque.
///
/// Les images qcow2 sont présentées à DiscUtils comme un disque brut par-
/// dessus le pilote maison (`Qcow2Stream`) : aucune conversion n'est faite,
/// le fichier qcow2 reste le stockage de référence. Les images vhd, vhdx,
/// vmdk et raw sont ouvertes nativement par DiscUtils. Le système de fichiers
/// (ntfs, fat, ext, btrfs, …) est détecté automatiquement après enregistrement
/// des fournisseurs via `SetupHelper.SetupComplete()`.
module FsImage =

    let private toRealRel (fsPath: string) =
        fsPath
            .TrimStart('\\', '/')
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)

    let private realFrom (realRoot: string) (fsPath: string) =
        Path.Combine(realRoot, toRealRel fsPath)

    /// Enregistre une seule fois les fournisseurs DiscUtils (conteneurs et
    /// systèmes de fichiers) pour la détection automatique des formats.
    let private registrations =
        lazy (DiscUtils.Complete.SetupHelper.SetupComplete())

    let private ensureRegistered () =
        registrations.Force()

    /// Ouvre une image disque en tant que disque virtuel DiscUtils.
    let private openDisk (sourcePath: string) (readOnly: bool) : VirtualDisk =
        ensureRegistered ()
        let access = if readOnly then FileAccess.Read else FileAccess.ReadWrite
        match DiskFormat.detect sourcePath with
        | DiskFormat.Qcow2 ->
            new Raw.Disk(new Qcow2Stream(sourcePath, access), Ownership.Dispose) :> VirtualDisk
        | DiskFormat.Raw ->
            new Raw.Disk(new FileStream(sourcePath, FileMode.Open, access, FileShare.Read), Ownership.Dispose) :> VirtualDisk
        | _ ->
            VirtualDisk.OpenDisk(sourcePath, access)

    /// Retrouve le système de fichiers de l'image : volumes logiques puis
    /// physiques, détectés automatiquement parmi les fournisseurs enregistrés.
    let private openFileSystem (disk: VirtualDisk) : DiscFileSystem =
        let vm = new VolumeManager(disk)
        let logical = vm.GetLogicalVolumes() |> Seq.cast<VolumeInfo>
        let physical = vm.GetPhysicalVolumes() |> Seq.cast<VolumeInfo>
        Seq.append logical physical
        |> Seq.choose (fun v ->
            let detected = FileSystemManager.DetectFileSystems v
            if detected.Count > 0 then Some (v, detected.[0]) else None)
        |> Seq.tryHead
        |> function
            | Some (volume, fsi) -> fsi.Open volume
            | None -> failwith "Aucun système de fichiers détecté dans l'image disque"

    let rec private copyDirectory (fs: DiscFileSystem) (fsDir: string) (realRoot: string) (counter: int ref) =
        Directory.CreateDirectory(realFrom realRoot fsDir) |> ignore
        for file in fs.GetFiles fsDir do
            let target = realFrom realRoot file
            Directory.CreateDirectory(Path.GetDirectoryName target) |> ignore
            use src = fs.OpenFile(file, FileMode.Open, FileAccess.Read)
            use dst = File.Create target
            src.CopyTo dst
            counter := !counter + 1
        for sub in fs.GetDirectories fsDir do
            copyDirectory fs sub realRoot counter

    /// Extrait le contenu du système de fichiers de l'image dans `targetDir`.
    /// Retourne le nombre de fichiers extraits.
    /// Essaie Hawkynt en premier (Btrfs, XFS, HFS+), puis fallback DiscUtils.
    let extract (sourcePath: string) (targetDir: string) (readOnly: bool) : int =
        match HawkyntFs.tryExtract sourcePath targetDir with
        | Some count -> count
        | None ->
            use disk = openDisk sourcePath readOnly
            use fs = openFileSystem disk
            let counter = ref 0
            copyDirectory fs "\\" targetDir counter
            !counter

    let rec private copyIntoFs (fs: DiscFileSystem) (fsDir: string) (realDir: string) =
        for file in Directory.GetFiles realDir do
            let fsPath = fsDir.TrimEnd('\\') + "\\" + Path.GetFileName file
            use src = File.OpenRead file
            use dst = fs.OpenFile(fsPath, FileMode.Create, FileAccess.Write)
            src.CopyTo dst
        for dir in Directory.GetDirectories realDir do
            let fsPath = fsDir.TrimEnd('\\') + "\\" + Path.GetFileName dir
            if not (fs.DirectoryExists fsPath) then fs.CreateDirectory fsPath
            copyIntoFs fs fsPath dir

    let rec private deleteFsEntries (fs: DiscFileSystem) (fsDir: string) (realRoot: string) =
        for file in fs.GetFiles fsDir do
            if not (File.Exists(realFrom realRoot file)) then fs.DeleteFile file
        for sub in fs.GetDirectories fsDir do
            deleteFsEntries fs sub realRoot
            if not (Directory.Exists(realFrom realRoot sub)) then fs.DeleteDirectory(sub, false)

    /// Réécrit le contenu de `sourceDir` dans le système de fichiers de
    /// l'image : retour arrière des modifications effectuées par le conteneur.
    /// Pour le qcow2, l'écriture se fait directement dans les clusters de
    /// l'image via le pilote maison (aucune conversion, aucun intermédiaire).
    /// Essaie Hawkynt en premier (Btrfs, XFS, HFS+), puis fallback DiscUtils.
    let writeBack (sourcePath: string) (sourceDir: string) =
        if not (HawkyntFs.tryWriteBack sourcePath sourceDir) then
            use disk = openDisk sourcePath false
            use fs = openFileSystem disk
            copyIntoFs fs "\\" sourceDir
            deleteFsEntries fs "\\" sourceDir
