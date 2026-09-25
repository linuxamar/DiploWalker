namespace DiploWalker.Disk

open System
open System.IO

module Qcow1 =
    open BinaryIo

    [<CLIMutable>]
    type Header =
        { mutable Version: int
          mutable ClusterBits: int
          mutable ClusterSize: int64
          mutable VirtualSize: int64
          mutable L1Size: int
          mutable L1TableOffset: int64
          mutable CryptMethod: int }

    let private hostOffsetMask (clusterBits: int) =
        ignore clusterBits
        // Bits 9-55 de l'entrÃ©e L1 : offset HÃ”TE EN OCTETS du cluster (spec QCOW).
        0x00FFFFFFFFFFFE00L

    let private clusterOffsetMask (clusterBits: int) = (1L <<< clusterBits) - 1L

    let private readHeaderCore (s: Stream) : Header =
        let buf = Array.zeroCreate<byte> 72
        s.Position <- 0L
        readFully s buf 0 72

        if
            buf.[0] <> byte 'Q'
            || buf.[1] <> byte 'F'
            || buf.[2] <> byte 'I'
            || buf.[3] <> 0xFEuy
        then
            failwith "Magic QCOW1 invalide"

        let version = be32 buf 4

        if version <> 1 then
            failwithf "Version QCOW1 non supportee : %d" version

        let backingSize = be32 buf 16

        if backingSize > 0 then
            failwith "Backing file non supporte"

        let clusterBits = be32 buf 20

        // BornÃ© Ã  21 bits (2 Mo) : une image forgÃ©e avec cluster_bits Ã©levÃ©
        // dÃ©clencherait des allocations d'un Go par cluster Ã©crit.
        if clusterBits < 9 || clusterBits > 21 then
            failwithf "Cluster bits invalide : %d" clusterBits

        let clusterSize = 1L <<< clusterBits
        let virtualSize = be64 buf 28
        let cryptMethod = be32 buf 32

        if cryptMethod <> 0 then
            failwith "Chiffrement non supporte"

        let l1TableOffset = be64 buf 40
        let l1Size = be32 buf 48

        // Borne de sÃ©curitÃ© sur une image forgÃ©e : une table L1 dÃ©bordante
        // provoquerait des parcours Ã©normes (voir readBytesAtCore).
        if l1Size < 1 || int64 l1Size > int64 (1 <<< 24) then
            failwithf "Table L1 invalide (l1_size = %d)" l1Size

        { Version = version
          ClusterBits = clusterBits
          ClusterSize = clusterSize
          VirtualSize = virtualSize
          L1Size = l1Size
          L1TableOffset = l1TableOffset
          CryptMethod = cryptMethod }

    let readHeader (s: Stream) : Result<Header, string> =
        protect (fun () -> readHeaderCore s)

    let writeHeader (s: Stream) (h: Header) =
        let buf = Array.zeroCreate<byte> 72
        buf.[0] <- byte 'Q'
        buf.[1] <- byte 'F'
        buf.[2] <- byte 'I'
        buf.[3] <- 0xFEuy
        putBe32 h.Version buf 4
        putBe32 h.ClusterBits buf 20
        putBe64 h.VirtualSize buf 28
        putBe64 h.L1TableOffset buf 40
        putBe32 h.L1Size buf 48
        s.Position <- 0L
        s.Write(buf, 0, 72)

    let private readUInt64At (s: Stream) (offset: int64) : int64 =
        let buf = Array.zeroCreate<byte> 8
        s.Position <- offset
        readFully s buf 0 8
        be64 buf 0

    let private writeUInt64At (s: Stream) (offset: int64) (v: int64) =
        let buf = Array.zeroCreate<byte> 8
        putBe64 v buf 0
        s.Position <- offset
        s.Write(buf, 0, 8)

    let private readBytesAtCore (s: Stream) (h: Header) (vOffset: int64) (count: int) (buf: byte[]) (bufOff: int) =
        if vOffset < 0L then
            raise (ArgumentOutOfRangeException(nameof vOffset))

        let clusterBits = h.ClusterBits
        let offMask = hostOffsetMask clusterBits
        let dataMask = clusterOffsetMask clusterBits
        let mutable remaining = count
        let mutable vOff = vOffset
        let mutable bOff = bufOff

        while remaining > 0 do
            if vOff >= h.VirtualSize then
                Array.Clear(buf, bOff, remaining)
                remaining <- 0
            else
                let l1Index64 = vOff >>> clusterBits
                let clusterOff = vOff &&& dataMask

                if l1Index64 >= int64 h.L1Size then
                    Array.Clear(buf, bOff, remaining)
                    remaining <- 0
                else
                    let l1Index = int l1Index64
                    let l1EntryOff = h.L1TableOffset + int64 l1Index * 8L
                    let descriptor = readUInt64At s l1EntryOff

                    // L'entrÃ©e L1 contient un OFFSET EN OCTETS : le dÃ©caler puis
                    // re-multiplier par la taille de cluster faussait tout d'un
                    // facteur 2^clusterBits+8 et rendait les images illisibles.
                    let hostOffset = descriptor &&& offMask
                    let toRead = min remaining (int (h.ClusterSize - clusterOff))

                    if hostOffset = 0L then
                        Array.Clear(buf, bOff, toRead)
                    else
                        s.Position <- hostOffset + clusterOff
                        readFully s buf bOff toRead

                    remaining <- remaining - toRead
                    bOff <- bOff + toRead
                    vOff <- vOff + int64 toRead

    let readBytesAt (s: Stream) (h: Header) (vOffset: int64) (count: int) (buf: byte[]) (bufOff: int) : Result<unit, string> =
        protect (fun () -> readBytesAtCore s h vOffset count buf bufOff)

    /// Alloue un nouveau cluster hÃ´te : balayer la table L1 pour trouver la plus
    /// haute allocation RÃ‰ELLE. Un Ã©tat partagÃ© serait nÃ©cessaire sinon ; sans
    /// lui, retourner systÃ©matiquement Â« le premier cluster libre Â» fait que
    /// deux Ã©critures Ã©crasent mutuellement leurs donnÃ©es.
    let private allocateCluster (s: Stream) (h: Header) : int64 =
        let endOfL1 = h.L1TableOffset + int64 h.L1Size * 8L

        let floorOffset =
            ((endOfL1 + h.ClusterSize - 1L) / h.ClusterSize) * h.ClusterSize

        let mutable maxOffset = 0L

        for i in 0 .. h.L1Size - 1 do
            let d = readUInt64At s (h.L1TableOffset + int64 i * 8L)
            maxOffset <- max maxOffset (d &&& hostOffsetMask h.ClusterBits)

        max (maxOffset + h.ClusterSize) floorOffset

    let private writeBytesAtCore (s: Stream) (h: Header) (vOffset: int64) (data: byte[]) (dataOff: int) (count: int) =
        if vOffset < 0L then
            raise (ArgumentOutOfRangeException(nameof vOffset))

        let clusterBits = h.ClusterBits
        let offMask = hostOffsetMask clusterBits
        let dataMask = clusterOffsetMask clusterBits
        let mutable remaining = count
        let mutable vOff = vOffset
        let mutable dOff = dataOff

        while remaining > 0 do
            let l1Index = int (vOff >>> clusterBits)
            let clusterOff = vOff &&& dataMask

            if l1Index >= h.L1Size then
                failwithf "L1 index hors limites : %d >= %d" l1Index h.L1Size

            let l1EntryOff = h.L1TableOffset + int64 l1Index * 8L
            let descriptor = readUInt64At s l1EntryOff
            let hostOffset = descriptor &&& offMask
            let toWrite = min remaining (int (h.ClusterSize - clusterOff))

            if hostOffset = 0L then
                // Nouvelle entrÃ©e : offset hÃ´te en octets, alignÃ© sur le cluster
                // (les 9 bits bas restent nuls â€” conforme au masque de la spec).
                let newHostOffset = allocateCluster s h
                writeUInt64At s l1EntryOff newHostOffset

                let zeroBuf = Array.zeroCreate<byte> (int h.ClusterSize)
                s.Position <- newHostOffset
                s.Write(zeroBuf, 0, int h.ClusterSize)
                s.Position <- newHostOffset + clusterOff
                s.Write(data, dOff, toWrite)
            else
                s.Position <- hostOffset + clusterOff
                s.Write(data, dOff, toWrite)

            remaining <- remaining - toWrite
            dOff <- dOff + toWrite
            vOff <- vOff + int64 toWrite

    let writeBytesAt (s: Stream) (h: Header) (vOffset: int64) (data: byte[]) (dataOff: int) (count: int) : Result<unit, string> =
        protect (fun () -> writeBytesAtCore s h vOffset data dataOff count)

