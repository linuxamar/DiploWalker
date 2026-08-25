namespace Diplo.Disk

open System
open System.Collections.Generic
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
        fsPath.TrimStart('\\', '/').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)

    let private realFrom (realRoot: string) (fsPath: string) =
        Path.Combine(realRoot, toRealRel fsPath)

    /// Enregistre une seule fois les fournisseurs DiscUtils (conteneurs et
    /// systèmes de fichiers) pour la détection automatique des formats.
    let private registrations = lazy (DiscUtils.Complete.SetupHelper.SetupComplete())

    let private ensureRegistered () = registrations.Force()

    /// Ouvre une image disque en tant que disque virtuel DiscUtils.
    let private openDisk (sourcePath: string) (readOnly: bool) : VirtualDisk =
        ensureRegistered ()
        let access = if readOnly then FileAccess.Read else FileAccess.ReadWrite

        match DiskFormat.detect sourcePath with
        | DiskFormat.Qcow2 -> new Raw.Disk(new Qcow2Stream(sourcePath, access), Ownership.Dispose) :> VirtualDisk
        | DiskFormat.Qcow1 -> new Raw.Disk(new Qcow1Stream(sourcePath, access), Ownership.Dispose) :> VirtualDisk
        | DiskFormat.Vdi ->
            new Vdi.Disk(new FileStream(sourcePath, FileMode.Open, access, FileShare.Read), Ownership.Dispose)
            :> VirtualDisk
        | DiskFormat.Parallels ->
            new Raw.Disk(new ParallelsStream(sourcePath, access), Ownership.Dispose) :> VirtualDisk
        | DiskFormat.Raw ->
            new Raw.Disk(new FileStream(sourcePath, FileMode.Open, access, FileShare.Read), Ownership.Dispose)
            :> VirtualDisk
        | _ -> VirtualDisk.OpenDisk(sourcePath, access)

    /// Retrouve le système de fichiers de l'image : volumes logiques puis
    /// physiques, détectés automatiquement parmi les fournisseurs enregistrés.
    let private openFileSystem (disk: VirtualDisk) : DiscFileSystem =
        let vm = new VolumeManager(disk)

        let candidates =
            Seq.append
                (vm.GetLogicalVolumes() |> Seq.cast<VolumeInfo>)
                (vm.GetPhysicalVolumes() |> Seq.cast<VolumeInfo>)
            |> Seq.toArray

        candidates
        |> Array.tryFind (fun v -> FileSystemManager.DetectFileSystems(v).Count > 0)
        |> function
            | Some volume ->
                let fsi = FileSystemManager.DetectFileSystems(volume).[0]
                fsi.Open(volume)
            | None -> failwith "Aucun système de fichiers détecté dans l'image disque"

    let rec private copyDirectory (fs: DiscFileSystem) (fsDir: string) (realRoot: string) (counter: int ref) =
        Directory.CreateDirectory(realFrom realRoot fsDir) |> ignore

        for file in fs.GetFiles fsDir |> Seq.toArray do
            let target = realFrom realRoot file
            Directory.CreateDirectory(Path.GetDirectoryName target) |> ignore
            use src = fs.OpenFile(file, FileMode.Open, FileAccess.Read)
            use dst = File.Create target
            src.CopyTo dst
            counter := !counter + 1

        for sub in fs.GetDirectories fsDir |> Seq.toArray do
            copyDirectory fs sub realRoot counter

    /// Extrait le contenu du système de fichiers de l'image dans `targetDir`.
    /// Retourne le nombre de fichiers extraits.
    /// Essaie les adaptateurs spécialisés (Hawkynt Btrfs/XFS/HFS+, VDI, DMG)
    /// avant de fallback sur DiscUtils générique.
    let extract (sourcePath: string) (targetDir: string) (readOnly: bool) : int =
        match HawkyntFs.tryExtract sourcePath targetDir with
        | Some count -> count
        | None ->
            match VdiFs.tryExtract sourcePath targetDir with
            | Some count -> count
            | None ->
                match DmgFs.tryExtract sourcePath targetDir with
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

            if not (fs.DirectoryExists fsPath) then
                fs.CreateDirectory fsPath

            copyIntoFs fs fsPath dir

    let rec private deleteFsEntries (fs: DiscFileSystem) (fsDir: string) (realRoot: string) =
        for file in fs.GetFiles fsDir |> Seq.toArray do
            if not (File.Exists(realFrom realRoot file)) then
                fs.DeleteFile file

        for sub in fs.GetDirectories fsDir |> Seq.toArray do
            deleteFsEntries fs sub realRoot

            if not (Directory.Exists(realFrom realRoot sub)) then
                fs.DeleteDirectory(sub, false)

    /// Réécrit le contenu de `sourceDir` dans le système de fichiers de
    /// l'image : retour arrière des modifications effectuées par le conteneur.
    /// Essaie les adaptateurs spécialisés avant fallback DiscUtils générique.
    let writeBack (sourcePath: string) (sourceDir: string) =
        if HawkyntFs.tryWriteBack sourcePath sourceDir then
            ()
        elif VdiFs.tryWriteBack sourcePath sourceDir then
            ()
        elif DmgFs.tryWriteBack sourcePath sourceDir then
            ()
        else
            use disk = openDisk sourcePath false
            use fs = openFileSystem disk
            copyIntoFs fs "\\" sourceDir
            deleteFsEntries fs "\\" sourceDir

    // ── Création d'image disque ─────────────────────────────────────

    let rec private calcDirSize (dir: string) : int64 =
        let mutable size = 0L

        for file in Directory.GetFiles dir do
            size <- size + (FileInfo(file).Length)

        for sub in Directory.GetDirectories dir do
            size <- size + calcDirSize sub

        size

    /// Copie le contenu de `realDir` dans le système de fichiers DiscUtils
    /// à la racine `fsDir`.
    let rec private copyDirIntoFs (fs: DiscFileSystem) (fsDir: string) (realDir: string) =
        for file in Directory.GetFiles realDir do
            let fsPath = fsDir.TrimEnd('\\') + "\\" + Path.GetFileName file
            use src = File.OpenRead file
            use dst = fs.OpenFile(fsPath, FileMode.Create, FileAccess.Write)
            src.CopyTo dst

        for dir in Directory.GetDirectories realDir do
            let fsPath = fsDir.TrimEnd('\\') + "\\" + Path.GetFileName dir

            if not (fs.DirectoryExists fsPath) then
                fs.CreateDirectory fsPath

            copyDirIntoFs fs fsPath dir

    /// Ouvre ou crée un disque virtuel dans le format donné.
    /// Si le fichier existe déjà, il est ouvert ; sinon, un nouveau disque
    /// est créé avec la taille virtuelle spécifiée (en octets).
    let private openOrCreateDisk (path: string) (format: DiskFormat.Format) (virtualSize: int64) : VirtualDisk =
        ensureRegistered ()

        if File.Exists path then
            VirtualDisk.OpenDisk(path, FileAccess.ReadWrite)
        else
            match format with
            | DiskFormat.Raw ->
                do
                    use fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None)
                    fs.SetLength(virtualSize)
                    fs.Flush()
                new Raw.Disk(new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read), Ownership.Dispose) :> VirtualDisk
            | _ ->
                let typeName, variant =
                    match format with
                    | DiskFormat.Vhd -> "vhd", "dynamic"
                    | DiskFormat.Vhdx -> "vhdx", "dynamic"
                    | DiskFormat.Vmdk -> "vmdk", "dynamic"
                    | DiskFormat.Vdi -> "vdi", "dynamic"
                    | _ -> "raw", null

                VirtualDisk.CreateDisk(
                    typeName,
                    variant,
                    path,
                    virtualSize,
                    Nullable<Geometry>(),
                    Dictionary<string, string>()
                )

    /// Formate le disque avec NTFS et copie le contenu de `sourceDir`
    /// dans l'image créée. Retourne le chemin du fichier image.
    ///
    /// Formats supportés en écriture : VHD, VHDX, VMDK, VDI, Raw.
    /// Les formats QCOW1, QCOW2, Parallels et DMG ne sont pas supportés
    /// en création (pas de factory publique dans DiscUtils).
    let create (sourceDir: string) (destPath: string) (format: DiskFormat.Format) : string =
        if not (Directory.Exists sourceDir) then
            invalidArg "sourceDir" (sprintf "Le répertoire source n'existe pas : '%s'" sourceDir)

        match format with
        | DiskFormat.Qcow1
        | DiskFormat.Qcow2
        | DiskFormat.Parallels
        | DiskFormat.Dmg
        | DiskFormat.Unknown ->
            invalidArg "format" (sprintf "Le format '%s' n'est pas supporté en création" (DiskFormat.toString format))
        | _ -> ()

        let dirSize = calcDirSize sourceDir
        let minSize = 64L * 1024L * 1024L
        let virtualSize = max minSize (dirSize + dirSize / 10L)

        let parentDir = Path.GetDirectoryName(destPath)

        if not (String.IsNullOrEmpty parentDir) then
            Directory.CreateDirectory(parentDir) |> ignore

        let fileCreated = File.Exists(destPath)

        try
            use disk = openOrCreateDisk destPath format virtualSize

            let volumeManager = VolumeManager(disk)

            let physicalVolumes =
                volumeManager.GetPhysicalVolumes() |> Seq.cast<VolumeInfo> |> Seq.toList

            if physicalVolumes.IsEmpty then
                failwith "Aucun volume physique détecté dans le disque créé"

            let pv = physicalVolumes.Head
            Ntfs.NtfsFileSystem.Format(pv, "Diplo", Ntfs.NtfsFormatOptions()) |> ignore
            use fs = openFileSystem disk
            copyDirIntoFs fs "\\" sourceDir
            destPath
        with ex ->
            try
                if File.Exists destPath then
                    File.Delete destPath
            with _ ->
                ()

            reraise ()
