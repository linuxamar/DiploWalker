namespace DiploWalker.Disk

open System
open System.IO
open System.Text

/// Source de lecture aléatoire pour une image disque (ISO), sans charger le
/// fichier complet en mémoire : nécessaire pour les images de plus de 2 Go
/// (File.ReadAllBytes est limité à 2 Go).
module IsoSource =

    /// Source de lecture aléatoire : longueur totale et lecture d'une plage.
    type T =
        abstract Length: int64
        abstract ReadBytes: offset: int64 -> count: int -> byte[]

    /// Source mémoire, utilisée pour les petites images.
    let fromBytes (data: byte[]) : T =
        { new T with
            member _.Length = int64 data.Length

            member _.ReadBytes offset count =
                let offset = max 0L offset
                let count = int (min (int64 count) (max 0L (int64 data.Length - offset)))

                if count <= 0 then
                    Array.empty
                else
                    Array.sub data (int offset) count }

    /// Source fichier : lecture par positionnement du flux, requise pour les
    /// images de plus de 2 Go non matérialisables en mémoire.
    let fromStream (stream: Stream) : T =
        { new T with
            member _.Length = stream.Length

            member _.ReadBytes offset count =
                let offset = max 0L offset
                let count = int (min (int64 count) (max 0L (stream.Length - offset)))

                if count <= 0 then
                    Array.empty
                else
                    let buffer = Array.zeroCreate<byte> count
                    stream.Position <- offset
                    let mutable total = 0

                    while total < count do
                        let read = stream.Read(buffer, total, count - total)

                        if read = 0 then
                            failwithf "Fin de flux inattendue lors de la lecture de l'image à l'offset %d" offset

                        total <- total + read

                    buffer }

    /// Ouvre un fichier image en lecture aléatoire.
    let openFile (path: string) : FileStream =
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)

    /// Remplace les caractères interdits dans un nom de fichier Windows
    /// (contrôles et séparateurs) par un tiret bas. Partagé par les parseurs
    /// ISO9660 et UDF lors de l'extraction.
    let sanitizeName (name: string) =
        name
        |> Seq.map (fun c ->
            if
                Char.IsControl c
                || c = '\\'
                || c = '/'
                || c = ':'
                || c = '*'
                || c = '?'
                || c = '"'
                || c = '<'
                || c = '>'
                || c = '|'
            then
                '_'
            else
                c)
        |> String.Concat

