namespace DiploWalker.Disk

open System
open System.IO

/// Classe de base des flux d'accÃ¨s alÃ©atoire sur les images QCOW1, QCOW2 et
/// Parallels : prÃ©sente l'image comme un disque brut virtuel consommable par
/// DiscUtils. Factorise le cycle de vie du FileStream sous-jacent (avec son
/// partage restrictif), les propriÃ©tÃ©s communes et la libÃ©ration idempotente.
/// La taille virtuelle, la lecture/Ã©criture des secteurs et le
/// redimensionnement restent spÃ©cifiques Ã  chaque format.
[<AbstractClass>]
type RawImageStream(path: string, access: FileAccess) =
    inherit Stream()

    // FileShare.Read : un second accÃ¨s en Ã©criture doit Ã©chouer franchement
    // plutÃ´t que corrompre silencieusement les mÃ©tadonnÃ©es.
    let fs = new FileStream(path, FileMode.Open, access, FileShare.Read)
    let mutable released = false

    /// Flux de fichier sous-jacent, exposÃ© aux sous-classes pour les accÃ¨s
    /// disque (lecture/Ã©criture des secteurs, redimensionnement).
    member internal _.UnderlyingStream = fs

    override _.CanRead = true
    override _.CanSeek = true
    override _.CanWrite = access <> FileAccess.Read

    override _.Flush() = fs.Flush()

    override _.Dispose(disposing) =
        if not released then
            released <- true

            if disposing then
                try
                    fs.Flush()
                finally
                    fs.Dispose()

        base.Dispose(disposing)

