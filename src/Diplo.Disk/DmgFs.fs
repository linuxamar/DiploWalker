namespace Diplo.Disk

open System.IO
open DiscUtils
open DiscUtils.Streams
open Serilog

/// Adaptateur DMG (Apple Disk Image) pour extraction et reecriture.
/// Lecture via DiscUtils.Dmg (UDIF compresse), ecriture via Hawkynt DmgWriter.
module DmgFs =
    open DiscFsHelper

    let private tryOpenDisk (sourcePath: string) =
        try
            let fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read)

            try
                let disk = new Dmg.Disk(fs, Ownership.Dispose)
                Some(disk :> VirtualDisk)
            with ex ->
                fs.Dispose()
                reraise ()
        with
        | :? IOException -> None
        | :? System.NotSupportedException -> None
        | ex ->
            Log.Warning(ex, "Erreur inattendue ouverture DMG pour {Path}", sourcePath)
            None

    /// Tente d'extraire une image DMG via DiscUtils.
    let tryExtract (sourcePath: string) (targetDir: string) : int option =
        if not (File.Exists(sourcePath)) then
            None
        else
            let diskOpt = tryOpenDisk sourcePath

            match diskOpt with
            | Some disk ->
                use _disk = disk
                let toHostPath = realFrom targetDir

                match openFileSystem disk with
                | Some fs ->
                    use _fs = fs
                    let counter = ref 0
                    copyDirectory toHostPath fs "/" counter
                    Some !counter
                | None -> None
            | None -> None

    /// Reecrit le contenu de sourceDir dans l'image DMG.
    let tryWriteBack (_sourcePath: string) (_sourceDir: string) : bool = false
