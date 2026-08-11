namespace Diplo.Disk

open System
open System.IO

/// Pilote d'image qcow2 « maison » (lecture et écriture en place).
///
/// L'image qcow2 est utilisée directement comme stockage de référence :
/// aucun fichier intermédiaire, aucune conversion. Le pilote résout les
/// tables L1/L2, maintient les refcounts et alloue de nouveaux clusters
/// lors des écritures, à la manière du block driver de QEMU (qcow2.c).
///
/// Formats pris en charge : qcow2 version 2 et 3, clusters non compressés,
/// image non chiffrée, sans fichier de sauvegarde (backing file) et sans
/// fichier de données externe. Les clusters compressés sont refusés à la
/// lecture (message explicite).
module Qcow2 =

    type Header =
        { Version: int
          ClusterBits: int
          ClusterSize: int
          VirtualSize: int64
          L1Size: int
          L1TableOffset: int64
          RefcountTableOffset: int64
          RefcountTableClusters: int
          RefcountBits: int
          RefcountOrder: int }

    /// Nombre d'entrées d'une table L2 (chaque entrée couvre un cluster).
    let private l2Entries (h: Header) = h.ClusterSize / 8

    /// Décalage de bits pour l'index L1 dans une adresse virtuelle.
    let private l1Shift (h: Header) = 2 * h.ClusterBits - 3

    /// Nombre de clusters couverts par un bloc de refcounts.
    let private refcountsPerBlock (h: Header) = h.ClusterSize / 2

    /// Masque des bits d'un descripteur L2 : offset hôte d'un cluster alloué
    /// (bits 9 à 55, aligné sur un cluster). S'applique aussi aux entrées de
    /// la table L1 pour retrouver l'offset d'une table L2.
    let private hostOffsetMask = 0x00FFFFFFFFFFFFFE00L

    /// Bit 0 du « Standard Cluster Descriptor » (version 3) : cluster lu
    /// comme des zéros.
    let private zeroReadFlag = 1L

    /// Bit 62 d'un descripteur L2 : cluster compressé.
    let private compressedFlag = 0x4000000000000000L

    // ── lecture binaire big-endian ─────────────────────────────────────────

    let private readFully (s: Stream) (buffer: byte[]) =
        let mutable total = 0
        while total < buffer.Length do
            let r = s.Read(buffer, total, buffer.Length - total)
            if r <= 0 then failwith "Fichier qcow2 tronqué (lecture incomplète)"
            total <- total + r

    let private be16 (d: byte[]) (o: int) = (int d.[o] <<< 8) ||| int d.[o + 1]

    let private be32 (d: byte[]) (o: int) =
        (int64 d.[o] <<< 24) ||| (int64 d.[o + 1] <<< 16) ||| (int64 d.[o + 2] <<< 8) ||| int64 d.[o + 3]
        |> int32

    let private be64 (d: byte[]) (o: int) =
        let mutable v = 0L
        for i in 0 .. 7 do v <- (v <<< 8) ||| int64 d.[o + i]
        v

    let private putBe16 (v: int) (d: byte[]) (o: int) =
        d.[o] <- byte (v >>> 8)
        d.[o + 1] <- byte v

    let private putBe32 (v: int) (d: byte[]) (o: int) =
        d.[o] <- byte (v >>> 24)
        d.[o + 1] <- byte (v >>> 16)
        d.[o + 2] <- byte (v >>> 8)
        d.[o + 3] <- byte v

    let private putBe64 (v: int64) (d: byte[]) (o: int) =
        d.[o] <- byte (v >>> 56)
        d.[o + 1] <- byte (v >>> 48)
        d.[o + 2] <- byte (v >>> 40)
        d.[o + 3] <- byte (v >>> 32)
        d.[o + 4] <- byte (v >>> 24)
        d.[o + 5] <- byte (v >>> 16)
        d.[o + 6] <- byte (v >>> 8)
        d.[o + 7] <- byte v

    // ── en-tête ────────────────────────────────────────────────────────────

    /// Analyse l'en-tête qcow2 (versions 2 et 3).
    let readHeader (stream: Stream) : Header =
        let buf = Array.zeroCreate<byte> 128
        stream.Position <- 0L
        readFully stream buf
        if not (buf.[0] = byte 'Q' && buf.[1] = byte 'F' && buf.[2] = byte 'I' && buf.[3] = 0xFBuy) then
            failwith "En-tête qcow2 invalide (signature 'QFI' absente)"
        let version = be32 buf 4
        if version <> 2 && version <> 3 then
            failwithf "Version qcow2 %d non prise en charge (versions 2 et 3 uniquement)" version
        if be64 buf 8 <> 0L then
            failwith "Les images qcow2 avec fichier de sauvegarde (backing file) ne sont pas prises en charge"
        let clusterBits = be32 buf 20
        if clusterBits < 9 || clusterBits > 21 then
            failwithf "Taille de cluster qcow2 invalide (cluster_bits = %d)" clusterBits
        let virtualSize = be64 buf 24
        if virtualSize <= 0L then
            failwith "Image qcow2 invalide (taille virtuelle nulle)"
        if be32 buf 32 <> 0 then
            failwith "Les images qcow2 chiffrées ne sont pas prises en charge"
        let l1Size = be32 buf 36
        let l1TableOffset = be64 buf 40
        let refcountTableOffset = be64 buf 48
        let refcountTableClusters = be32 buf 56
        let refcountOrder =
            if version = 3 then be32 buf 96 else 4
        let refcountBits = 1 <<< refcountOrder
        if refcountBits <> 16 then
            failwithf "Largeur de refcount %d non prise en charge (seul 16 bits l'est)" refcountBits
        if version = 3 then
            let incompatible = be32 buf 72
            if incompatible &&& 4 <> 0 then
                failwith "Les images qcow2 avec fichier de données externe ne sont pas prises en charge"
        { Version = version
          ClusterBits = clusterBits
          ClusterSize = 1 <<< clusterBits
          VirtualSize = virtualSize
          L1Size = l1Size
          L1TableOffset = l1TableOffset
          RefcountTableOffset = refcountTableOffset
          RefcountTableClusters = refcountTableClusters
          RefcountBits = refcountBits
          RefcountOrder = refcountOrder }

    let clusterSize (h: Header) = h.ClusterSize
    let virtualSize (h: Header) = h.VirtualSize

    // ── accès bas niveau au fichier ────────────────────────────────────────

    let private readAt (s: Stream) (offset: int64) (buffer: byte[]) (bufferOffset: int) (count: int) =
        s.Position <- offset
        let mutable total = 0
        while total < count do
            let r = s.Read(buffer, bufferOffset + total, count - total)
            if r <= 0 then failwith "Lecture au-delà de la fin du fichier qcow2"
            total <- total + r

    let private readUInt64At (s: Stream) (offset: int64) : int64 =
        let b = Array.zeroCreate<byte> 8
        readAt s offset b 0 8
        be64 b 0

    let private writeUInt64At (s: Stream) (offset: int64) (value: int64) =
        let b = Array.zeroCreate<byte> 8
        putBe64 value b 0
        s.Position <- offset
        s.Write(b, 0, 8)

    // ── refcounts ──────────────────────────────────────────────────────────

    /// Offset hôte du bloc de refcounts couvrant le cluster `n` (0 si absent).
    let private refcountBlockOffset (s: Stream) (h: Header) (n: int64) : int64 =
        let tableIndex = int (n / int64 (refcountsPerBlock h))
        readUInt64At s (h.RefcountTableOffset + int64 tableIndex * 8L)

    let private readRefcount (s: Stream) (h: Header) (n: int64) : int =
        let blockOffset = refcountBlockOffset s h n
        if blockOffset = 0L then 0
        else
            let idx = int (n % int64 (refcountsPerBlock h))
            let b = Array.zeroCreate<byte> 2
            readAt s (blockOffset + int64 idx * 2L) b 0 2
            be16 b 0

    let private writeRefcount (s: Stream) (h: Header) (n: int64) (value: int) =
        let blockOffset = refcountBlockOffset s h n
        if blockOffset = 0L then
            failwithf "Image qcow2 saturée : aucun bloc de refcount pour le cluster %d (limite du pilote MVP)" n
        let idx = int (n % int64 (refcountsPerBlock h))
        let b = Array.zeroCreate<byte> 2
        putBe16 value b 0
        s.Position <- blockOffset + int64 idx * 2L
        s.Write(b, 0, 2)

    /// Recherche le premier cluster hôte libre (refcount nul) en évitant le
    /// cluster 0 (en-tête). Nécessite que les métadonnées de l'image soient
    /// correctement refcountées, ce qui est le cas des images produites par
    /// qemu-img.
    let private findFreeCluster (s: Stream) (h: Header) : int64 =
        let mutable n = 1L
        let mutable found = -1L
        while found < 0L do
            if readRefcount s h n = 0 then found <- n else n <- n + 1L
        found

    // ── lecture ────────────────────────────────────────────────────────────

    /// Lit `count` octets à l'offset virtuel `virtualOffset`.
    /// Les clusters non alloués et les clusters « zéros » sont lus comme des zéros.
    let readBytesAt (s: Stream) (h: Header) (virtualOffset: int64) (count: int) : byte[] =
        if virtualOffset < 0L || virtualOffset + int64 count > h.VirtualSize then
            failwithf "Lecture hors de l'image qcow2 (offset %d, %d octets, taille %d)" virtualOffset count h.VirtualSize
        let out = Array.zeroCreate<byte> count
        let l1Shift = l1Shift h
        let l2Entries = l2Entries h
        let mutable pos = virtualOffset
        let mutable outPos = 0
        let mutable remaining = count
        while remaining > 0 do
            let l1Index = int (pos >>> l1Shift)
            if l1Index >= h.L1Size then
                failwithf "Index de table L1 %d hors limites (l1_size = %d)" l1Index h.L1Size
            let l2Index = int ((pos >>> h.ClusterBits) &&& int64 (l2Entries - 1))
            let inCluster = int (pos &&& int64 (h.ClusterSize - 1))
            let toCopy = min remaining (h.ClusterSize - inCluster)
            let l2Offset = readUInt64At s (h.L1TableOffset + int64 l1Index * 8L) &&& hostOffsetMask
            if l2Offset = 0L then
                Array.Clear(out, outPos, toCopy)
            else
                let desc = readUInt64At s (l2Offset + int64 l2Index * 8L)
                if desc = 0L || desc &&& zeroReadFlag <> 0L then
                    Array.Clear(out, outPos, toCopy)
                elif desc &&& compressedFlag <> 0L then
                    failwith "Cluster compressé détecté : la compression qcow2 n'est pas prise en charge par le pilote"
                else
                    let hostOffset = desc &&& hostOffsetMask
                    readAt s (hostOffset + int64 inCluster) out outPos toCopy
            pos <- pos + int64 toCopy
            outPos <- outPos + toCopy
            remaining <- remaining - toCopy
        out

    // ── écriture ───────────────────────────────────────────────────────────

    /// Écrit `data` à l'offset virtuel `virtualOffset`.
    ///
    /// Les clusters non alloués sont alloués à la volée : recherche d'un
    /// cluster hôte libre via les refcounts, écriture des données, mise à
    /// jour de la table L2 (et de L1 si une table L2 doit être créée) puis
    /// incrément du refcount. Les écritures partielles de cluster suivent un
    /// modèle lecture-modification-écriture.
    let writeBytesAt (s: Stream) (h: Header) (virtualOffset: int64) (data: byte[]) =
        if virtualOffset < 0L || virtualOffset + int64 data.Length > h.VirtualSize then
            failwithf "Écriture hors de l'image qcow2 (offset %d, %d octets, taille %d)" virtualOffset data.Length h.VirtualSize
        let l1Shift = l1Shift h
        let l2Entries = l2Entries h
        let mutable pos = virtualOffset
        let mutable dataPos = 0
        let mutable remaining = data.Length
        while remaining > 0 do
            let l1Index = int (pos >>> l1Shift)
            if l1Index >= h.L1Size then
                failwithf "Index de table L1 %d hors limites (l1_size = %d)" l1Index h.L1Size
            let l2Index = int ((pos >>> h.ClusterBits) &&& int64 (l2Entries - 1))
            let inCluster = int (pos &&& int64 (h.ClusterSize - 1))
            let toWrite = min remaining (h.ClusterSize - inCluster)
            let clusterStart = pos - int64 inCluster

            let mutable l2Offset =
                readUInt64At s (h.L1TableOffset + int64 l1Index * 8L) &&& hostOffsetMask
            if l2Offset = 0L then
                let newL2 = findFreeCluster s h
                let zero = Array.zeroCreate<byte> h.ClusterSize
                s.Position <- newL2 * int64 h.ClusterSize
                s.Write(zero, 0, h.ClusterSize)
                writeRefcount s h newL2 1
                writeUInt64At s (h.L1TableOffset + int64 l1Index * 8L) (newL2 * int64 h.ClusterSize)
                l2Offset <- newL2 * int64 h.ClusterSize

            let desc = readUInt64At s (l2Offset + int64 l2Index * 8L)
            if desc = 0L || desc &&& zeroReadFlag <> 0L || desc &&& compressedFlag <> 0L then
                let newCluster = findFreeCluster s h
                let zero = Array.zeroCreate<byte> h.ClusterSize
                s.Position <- newCluster * int64 h.ClusterSize
                s.Write(zero, 0, h.ClusterSize)
                writeUInt64At s (l2Offset + int64 l2Index * 8L) (newCluster * int64 h.ClusterSize)
                writeRefcount s h newCluster 1
                let host = newCluster * int64 h.ClusterSize
                s.Position <- host + int64 inCluster
                s.Write(data, dataPos, toWrite)
            else
                let host = desc &&& hostOffsetMask
                let whole = readBytesAt s h clusterStart h.ClusterSize
                Array.Copy(data, dataPos, whole, inCluster, toWrite)
                s.Position <- host
                s.Write(whole, 0, h.ClusterSize)

            pos <- pos + int64 toWrite
            dataPos <- dataPos + toWrite
            remaining <- remaining - toWrite
        s.Flush()

