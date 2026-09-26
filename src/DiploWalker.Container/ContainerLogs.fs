namespace DiploWalker.Container

open System
open System.IO
open DiploWalker.Abstractions

/// Gestion des journaux des conteneurs capturés au démarrage.
/// containerd v2 a supprimé 'ctr task logs' : les logs sont donc capturés
/// dans un fichier par conteneur (%ProgramData%\Diplo\logs\<id>.log) lorsque
/// le conteneur est démarré en mode détaché, puis relus par GetContainerLogs.
module ContainerLogs =

    let private logsDirRef = ref (Path.Combine(AppPaths.dataRoot (), "logs"))

    /// Remplace le répertoire des journaux (utile pour les tests).
    let setLogsDir (dir: string) =
        lock logsDirRef (fun () -> logsDirRef := dir)

    /// Répertoire courant des journaux de conteneurs.
    let logsDir () = !logsDirRef

    /// Chemin du journal d'un conteneur.
    let fileFor (id: string) =
        Path.Combine(logsDir (), sprintf "%s.log" id)

    /// Longueur actuelle (en octets) du journal d'un conteneur (0 si absent).
    let fileLength (id: string) =
        let file = fileFor id

        if not (File.Exists file) then
            0L
        else
            (FileInfo(file)).Length

    /// Lit puis décode les lignes d'une fenêtre [startOffset, startOffset + byteLimit)
    /// sans allouer un seul tampon géant : StreamReader consomme le flux par blocs
    /// (256 Ko) et décode les caractères multi-octets continûment, y compris à
    /// cheval sur les limites de blocs. Une ligne dont la lecture dépasse la
    /// fenêtre est délibérément ignorée (dégénérescence d'un journal de plus de
    /// 1 Go), ce qui borne strictement la mémoire aux lignes retournées.
    let private readLines (fs: FileStream) (startOffset: int64) (byteLimit: int64) : string array =
        fs.Position <- startOffset

        use reader =
            new StreamReader(fs, System.Text.Encoding.UTF8, false, 256 * 1024, leaveOpen = true)

        let lines = ResizeArray<string>()
        let mutable reading = true

        while reading do
            let line = reader.ReadLine()

            if isNull line then
                reading <- false
            elif fs.Position - startOffset > byteLimit then
                reading <- false
            else
                lines.Add(line.TrimEnd('\r'))

        lines.ToArray()

    /// Lit les lignes du journal jusqu'à une marque (watermark) capturée AVANT
    /// la lecture, et retourne (lignes, octets consommés). Utilisé par le suivi :
    /// lire d'abord la longueur puis le contenu garantit qu'aucun octet n'est ni
    /// sauté (écriture entre lecture et mesure) ni dupliqué au redémarrage.
    /// La fenêtre est bornée à 1 Go et lue par blocs (voir readLines) pour
    /// limiter la mémoire sur journaux géants.
    let readUpToCore (id: string) (tail: int) (since: string) : string array * int64 =
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
                let count = watermark - startOffset
                let lines = readLines fs startOffset count

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

    /// Lit le journal d'un conteneur de manière bornée (fenêtre au plus 1 Go,
    /// partagée avec readUpToCore) : dernières `tail` lignes, filtrées par
    /// `since`. Retourne [||] si aucun journal n'existe.
    let read (id: string) (tail: int) (since: string) : string array =
        readUpToCore id tail since |> fst

    let readUpTo (id: string) (tail: int) (since: string) : Result<string array * int64, string> =
        try Ok(readUpToCore id tail since)
        with ex -> Error ex.Message

    /// Lit les lignes complètes ajoutées au journal depuis l'octet `fromOffset`.
    ///
    /// Retourne (lignes, nouvel offset). Une ligne partielle (non terminée par
    /// un saut de ligne) n'est pas retournée et l'offset n'avance pas, afin de
    /// la relire lors d'un prochain appel une fois le délimiteur écrit.
    /// Le repérage du dernier délimiteur se fait par balayage arrière par blocs
    /// de 256 Ko — aucune allocation proportionnelle à la taille du journal.
    let readIncrementalCore (id: string) (fromOffset: int64) : string array * int64 =
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
                let chunkSize = 256 * 1024
                let mutable cursor = length
                let mutable lastNl = -1L

                while lastNl < 0L && cursor > fromOffset do
                    let start = max fromOffset (cursor - int64 chunkSize)
                    let len = int (cursor - start)
                    let buffer = Array.zeroCreate<byte> len
                    fs.Position <- start
                    let mutable total = 0

                    while total < len do
                        let r = fs.Read(buffer, total, len - total)

                        if r <= 0 then
                            failwith "Lecture tronquée du journal"

                        total <- total + r

                    let mutable i = len - 1

                    while i >= 0 && lastNl < 0L do
                        if buffer.[i] = 0x0Auy || buffer.[i] = 0x0Duy then
                            lastNl <- start + int64 i
                        i <- i - 1

                    cursor <- start

                if lastNl < 0L then
                    [||], fromOffset
                else
                    let consumed = lastNl + 1L
                    let lines = readLines fs fromOffset (consumed - fromOffset)
                    lines, consumed

    let readIncremental (id: string) (fromOffset: int64) : Result<string array * int64, string> =
        try Ok(readIncrementalCore id fromOffset)
        with ex -> Error ex.Message

