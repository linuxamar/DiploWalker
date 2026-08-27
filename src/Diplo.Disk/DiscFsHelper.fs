namespace Diplo.Disk

open System
open System.IO
open DiscUtils

/// Opérations communes sur les systèmes de fichiers DiscUtils (extraction,
/// réécriture, suppression) partagées par les adaptateurs VDI, DMG et le
/// fallback générique de FsImage.
///
/// La résolution des chemins HÔTES est confinée au répertoire de staging via
/// `realFrom` : les noms internes à l'image ne sont pas fiables (ils peuvent
/// contenir « .. » ou des préfixes enracinés type « C:\x ») ; sans ce garde-fou,
/// un service (LocalSystem) écrirait n'importe où sur l'hôte. Tous les
/// adaptateurs passent donc par `toHostPath`, jamais par une concaténation brute.
module DiscFsHelper =

    /// Nettoie un chemin interne au FS image en séparateur hôte.
    let toRealRel (fsPath: string) =
        fsPath.TrimStart('\\', '/').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)

    /// Convertit un chemin INTERNE au système de fichiers image en chemin hôte,
    /// confiné à `realRoot`. Lève une exception en cas de nom enraciné, de
    /// traversal « .. » ou si le résultat sort du répertoire de staging.
    let realFrom (realRoot: string) (fsPath: string) =
        let rel = toRealRel fsPath

        let rooted =
            try
                Path.IsPathRooted(rel)
            with _ ->
                true

        let hasTraversal =
            rel.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            |> Array.exists (fun seg -> seg = "..")

        if rooted || hasTraversal then
            invalidArg "fsPath" (sprintf "Chemin interne d'image invalide : '%s'" fsPath)

        let combined =
            try
                Path.GetFullPath(Path.Combine(realRoot, rel))
            with _ ->
                invalidArg "fsPath" (sprintf "Chemin interne d'image invalide : '%s'" fsPath)

        let rootFull = Path.GetFullPath(realRoot)

        let rootWithSep =
            realRoot.TrimEnd(Path.DirectorySeparatorChar) + string Path.DirectorySeparatorChar

        // Le chemin doit rester dans le staging ; la RACINE elle-même est
        // autorisée (l'extraction démarre par « \ »).
        let inside =
            combined.Equals(rootFull, StringComparison.OrdinalIgnoreCase)
            || combined.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase)

        if not inside then
            invalidArg "fsPath" (sprintf "Le chemin interne sort du répertoire de staging : '%s'" fsPath)

        combined

    /// Retrouve le système de fichiers d'un disque virtuel : volumes logiques
    /// puis physiques, détectés automatiquement. Retourne None si aucun FS.
    let openFileSystem (disk: VirtualDisk) : DiscFileSystem option =
        let vm = new VolumeManager(disk)
        let logical = vm.GetLogicalVolumes() |> Seq.cast<VolumeInfo>
        let physical = vm.GetPhysicalVolumes() |> Seq.cast<VolumeInfo>

        Seq.append logical physical
        |> Seq.choose (fun v ->
            let detected = FileSystemManager.DetectFileSystems v
            if detected.Count > 0 then Some(v, detected.[0]) else None)
        |> Seq.tryHead
        |> function
            | Some(volume, fsi) -> Some(fsi.Open volume)
            | None -> None

    /// Copie récursivement `fsDir` (FS image) vers l'hôte. `toHostPath` convertit
    /// un chemin image en chemin hôte confiné. Incrémente `counter` par fichier.
    let rec copyDirectory (toHostPath: string -> string) (fs: DiscFileSystem) (fsDir: string) (counter: int ref) =
        Directory.CreateDirectory(toHostPath fsDir) |> ignore

        for file in fs.GetFiles fsDir |> Seq.toArray do
            let target = toHostPath file
            Directory.CreateDirectory(Path.GetDirectoryName target) |> ignore
            use src = fs.OpenFile(file, FileMode.Open, FileAccess.Read)
            use dst = File.Create target
            src.CopyTo dst
            counter := !counter + 1

        for sub in fs.GetDirectories fsDir |> Seq.toArray do
            copyDirectory toHostPath fs sub counter

    /// Copie récursivement `realDir` (hôte) vers `fsDir` (FS image).
    let rec copyIntoFs (fs: DiscFileSystem) (fsDir: string) (realDir: string) =
        for file in Directory.GetFiles realDir do
            let fsPath = fsDir.TrimEnd('\\', '/') + "\\" + Path.GetFileName file
            use src = File.OpenRead file
            use dst = fs.OpenFile(fsPath, FileMode.Create, FileAccess.Write)
            src.CopyTo dst

        for dir in Directory.GetDirectories realDir do
            let fsPath = fsDir.TrimEnd('\\', '/') + "\\" + Path.GetFileName dir

            if not (fs.DirectoryExists fsPath) then
                fs.CreateDirectory fsPath

            copyIntoFs fs fsPath dir

    /// Supprime les entrées du FS image absentes de l'hôte. `toHostPath`
    /// convertit et confine un chemin image en chemin hôte.
    let rec deleteFsEntries (toHostPath: string -> string) (fs: DiscFileSystem) (fsDir: string) =
        for file in fs.GetFiles fsDir |> Seq.toArray do
            if not (File.Exists(toHostPath file)) then
                fs.DeleteFile file

        for sub in fs.GetDirectories fsDir |> Seq.toArray do
            deleteFsEntries toHostPath fs sub

            if not (Directory.Exists(toHostPath sub)) then
                fs.DeleteDirectory(sub, false)