/// Flux d'accès aléatoire (lecture/écriture) sur une image qcow2, exploité
/// par DiscUtils pour accéder au système de fichiers contenu dans l'image.
type Qcow2Stream(path: string, access: FileAccess) =
    inherit Stream()

    let fs = new FileStream(path, FileMode.Open, access, FileShare.Read)
    let header = Qcow2.readHeader fs
    let mutable position = 0L

    member _.Header = header

    override _.CanRead = true
    override _.CanSeek = true
    override _.CanWrite = access <> FileAccess.Read
    override _.Length = header.VirtualSize

    override _.Position
        with get () = position
        and set v = position <- v

    override _.Read(buffer, offset, count) =
        let count = int (min (int64 count) (max 0L (header.VirtualSize - position)))
        if count <= 0 then 0
        else
            let data = Qcow2.readBytesAt fs header position count
            Array.Copy(data, 0, buffer, offset, count)
            position <- position + int64 count
            count

    override _.Write(buffer, offset, count) =
        if count > 0 then
            let data = Array.zeroCreate<byte> count
            Array.Copy(buffer, offset, data, 0, count)
            Qcow2.writeBytesAt fs header position data
            position <- position + int64 count

    override _.Flush() = fs.Flush()

    override _.Seek(offset, origin) =
        position <-
            match origin with
            | SeekOrigin.Begin -> offset
            | SeekOrigin.Current -> position + offset
            | _ -> header.VirtualSize + offset
        position

    override _.SetLength _ = raise (NotSupportedException "Le redimensionnement d'image qcow2 n'est pas pris en charge")

    override _.Dispose(disposing) =
        if disposing then
            fs.Flush()
            fs.Dispose()
        base.Dispose(disposing)
