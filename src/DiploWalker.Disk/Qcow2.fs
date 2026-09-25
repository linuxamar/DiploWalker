namespace DiploWalker.Disk

open System
open System.IO

/// Pilote d'image qcow2 Â« maison Â» (lecture et Ã©criture en place).
///
/// L'image qcow2 est utilisÃ©e directement comme stockage de rÃ©fÃ©rence :
/// aucun fichier intermÃ©diaire, aucune conversion. Le pilote rÃ©sout les
/// tables L1/L2, maintient les refcounts et alloue de nouveaux clusters
/// lors des Ã©critures, Ã  la maniÃ¨re du block driver de QEMU (qcow2.c).
///
/// Formats pris en charge : qcow2 version 2 et 3, clusters non compressÃ©s,
/// image non chiffrÃ©e, sans fichier de sauvegarde (backing file) et sans
/// fichier de donnÃ©es externe. Les clusters compressÃ©s sont refusÃ©s Ã  la
/// lecture (message explicite).
module Qcow2 =
    open BinaryIo

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

    /// Nombre d'entrÃ©es d'une table L2 (chaque entrÃ©e couvre un cluster).
    let private l2Entries (h: Header) = h.ClusterSize / 8

    /// DÃ©calage de bits pour l'index L1 dans une adresse virtuelle.
    let private l1Shift (h: Header) = 2 * h.ClusterBits - 3

    /// Nombre de clusters couverts par un bloc de refcounts.
    let private refcountsPerBlock (h: Header) = h.ClusterSize / 2

    /// Masque des bits d'un descripteur L2 : offset hÃ´te d'un cluster allouÃ©
    /// (bits 9 Ã  55, alignÃ© sur un cluster). S'applique aussi aux entrÃ©es de
    /// la table L1 pour retrouver l'offset d'une table L2.
    let private hostOffsetMask = 0x00FFFFFFFFFFFFFE00L

    /// Bit 0 du Â« Standard Cluster Descriptor Â» (version 3) : cluster lu
    /// comme des zÃ©ros.
    let private zeroReadFlag = 1L

    /// Bit 62 d'un descripteur L2 : cluster compressÃ©.
    let private compressedFlag = 0x4000000000000000L

    // â”€â”€ en-tÃªte â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// Analyse l'en-tÃªte qcow2 (versions 2 et 3).
    let private readHeaderCore (stream: Stream) : Header =
        let buf = Array.zeroCreate<byte> 128
        stream.Position <- 0L
        BinaryIo.readFully stream buf 0 buf.Length

        if
            not (
                buf.[0] = byte 'Q'
                && buf.[1] = byte 'F'
                && buf.[2] = byte 'I'
                && buf.[3] = 0xFBuy
            )
        then
            failwith "En-tÃªte qcow2 invalide (signature 'QFI' absente)"

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
            failwith "Les images qcow2 chiffrÃ©es ne sont pas prises en charge"

        let l1Size = be32 buf 36
        let l1TableOffset = be64 buf 40
        let refcountTableOffset = be64 buf 48
        let refcountTableClusters = be32 buf 56
        let refcountOrder = if version = 3 then be32 buf 96 else 4

        // 1 <<< 36 masquerait le dÃ©calage Ã  36âˆ§31=4 et Â« accepterait Â» une
        // largeur fantÃ´me : refuser explicitement toute valeur autre que 4.
        if refcountOrder <> 4 then
            failwithf "refcount_order %d non pris en charge (seul 4, soit 16 bits, l'est)" refcountOrder

        // Bornes de sÃ©curitÃ© sur une image forgÃ©e : l1_size et la table de
        // refcounts ne doivent pas pouvoir allouer des parcours Ã©normes ni des
        // index L1 qui dÃ©borderaient de int (voir readBytesAtCore).
        if refcountTableClusters < 1 || refcountTableClusters > (1 <<< 20) then
            failwithf "Table de refcounts invalide (refcount_table_clusters = %d)" refcountTableClusters

        if l1Size < 1 || int64 l1Size > int64 (1 <<< 24) then
            failwithf "Table L1 invalide (l1_size = %d)" l1Size

        let refcountBits = 1 <<< refcountOrder

        if refcountBits <> 16 then
            failwithf "Largeur de refcount %d non prise en charge (seul 16 bits l'est)" refcountBits

        if version = 3 then
            let incompatible = be32 buf 72

            if incompatible &&& 4 <> 0 then
                failwith "Les images qcow2 avec fichier de donnÃ©es externe ne sont pas prises en charge"

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

    /// Analyse l'en-tÃªte qcow2, version Result.
    let readHeader (stream: Stream) : Result<Header, string> =
        protect (fun () -> readHeaderCore stream)

    let clusterSize (h: Header) = h.ClusterSize
    let virtualSize (h: Header) = h.VirtualSize

    // â”€â”€ accÃ¨s bas niveau au fichier â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    let private readAt (s: Stream) (offset: int64) (buffer: byte[]) (bufferOffset: int) (count: int) =
        s.Position <- offset
        readFully s buffer bufferOffset count

    let private readUInt64At (s: Stream) (offset: int64) : int64 =
        let b = Array.zeroCreate<byte> 8
        readAt s offset b 0 8
        be64 b 0

    let private writeUInt64At (s: Stream) (offset: int64) (value: int64) =
        let b = Array.zeroCreate<byte> 8
        putBe64 value b 0
        s.Position <- offset
        s.Write(b, 0, 8)

    // â”€â”€ refcounts â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// Offset hÃ´te du bloc de refcounts couvrant le cluster `n` (0 si absent).
    let private refcountBlockOffset (s: Stream) (h: Header) (n: int64) : int64 =
        let tableIndex = int (n / int64 (refcountsPerBlock h))
        readUInt64At s (h.RefcountTableOffset + int64 tableIndex * 8L)

    let private readRefcount (s: Stream) (h: Header) (n: int64) : int =
        let blockOffset = refcountBlockOffset s h n

        if blockOffset = 0L then
            0
        else
            let idx = int (n % int64 (refcountsPerBlock h))
            let b = Array.zeroCreate<byte> 2
            readAt s (blockOffset + int64 idx * 2L) b 0 2
            be16 b 0

    let private writeRefcount (s: Stream) (h: Header) (n: int64) (value: int) =
        let blockOffset = refcountBlockOffset s h n

        if blockOffset = 0L then
            failwithf "Image qcow2 saturÃ©e : aucun bloc de refcount pour le cluster %d (limite du pilote MVP)" n

        let idx = int (n % int64 (refcountsPerBlock h))
        let b = Array.zeroCreate<byte> 2
        putBe16 value b 0
        s.Position <- blockOffset + int64 idx * 2L
        s.Write(b, 0, 2)

    /// Recherche le premier cluster hÃ´te libre (refcount nul) en Ã©vitant le
    /// cluster 0 (en-tÃªte). NÃ©cessite que les mÃ©tadonnÃ©es de l'image soient
    /// correctement refcountÃ©es, ce qui est le cas des images produites par
    /// qemu-img.
    let private findFreeCluster (s: Stream) (h: Header) : int64 =
        let maxClusters = int64 h.RefcountTableClusters * int64 (refcountsPerBlock h)
        let mutable n = 1L
        let mutable found = -1L

        while found < 0L && n < maxClusters do
            if readRefcount s h n = 0 then found <- n else n <- n + 1L

        if found < 0L then
            failwith "Image qcow2 pleine : aucun cluster libre disponible"

        found

    // â”€â”€ lecture â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// Lit `count` octets Ã  l'offset virtuel `virtualOffset`.
    /// Les clusters non allouÃ©s et les clusters Â« zÃ©ros Â» sont lus comme des zÃ©ros.
    let private readBytesAtCore (s: Stream) (h: Header) (virtualOffset: int64) (count: int) : byte[] =
        if virtualOffset < 0L || virtualOffset + int64 count > h.VirtualSize then
            failwithf
                "Lecture hors de l'image qcow2 (offset %d, %d octets, taille %d)"
                virtualOffset
                count
                h.VirtualSize

        let out = Array.zeroCreate<byte> count
        let l1Shift = l1Shift h
        let l2Entries = l2Entries h
        let mutable pos = virtualOffset
        let mutable outPos = 0
        let mutable remaining = count

        while remaining > 0 do
            let l1Index64 = pos >>> l1Shift

            if l1Index64 >= int64 h.L1Size then
                failwithf "Index de table L1 %d hors limites (l1_size = %d)" l1Index64 h.L1Size

            let l1Index = int l1Index64

            let l2Index = int ((pos >>> h.ClusterBits) &&& int64 (l2Entries - 1))
            let inCluster = int (pos &&& int64 (h.ClusterSize - 1))
            let toCopy = min remaining (h.ClusterSize - inCluster)

            let l2Offset =
                readUInt64At s (h.L1TableOffset + int64 l1Index * 8L) &&& hostOffsetMask

            if l2Offset = 0L then
                Array.Clear(out, outPos, toCopy)
            else
                let desc = readUInt64At s (l2Offset + int64 l2Index * 8L)

                if desc = 0L || desc &&& zeroReadFlag <> 0L then
                    Array.Clear(out, outPos, toCopy)
                elif desc &&& compressedFlag <> 0L then
                    failwith "Cluster compressÃ© dÃ©tectÃ© : la compression qcow2 n'est pas prise en charge par le pilote"
                else
                    let hostOffset = desc &&& hostOffsetMask
                    readAt s (hostOffset + int64 inCluster) out outPos toCopy

            pos <- pos + int64 toCopy
            outPos <- outPos + toCopy
            remaining <- remaining - toCopy

        out

    /// Lit `count` octets Ã  l'offset virtuel `virtualOffset`, version Result.
    let readBytesAt (s: Stream) (h: Header) (virtualOffset: int64) (count: int) : Result<byte[], string> =
        protect (fun () -> readBytesAtCore s h virtualOffset count)

    // â”€â”€ Ã©criture â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// Ã‰crit `data` Ã  l'offset virtuel `virtualOffset`.
    ///
    /// Les clusters non allouÃ©s sont allouÃ©s Ã  la volÃ©e : recherche d'un
    /// cluster hÃ´te libre via les refcounts, Ã©criture des donnÃ©es, mise Ã 
    /// jour de la table L2 (et de L1 si une table L2 doit Ãªtre crÃ©Ã©e) puis
    /// incrÃ©ment du refcount. Les Ã©critures partielles de cluster suivent un
    /// modÃ¨le lecture-modification-Ã©criture.
    let private writeBytesAtCore (s: Stream) (h: Header) (virtualOffset: int64) (data: byte[]) =
        if virtualOffset < 0L || virtualOffset + int64 data.Length > h.VirtualSize then
            failwithf
                "Ã‰criture hors de l'image qcow2 (offset %d, %d octets, taille %d)"
                virtualOffset
                data.Length
                h.VirtualSize

        let l1Shift = l1Shift h
        let l2Entries = l2Entries h
        let mutable pos = virtualOffset
        let mutable dataPos = 0
        let mutable remaining = data.Length

        while remaining > 0 do
            let l1Index64 = pos >>> l1Shift

            if l1Index64 >= int64 h.L1Size then
                failwithf "Index de table L1 %d hors limites (l1_size = %d)" l1Index64 h.L1Size

            let l1Index = int l1Index64

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

            if desc &&& compressedFlag <> 0L then
                // RÃ©Ã©criture d'un cluster compressÃ© : l'ancien cluster compressÃ©
                // n'est pas libÃ©rable proprement ici (encodage hÃ´te variable) ;
                // allouer sans libÃ©ration fuirait de l'espace Ã  chaque Ã©criture.
                failwith "Ã‰criture dans un cluster compressÃ© non prise en charge"
            elif desc = 0L || desc &&& zeroReadFlag <> 0L then
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
                let whole = readBytesAtCore s h clusterStart h.ClusterSize
                Array.Copy(data, dataPos, whole, inCluster, toWrite)
                s.Position <- host
                s.Write(whole, 0, h.ClusterSize)

            pos <- pos + int64 toWrite
            dataPos <- dataPos + toWrite
            remaining <- remaining - toWrite

        s.Flush()

    /// Ã‰crit `data` Ã  l'offset virtuel `virtualOffset`, version Result.
    let writeBytesAt (s: Stream) (h: Header) (virtualOffset: int64) (data: byte[]) : Result<unit, string> =
        protect (fun () -> writeBytesAtCore s h virtualOffset data)

    // â”€â”€ redimensionnement â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    let private ceilDiv (a: int64) (b: int64) = (a + b - 1L) / b

    /// Nombre d'entrÃ©es L1 nÃ©cessaires pour couvrir `virtualSize`.
    let private l1EntriesNeeded (h: Header) (virtualSize: int64) =
        let coverage = int64 (l2Entries h) * int64 h.ClusterSize
        int (ceilDiv virtualSize coverage)

    /// Nombre de clusters hÃ´tes couverts par la table de refcounts.
    let private refcountedClusters (h: Header) =
        int64 h.RefcountTableClusters * int64 (refcountsPerBlock h)

    /// DÃ©crÃ©mente le refcount du cluster hÃ´te `n` (jamais sous zÃ©ro).
    let private freeCluster (s: Stream) (h: Header) (n: int64) =
        let rc = readRefcount s h n

        if rc <= 0 then
            failwithf "Refcount incohÃ©rent pour le cluster %d (valeur %d)" n rc

        writeRefcount s h n (rc - 1)

    /// LibÃ¨re un cluster de donnÃ©es rÃ©fÃ©rencÃ© par un descripteur L2 et remet
    /// le descripteur Ã  zÃ©ro. Les clusters compressÃ©s sont laissÃ©s intacts.
    let private freeDataEntry (s: Stream) (h: Header) (entryPos: int64) =
        let desc = readUInt64At s entryPos

        if desc <> 0L && desc &&& compressedFlag = 0L then
            let host = desc &&& hostOffsetMask

            if host <> 0L then
                freeCluster s h (host / int64 h.ClusterSize)

            writeUInt64At s entryPos 0L

    /// Recherche `count` clusters hÃ´tes libres contigus (Ã©vite le cluster 0).
    /// Retourne l'offset hÃ´te du premier cluster, ou -1.
    let private findFreeContiguous (s: Stream) (h: Header) (count: int) : int64 =
        let maxClusters = refcountedClusters h
        let mutable n = 1L
        let mutable found = -1L

        while found < 0L && n + int64 count - 1L < maxClusters do
            let mutable ok = true
            let mutable i = 0

            while ok && i < count do
                if readRefcount s h (n + int64 i) <> 0 then
                    ok <- false

                i <- i + 1

            if ok then
                found <- n

            n <- n + 1L

        if found >= 0L then found * int64 h.ClusterSize else -1L

    /// LibÃ¨re les clusters de donnÃ©es et les tables L2 au-delÃ  de
    /// `newVirtualSize` (utilisÃ© lors d'une rÃ©duction).
    let private freeBeyond (s: Stream) (h: Header) (newVirtualSize: int64) =
        let l2Entries = l2Entries h
        let coverage = int64 l2Entries * int64 h.ClusterSize
        let firstFullL1 = int (newVirtualSize / coverage)
        // tables L2 entiÃ¨rement au-delÃ  de la nouvelle taille
        for l1Index in firstFullL1 .. h.L1Size - 1 do
            let l1Pos = h.L1TableOffset + int64 l1Index * 8L
            let l2Offset = readUInt64At s l1Pos &&& hostOffsetMask

            if l2Offset <> 0L then
                for l2Index in 0 .. l2Entries - 1 do
                    freeDataEntry s h (l2Offset + int64 l2Index * 8L)

                freeCluster s h (l2Offset / int64 h.ClusterSize)
                writeUInt64At s l1Pos 0L
        // table L2 partiellement couverte par la nouvelle taille
        let inCoverage = newVirtualSize % coverage

        if inCoverage <> 0L && firstFullL1 < h.L1Size then
            let l1Pos = h.L1TableOffset + int64 firstFullL1 * 8L
            let l2Offset = readUInt64At s l1Pos &&& hostOffsetMask

            if l2Offset <> 0L then
                let startL2 = int (inCoverage / int64 h.ClusterSize)

                for l2Index in startL2 .. l2Entries - 1 do
                    freeDataEntry s h (l2Offset + int64 l2Index * 8L)

    /// Cluster hÃ´te le plus Ã©levÃ© dont le refcount est non nul (0 si aucun).
    let private highestUsedCluster (s: Stream) (h: Header) : int64 =
        let mutable n = refcountedClusters h - 1L
        let mutable found = 0L

        while found = 0L && n >= 0L do
            if readRefcount s h n <> 0 then
                found <- n

            n <- n - 1L

        found

    /// Redimensionne l'image qcow2 Ã  `newVirtualSize` octets (agrandissement
    /// ou rÃ©duction). Une rÃ©duction libÃ¨re les clusters de donnÃ©es et les
    /// tables L2 au-delÃ  de la nouvelle taille (refcounts dÃ©crÃ©mentÃ©s) puis
    /// tronque le fichier Ã  la derniÃ¨re position utile. La table L1 est
    /// relocalisÃ©e (bloc contigu) lorsque sa taille change de nombre de
    /// clusters. Retourne l'en-tÃªte relu aprÃ¨s modification.
    let private resizeCore (s: Stream) (newVirtualSize: int64) : Header =
        if newVirtualSize <= 0L then
            invalidArg (nameof newVirtualSize) "La nouvelle taille virtuelle doit Ãªtre positive"

        let h = readHeaderCore s

        if newVirtualSize = h.VirtualSize then
            h
        else
            let newL1Size = l1EntriesNeeded h newVirtualSize
            let oldClusters = int (ceilDiv (int64 h.L1Size * 8L) (int64 h.ClusterSize))
            let newClusters = int (ceilDiv (int64 newL1Size * 8L) (int64 h.ClusterSize))

            if newVirtualSize < h.VirtualSize then
                freeBeyond s h newVirtualSize

            let mutable newL1Offset = h.L1TableOffset

            if newClusters <> oldClusters then
                let offset = findFreeContiguous s h newClusters

                if offset < 0L then
                    failwith "Aucun espace libre contigu pour la table L1 (limite du pilote MVP)"

                let copyCount = min h.L1Size newL1Size
                let entryBytes = int64 copyCount * 8L

                if entryBytes > 0L then
                    if entryBytes > int64 Int32.MaxValue then
                        failwith "Table L1 trop grande pour l'allocation mÃ©moire (limite du pilote MVP)"

                    let entryBytes = int entryBytes
                    let buffer = Array.zeroCreate<byte> entryBytes
                    readAt s h.L1TableOffset buffer 0 entryBytes
                    s.Position <- offset
                    s.Write(buffer, 0, entryBytes)

                let totalBytes = int64 newClusters * int64 h.ClusterSize

                if totalBytes > entryBytes then
                    let remaining = totalBytes - entryBytes

                    if remaining > int64 Int32.MaxValue then
                        failwith "Table L1 trop grande pour l'allocation mÃ©moire (limite du pilote MVP)"

                    let zeros = Array.zeroCreate<byte> (int remaining)
                    s.Write(zeros, 0, zeros.Length)

                s.Flush()

                for i in 0 .. newClusters - 1 do
                    writeRefcount s h (offset / int64 h.ClusterSize + int64 i) 1

                for i in 0 .. oldClusters - 1 do
                    freeCluster s h (h.L1TableOffset / int64 h.ClusterSize + int64 i)

                newL1Offset <- offset

            // croissance sans relocalisation : zÃ©ro sur les nouvelles entrÃ©es L1
            if newVirtualSize > h.VirtualSize && newClusters = oldClusters then
                for i in h.L1Size .. newL1Size - 1 do
                    writeUInt64At s (newL1Offset + int64 i * 8L) 0L

            // mise Ã  jour de l'en-tÃªte (taille virtuelle, table L1)
            let header = Array.zeroCreate<byte> 104
            readAt s 0L header 0 104
            putBe64 newVirtualSize header 24
            putBe32 newL1Size header 36
            putBe64 newL1Offset header 40
            s.Position <- 0L
            s.Write(header, 0, header.Length)
            s.Flush()

            // troncature du fichier Ã  la derniÃ¨re position utile
            let endOffset = (highestUsedCluster s h + 1L) * int64 h.ClusterSize

            if s.Length > endOffset then
                s.SetLength(endOffset)
                s.Flush()

            readHeaderCore s

    /// Redimensionne l'image qcow2, version Result.
    let resize (s: Stream) (newVirtualSize: int64) : Result<Header, string> =
        protect (fun () -> resizeCore s newVirtualSize)

