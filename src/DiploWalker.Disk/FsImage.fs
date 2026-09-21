namespace DiploWalker.Disk

open System
open System.Collections.Generic
open System.IO
open DiscUtils
open DiscUtils.Streams
open Serilog

/// Couche d'accÃ¨s au systÃ¨me de fichiers contenu dans une image disque.
///
/// Les images qcow2 sont prÃ©sentÃ©es Ã  DiscUtils comme un disque brut par-
/// dessus le pilote maison (`Qcow2Stream`) : aucune conversion n'est faite,
/// le fichier qcow2 reste le stockage de rÃ©fÃ©rence. Les images vhd, vhdx,
/// vmdk et raw sont ouvertes nativement par DiscUtils. Le systÃ¨me de fichiers
/// (ntfs, fat, ext, btrfs, â€¦) est dÃ©tectÃ© automatiquement aprÃ¨s enregistrement
/// des fournisseurs via `SetupHelper.SetupComplete()`.
module FsImage =
    open DiscFsHelper

    /// Enregistre une seule fois les fournisseurs DiscUtils (conteneurs et
    /// systÃ¨mes de fichiers) pour la dÃ©tection automatique des formats.
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

    /// Retrouve le systÃ¨me de fichiers de l'image via l'adaptateur partagÃ©.
    let private openFileSystem (disk: VirtualDisk) : DiscFileSystem =
        match DiscFsHelper.openFileSystem disk with
        | Some fs -> fs
        | None -> failwith "Aucun systÃ¨me de fichiers dÃ©tectÃ© dans l'image disque"

    /// Extrait le contenu du systÃ¨me de fichiers de l'image dans `targetDir`.
    /// Retourne le nombre de fichiers extraits.
    /// Essaie les adaptateurs spÃ©cialisÃ©s (Hawkynt Btrfs/XFS/HFS+, VDI, DMG)
    /// avant de fallback sur DiscUtils gÃ©nÃ©rique.
    let extractCore (sourcePath: string) (targetDir: string) (readOnly: bool) : int =
        match IsoFs.tryExtract sourcePath targetDir with
        | Some count -> count
        | None ->
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
                        DiscFsHelper.copyDirectory (realFrom targetDir) fs "\\" counter
                        !counter

    let extract (sourcePath: string) (targetDir: string) (readOnly: bool) : Result<int, string> =
        BinaryIo.protect (fun () -> extractCore sourcePath targetDir readOnly)

    /// RÃ©Ã©crit le contenu de `sourceDir` dans le systÃ¨me de fichiers de
    /// l'image : retour arriÃ¨re des modifications effectuÃ©es par le conteneur.
    /// Essaie les adaptateurs spÃ©cialisÃ©s avant fallback DiscUtils gÃ©nÃ©rique.
    let writeBackCore (sourcePath: string) (sourceDir: string) =
        if HawkyntFs.tryWriteBack sourcePath sourceDir then
            ()
        elif VdiFs.tryWriteBack sourcePath sourceDir then
            ()
        elif DmgFs.tryWriteBack sourcePath sourceDir then
            ()
        else
            use disk = openDisk sourcePath false
            use fs = openFileSystem disk
            DiscFsHelper.copyIntoFs fs "\\" sourceDir
            DiscFsHelper.deleteFsEntries (realFrom sourceDir) fs "\\"

    let writeBack (sourcePath: string) (sourceDir: string) : Result<unit, string> =
        BinaryIo.protect (fun () -> writeBackCore sourcePath sourceDir)

    // â”€â”€ CrÃ©ation d'image disque â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    let rec private calcDirSize (dir: string) : int64 =
        let mutable size = 0L

        for file in Directory.GetFiles dir do
            size <- size + (FileInfo(file).Length)

        for sub in Directory.GetDirectories dir do
            size <- size + calcDirSize sub

        size

    /// Ouvre ou crÃ©e un disque virtuel dans le format donnÃ©.
    /// Si le fichier existe dÃ©jÃ , il est ouvert ; sinon, un nouveau disque
    /// est crÃ©Ã© avec la taille virtuelle spÃ©cifiÃ©e (en octets).
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
    /// dans l'image crÃ©Ã©e. Retourne le chemin du fichier image.
    ///
    /// Formats supportÃ©s en Ã©criture : VHD, VHDX, VMDK, VDI, Raw.
    /// Les formats QCOW1, QCOW2, Parallels et DMG ne sont pas supportÃ©s
    /// en crÃ©ation (pas de factory publique dans DiscUtils).
    let createCore (sourceDir: string) (destPath: string) (format: DiskFormat.Format) : string =
        if not (Directory.Exists sourceDir) then
            invalidArg "sourceDir" (sprintf "Le rÃ©pertoire source n'existe pas : '%s'" sourceDir)

        if format = DiskFormat.Iso then
            // CrÃ©ation ISO9660 niveau 1 directement (pas de disque virtuel).
            IsoFs.createCore sourceDir destPath |> ignore
            destPath
        else
            match format with
            | DiskFormat.Qcow1
            | DiskFormat.Qcow2
            | DiskFormat.Parallels
            | DiskFormat.Dmg
            | DiskFormat.Unknown ->
                invalidArg "format" (sprintf "Le format '%s' n'est pas supportÃ© en crÃ©ation" (DiskFormat.toString format))
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
                    failwith "Aucun volume physique dÃ©tectÃ© dans le disque crÃ©Ã©"

                let pv = physicalVolumes.Head
                Ntfs.NtfsFileSystem.Format(pv, "Diplo", Ntfs.NtfsFormatOptions()) |> ignore
                use fs = openFileSystem disk
                DiscFsHelper.copyIntoFs fs "\\" sourceDir
                destPath
            with ex ->
                // Ne supprimer l'image que si NOUS l'avons crÃ©Ã©e : un Ã©chec de
                // formatage sur une image EXISTANTE (ouverte par openOrCreateDisk)
                // ne doit jamais la dÃ©truire.
                if fileCreated then
                    try
                        if File.Exists destPath then
                            File.Delete destPath
                    with cleanupEx ->
                        Log.Warning(cleanupEx, "Ã‰chec de la suppression du fichier temporaire {DestPath}", destPath)

                reraise ()

    let create (sourceDir: string) (destPath: string) (format: DiskFormat.Format) : Result<string, string> =
        try Ok(createCore sourceDir destPath format)
        with ex -> Error ex.Message

