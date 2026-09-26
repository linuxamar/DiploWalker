namespace DiploWalker.Disk

open System
open System.IO

/// Classe de base des flux d'accès aléatoire sur les images QCOW1, QCOW2 et
/// Parallels : présente l'image comme un disque brut virtuel consommable par
/// DiscUtils. Factorise le cycle de vie du FileStream sous-jacent (avec son
/// partage restrictif), les propriétés communes et la libération idempotente.
/// La taille virtuelle, la lecture/écriture des secteurs et le
/// redimensionnement restent spécifiques à chaque format.
[<AbstractClass>]
type RawImageStream(path: string, access: FileAccess) =
    inherit Stream()

    // FileShare.Read : un second accès en écriture doit échouer franchement
    // plutôt que corrompre silencieusement les métadonnées.
    let fs = new FileStream(path, FileMode.Open, access, FileShare.Read)
    let mutable released = false

    /// Flux de fichier sous-jacent, exposé aux sous-classes pour les accès
    /// disque (lecture/écriture des secteurs, redimensionnement).
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

