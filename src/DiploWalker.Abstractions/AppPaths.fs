module DiploWalker.Abstractions.AppPaths

open System
open System.IO

/// Surcharge de la racine des données, mainly pour les tests et les déploiages
/// multi-rôles : `DIPLO_DATA_ROOT` si défini, sinon la valeur par défaut de la
/// plateforme.
let dataRootFromEnvironment () =
    let dir = Environment.GetEnvironmentVariable "DIPLO_DATA_ROOT"
    if isNull dir || String.IsNullOrWhiteSpace dir then "" else dir

/// Racine par défaut des données applicatives partagées entre comptes (état,
/// secrets, staging, journaux) : `%ProgramData%\Diplo` sous Windows,
/// `$XDG_DATA_HOME/Diplo` (à défaut `~/.local/share/Diplo`) ailleurs.
///
/// `Environment.SpecialFolder.CommonApplicationData` n'est pas portable : sous
/// Linux il vaut `/usr/share`, répertoire système que le service ne peut pas
/// écrire sans privilèges root.
let defaultDataRoot () =
    let overridden = dataRootFromEnvironment ()

    if overridden <> "" then
        overridden
    elif OperatingSystem.IsWindows () then
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Diplo")
    else
        let xdg = Environment.GetEnvironmentVariable "XDG_DATA_HOME"

        let baseDir =
            if not (String.IsNullOrWhiteSpace xdg) then
                xdg
            else
                let home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                let homeDir = if String.IsNullOrWhiteSpace home then "/tmp" else home
                Path.Combine(homeDir, ".local", "share")

        Path.Combine(baseDir, "Diplo")

/// Racine courante des données applicatives. Résolue une seule fois au démarrage
/// du processus : `DIPLO_DATA_ROOT` doit donc être définie avant tout accès.
let dataRoot () = defaultDataRoot ()

/// Racine des données applicatives *par utilisateur* (clé de chiffrement des
/// secrets) : `%LocalAppData%\Diplo` sous Windows, identique à
/// [`dataRoot`](dataRoot) ailleurs (les données Unix sont déjà cloisonnées par
/// compte). Surchargeable par `DIPLO_DATA_ROOT` comme [`dataRoot`](dataRoot).
let userRoot () =
    let overridden = dataRootFromEnvironment ()

    if overridden <> "" then
        overridden
    elif OperatingSystem.IsWindows() then
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Diplo")
    else
        dataRoot ()

/// Sous-répertoire de [`dataRoot`](dataRoot), créé s'il est absent.
let dataDir (name: string) =
    let dir = Path.Combine(dataRoot (), name)
    Directory.CreateDirectory dir |> ignore
    dir
