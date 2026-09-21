namespace DiploWalker.Abstractions

open System
open System.IO
open Serilog

/// Ã‰criture de fichiers avec remplacement atomique : le contenu est d'abord
/// Ã©crit dans un fichier temporaire du mÃªme rÃ©pertoire, puis remplacÃ© d'un bloc,
/// afin de ne jamais laisser un fichier tronquÃ© ou partiellement Ã©crit en cas de
/// panne au cours de l'Ã©criture.
[<RequireQualifiedAccess>]
module AtomicFile =

    /// Ã‰crit `content` dans `path` de faÃ§on atomique (le rÃ©pertoire parent est
    /// crÃ©Ã© au besoin).
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

        try
            try
                // WriteAllText inclus dans la zone protÃ©gÃ©e : un disque plein ne
                // doit pas laisser de .tmp orphelin dans le rÃ©pertoire cible.
                File.WriteAllText(tmp, content)
                File.Replace(tmp, path, null)
            with :? FileNotFoundException ->
                File.Move(tmp, path)
        with
        | _ ->
            try
                File.Delete(tmp)
            with ex -> Log.Warning(ex, "Ã‰chec de la suppression du fichier temporaire {Tmp}", tmp)
            reraise()

