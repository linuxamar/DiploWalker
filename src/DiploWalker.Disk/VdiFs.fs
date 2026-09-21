namespace DiploWalker.Disk

open System.IO
open DiscUtils
open DiscUtils.Streams
open Serilog

/// Adaptateur DiscUtils.Vdi pour l'extraction et la rÃ©Ã©criture
/// de systÃ¨mes de fichiers contenus dans des images VDI (VirtualBox).
///
/// DiscUtils.Vdi.Disk gÃ¨re nativement la lecture et l'Ã©criture VDI
/// (fixe et dynamique). L'approche est identique Ã  qcow2 : on passe
/// un Stream au disque DiscUtils, puis on extrait le FS contenu.
module VdiFs =
    open DiscFsHelper

    let private tryOpen (sourcePath: string) (readOnly: bool) =
        let access = if readOnly then FileAccess.Read else FileAccess.ReadWrite
        let fs = new FileStream(sourcePath, FileMode.Open, access, FileShare.Read)

        try
            let disk = new Vdi.Disk(fs, Ownership.Dispose)
            disk :> VirtualDisk
        with ex ->
            fs.Dispose()
            reraise ()

    /// Tente d'extraire une image VDI via DiscUtils.Vdi.
    /// Retourne Some(nombreFichiers) si le format est gÃ©rÃ©, None sinon.
    let tryExtract (sourcePath: string) (targetDir: string) : int option =
        if not (File.Exists(sourcePath)) then
            None
        else
            try
                use disk = tryOpen sourcePath true
                let toHostPath = realFrom targetDir

                match openFileSystem disk with
                | Some fs ->
                    use _fs = fs
                    let counter = ref 0
                    copyDirectory toHostPath fs "/" counter
                    Some !counter
                | None -> None
            with
            | :? IOException as ex ->
                Log.Warning(ex, "Ã‰chec extraction VDI pour {Path}", sourcePath)
                None
            | ex ->
                Log.Warning(ex, "Erreur inattendue extraction VDI pour {Path}", sourcePath)
                None

    /// RÃ©Ã©crit le contenu de `sourceDir` dans l'image VDI via DiscUtils.Vdi.
    /// Retourne true si rÃ©ussi, false sinon.
    let tryWriteBack (sourcePath: string) (sourceDir: string) : bool =
        if not (File.Exists(sourcePath)) then
            false
        else
            try
                use disk = tryOpen sourcePath false
                let toHostPath = realFrom sourceDir

                match openFileSystem disk with
                | Some fs ->
                    use _fs = fs
                    copyIntoFs fs "/" sourceDir
                    deleteFsEntries toHostPath fs "/"
                    true
                | None -> false
            with
            | :? IOException as ex ->
                Log.Warning(ex, "Ã‰chec rÃ©Ã©criture VDI pour {Path}", sourcePath)
                false
            | ex ->
                Log.Warning(ex, "Erreur inattendue rÃ©Ã©criture VDI pour {Path}", sourcePath)
                false

