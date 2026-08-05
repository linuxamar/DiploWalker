namespace Diplo.Volume.Drivers

open System
open System.IO
open System.Text
open System.Text.Json
open Serilog
open Diplo.Abstractions
open Diplo.Abstractions.Interfaces

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

    type private DirRecord =
        { extent: int
          dataLength: int
          isDirectory: bool
          name: string }

    let private parseDirRecord (data: byte[]) (offset: int) =
        let length = int data.[offset]
        if length = 0 then
            None
        else
            let extent = readUInt32LE data (offset + 2)
            let dataLength = readUInt32LE data (offset + 10)
            let flags = data.[offset + 25]
            let nameLen = int data.[offset + 32]
            let rawName = ascii data (offset + 33) nameLen
            Some
                { extent = extent
                  dataLength = dataLength
                  isDirectory = (flags &&& 0x02uy) <> 0uy
                  name = rawName }

    let private readDirectoryRecords (iso: byte[]) (extent: int) (length: int) =
        let start = extent * blockSize
        let count = min (iso.Length - start) (max length 0)
        if count <= 0 then
            []
        else
            let data = iso.[start .. start + count - 1]
            let records = ResizeArray<DirRecord>()
            let mutable pos = 0
            while pos < data.Length do
                let len = int data.[pos]
                if len = 0 then
                    let blockOffset = pos % blockSize
                    if blockOffset = 0 then pos <- pos + 1 else pos <- pos + (blockSize - blockOffset)
                else
                    match parseDirRecord data pos with
                    | Some rec' ->
                        records.Add(rec')
                        pos <- pos + len
                    | None -> pos <- pos + 1
            records |> Seq.toList

    let private readFile (iso: byte[]) (extent: int) (dataLength: int) =
        let start = extent * blockSize
        let count = min (iso.Length - start) (max dataLength 0)
        if count <= 0 then Array.empty else iso.[start .. start + count - 1]

    /// Nom ISO9660 d'un fichier : "NOM.EXT;1" → retire le suffixe ;version et les points terminaux.
    let private cleanFileName (rawName: string) =
        let withoutVersion =
            let idx = rawName.IndexOf(';')
            if idx >= 0 then rawName.Substring(0, idx) else rawName
        withoutVersion.TrimEnd('.', '\x00')

    let private cleanDirName (rawName: string) =
        rawName.Trim([| '\x00'; '\x01'; ' ' |])

    /// Remplace les caractères interdits dans un nom de fichier Windows.
    let private sanitizeName (name: string) =
        name
        |> Seq.map (fun c ->
            if Char.IsControl c
               || c = '\\' || c = '/' || c = ':' || c = '*' || c = '?' || c = '"' || c = '<' || c = '>' || c = '|' then
                '_'
            else c)
        |> String.Concat

    let private pathFromRelative (targetPath: string) (relPath: string) =
        Path.Combine(Array.append [| targetPath |] (relPath.Split('/')))

    /// Lit le descripteur de volume principal (PVD) et retourne les
    /// (extent, longueur) du répertoire racine.
    let readPvd (iso: byte[]) (isoPath: string) : int * int =
        let pvdOffset = 16 * blockSize
        if pvdOffset + 170 > iso.Length then
            failwithf "Le fichier '%s' est trop petit pour être une image ISO9660" isoPath
        let magic = ascii iso (pvdOffset + 1) 5
        if magic <> "CD001" then
            failwithf "'%s' n'est pas une image ISO9660 valide (en-tête CD001 absent)" isoPath
        let logicalBlockSize = readUInt16LE iso (pvdOffset + 128)
        if logicalBlockSize <> blockSize then
            failwithf "Taille de bloc logique ISO %d non prise en charge (attendu %d)" logicalBlockSize blockSize
        let rootLen = int iso.[pvdOffset + 156]
        if rootLen = 0 then
            failwith "Image ISO9660 invalide (enregistrement de répertoire racine manquant)"
        let rootExtent = readUInt32LE iso (pvdOffset + 158)
        let rootLength = readUInt32LE iso (pvdOffset + 166)
        (rootExtent, rootLength)

    /// Retrouve le contenu d'un fichier de l'image par son chemin ISO9660
    /// (chemin de répertoires séparés par '/', ex. "/DOSSIER/HELLO.TXT").
    let readFileByPath (iso: byte[]) (isoPath: string) (pathInImage: string) : byte[] =
        let segments =
            pathInImage.Split('/')
            |> Array.filter (fun s -> s <> "" && s <> "." && s <> "..")
        if segments.Length = 0 then
            failwithf "Chemin de fichier invalide : '%s'" pathInImage
        let rec find (extent: int) (length: int) (remaining: string[]) =
            let entries = readDirectoryRecords iso extent length
            let segment = remaining.[0]
            let isLast = remaining.Length = 1
            let name = if isLast then cleanFileName segment else cleanDirName segment
            let entry = entries |> List.tryFind (fun e -> (if isLast then cleanFileName e.name else cleanDirName e.name) = name)
            match entry with
            | None ->
                failwithf "Fichier introuvable dans l'image '%s' : %s" isoPath pathInImage
            | Some e when isLast ->
                if e.isDirectory then
                    failwithf "'%s' est un répertoire dans l'image '%s'" pathInImage isoPath
                readFile iso e.extent e.dataLength
            | Some e ->
                if not e.isDirectory then
                    failwithf "'%s' n'est pas un répertoire dans l'image '%s'" pathInImage isoPath
                find e.extent e.dataLength remaining.[1..]
        let (rootExtent, rootLength) = readPvd iso isoPath
        find rootExtent rootLength segments

    /// Extrait tous les fichiers d'un tampon ISO9660 dans le répertoire cible.
    /// Retourne le nombre de fichiers extraits.
    let extract (iso: byte[]) (isoPath: string) (targetPath: string) : int =
        let (rootExtent, rootLength) = readPvd iso isoPath

        let pending = System.Collections.Generic.Queue<(string * int * int)>()
        let visited = System.Collections.Generic.HashSet<string * int>()
        pending.Enqueue("", rootExtent, rootLength)
        visited.Add("", rootExtent) |> ignore
        let mutable count = 0
        while pending.Count > 0 do
            let (relDir, extent, length) = pending.Dequeue()
            for entry in readDirectoryRecords iso extent length do
                if entry.isDirectory then
                    let name = cleanDirName entry.name
                    if name <> "" && name <> "." && name <> ".." then
                        let sub = if relDir = "" then sanitizeName name else relDir + "/" + sanitizeName name
                        if sub <> "" && sub <> "." && sub <> ".." then
                            Directory.CreateDirectory(pathFromRelative targetPath sub) |> ignore
                            if visited.Add(sub, entry.extent) then
                                pending.Enqueue(sub, entry.extent, entry.dataLength)
                else
                    let name = cleanFileName entry.name
                    if name <> "" && name <> "." && name <> ".." then
                        let sub = if relDir = "" then sanitizeName name else relDir + "/" + sanitizeName name
                        if sub <> "" && sub <> "." && sub <> ".." then
                            let filePath = pathFromRelative targetPath sub
                            Directory.CreateDirectory(Path.GetDirectoryName(filePath)) |> ignore
                            File.WriteAllBytes(filePath, readFile iso entry.extent entry.dataLength)
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

    let private sanitizeName (name: string) =
        name
        |> Seq.map (fun c ->
            if Char.IsControl c
               || c = '\\' || c = '/' || c = ':' || c = '*' || c = '?' || c = '"' || c = '<' || c = '>' || c = '|' then
                '_'
            else c)
        |> String.Concat

    let private pathFromRelative (targetPath: string) (relPath: string) =
        Path.Combine(Array.append [| targetPath |] (relPath.Split('/')))

    /// Localise le descripteur NSR dans la séquence de reconnaissance de
    /// volume (bloc 16). Retourne l'offset du descripteur ou -1.
    let private findNsr (iso: byte[]) : int =
        let mutable offset = 16 * blockSize
        let mutable result = -1
        let mutable running = offset + blockSize <= iso.Length
        while running do
            let ty = int iso.[offset]
            let id = ascii iso (offset + 1) 5
            if id = "NSR02" || id = "NSR03" then
                result <- offset
                running <- false
            elif ty = 4 || id = "TEA01" then
                running <- false
            else
                offset <- offset + blockSize
                running <- offset + blockSize <= iso.Length
        result

    /// Détecte une image UDF en cherchant un descripteur NSR.
    let isUdf (iso: byte[]) : bool =
        findNsr iso >= 0

    /// Retrouve la séquence de descripteurs de volume depuis le pointeur
    /// d'ancrage (AVDP) situé conventionnellement au bloc 256.
    let private findVdsLocation (iso: byte[]) : int =
        let mutable result = -1
        for candidate in [ 256; 257; 512 ] do
            if result < 0 && (candidate + 1) * blockSize <= iso.Length then
                let offset = candidate * blockSize
                if readUInt16LE iso offset = 0x0002 then
                    result <- readUInt32LE iso (offset + 20)
        if result < 0 then
            failwith "Image UDF invalide (pointeur de volume d'ancrage introuvable)"
        result

    /// Lit la séquence de descripteurs de volume : emplacement de partition,
    /// taille de bloc logique et emplacement du descripteur de jeu de fichiers
    /// (issu de logicalVolContentsUse du descripteur de volume logique).
    let private readVds (iso: byte[]) =
        let mutable offset = findVdsLocation iso * blockSize
        let mutable partitionStart = 0
        let mutable logicalBlockSize = 2048
        let mutable fsdLocation = -1
        let mutable running = true
        while running && offset + blockSize <= iso.Length do
            let tagId = readUInt16LE iso offset
            if tagId = 0x0005 then
                partitionStart <- readUInt32LE iso (offset + 188)
                offset <- offset + blockSize
            elif tagId = 0x0006 then
                logicalBlockSize <- readUInt32LE iso (offset + 212)
                fsdLocation <- readUInt32LE iso (offset + 252)
                offset <- offset + blockSize
            else
                offset <- offset + blockSize
                if tagId = 0x0008 || tagId = 0x0000 then running <- false
        if fsdLocation < 0 then
            failwith "Image UDF invalide (descripteur de volume logique manquant)"
        (partitionStart, logicalBlockSize, fsdLocation)

    let private readUInt64LE (data: byte[]) (offset: int) =
        let mutable v = 0UL
        for i in 0 .. 7 do
            v <- v ||| (uint64 data.[offset + i] <<< (8 * i))
        v

    /// Assemble les données pointées par un ICB (fichier ou répertoire) en
    /// parcourant ses descripteurs d'allocation (short_ad, long_ad ou
    /// extended_ad) ou en lisant le contenu inline (AD_IN_ICB).
    let private readIcbData (iso: byte[]) (icbOffset: int) (partitionStart: int) (logicalBlockSize: int) =
        let tag = readUInt16LE iso icbOffset
        if tag <> 0x0105 && tag <> 0x0201 then
            failwithf "Image UDF invalide (ICB fichier manquant, tag 0x%04x)" tag
        let flags = readUInt16LE iso (icbOffset + 34)
        let allocationType = flags &&& 7
        let informationLength = int64 (readUInt64LE iso (icbOffset + 56))
        let extendedAttrLength = readUInt32LE iso (icbOffset + 168)
        let allocLength = readUInt32LE iso (icbOffset + 172)
        let allocStart = icbOffset + 176 + extendedAttrLength
        let allocEnd = allocStart + allocLength
        let content = ResizeArray<byte>()
        if allocationType = 3 then
            // AD_IN_ICB : les données sont stockées dans l'ICB lui-même.
            let inlineLen = int (min informationLength (int64 allocLength))
            for i in 0 .. inlineLen - 1 do
                if allocStart + i < iso.Length then content.Add(iso.[allocStart + i])
        else
            let step = if allocationType = 0 then 8 elif allocationType = 1 then 16 else 20
            let locOffset = if allocationType = 2 then 12 else 4
            let mutable pos = allocStart
            let mutable running = pos + 8 <= allocEnd && pos + 8 <= iso.Length
            while running do
                let extLength = readUInt32LE iso pos
                if extLength = 0 then running <- false
                else
                    let length = extLength &&& 0x3FFFFFFF
                    let location = readUInt32LE iso (pos + locOffset)
                    let start = (partitionStart + location) * logicalBlockSize
                    let available = min length (max 0 (iso.Length - start))
                    if available > 0 then
                        content.AddRange(iso.[start .. start + available - 1])
                    pos <- pos + step
                    running <- pos + 8 <= allocEnd && pos + 8 <= iso.Length
        let data = content.ToArray()
        if int64 data.Length > informationLength then
            Array.sub data 0 (int informationLength)
        else
            data

    /// Liste les entrées d'un répertoire UDF identifié par son ICB. Retourne
    /// pour chaque entrée (nom brut, emplacement de l'ICB enfant, estRépertoire).
    /// Les entrées parent (« . » / « .. ») et les noms vides sont exclus.
    let readDirEntries (iso: byte[]) (icbLocation: int) (partitionStart: int) (logicalBlockSize: int) : (string * int * bool) list =
        let icbOffset = (partitionStart + icbLocation) * logicalBlockSize
        if icbOffset + 36 > iso.Length then
            failwithf "Image UDF invalide (ICB à l'adresse %d)" icbOffset
        let icbTag = readUInt16LE iso icbOffset
        if icbTag <> 0x0105 && icbTag <> 0x0201 then
            failwithf "Image UDF invalide (ICB manquant, tag 0x%04x)" icbTag
        let dirData = readIcbData iso icbOffset partitionStart logicalBlockSize
        let entries = ResizeArray<string * int * bool>()
        let mutable pos = 0
        let mutable running = pos + 38 <= dirData.Length
        while running do
            let fidTag = readUInt16LE dirData pos
            if fidTag <> 0x0101 then running <- false
            else
                let characteristics = dirData.[pos + 18]
                let nameLen = int dirData.[pos + 19]
                let childIcbLocation = readUInt32LE dirData (pos + 24)
                let implUseLen = readUInt16LE dirData (pos + 36)
                // Le nom de fichier est un d-string (ECMA-167 1/7.2.12) :
                // un octet de type (8 = ASCII, 16 = UTF-16BE) suivi du nom.
                let nameOffset = pos + 38 + implUseLen
                let rawName =
                    if nameLen <= 1 then ""
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
    let extract (iso: byte[]) (targetPath: string) : int =
        let nsrOffset = findNsr iso
        if nsrOffset < 0 then
            failwith "Image non UDF (descripteur NSR introuvable)"
        let (partitionStart, logicalBlockSize, fsdLocation) = readVds iso

        let fsdOffset = (partitionStart + fsdLocation) * logicalBlockSize
        if fsdOffset + 466 > iso.Length then
            failwith "Image UDF invalide (descripteur de jeu de fichiers manquant)"
        if readUInt16LE iso fsdOffset <> 0x0100 then
            failwith "Image UDF invalide (descripteur de jeu de fichiers introuvable)"
        let rootIcb = readUInt32LE iso (fsdOffset + 404)

        let pending = System.Collections.Generic.Queue<string * int>()
        let visited = System.Collections.Generic.HashSet<string * int>()
        pending.Enqueue("", rootIcb)
        visited.Add("", rootIcb) |> ignore
        let mutable count = 0
        while pending.Count > 0 do
            let (relDir, icbLocation) = pending.Dequeue()
            let icbOffset = (partitionStart + icbLocation) * logicalBlockSize
            if icbOffset + 36 > iso.Length then
                failwithf "Image UDF invalide (ICB à l'adresse %d)" icbOffset
            let icbTag = readUInt16LE iso icbOffset
            if icbTag <> 0x0105 && icbTag <> 0x0201 then
                failwithf "Image UDF invalide (ICB manquant, tag 0x%04x)" icbTag
            let fileType = int iso.[icbOffset + 27]
            if fileType = 4 then
                // Répertoire : la séquence de descripteurs d'identifiant de
                // fichier est stockée dans les blocs pointés par l'ICB.
                for (rawName, childIcbLocation, isDir) in readDirEntries iso icbLocation partitionStart logicalBlockSize do
                    let name = sanitizeName rawName
                    if name <> "" && name <> "." && name <> ".." then
                        let sub = if relDir = "" then name else relDir + "/" + name
                        if isDir then
                            Directory.CreateDirectory(pathFromRelative targetPath sub) |> ignore
                            if visited.Add(sub, childIcbLocation) then
                                pending.Enqueue(sub, childIcbLocation)
                        else
                            let fileIcb = (partitionStart + childIcbLocation) * logicalBlockSize
                            let fileData = readIcbData iso fileIcb partitionStart logicalBlockSize
                            let filePath = pathFromRelative targetPath sub
                            Directory.CreateDirectory(Path.GetDirectoryName(filePath)) |> ignore
                            File.WriteAllBytes(filePath, fileData)
                            count <- count + 1
        count

    /// Retrouve le contenu d'un fichier de l'image par son chemin UDF
    /// (chemin de répertoires séparés par '/', ex. "/DOSSIER/HELLO.TXT").
    let readFileByPath (iso: byte[]) (isoPath: string) (pathInImage: string) : byte[] =
        let segments =
            pathInImage.Split('/')
            |> Array.filter (fun s -> s <> "" && s <> "." && s <> "..")
        if segments.Length = 0 then
            failwithf "Chemin de fichier invalide : '%s'" pathInImage
        let (partitionStart, logicalBlockSize, fsdLocation) = readVds iso
        let fsdOffset = (partitionStart + fsdLocation) * logicalBlockSize
        if fsdOffset + 466 > iso.Length then
            failwith "Image UDF invalide (descripteur de jeu de fichiers manquant)"
        if readUInt16LE iso fsdOffset <> 0x0100 then
            failwith "Image UDF invalide (descripteur de jeu de fichiers introuvable)"
        let rootIcb = readUInt32LE iso (fsdOffset + 404)
        let rec find (icbLocation: int) (remaining: string[]) =
            let entries = readDirEntries iso icbLocation partitionStart logicalBlockSize
            let segment = remaining.[0]
            let isLast = remaining.Length = 1
            let entry = entries |> List.tryFind (fun (n, _, _) -> n = segment)
            match entry with
            | None ->
                failwithf "Fichier introuvable dans l'image '%s' : %s" isoPath pathInImage
            | Some (_, childIcb, isDir) when isLast ->
                if isDir then
                    failwithf "'%s' est un répertoire dans l'image '%s'" pathInImage isoPath
                let fileIcb = (partitionStart + childIcb) * logicalBlockSize
                readIcbData iso fileIcb partitionStart logicalBlockSize
            | Some (_, childIcb, isDir) ->
                if not isDir then
                    failwithf "'%s' n'est pas un répertoire dans l'image '%s'" pathInImage isoPath
                find childIcb remaining.[1..]
        find rootIcb segments

/// Extraction d'une image (ISO9660 ou UDF pour les DVD) dans un répertoire cible.
module IsoImage =

    /// Extrait le contenu de l'image dans le répertoire cible. Retourne le
    /// nombre de fichiers extraits.
    let extract (isoPath: string) (targetPath: string) : int =
        let iso = File.ReadAllBytes(isoPath)
        Directory.CreateDirectory(targetPath) |> ignore
        if Udf.isUdf iso then
            Udf.extract iso targetPath
        else
            Iso9660.extract iso isoPath targetPath

    /// Lit le contenu d'un fichier de l'image par son chemin (ISO9660 ou UDF).
    let readFile (isoPath: string) (pathInImage: string) : byte[] =
        let iso = File.ReadAllBytes(isoPath)
        if Udf.isUdf iso then
            Udf.readFileByPath iso isoPath pathInImage
        else
            Iso9660.readFileByPath iso isoPath pathInImage

/// Pilote de volume permettant de monter un fichier ISO9660 ou UDF (DVD) en
/// extrayant son contenu (lecture seule) dans le répertoire cible.
type IsoDriver(dataRoot: string) =

    let store = RemoteVolumeStore(dataRoot, "iso")

    let validateIso (driverOpts: Map<string, string>) =
        match driverOpts.TryFind("iso") with
        | None -> failwith "L'option 'iso' est requise pour le driver ISO"
        | Some isoPath when String.IsNullOrWhiteSpace(isoPath) -> failwith "L'option 'iso' est requise pour le driver ISO"
        | Some isoPath ->
            if not (File.Exists(isoPath)) then
                failwithf "Le fichier ISO '%s' est introuvable" isoPath
            isoPath

    interface IVolumeDriver with

        member _.CreateVolume(name, driverOpts, labels) =
            let isoPath = validateIso driverOpts
            store.CreateVolume(name, isoPath, labels, driverOpts)

        member _.RemoveVolume(id, force) =
            let isoPath =
                match store.InspectVolume(id) with
                | Some info ->
                    let mutable v = Unchecked.defaultof<JsonElement>
                    if info.TryGetProperty("remotePath", &v) then v.GetString() else null
                | None -> null
            store.RemoveVolume(id) |> ignore
            if force && not (String.IsNullOrEmpty(isoPath)) && File.Exists(isoPath) then
                File.Delete(isoPath)
            true

        member _.InspectVolume(id) =
            store.InspectVolume(id)

        member _.ListVolumes(_filters) =
            store.ListVolumes()

        member _.MountVolume(id, targetPath, _options) =
            SecurityValidation.validateId id "L'identifiant du volume"
            SecurityValidation.validateVolumePath targetPath "Le chemin cible"
            match store.InspectVolume(id) with
            | None -> failwithf "Volume %s introuvable" id
            | Some info ->
                let mutable v = Unchecked.defaultof<JsonElement>
                if not (info.TryGetProperty("remotePath", &v)) then
                    failwithf "Volume %s invalide (chemin ISO manquant)" id
                let isoPath = v.GetString()
                if not (File.Exists(isoPath)) then
                    failwithf "Le fichier ISO '%s' est introuvable" isoPath
                IsoImage.extract isoPath targetPath |> ignore
                (true, targetPath)

        member _.UnmountVolume(id, targetPath) =
            SecurityValidation.validateId id "L'identifiant du volume"
            SecurityValidation.validateVolumePath targetPath "Le chemin cible"
            if Directory.Exists(targetPath) then
                Directory.Delete(targetPath, true)
            (true, "Démonté")

        member _.GetVolumeSize(_id) =
            0L

        member _.PruneVolumes() =
            let removed =
                store.ListVolumes()
                |> List.choose (fun vol ->
                    let mutable v = Unchecked.defaultof<JsonElement>
                    if vol.TryGetProperty("id", &v) then
                        let id = v.GetString()
                        try
                            store.RemoveVolume(id) |> ignore
                            Some id
                        with ex ->
                            Log.Warning(ex, "Erreur lors du nettoyage du volume ISO {VolumeId}", id)
                            None
                    else None)
            removed

    member this.CreateVolume(name, driverOpts, labels) = (this :> IVolumeDriver).CreateVolume(name, driverOpts, labels)
    member this.RemoveVolume(id, force) = (this :> IVolumeDriver).RemoveVolume(id, force)
    member this.InspectVolume(id) = (this :> IVolumeDriver).InspectVolume(id)
    member this.ListVolumes(filters) = (this :> IVolumeDriver).ListVolumes(filters)
    member this.MountVolume(id, targetPath, options) = (this :> IVolumeDriver).MountVolume(id, targetPath, options)
    member this.UnmountVolume(id, targetPath) = (this :> IVolumeDriver).UnmountVolume(id, targetPath)
    member this.GetVolumeSize(id) = (this :> IVolumeDriver).GetVolumeSize(id)
    member this.PruneVolumes() = (this :> IVolumeDriver).PruneVolumes()