/// Parseur minimal d'images ISO9660 (lecture seule) permettant d'extraire
/// le contenu d'un fichier ISO dans un répertoire cible.
///
/// Le parseur ne traite que le descripteur de volume principal (PVD) situé au
/// bloc 16. Les extensions et variantes suivantes ne sont volontairement PAS
/// prises en charge :
/// - Joliet : le descripteur de volume supplémentaire (type 2, jeu de
///   caractères UCS-2) est ignoré, seuls les noms de fichiers ISO9660 de
///   niveau 1 (8.3) sont lus ;
/// - Rock Ridge (RRIP) : les attributs étendus POSIX (entrées NM, SL, etc.)
///   présents dans les enregistrements de répertoire sont ignorés ;
/// - Multi-session : seule la première session (bloc 16) est lue, les
///   sessions suivantes sont ignorées.
module Iso9660 =

    let private blockSize = 2048

    let private ascii (data: byte[]) (offset: int) (count: int) =
        Encoding.ASCII.GetString(data, offset, count)

    let private readUInt16LE (data: byte[]) (offset: int) =
        int data.[offset] ||| (int data.[offset + 1] <<< 8)

    let private readUInt32LE (data: byte[]) (offset: int) =
        int data.[offset]
        ||| (int data.[offset + 1] <<< 8)
        ||| (int data.[offset + 2] <<< 16)
        ||| (int data.[offset + 3] <<< 24)

    /// Lit une valeur 32 bits little-endian en tant qu'entier non signé
    /// (int64). Essentiel pour les tailles de fichiers > 2 Go du niveau 1,
    /// qui dépassent Int32.MaxValue et deviendraient négatives avec un `int`.
    let private readUInt32LE64 (data: byte[]) (offset: int) =
        int64 data.[offset]
        ||| (int64 data.[offset + 1] <<< 8)
        ||| (int64 data.[offset + 2] <<< 16)
        ||| (int64 data.[offset + 3] <<< 24)

    type private DirRecord =
        { extent: int
          dataLength: int64
          isDirectory: bool
          name: string }

    let private parseDirRecord (data: byte[]) (offset: int) =
        if offset >= data.Length then
            None
        else
            let length = int data.[offset]

            if length = 0 then
                None
            else
                // Enregistrement tronqué (fin de répertoire) : borner la lecture
                // du nom au lieu de lever IndexOutOfRange sur une image
                // malformée.
                if length < 33 || offset + length > data.Length then
                    Some
                        { extent = 0
                          dataLength = 0
                          isDirectory = false
                          name = "\x01" } // sentinelle ignorée par l'appelant
                else
                    let extent = readUInt32LE data (offset + 2)
                    let dataLength = readUInt32LE64 data (offset + 10)
                    let flags = data.[offset + 25]
                    let rawNameLen = min (int data.[offset + 32]) (length - 33) |> max 0
                    let rawName = ascii data (offset + 33) rawNameLen

                    Some
                        { extent = extent
                          dataLength = dataLength
                          isDirectory = (flags &&& 0x02uy) <> 0uy
                          name = rawName }

    let private readDirectoryRecords (src: IsoSource.T) (extent: int) (length: int64) =
        let start = int64 extent * int64 blockSize
        let count = min (src.Length - start) (max length 0L)

        if count <= 0L then
            []
        elif count > int64 Int32.MaxValue then
            // Au-delà de 2 Go, `int` bouclerait sur un négatif et produirait un
            // résultat vide SILENCIEUX : échouer explicitement.
            failwithf "Répertoire ISO trop volumineux pour l'extraction en mémoire (%d octets)" count
        else
            let data = src.ReadBytes start (int count)
            let records = ResizeArray<DirRecord>()
            let mutable pos = 0

            while pos < data.Length do
                let len = int data.[pos]

                if len = 0 then
                    let blockOffset = pos % blockSize

                    if blockOffset = 0 then
                        pos <- pos + 1
                    else
                        pos <- pos + (blockSize - blockOffset)
                else
                    match parseDirRecord data pos with
                    | Some rec' ->
                        records.Add(rec')
                        pos <- pos + len
                    | None -> pos <- pos + 1

            records |> Seq.toList

    let private readFile (src: IsoSource.T) (extent: int) (dataLength: int64) =
        let start = int64 extent * int64 blockSize
        let count = min (src.Length - start) (max dataLength 0L)

        if count <= 0L then
            Array.empty
        elif count > int64 Int32.MaxValue then
            // Fichier ≥ 2 Go : le stockage en mémoire dépasserait la limite d'un
            // tableau .NET — échouer explicitement (utiliser l'extraction par
            // blocs, qui ne matérialise pas le fichier).
            failwithf "Fichier ISO trop volumineux pour l'extraction en mémoire (%d octets)" count
        else
            src.ReadBytes start (int count)

    /// Écrit le contenu d'un fichier de l'image directement dans `destPath`,
    /// par blocs (1 Mo) — sans matérialiser le fichier en mémoire, ce qui
    /// permet les fichiers de plusieurs Go.
    let private extractFile (src: IsoSource.T) (extent: int) (dataLength: int64) (destPath: string) =
        use output = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None)
        let start = int64 extent * int64 blockSize
        let available = max 0L (src.Length - start)
        let total = min (max dataLength 0L) available
        let buffer = Array.zeroCreate<byte> (1024 * 1024)
        let mutable pos = 0L

        while pos < total do
            let toRead = int (min (total - pos) (int64 buffer.Length))
            let chunk = src.ReadBytes (start + pos) toRead
            output.Write(chunk, 0, chunk.Length)
            pos <- pos + int64 chunk.Length

    /// Nom ISO9660 d'un fichier : "NOM.EXT;1" → retire le suffixe ;version et les points terminaux.
    let private cleanFileName (rawName: string) =
        let withoutVersion =
            let idx = rawName.IndexOf(';')
            if idx >= 0 then rawName.Substring(0, idx) else rawName

        withoutVersion.TrimEnd('.', '\x00')

    let private cleanDirName (rawName: string) = rawName.Trim([| '\x00'; '\x01'; ' ' |])

    let private pathFromRelative (targetPath: string) (relPath: string) =
        Path.Combine(Array.append [| targetPath |] (relPath.Split('/')))

    /// Lit le descripteur de volume principal (PVD) et retourne les
    /// (extent, longueur) du répertoire racine.
    let readPvd (src: IsoSource.T) (isoPath: string) : int * int64 =
        let pvdOffset = int64 (16 * blockSize)

        if pvdOffset + 170L > src.Length then
            failwithf "Le fichier '%s' est trop petit pour être une image ISO9660" isoPath

        let pvd = src.ReadBytes pvdOffset 170
        let magic = ascii pvd 1 5

        if magic <> "CD001" then
            failwithf "'%s' n'est pas une image ISO9660 valide (en-tête CD001 absent)" isoPath

        let logicalBlockSize = readUInt16LE pvd 128

        if logicalBlockSize <> blockSize then
            failwithf "Taille de bloc logique ISO %d non prise en charge (attendu %d)" logicalBlockSize blockSize

        let rootLen = int pvd.[156]

        if rootLen = 0 then
            failwith "Image ISO9660 invalide (enregistrement de répertoire racine manquant)"

        let rootExtent = readUInt32LE pvd 158
        let rootLength = readUInt32LE64 pvd 166
        (rootExtent, rootLength)

    /// Retrouve le contenu d'un fichier de l'image par son chemin ISO9660
    /// (chemin de répertoires séparés par '/', ex. "/DOSSIER/HELLO.TXT").
    let readFileByPath (src: IsoSource.T) (isoPath: string) (pathInImage: string) : byte[] =
        let segments =
            pathInImage.Split('/')
            |> Array.filter (fun s -> s <> "" && s <> "." && s <> "..")

        if segments.Length = 0 then
            failwithf "Chemin de fichier invalide : '%s'" pathInImage

        let rec find (extent: int) (length: int64) (remaining: string[]) =
            let entries = readDirectoryRecords src extent length
            let segment = remaining.[0]
            let isLast = remaining.Length = 1

            let name =
                if isLast then
                    cleanFileName segment
                else
                    cleanDirName segment

            let entry =
                entries
                |> List.tryFind (fun e ->
                    let entryName = if isLast then cleanFileName e.name else cleanDirName e.name
                    String.Equals(entryName, name, StringComparison.OrdinalIgnoreCase))

            match entry with
            | None -> failwithf "Fichier introuvable dans l'image '%s' : %s" isoPath pathInImage
            | Some e when isLast ->
                if e.isDirectory then
                    failwithf "'%s' est un répertoire dans l'image '%s'" pathInImage isoPath

                readFile src e.extent e.dataLength
            | Some e ->
                if not e.isDirectory then
                    failwithf "'%s' n'est pas un répertoire dans l'image '%s'" pathInImage isoPath

                find e.extent e.dataLength remaining.[1..]

        let (rootExtent, rootLength) = readPvd src isoPath
        find rootExtent rootLength segments

    /// Extrait tous les fichiers d'un tampon ISO9660 dans le répertoire cible.
    /// Retourne le nombre de fichiers extraits.
    let extract (src: IsoSource.T) (isoPath: string) (targetPath: string) : int =
        let (rootExtent, rootLength) = readPvd src isoPath

        let pending = System.Collections.Generic.Queue<(string * int * int64)>()
        let visited = System.Collections.Generic.HashSet<string * int>()
        pending.Enqueue("", rootExtent, rootLength)
        visited.Add("", rootExtent) |> ignore
        let mutable count = 0

        while pending.Count > 0 do
            let (relDir, extent, length) = pending.Dequeue()

            for entry in readDirectoryRecords src extent length do
                if entry.isDirectory then
                    let name = cleanDirName entry.name

                    if name <> "" && name <> "." && name <> ".." then
                        let sub =
                            if relDir = "" then
                                IsoSource.sanitizeName name
                            else
                                relDir + "/" + IsoSource.sanitizeName name

                        if sub <> "" && sub <> "." && sub <> ".." then
                            Directory.CreateDirectory(pathFromRelative targetPath sub) |> ignore

                            if visited.Add(sub, entry.extent) then
                                pending.Enqueue(sub, entry.extent, entry.dataLength)
                else
                    let name = cleanFileName entry.name

                    if name <> "" && name <> "." && name <> ".." then
                        let sub =
                            if relDir = "" then
                                IsoSource.sanitizeName name
                            else
                                relDir + "/" + IsoSource.sanitizeName name

                        if sub <> "" && sub <> "." && sub <> ".." then
                            let filePath = pathFromRelative targetPath sub
                            Directory.CreateDirectory(Path.GetDirectoryName(filePath)) |> ignore
                            extractFile src entry.extent entry.dataLength filePath
                            count <- count + 1

        count

