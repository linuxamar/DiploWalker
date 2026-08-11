namespace Diplo.Container

open System
open System.IO

/// Gestion des journaux des conteneurs capturés au démarrage.
/// containerd v2 a supprimé 'ctr task logs' : les logs sont donc capturés
/// dans un fichier par conteneur (%ProgramData%\Diplo\logs\<id>.log) lorsque
/// le conteneur est démarré en mode détaché, puis relus par GetContainerLogs.
module ContainerLogs =

    let private logsDirRef =
        ref (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Diplo", "logs"))

    /// Remplace le répertoire des journaux (utile pour les tests).
    let setLogsDir (dir: string) =
        lock logsDirRef (fun () -> logsDirRef := dir)

    /// Répertoire courant des journaux de conteneurs.
    let logsDir () = !logsDirRef

    /// Chemin du journal d'un conteneur.
    let fileFor (id: string) =
        Path.Combine(logsDir (), sprintf "%s.log" id)

    /// Lit le journal d'un conteneur : dernières `tail` lignes, filtrées par
    /// `since` (comparaison ordinale sur le début de ligne, adaptée aux
    /// horodatages ISO). `follow` n'est pas applicable à un fichier : un
    /// instantané est retourné. Retourne [||] si aucun journal n'existe.
    let read (id: string) (tail: int) (since: string) =
        let file = fileFor id
        if not (File.Exists file) then [||]
        else
            let lines = File.ReadAllLines file
            let lines =
                if String.IsNullOrEmpty since then lines
                else lines |> Array.skipWhile (fun l -> String.CompareOrdinal(l, since) < 0)
            if tail > 0 && lines.Length > tail then lines.[lines.Length - tail..]
            else lines