type Qcow1Stream(path: string, access: FileAccess) =
    inherit RawImageStream(path, access)

    let fs = base.UnderlyingStream
    let header = Qcow1.readHeader fs |> Result.defaultWith failwith
    let mutable position = 0L

    override _.Length = header.VirtualSize

    override _.Position
        with get () = position

        and set v =
            if v < 0L then
                raise (ArgumentOutOfRangeException(nameof position))

            position <- v

    override _.Read(buf, offset, count) =
        if position < 0L then
            raise (ArgumentOutOfRangeException(nameof position))

        let count = min count (int (header.VirtualSize - position))

        if count <= 0 then
            0
        else
            Qcow1.readBytesAt fs header position count buf offset |> Result.defaultWith failwith
            position <- position + int64 count
            count

    override _.Write(buf, offset, count) =
        if position < 0L then
            raise (ArgumentOutOfRangeException(nameof position))

        Qcow1.writeBytesAt fs header position buf offset count |> Result.defaultWith failwith
        position <- position + int64 count

    override _.Seek(offset, origin) =
        position <-
            match origin with
            | SeekOrigin.Begin -> offset
            | SeekOrigin.Current -> position + offset
            | SeekOrigin.End -> header.VirtualSize + offset
            | _ -> position

        position

    override _.SetLength(value) =
        header.VirtualSize <- value
        Qcow1.writeHeader fs header

