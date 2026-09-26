namespace DiploWalker.TestHelpers

open System
open System.IO

/// Helpers partagés pour les projets de tests.
module TestHelpers =

    /// Crée un répertoire temporaire unique pour les tests.
    let createTempDir (prefix: string) =
        let dir =
            Path.Combine(Path.GetTempPath(), sprintf "diplo-%s-%s" prefix (Guid.NewGuid().ToString("N")))

        Directory.CreateDirectory(dir) |> ignore
        dir

    /// Supprime un répertoire de manière sûre.
    let cleanupDir (dir: string) =
        try
            if Directory.Exists(dir) then
                Directory.Delete(dir, true)
        with _ ->
            ()