/// Flux d'accÃ¨s alÃ©atoire (lecture/Ã©criture) sur une image qcow2, exploitÃ©
/// par DiscUtils pour accÃ©der au systÃ¨me de fichiers contenu dans l'image.
type Qcow2Stream(path: string, access: FileAccess) =
    inherit RawImageStream(path, access)

    let fs = base.UnderlyingStream
    let mutable header = Qcow2.readHeader fs |> Result.defaultWith failwith
    let mutable position = 0L

    member _.Header = header

    override _.Length = header.VirtualSize

    override _.Position
        with get () = position
        and set v = position <- v

    override _.Read(buffer, offset, count) =
        let count = int (min (int64 count) (max 0L (header.VirtualSize - position)))

        if count <= 0 then
            0
        else
            let data = Qcow2.readBytesAt fs header position count |> Result.defaultWith failwith
            Array.Copy(data, 0, buffer, offset, count)
            position <- position + int64 count
            count

    override _.Write(buffer, offset, count) =
        if count > 0 then
            let data = Array.zeroCreate<byte> count
            Array.Copy(buffer, offset, data, 0, count)
            Qcow2.writeBytesAt fs header position data |> Result.defaultWith failwith
            position <- position + int64 count

    override _.Seek(offset, origin) =
        position <-
            match origin with
            | SeekOrigin.Begin -> offset
            | SeekOrigin.Current -> position + offset
            | _ -> header.VirtualSize + offset

        position

    override _.SetLength(value: int64) =
        if value <> header.VirtualSize then
            header <- Qcow2.resize fs value |> Result.defaultWith failwith

