namespace Diplo.Abstractions

open System
open System.IO

/// Écriture de fichiers avec remplacement atomique : le contenu est d'abord
/// écrit dans un fichier temporaire du même répertoire, puis remplacé d'un bloc,
/// afin de ne jamais laisser un fichier tronqué ou partiellement écrit en cas de
/// panne au cours de l'écriture.
[<RequireQualifiedAccess>]
module AtomicFile =

    /// Écrit `content` dans `path` de façon atomique (le répertoire parent est
    /// créé au besoin).
    let write (path: string) (content: string) : unit =
        let dir = Path.GetDirectoryName(path)

        if not (String.IsNullOrEmpty dir) && not (Directory.Exists dir) then
            Directory.CreateDirectory(dir) |> ignore

        let name = Path.GetFileName(path)

        let tmp =
            if String.IsNullOrEmpty dir then
                name + "." + Guid.NewGuid().ToString("N") + ".tmp"
            else
                Path.Combine(dir, name + "." + Guid.NewGuid().ToString("N") + ".tmp")

        File.WriteAllText(tmp, content)

        try
            File.Replace(tmp, path, null)
        with :? FileNotFoundException ->
            File.Move(tmp, path)