/// Parseur minimal d'images UDF (lecture seule) destiné aux images DVD,
/// permettant d'extraire le contenu d'une image dans un répertoire cible.
module Udf =

    let private blockSize = 2048

    let private ascii (data: byte[]) (offset: int) (count: int) =
        Encoding.ASCII.GetString(data, offset, count)

    let private readUInt16LE (data: byte[]) (offset: int) =
        int data.[offset] ||| (int data.[offset + 1] <<< 8)

    let private readUInt32LE (data: byte[]) (offset: int) =
        int data.[offset]
        ||| (int data.[offset + 1] <<< 8)
        ||| (int data.[offset + 2] <<< 16)
        ||| (int data.[offset + 3] <<< 24)

    let private pathFromRelative (targetPath: string) (relPath: string) =
        Path.Combine(Array.append [| targetPath |] (relPath.Split('/')))

    /// Localise le descripteur NSR dans la séquence de reconnaissance de
    /// volume (bloc 16). Retourne l'offset du descripteur ou -1.
    ///
    /// La séquence de reconnaissance de volume d'une image UDF est courte et
    /// contiguë : BEA01 (bloc 16), NSR02/NSR03 (17), TEA01 (18), suivi des
    /// descripteurs de volume. On borne donc la recherche à une fenêtre de 64
    /// blocs : une image ISO9660 pure (sans NSR) n'a jamais de descripteur au-
    /// delà, et tout balayage de la totalité d'une grosse image (> 2 Go) serait
    /// d'une lenteur inacceptable (un Read par bloc de 2048 octets).
    let private findNsr (src: IsoSource.T) : int =
        let maxBlocks = 64
        let mutable offset = 16 * blockSize
        let mutable scanned = 0
        let mutable result = -1
        let mutable running = int64 offset + int64 blockSize <= src.Length

        while running && scanned < maxBlocks do
            let block = src.ReadBytes (int64 offset) blockSize
            let ty = int block.[0]
            let id = ascii block 1 5

            if id = "NSR02" || id = "NSR03" then
                result <- offset
                running <- false
            elif ty = 4 || id = "TEA01" then
                running <- false
            else
                offset <- offset + blockSize
                scanned <- scanned + 1
                running <- int64 offset + int64 blockSize <= src.Length

        result

    /// Détecte une image UDF en cherchant un descripteur NSR.
    let isUdf (src: IsoSource.T) : bool = findNsr src >= 0

    /// Retrouve la séquence de descripteurs de volume depuis le pointeur
    /// d'ancrage (AVDP) situé conventionnellement au bloc 256.
    let private findVdsLocation (src: IsoSource.T) : int =
        let mutable result = -1

        for candidate in [ 256; 257; 512 ] do
            if result < 0 && int64 ((candidate + 1) * blockSize) <= src.Length then
                let block = src.ReadBytes (int64 (candidate * blockSize)) blockSize

                if readUInt16LE block 0 = 0x0002 then
                    result <- readUInt32LE block 20

        if result < 0 then
            failwith "Image UDF invalide (pointeur de volume d'ancrage introuvable)"

        result

    /// Lit la séquence de descripteurs de volume : emplacement de partition,
    /// taille de bloc logique et emplacement du descripteur de jeu de fichiers
    /// (issu de logicalVolContentsUse du descripteur de volume logique).
    let private readVds (src: IsoSource.T) =
        let mutable offset = int64 (findVdsLocation src) * int64 blockSize
        let mutable partitionStart = 0
        let mutable logicalBlockSize = 2048
        let mutable fsdLocation = -1
        let mutable running = true

        while running && offset + int64 blockSize <= src.Length do
            let block = src.ReadBytes offset blockSize
            let tagId = readUInt16LE block 0

            if tagId = 0x0005 then
                partitionStart <- readUInt32LE block 188
                offset <- offset + int64 blockSize
            elif tagId = 0x0006 then
                logicalBlockSize <- readUInt32LE block 212
                fsdLocation <- readUInt32LE block 252
                offset <- offset + int64 blockSize
            else
                offset <- offset + int64 blockSize

                if tagId = 0x0008 || tagId = 0x0000 then
                    running <- false

        if fsdLocation < 0 then
            failwith "Image UDF invalide (descripteur de volume logique manquant)"

        (partitionStart, logicalBlockSize, fsdLocation)

    let private readUInt64LE (data: byte[]) (offset: int) =
        let mutable v = 0UL

        for i in 0..7 do
            v <- v ||| (uint64 data.[offset + i] <<< (8 * i))

        v

    /// Assemble les données pointées par un ICB (fichier ou répertoire) en
    /// parcourant ses descripteurs d'allocation (short_ad, long_ad ou
    /// extended_ad) ou en lisant le contenu inline (AD_IN_ICB).
    /// Limite de sécurité : accumulation maximale acceptée pour le contenu
    /// d'un ICB (des descripteurs d'allocation forgés peuvent gonfler la
    /// mémoire à plusieurs Go avant toute autre vérification).
    let private maxIcbContentBytes = 4L * 1024L * 1024L * 1024L

    /// Lit intégralement en mémoire le contenu d'un fichier UDF pointé par son
    /// ICB. Limité aux fichiers ≤ 2 Go : au-delà, le stockage en mémoire
    /// déborderait la capacité d'un tableau .NET — échouer explicitement
    /// (l'extraction par blocs via `extractIcbFile` gère les fichiers de
    /// plusieurs Go sans matérialiser le contenu).
    let private readIcbData (src: IsoSource.T) (icbOffset: int64) (partitionStart: int) (logicalBlockSize: int) =
        let desc = src.ReadBytes icbOffset 176

        if desc.Length < 176 then
            failwith "Image UDF invalide (ICB fichier incomplet)"

        let tag = readUInt16LE desc 0

        if tag <> 0x0105 && tag <> 0x0201 then
            failwithf "Image UDF invalide (ICB fichier manquant, tag 0x%04x)" tag

        let flags = readUInt16LE desc 34
        let allocationType = flags &&& 7
        let informationLength = int64 (readUInt64LE desc 56)

        if informationLength > int64 Int32.MaxValue then
            failwithf "Fichier UDF trop volumineux pour l'extraction en mémoire (%d octets)" informationLength
        // Masque uint32 : une valeur ≥ 2^31 deviendrait négative en int et
        // décalerait allocStart EN ARRIÈRE.
        let extendedAttrLength = int64 (readUInt32LE desc 168)
        let allocLength = int64 (readUInt32LE desc 172)
        let allocStart = icbOffset + 176L + extendedAttrLength
        let allocEnd = allocStart + allocLength
        let content = ResizeArray<byte>()
        let mutable accumulated = 0L

        if allocationType = 3 then
            // AD_IN_ICB : les données sont stockées dans l'ICB lui-même.
            let inlineLen = int (min informationLength (int64 allocLength))

            if inlineLen > 0 then
                content.AddRange(src.ReadBytes allocStart inlineLen)
                accumulated <- int64 inlineLen
        else
            let step =
                if allocationType = 0 then 8
                elif allocationType = 1 then 16
                else 20

            let locOffset = if allocationType = 2 then 12 else 4
            let mutable pos = allocStart

            let mutable running =
                pos + int64 step <= allocEnd && pos + int64 step <= src.Length

            while running do
                let ad = src.ReadBytes pos step
                let extLength = readUInt32LE ad 0

                if extLength = 0 then
                    running <- false
                else
                    let length = extLength &&& 0x3FFFFFFF
                    let location = readUInt32LE ad locOffset
                    let start = (int64 partitionStart + int64 location) * int64 logicalBlockSize
                    let available = min (int64 length) (max 0L (src.Length - start))

                    if available > 0 then
                        // Plafonner l'accumulation AVANT d'allouer : sinon des
                        // AD redondants/OOM-forgés gonflent `content` sans borne.
                        let remainingBudget = min (informationLength - accumulated) maxIcbContentBytes

                        if remainingBudget <= 0L then
                            running <- false
                        else
                            let toRead = min available remainingBudget |> int
                            content.AddRange(src.ReadBytes start toRead)
                            accumulated <- accumulated + int64 toRead

                    pos <- pos + int64 step
                    running <- pos + int64 step <= allocEnd && pos + int64 step <= src.Length

        let data = content.ToArray()

        if int64 data.Length > informationLength then
            Array.sub data 0 (int informationLength)
        else
            data

    /// Écrit le contenu d'un fichier UDF directement dans `destPath`, en
    /// parcourant les descripteurs d'allocation par blocs (1 Mo) — sans
    /// matérialiser le fichier en mémoire, ce qui permet les fichiers de
    /// plusieurs Go.
    let private extractIcbFile (src: IsoSource.T) (icbOffset: int64) (partitionStart: int) (logicalBlockSize: int) (destPath: string) =
        use output = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None)
        let desc = src.ReadBytes icbOffset 176

        if desc.Length < 176 then
            failwith "Image UDF invalide (ICB fichier incomplet)"

        let tag = readUInt16LE desc 0

        if tag <> 0x0105 && tag <> 0x0201 then
            failwithf "Image UDF invalide (ICB fichier manquant, tag 0x%04x)" tag

        let flags = readUInt16LE desc 34
        let allocationType = flags &&& 7
        let informationLength = int64 (readUInt64LE desc 56)
        let extendedAttrLength = int64 (readUInt32LE desc 168)
        let allocLength = int64 (readUInt32LE desc 172)
        let allocStart = icbOffset + 176L + extendedAttrLength
        let allocEnd = allocStart + allocLength
        let buffer = Array.zeroCreate<byte> (1024 * 1024)
        let mutable accumulated = 0L

        let streamThrough (start: int64) (count: int) =
            let mutable remaining = int64 count
            let mutable cursor = start

            while remaining > 0L do
                let toRead = int (min remaining (int64 buffer.Length))
                let chunk = src.ReadBytes cursor toRead

                if chunk.Length = 0 then
                    failwithf "Fin de flux inattendue lors de l'extraction UDF à l'offset %d" cursor

                output.Write(chunk, 0, chunk.Length)
                remaining <- remaining - int64 chunk.Length
                cursor <- cursor + int64 chunk.Length

        if allocationType = 3 then
            // AD_IN_ICB : les données sont stockées dans l'ICB lui-même.
            let inlineLen = int (min informationLength (int64 allocLength))

            if inlineLen > 0 then
                streamThrough allocStart inlineLen
        else
            let step =
                if allocationType = 0 then 8
                elif allocationType = 1 then 16
                else 20

            let locOffset = if allocationType = 2 then 12 else 4
            let mutable pos = allocStart

            let mutable running =
                pos + int64 step <= allocEnd && pos + int64 step <= src.Length

            while running do
                let ad = src.ReadBytes pos step
                let extLength = readUInt32LE ad 0

                if extLength = 0 then
                    running <- false
                else
                    let length = extLength &&& 0x3FFFFFFF
                    let location = readUInt32LE ad locOffset
                    let start = (int64 partitionStart + int64 location) * int64 logicalBlockSize
                    let available = min (int64 length) (max 0L (src.Length - start))

                    if available > 0L then
                        let remaining = min available (informationLength - accumulated)

                        if remaining > 0L then
                            let toRead = min remaining (int64 System.Int32.MaxValue) |> int
                            streamThrough start toRead
                            accumulated <- accumulated + int64 toRead

                    pos <- pos + int64 step
                    running <- pos + int64 step <= allocEnd && pos + int64 step <= src.Length

    /// Liste les entrées d'un répertoire UDF identifié par son ICB. Retourne
    /// pour chaque entrée (nom brut, emplacement de l'ICB enfant, estRépertoire).
    /// Les entrées parent (« . » / « .. ») et les noms vides sont exclus.
    let readDirEntries
        (src: IsoSource.T)
        (icbLocation: int)
        (partitionStart: int)
        (logicalBlockSize: int)
        : (string * int * bool) list =
        let icbOffset = (int64 partitionStart + int64 icbLocation) * int64 logicalBlockSize

        if icbOffset + 36L > src.Length then
            failwithf "Image UDF invalide (ICB à l'adresse %d)" icbOffset

        let desc = src.ReadBytes icbOffset 176
        let icbTag = readUInt16LE desc 0

        if icbTag <> 0x0105 && icbTag <> 0x0201 then
            failwithf "Image UDF invalide (ICB manquant, tag 0x%04x)" icbTag

        let dirData = readIcbData src icbOffset partitionStart logicalBlockSize
        let entries = ResizeArray<string * int * bool>()
        let mutable pos = 0
        let mutable running = pos + 38 <= dirData.Length

        while running do
            let fidTag = readUInt16LE dirData pos

            if fidTag <> 0x0101 then
                running <- false
            else
                let characteristics = dirData.[pos + 18]
                let nameLen = int dirData.[pos + 19]
                let childIcbLocation = readUInt32LE dirData (pos + 24)
                let implUseLen = int (readUInt16LE dirData (pos + 36))
                // Le nom de fichier est un d-string (ECMA-167 1/7.2.12) :
                // un octet de type (8 = ASCII, 16 = UTF-16BE) suivi du nom.
                let nameOffset = pos + 38 + implUseLen

                // FID malformé : sortir proprement au lieu de lire hors bornes
                // et interrompre toute l'extraction.
                if nameOffset + nameLen > dirData.Length then
                    running <- false
                else
                    let rawName =
                        if nameLen <= 1 then
                            ""
                        else
                            let dType = dirData.[nameOffset]

                            if dType = 8uy then
                                ascii dirData (nameOffset + 1) (nameLen - 1)
                            elif dType = 16uy then
                                let chars = ResizeArray<char>()
                                let mutable i = nameOffset + 1

                                while i + 1 <= nameOffset + nameLen - 1 do
                                    chars.Add(char ((int dirData.[i] <<< 8) ||| int dirData.[i + 1]))
                                    i <- i + 2

                                System.String(chars.ToArray())
                            else
                                ""

                    let isParent = (characteristics &&& 0x08uy) <> 0uy

                    if not isParent && rawName <> "\x00" && rawName <> "\x01" && nameLen > 0 then
                        let isDir = (characteristics &&& 0x02uy) <> 0uy
                        entries.Add(rawName, childIcbLocation, isDir)
                    // Descripteur suivant, aligné sur 4 octets.
                    let total = 38 + implUseLen + nameLen
                    pos <- pos + total + ((4 - (total % 4)) % 4)
                    running <- pos + 38 <= dirData.Length

        List.ofSeq entries

    /// Extrait tous les fichiers de l'image UDF dans le répertoire cible.
    /// Retourne le nombre de fichiers extraits.
    let extract (src: IsoSource.T) (targetPath: string) : int =
        let nsrOffset = findNsr src

        if nsrOffset < 0 then
            failwith "Image non UDF (descripteur NSR introuvable)"

        let (partitionStart, logicalBlockSize, fsdLocation) = readVds src

        let fsdOffset = (int64 partitionStart + int64 fsdLocation) * int64 logicalBlockSize

        if fsdOffset + 466L > src.Length then
            failwith "Image UDF invalide (descripteur de jeu de fichiers manquant)"

        let fsd = src.ReadBytes fsdOffset 466

        if readUInt16LE fsd 0 <> 0x0100 then
            failwith "Image UDF invalide (descripteur de jeu de fichiers introuvable)"

        let rootIcb = readUInt32LE fsd 404

        let pending = System.Collections.Generic.Queue<string * int>()
        let visited = System.Collections.Generic.HashSet<string * int>()
        pending.Enqueue("", rootIcb)
        visited.Add("", rootIcb) |> ignore
        let mutable count = 0

        while pending.Count > 0 do
            let (relDir, icbLocation) = pending.Dequeue()
            let icbOffset = (int64 partitionStart + int64 icbLocation) * int64 logicalBlockSize

            if icbOffset + 36L > src.Length then
                failwithf "Image UDF invalide (ICB à l'adresse %d)" icbOffset

            let desc = src.ReadBytes icbOffset 176
            let icbTag = readUInt16LE desc 0

            if icbTag <> 0x0105 && icbTag <> 0x0201 then
                failwithf "Image UDF invalide (ICB manquant, tag 0x%04x)" icbTag

            let fileType = int desc.[27]

            if fileType = 4 then
                // Répertoire : la séquence de descripteurs d'identifiant de
                // fichier est stockée dans les blocs pointés par l'ICB.
                for (rawName, childIcbLocation, isDir) in readDirEntries src icbLocation partitionStart logicalBlockSize do
                    let name = IsoSource.sanitizeName rawName

                    if name <> "" && name <> "." && name <> ".." then
                        let sub = if relDir = "" then name else relDir + "/" + name

                        if isDir then
                            Directory.CreateDirectory(pathFromRelative targetPath sub) |> ignore

                            if visited.Add(sub, childIcbLocation) then
                                pending.Enqueue(sub, childIcbLocation)
                        else
                            let fileIcb =
                                (int64 partitionStart + int64 childIcbLocation) * int64 logicalBlockSize

                            let filePath = pathFromRelative targetPath sub
                            Directory.CreateDirectory(Path.GetDirectoryName(filePath)) |> ignore
                            extractIcbFile src fileIcb partitionStart logicalBlockSize filePath
                            count <- count + 1

        count

    /// Retrouve le contenu d'un fichier de l'image par son chemin UDF
    /// (chemin de répertoires séparés par '/', ex. "/DOSSIER/HELLO.TXT").
    let readFileByPath (src: IsoSource.T) (isoPath: string) (pathInImage: string) : byte[] =
        let segments =
            pathInImage.Split('/')
            |> Array.filter (fun s -> s <> "" && s <> "." && s <> "..")

        if segments.Length = 0 then
            failwithf "Chemin de fichier invalide : '%s'" pathInImage

        let (partitionStart, logicalBlockSize, fsdLocation) = readVds src
        let fsdOffset = (int64 partitionStart + int64 fsdLocation) * int64 logicalBlockSize

        if fsdOffset + 466L > src.Length then
            failwith "Image UDF invalide (descripteur de jeu de fichiers manquant)"

        let fsd = src.ReadBytes fsdOffset 466

        if readUInt16LE fsd 0 <> 0x0100 then
            failwith "Image UDF invalide (descripteur de jeu de fichiers introuvable)"

        let rootIcb = readUInt32LE fsd 404

        let rec find (icbLocation: int) (remaining: string[]) =
            let entries = readDirEntries src icbLocation partitionStart logicalBlockSize
            let segment = remaining.[0]
            let isLast = remaining.Length = 1

            let entry =
                entries
                |> List.tryFind (fun (n, _, _) -> String.Equals(n, segment, StringComparison.OrdinalIgnoreCase))

            match entry with
            | None -> failwithf "Fichier introuvable dans l'image '%s' : %s" isoPath pathInImage
            | Some(_, childIcb, isDir) when isLast ->
                if isDir then
                    failwithf "'%s' est un répertoire dans l'image '%s'" pathInImage isoPath

                let fileIcb = (int64 partitionStart + int64 childIcb) * int64 logicalBlockSize
                readIcbData src fileIcb partitionStart logicalBlockSize
            | Some(_, childIcb, isDir) ->
                if not isDir then
                    failwithf "'%s' n'est pas un répertoire dans l'image '%s'" pathInImage isoPath

                find childIcb remaining.[1..]

        find rootIcb segments

