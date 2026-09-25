namespace DiploWalker.TestHelpers

open System
open System.IO

/// Helpers partagÃ©s pour les projets de tests.
module TestHelpers =

    /// CrÃ©e un rÃ©pertoire temporaire unique pour les tests.
    let createTempDir (prefix: string) =
        let dir =
            Path.Combine(Path.GetTempPath(), sprintf "diplo-%s-%s" prefix (Guid.NewGuid().ToString("N")))

        Directory.CreateDirectory(dir) |> ignore
        dir

    /// Supprime un rÃ©pertoire de maniÃ¨re sÃ»re.
    let cleanupDir (dir: string) =
        try
            if Directory.Exists(dir) then
                Directory.Delete(dir, true)
        with _ ->
            ()

