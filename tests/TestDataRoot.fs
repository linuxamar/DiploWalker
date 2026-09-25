module DiploWalker.TestDataRoot

open System
open System.IO
open Xunit

/// Racine des données applicatives utilisée par la session de tests courante.
let dataRoot = Path.Combine(Path.GetTempPath(), "diplo-test-data")

/// Fixture d'assembly : redirige `AppPaths.dataRoot` (via `DIPLO_DATA_ROOT`)
/// avant l'exécution du premier test du projet.
///
/// Sans cela la suite écrit dans `%ProgramData%\Diplo` sous Windows et
/// `$XDG_DATA_HOME/Diplo` ailleurs : l'état résiduel (état des montages,
/// identifiants de registres, dossier de staging) rend les tests de purge et
/// de persistance dépendants de la machine, et échoue outright quand le
/// répertoire personnel n'est pas inscriptible (conteneur CI, utilisateur non
/// privilégié sans home).
type DataRootFixture() =
    do
        Environment.SetEnvironmentVariable("DIPLO_DATA_ROOT", dataRoot)
        Directory.CreateDirectory dataRoot |> ignore

[<assembly: AssemblyFixture(typeof<DataRootFixture>)>]
do ()