/// Extraction d'une image (ISO9660 ou UDF pour les DVD) dans un répertoire cible.
module IsoImage =

    /// Extrait le contenu de l'image dans le répertoire cible. Retourne le
    /// nombre de fichiers extraits.
    let extractCore (isoPath: string) (targetPath: string) : int =
        use stream = IsoSource.openFile isoPath
        let src = IsoSource.fromStream stream
        Directory.CreateDirectory(targetPath) |> ignore

        if Udf.isUdf src then
            Udf.extract src targetPath
        else
            Iso9660.extract src isoPath targetPath

    let extract (isoPath: string) (targetPath: string) : Result<int, string> =
        try Ok(extractCore isoPath targetPath)
        with ex -> Error ex.Message

    /// Lit le contenu d'un fichier de l'image par son chemin (ISO9660 ou UDF).
    let readFileCore (isoPath: string) (pathInImage: string) : byte[] =
        use stream = IsoSource.openFile isoPath
        let src = IsoSource.fromStream stream

        if Udf.isUdf src then
            Udf.readFileByPath src isoPath pathInImage
        else
            Iso9660.readFileByPath src isoPath pathInImage

    let readFile (isoPath: string) (pathInImage: string) : Result<byte[], string> =
        try Ok(readFileCore isoPath pathInImage)
        with ex -> Error ex.Message

