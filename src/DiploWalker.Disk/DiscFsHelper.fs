namespace DiploWalker.Disk

open System
open System.IO
open System.Text
open DiscUtils

/// OpÃ©rations communes sur les systÃ¨mes de fichiers DiscUtils (extraction,
/// rÃ©Ã©criture, suppression) partagÃ©es par les adaptateurs VDI, DMG et le
/// fallback gÃ©nÃ©rique de FsImage.
///
/// La rÃ©solution des chemins HÃ”TES est confinÃ©e au rÃ©pertoire de staging via
/// `realFrom` : les noms internes Ã  l'image ne sont pas fiables (ils peuvent
/// contenir Â« .. Â» ou des prÃ©fixes enracinÃ©s type Â« C:\x Â») ; sans ce garde-fou,
/// un service (LocalSystem) Ã©crirait n'importe oÃ¹ sur l'hÃ´te. Tous les
/// adaptateurs passent donc par `toHostPath`, jamais par une concatÃ©nation brute.
module DiscFsHelper =

    /// Nettoie un chemin interne au FS image en sÃ©parateur hÃ´te.
    let toRealRel (fsPath: string) =
        let isSeparator (c: char) = c = '\\' || c = '/'
        let buffer = StringBuilder(fsPath.Length)
        let mutable lastWasSeparator = false

        for c in fsPath.TrimStart('\\', '/') do
            if isSeparator c then
                // Les séparateurs consécutifs sont réduits à un seul : les noms
                // internes aux images contiennent fréquemment « a//b » ou des
                // séparateurs mélangés.
                if not lastWasSeparator then
                    buffer.Append(Path.DirectorySeparatorChar) |> ignore
            else
                buffer.Append c |> ignore

            lastWasSeparator <- isSeparator c

        buffer.ToString()

    /// Vrai si un chemin brut porte une lettre de lecteur Windows (« C:\x »,
    /// « C:/x »). Les adaptateurs lisent des images créées sur d'autres
    /// systèmes : sur un hôte Unix `Path.IsPathRooted` considère un tel chemin
    /// comme relatif, il faut donc le détecter explicitement pour ne jamais le
    /// confondre avec un chemin relatif confinable dans le staging.
    let hasWindowsDriveLetter (fsPath: string) =
        fsPath.Length >= 3
        && Char.IsLetter fsPath[0]
        && fsPath[1] = ':'
        && (fsPath[2] = '\\' || fsPath[2] = '/')

    /// Convertit un chemin INTERNE au systÃ¨me de fichiers image en chemin hÃ´te,
    /// confinÃ© Ã  `realRoot`. LÃ¨ve une exception en cas de nom enracinÃ©, de
    /// traversal Â« .. Â» ou si le rÃ©sultat sort du rÃ©pertoire de staging.
    let realFrom (realRoot: string) (fsPath: string) =
        let rel = toRealRel fsPath

        let rooted =
            hasWindowsDriveLetter fsPath
            ||
            (try
                 Path.IsPathRooted(rel)
             with _ ->
                 true)

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

        // Le chemin doit rester dans le staging ; la RACINE elle-mÃªme est
        // autorisÃ©e (l'extraction dÃ©marre par Â« \ Â»).
        let inside =
            combined.Equals(rootFull, StringComparison.OrdinalIgnoreCase)
            || combined.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase)

        if not inside then
            invalidArg "fsPath" (sprintf "Le chemin interne sort du rÃ©pertoire de staging : '%s'" fsPath)

        combined

    /// Retrouve le systÃ¨me de fichiers d'un disque virtuel : volumes logiques
    /// puis physiques, dÃ©tectÃ©s automatiquement. Retourne None si aucun FS.
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

    /// Copie rÃ©cursivement `fsDir` (FS image) vers l'hÃ´te. `toHostPath` convertit
    /// un chemin image en chemin hÃ´te confinÃ©. IncrÃ©mente `counter` par fichier.
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

    /// Copie rÃ©cursivement `realDir` (hÃ´te) vers `fsDir` (FS image).
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

    /// Supprime les entrÃ©es du FS image absentes de l'hÃ´te. `toHostPath`
    /// convertit et confine un chemin image en chemin hÃ´te.
    let rec deleteFsEntries (toHostPath: string -> string) (fs: DiscFileSystem) (fsDir: string) =
        for file in fs.GetFiles fsDir |> Seq.toArray do
            if not (File.Exists(toHostPath file)) then
                fs.DeleteFile file

        for sub in fs.GetDirectories fsDir |> Seq.toArray do
            deleteFsEntries toHostPath fs sub

            if not (Directory.Exists(toHostPath sub)) then
                fs.DeleteDirectory(sub, false)

