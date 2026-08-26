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
    /// horodatages ISO). Retourne [||] si aucun journal n'existe.
    /// Ouvre le fichier avec un partage large (lecture/écriture/suppression)
    /// car la sortie du conteneur en cours d'exécution verrouille le fichier
    /// en écriture : File.ReadAllLines (partage strict) lèverait IOException.
    let read (id: string) (tail: int) (since: string) =
        let file = fileFor id

        if not (File.Exists file) then
            [||]
        else
            use fs =
                new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite ||| FileShare.Delete)

            use reader = new StreamReader(fs)
            let text = reader.ReadToEnd()

            let lines =
                if String.IsNullOrEmpty text then
                    [||]
                else
                    text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    |> Array.map (fun l -> l.TrimEnd('\r'))

            let lines =
                if String.IsNullOrEmpty since then
                    lines
                else
                    lines |> Array.skipWhile (fun l -> String.CompareOrdinal(l, since) < 0)

            if tail > 0 && lines.Length > tail then
                lines.[lines.Length - tail ..]
            else
                lines

    /// Longueur actuelle (en octets) du journal d'un conteneur (0 si absent).
    let fileLength (id: string) =
        let file = fileFor id

        if not (File.Exists file) then
            0L
        else
            (FileInfo(file)).Length

    /// Lit les lignes du journal jusqu'à une marque (watermark) capturée AVANT
    /// la lecture, et retourne (lignes, octets consommés). Utilisé par le suivi :
    /// lire d'abord la longueur puis le contenu garantit qu'aucun octet n'est ni
    /// sauté (écriture entre lecture et mesure) ni dupliqué au redémarrage.
    /// La fenêtre est bornée à 1 Go pour limiter la mémoire sur journaux géants.
    let readUpTo (id: string) (tail: int) (since: string) : string array * int64 =
        let file = fileFor id

        if not (File.Exists file) then
            [||], 0L
        else
            use fs =
                new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite ||| FileShare.Delete)

            let watermark = fs.Length

            if watermark = 0L then
                [||], 0L
            else
                let maxWindow = 1_000_000_000L
                let startOffset = max 0L (watermark - maxWindow)
                let count = int (watermark - startOffset)
                let buffer = Array.zeroCreate<byte> count
                fs.Position <- startOffset
                let mutable total = 0

                while total < count do
                    let r = fs.Read(buffer, total, count - total)

                    if r <= 0 then
                        failwith "Lecture tronquée du journal"

                    total <- total + r

                let text = System.Text.Encoding.UTF8.GetString(buffer, 0, count)

                let lines =
                    if String.IsNullOrEmpty text then
                        [||]
                    else
                        text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        |> Array.map (fun l -> l.TrimEnd('\r'))

                let lines =
                    if String.IsNullOrEmpty since then
                        lines
                    else
                        lines |> Array.skipWhile (fun l -> String.CompareOrdinal(l, since) < 0)

                let lines =
                    if tail > 0 && lines.Length > tail then
                        lines.[lines.Length - tail ..]
                    else
                        lines

                lines, watermark

    /// Lit les lignes complètes ajoutées au journal depuis l'octet `fromOffset`.
    ///
    /// Retourne (lignes, nouvel offset). Une ligne partielle (non terminée par
    /// un saut de ligne) n'est pas retournée et l'offset n'avance pas, afin de
    /// la relire lors d'un prochain appel une fois le délimiteur écrit.
    let readIncremental (id: string) (fromOffset: int64) : string array * int64 =
        let file = fileFor id

        if not (File.Exists file) then
            [||], fromOffset
        else
            use fs =
                new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite ||| FileShare.Delete)

            let length = fs.Length
            // Fichier tronqué ou recréé : repartir du début, sinon l'offset
            // resterait bloqué pour toujours et le suivi ne livrerait plus rien.
            let fromOffset = min fromOffset length

            if length <= fromOffset then
                [||], fromOffset
            else
                let count = int (length - fromOffset)
                let buffer = Array.zeroCreate<byte> count
                fs.Position <- fromOffset
                let mutable total = 0

                while total < count do
                    let r = fs.Read(buffer, total, count - total)

                    if r <= 0 then
                        failwith "Lecture tronquée du journal"

                    total <- total + r
                // index (en octets, relatif à fromOffset) du dernier délimiteur de ligne
                let mutable lastNl = -1

                for i in count - 1 .. -1 .. 0 do
                    if lastNl < 0 && (buffer.[i] = 0x0Auy || buffer.[i] = 0x0Duy) then
                        lastNl <- i

                if lastNl < 0 then
                    [||], fromOffset
                else
                    let consumed = fromOffset + int64 (lastNl + 1)
                    let text = System.Text.Encoding.UTF8.GetString(buffer, 0, lastNl + 1)

                    let lines =
                        text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        |> Array.map (fun l -> l.TrimEnd('\r'))

                    lines, consumed