/// Adaptateur ISO (ISO9660 / UDF) pour le moteur d'images disque DiploWalker.Disk.
///
/// L'extraction réutilise le parseur maison (`Iso9660`/`Udf`/`IsoImage`) et
/// l'UDF est détecté à l'extraction (un seul format `Iso`). La création produit
/// une image ISO9660 niveau 1 (8.3, ASCII) via un générateur maison, sans
/// Joliet ni Rock Ridge.
module IsoFs =
    open Serilog

    /// Tente d'extraire une image ISO (ISO9660 ou UDF) dans `targetDir`.
    /// Retourne Some(nombreFichiers) si le format est géré, None sinon.
    let tryExtract (sourcePath: string) (targetDir: string) : int option =
        if not (File.Exists(sourcePath)) then
            None
        else
            try
                Some(IsoImage.extractCore sourcePath targetDir)
            with
            | :? IOException as ex ->
                Log.Warning(ex, "Échec extraction ISO pour {Path}", sourcePath)
                None
            | ex ->
                Log.Warning(ex, "Erreur inattendue extraction ISO pour {Path}", sourcePath)
                None

    /// Les images ISO sont en lecture seule : aucune réécriture n'est possible.
    let tryWriteBack (_sourcePath: string) (_sourceDir: string) : bool = false

    // ── Génération ISO9660 niveau 1 ─────────────────────────────────

    let private blockSize = 2048

    /// Écrit une valeur entière little-endian (4 octets).
    let private writeInt32LE (buf: byte[]) (offset: int) (value: int) =
        buf.[offset] <- byte (value &&& 0xFF)
        buf.[offset + 1] <- byte ((value >>> 8) &&& 0xFF)
        buf.[offset + 2] <- byte ((value >>> 16) &&& 0xFF)
        buf.[offset + 3] <- byte ((value >>> 24) &&& 0xFF)

    let private writeInt16LE (buf: byte[]) (offset: int) (value: int) =
        buf.[offset] <- byte (value &&& 0xFF)
        buf.[offset + 1] <- byte ((value >>> 8) &&& 0xFF)

    /// Écrit une valeur en « both endian » (LE puis BE) sur 8 octets.
    let private writeBothEndian32 (buf: byte[]) (offset: int) (value: int) =
        writeInt32LE buf offset value
        writeInt32LE buf (offset + 4) value

    /// Écrit une valeur non signée (jusqu'à 32 bits) en « both endian ».
    /// Accepte un `int64` pour représenter les tailles > 2 Go du niveau 1 ;
    /// les masques `&&& 0xFF` extraient les octets exacts indépendamment du
    /// signe éventuel de la valeur encodée sur 32 bits.
    let private writeBothEndian32U (buf: byte[]) (offset: int) (value: int64) =
        buf.[offset] <- byte (value &&& 0xFFL)
        buf.[offset + 1] <- byte ((value >>> 8) &&& 0xFFL)
        buf.[offset + 2] <- byte ((value >>> 16) &&& 0xFFL)
        buf.[offset + 3] <- byte ((value >>> 24) &&& 0xFFL)
        buf.[offset + 4] <- byte (value &&& 0xFFL)
        buf.[offset + 5] <- byte ((value >>> 8) &&& 0xFFL)
        buf.[offset + 6] <- byte ((value >>> 16) &&& 0xFFL)
        buf.[offset + 7] <- byte ((value >>> 24) &&& 0xFFL)

    /// Écrit une valeur en « both endian » sur 4 octets (LE16 puis BE16).
    let private writeBothEndian16 (buf: byte[]) (offset: int) (value: int) =
        writeInt16LE buf offset value
        writeInt16LE buf (offset + 2) value

    /// Date ISO9660 (7 octets : années depuis 1900, mois, jour, heure,
    /// minute, seconde, décalage de fuseau en quarts d'heure / 15 min).
    let private writeIsoDate (buf: byte[]) (offset: int) (dt: DateTime) =
        buf.[offset] <- byte (dt.Year - 1900)
        buf.[offset + 1] <- byte dt.Month
        buf.[offset + 2] <- byte dt.Day
        buf.[offset + 3] <- byte dt.Hour
        buf.[offset + 4] <- byte dt.Minute
        buf.[offset + 5] <- byte dt.Second
        buf.[offset + 6] <- byte 0

    /// Convertit un nom de fichier hôte en nom ISO9660 niveau 1 (8.3, ASCII
    /// majuscule). Retourne None si le nom ne peut pas être représenté.
    let private toIsoName (name: string) : string option =
        if String.IsNullOrWhiteSpace name then
            None
        else
            let upper = name.ToUpperInvariant()
            let dotIdx = upper.LastIndexOf('.')

            let rawBase, rawExt =
                if dotIdx > 0 && dotIdx < upper.Length - 1 then
                    upper.Substring(0, dotIdx), upper.Substring(dotIdx + 1)
                else
                    upper, ""

            let sanitizeChar (c: char) = if Char.IsLetterOrDigit c || c = '_' then c else '_'
            let baseName = rawBase |> Seq.map sanitizeChar |> Seq.toArray |> System.String
            let ext = rawExt |> Seq.map sanitizeChar |> Seq.toArray |> System.String

            let baseName = if baseName = "" then "FILE" else baseName
            let basePart = if baseName.Length > 8 then baseName.Substring(0, 8) else baseName
            let extPart = if ext.Length > 3 then ext.Substring(0, 3) else ext

            let isoName = if extPart <> "" then basePart + "." + extPart else basePart
            if isoName = "" then None else Some isoName

    /// Nœud de l'arborescence des fichiers à écrire dans l'image.
    type private Node =
        { Name: string // nom ISO9660 8.3 (unique au sein du répertoire)
          HostPath: string // chemin hôte (répertoire = son dossier, fichier = le fichier)
          Children: Node list // non vide uniquement pour les répertoires
          mutable Extent: int
          mutable DataLength: int64
          mutable DirLength: int } // longueur des données du répertoire (pour les répertoires)

    /// Diffuse un nom 8.3 (`BASE[.EXT]`) en une variante unique au sein d'un
    /// répertoire, en cas de collision entre noms hôte distincts : le base est
    /// tronqué et un compteur (`_1`, `_2`, …) y est ajouté, forme `BASE_k.EXT`.
    /// Retourne None si aucune variante libre ne peut être générée (espace 8.3
    /// épuisé — cas théorique en niveau 1).
    let private mangleIsoName (initial: string) (taken: System.Collections.Generic.HashSet<string>) : string option =
        let dotIdx = initial.LastIndexOf('.')

        let basePart, extPart =
            if dotIdx > 0 && dotIdx <= initial.Length - 1 then
                initial.Substring(0, dotIdx), initial.Substring(dotIdx + 1)
            else
                initial, ""

        let mutable k = 1
        let mutable result: string option = None

        while result.IsNone && k < 1000 do
            let suffix = "_" + string k
            let maxBase = 8 - suffix.Length
            let nb = if basePart.Length > maxBase then basePart.Substring(0, maxBase) else basePart
            let candidate =
                if nb = "" then
                    (if extPart <> "" then "." + extPart else "")
                elif extPart <> "" then
                    nb + suffix + "." + extPart
                else
                    nb + suffix

            if not (taken.Contains(candidate)) then
                result <- Some candidate
            else
                k <- k + 1

        result

    /// Assure l'unicité des noms 8.3 parmi les entrées d'un même répertoire.
    /// `entries` = paires (nomHôte, nomISO optionnel). Les collisions sont
    /// désambiguïsées via `mangleIsoName` ; les noms dont la variante ne peut
    /// pas être générée sont écartés (None).
    let private uniqueNames (entries: (string * string option) list) : (string * string) list =
        let taken = System.Collections.Generic.HashSet<string>()
        let result = ResizeArray<string * string>()

        for (host, iso) in entries do
            match iso with
            | None -> ()
            | Some name ->
                let finalName =
                    if taken.Contains(name) then
                        match mangleIsoName name taken with
                        | Some m -> m
                        | None -> name // espace épuisé : on garde le nom, doublon assumé (dernier recours)
                    else
                        name

                taken.Add(finalName) |> ignore
                result.Add(host, finalName)

        List.ofSeq result

    /// Construit l'arborescence depuis le répertoire source, en assurant
    /// l'unicité des noms 8.3 dans chaque répertoire. Les fichiers ne sont pas
    /// chargés en mémoire : seule la taille (DataLength) est relevée à la
    /// création des nœuds ; le contenu est lu au moment de l'écriture.
    let rec private buildTree (sourceDir: string) : Node list =
        let dirEntries =
            Directory.GetDirectories(sourceDir)
            |> Array.map (fun d -> d, toIsoName (Path.GetFileName d))
            |> Array.toList

        let fileEntries =
            Directory.GetFiles(sourceDir)
            |> Array.map (fun f -> f, toIsoName (Path.GetFileName f))
            |> Array.toList

        // Les répertoires et fichiers partagent le même espace de noms 8.3
        // du répertoire courant : ils sont désambiguïsés ensemble.
        let allEntries = List.append dirEntries fileEntries
        let named = uniqueNames (List.filter (fun (_, iso) -> iso.IsSome) allEntries)

        let isDirInto (host: string) =
            Directory.Exists host

        named
        |> List.map (fun (host, name) ->
            if isDirInto host then
                { Name = name
                  HostPath = host
                  Children = buildTree host
                  Extent = 0
                  DataLength = 0L
                  DirLength = 0 }
            else
                let length = FileInfo(host).Length

                if length > 0xFFFFFFFFL then
                    // Le format ISO9660 stocke la taille d'un fichier sur 32
                    // bits (max ~4 Go) : un fichier plus gros ne peut pas être
                    // représenté en niveau 1.
                    invalidArg "sourceDir" (sprintf "Le fichier '%s' est trop volumineux pour ISO9660 niveau 1 (%d octets)" host length)

                { Name = name
                  HostPath = host
                  Children = []
                  Extent = 0
                  DataLength = length
                  DirLength = 0 })

    /// Longueur physique d'un enregistrement de répertoire pour un nom donné :
    /// 33 + longueur du nom, complété à un nombre pair.
    let private recordLength (name: string) =
        let raw = 33 + name.Length
        raw + (raw % 2)

    /// Longueur physique de l'enregistrement d'un nœud enfant.
    let private childRecordLength (node: Node) = recordLength node.Name

    /// Calcule la longueur des données d'un répertoire (octets) : somme des
    /// enregistrements "." et ".." plus ceux de tous les enfants.
    let rec private computeDirLength (node: Node) (selfDotRecordLen: int) (parentRecordLen: int) =
        node.DirLength <-
            selfDotRecordLen
            + parentRecordLen
            + (node.Children |> List.sumBy childRecordLength)

        for child in node.Children do
            if child.Children <> [] then
                computeDirLength child (recordLength "\x00") (recordLength "\x01")

    /// Enregistrement de répertoire ISO9660 conforme à la structure ECMA-119.
    let private buildDirRecord (name: string) (extent: int) (dataLength: int64) (isDir: bool) =
        let nameBytes = Encoding.ASCII.GetBytes name
        let nameLen = nameBytes.Length
        let recLen = 33 + nameLen
        let paddedLen = recLen + (recLen % 2)
        let buf = Array.zeroCreate<byte> paddedLen
        // LEN_DR = longueur totale de l'enregistrement, complétée à un nombre
        // pair (le saut de l'enregistrement s'appuie sur cette valeur).
        buf.[0] <- byte paddedLen
        writeBothEndian32 buf 2 extent
        writeBothEndian32U buf 10 dataLength
        let now = DateTime.Now
        writeIsoDate buf 18 now
        buf.[25] <- if isDir then 0x02uy else 0x00uy
        writeBothEndian16 buf 28 1 // volume sequence number
        buf.[32] <- byte nameLen
        Array.blit nameBytes 0 buf 33 nameLen
        buf

    /// Volume Identifier ISO9660 (≤ 32 octets ASCII) dérivé du nom du dossier
    /// source : caractères non-alphanumériques remplacés par `_`, tronqué à
    /// 32 caractères. Repli sur "DIPLO_ISO" si le résultat est vide.
    let private toVolumeId (sourceDir: string) : string =
        let name = Path.GetFileName(Path.GetFullPath sourceDir).ToUpperInvariant()
        let sanitizeChar (c: char) = if Char.IsLetterOrDigit c || c = '_' then c else '_'
        let sanitized = name |> Seq.map sanitizeChar |> Seq.toArray |> System.String

        if System.String.IsNullOrWhiteSpace sanitized then
            "DIPLO_ISO"
        else
            let cut = if sanitized.Length > 32 then sanitized.Substring(0, 32) else sanitized
            if cut = "" then "DIPLO_ISO" else cut

    /// Écrit l'image ISO9660 niveau 1 dans `destPath` depuis `sourceDir`.
    /// Retourne le nombre de fichiers écrits.
    let createCore (sourceDir: string) (destPath: string) : int =
        if not (Directory.Exists sourceDir) then
            invalidArg "sourceDir" (sprintf "Le répertoire source n'existe pas : '%s'" sourceDir)

        let rootChildren = buildTree sourceDir

        // Nœud synthétique racine.
        let rootRecord =
            { Name = ""
              HostPath = sourceDir
              Children = rootChildren
              Extent = 0
              DataLength = 0L
              DirLength = 0 }


        // Calcule les longueurs de répertoires (root + sous-répertoires).
        computeDirLength rootRecord (recordLength "\x00") (recordLength "\x01")

        // Allocation des extents : répertoires puis fichiers.
        let next = ref 18
        let mutable fileCount = 0
        let rootBlocks = max 1 ((rootRecord.DirLength + blockSize - 1) / blockSize)
        rootRecord.Extent <- !next
        next := !next + rootBlocks

        let rec allocSubDirs (children: Node list) =
            for child in children do
                if child.Children <> [] then
                    let blocks = max 1 ((child.DirLength + blockSize - 1) / blockSize)
                    child.Extent <- !next
                    next := !next + blocks
                    allocSubDirs child.Children

        allocSubDirs rootChildren

        let rec allocFiles (children: Node list) =
            for child in children do
                if child.Children = [] then
                    let blocks = max 1 (int ((child.DataLength + int64 blockSize - 1L) / int64 blockSize))
                    child.Extent <- !next
                    next := !next + blocks
                    fileCount <- fileCount + 1
                else
                    allocFiles child.Children

        allocFiles rootChildren

        let totalBlocks = !next

        let parentDir = Path.GetDirectoryName(destPath)

        if not (String.IsNullOrEmpty parentDir) then
            Directory.CreateDirectory(parentDir) |> ignore

        use fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None)
        fs.SetLength(int64 totalBlocks * int64 blockSize)

        let writeBlock (i: int) (data: byte[]) =
            fs.Position <- int64 i * int64 blockSize
            fs.Write(data, 0, data.Length)

        // Sérialise le contenu d'un répertoire (enregistrements concaténés).
        let buildDirData (node: Node) (parent: Node) : byte[] =
            let records = ResizeArray<byte[]>()

            // "." : se référence lui-même ; ".." : référence le parent.
            let selfRec = buildDirRecord "\x00" node.Extent (int64 node.DirLength) true
            let parentRec = buildDirRecord "\x01" parent.Extent (int64 parent.DirLength) true
            records.Add selfRec
            records.Add parentRec

            // En-tête + enfants (répertoires puis fichiers, dans l'ordre de l'arbre).
            for child in node.Children do
                let isDir = child.Children <> []
                let childLen = if isDir then int64 child.DirLength else child.DataLength
                records.Add(buildDirRecord child.Name child.Extent childLen isDir)

            Array.concat (Seq.toArray records)

        // Écrit tous les répertoires (racine + sous-répertoires) puis fichiers.
        let rec writeDirs (node: Node) (parent: Node) =
            let data = buildDirData node parent
            // Remplit sur un nombre entier de blocs.
            let buf = Array.zeroCreate<byte> (node.DirLength)
            Array.blit data 0 buf 0 (min data.Length buf.Length)
            fs.Position <- int64 node.Extent * int64 blockSize
            fs.Write(buf, 0, buf.Length)

            for child in node.Children do
                if child.Children <> [] then
                    writeDirs child node

        writeDirs rootRecord rootRecord

        // Copie le contenu d'un fichier hôte vers l'image par blocs (1 Mo),
        // sans matérialiser le fichier en mémoire : indispensable pour les
        // fichiers de l'ordre du Go.
        let copyFileToImage (hostPath: string) (extent: int) (dataLength: int64) =
            use input = new FileStream(hostPath, FileMode.Open, FileAccess.Read, FileShare.Read)
            let buffer = Array.zeroCreate<byte> (1024 * 1024)
            fs.Position <- int64 extent * int64 blockSize
            let mutable remaining = dataLength

            while remaining > 0L do
                let toRead = int (min remaining (int64 buffer.Length))
                let read = input.Read(buffer, 0, toRead)

                if read = 0 then
                    failwithf "Fin de flux inattendue lors de la lecture du fichier '%s'" hostPath

                fs.Write(buffer, 0, read)
                remaining <- remaining - int64 read

        let rec writeFiles (children: Node list) =
            for child in children do
                if child.Children = [] then
                    copyFileToImage child.HostPath child.Extent child.DataLength
                else
                    writeFiles child.Children

        writeFiles rootChildren

        // Primary Volume Descriptor (bloc 16).
        let pvd = Array.zeroCreate<byte> blockSize
        pvd.[0] <- 1uy
        Encoding.ASCII.GetBytes("CD001", 0, 5, pvd, 1) |> ignore
        pvd.[6] <- 1uy
        // Standard Identifier « CDROM » (32 octets, justifié à gauche).
        Encoding.ASCII.GetBytes("CDROM", 0, 5, pvd, 8) |> ignore
        // Volume Identifier (32 octets), dérivé du dossier source.
        let volumeId = toVolumeId sourceDir
        Encoding.ASCII.GetBytes(volumeId, 0, volumeId.Length, pvd, 40) |> ignore
        // Volume Space Size (both endian).
        writeBothEndian32 pvd 80 totalBlocks
        // Volume Set Size et Volume Sequence Number (both endian, = 1).
        writeBothEndian16 pvd 120 1 // volume set size
        writeBothEndian16 pvd 124 1 // volume sequence number
        // Logical Block Size = 2048 (both endian).
        writeBothEndian16 pvd 128 blockSize
        // Root directory record (34 + nom(=1) = 35 → 36 octets complétés).
        let rootRecordBytes = buildDirRecord "\x00" rootRecord.Extent (int64 rootRecord.DirLength) true
        Array.blit rootRecordBytes 0 pvd 156 rootRecordBytes.Length
        writeBlock 16 pvd

        // Volume Descriptor Set Terminator (bloc 17).
        let vdst = Array.zeroCreate<byte> blockSize
        vdst.[0] <- 255uy
        Encoding.ASCII.GetBytes("CD001", 0, 5, vdst, 1) |> ignore
        vdst.[6] <- 1uy
        writeBlock 17 vdst

        fs.Flush()
        fileCount

    /// Crée une image ISO depuis `sourceDir`. Retourne Ok(cheminImage).
    let create (sourceDir: string) (destPath: string) : Result<string, string> =
        try
            createCore sourceDir destPath |> ignore
            Ok destPath
        with ex -> Error ex.Message



