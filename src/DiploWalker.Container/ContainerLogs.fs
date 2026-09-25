namespace DiploWalker.Container

open System
open System.IO
open DiploWalker.Abstractions

/// Gestion des journaux des conteneurs capturÃ©s au dÃ©marrage.
/// containerd v2 a supprimÃ© 'ctr task logs' : les logs sont donc capturÃ©s
/// dans un fichier par conteneur (%ProgramData%\Diplo\logs\<id>.log) lorsque
/// le conteneur est dÃ©marrÃ© en mode dÃ©tachÃ©, puis relus par GetContainerLogs.
module ContainerLogs =

    let private logsDirRef = ref (Path.Combine(AppPaths.dataRoot (), "logs"))

    /// Remplace le rÃ©pertoire des journaux (utile pour les tests).
    let setLogsDir (dir: string) =
        lock logsDirRef (fun () -> logsDirRef := dir)

    /// RÃ©pertoire courant des journaux de conteneurs.
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

    /// Lit puis dÃ©code les lignes d'une fenÃªtre [startOffset, startOffset + byteLimit)
    /// sans allouer un seul tampon gÃ©ant : StreamReader consomme le flux par blocs
    /// (256 Ko) et dÃ©code les caractÃ¨res multi-octets continÃ»ment, y compris Ã 
    /// cheval sur les limites de blocs. Une ligne dont la lecture dÃ©passe la
    /// fenÃªtre est dÃ©libÃ©rÃ©ment ignorÃ©e (dÃ©gÃ©nÃ©rescence d'un journal de plus de
    /// 1 Go), ce qui borne strictement la mÃ©moire aux lignes retournÃ©es.
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

    /// Lit les lignes du journal jusqu'Ã  une marque (watermark) capturÃ©e AVANT
    /// la lecture, et retourne (lignes, octets consommÃ©s). UtilisÃ© par le suivi :
    /// lire d'abord la longueur puis le contenu garantit qu'aucun octet n'est ni
    /// sautÃ© (Ã©criture entre lecture et mesure) ni dupliquÃ© au redÃ©marrage.
    /// La fenÃªtre est bornÃ©e Ã  1 Go et lue par blocs (voir readLines) pour
    /// limiter la mÃ©moire sur journaux gÃ©ants.
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

    /// Lit le journal d'un conteneur de maniÃ¨re bornÃ©e (fenÃªtre au plus 1 Go,
    /// partagÃ©e avec readUpToCore) : derniÃ¨res `tail` lignes, filtrÃ©es par
    /// `since`. Retourne [||] si aucun journal n'existe.
    let read (id: string) (tail: int) (since: string) : string array =
        readUpToCore id tail since |> fst

    let readUpTo (id: string) (tail: int) (since: string) : Result<string array * int64, string> =
        try Ok(readUpToCore id tail since)
        with ex -> Error ex.Message

    /// Lit les lignes complÃ¨tes ajoutÃ©es au journal depuis l'octet `fromOffset`.
    ///
    /// Retourne (lignes, nouvel offset). Une ligne partielle (non terminÃ©e par
    /// un saut de ligne) n'est pas retournÃ©e et l'offset n'avance pas, afin de
    /// la relire lors d'un prochain appel une fois le dÃ©limiteur Ã©crit.
    /// Le repÃ©rage du dernier dÃ©limiteur se fait par balayage arriÃ¨re par blocs
    /// de 256 Ko â€” aucune allocation proportionnelle Ã  la taille du journal.
    let readIncrementalCore (id: string) (fromOffset: int64) : string array * int64 =
        let file = fileFor id

        if not (File.Exists file) then
            [||], fromOffset
        else
            use fs =
                new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite ||| FileShare.Delete)

            let length = fs.Length
            // Fichier tronquÃ© ou recrÃ©Ã© : repartir du dÃ©but, sinon l'offset
            // resterait bloquÃ© pour toujours et le suivi ne livrerait plus rien.
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
                            failwith "Lecture tronquÃ©e du journal"

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

