namespace Diplo.Disk

open System
open System.IO

module Qcow1 =

    [<CLIMutable>]
    type Header =
        { mutable Version: int
          mutable ClusterBits: int
          mutable ClusterSize: int64
          mutable VirtualSize: int64
          mutable L1Size: int
          mutable L1TableOffset: int64
          mutable CryptMethod: int }

    let private be32 (d: byte[]) o =
        (int d.[o] <<< 24)
        ||| (int d.[o + 1] <<< 16)
        ||| (int d.[o + 2] <<< 8)
        ||| int d.[o + 3]

    let private be64 (d: byte[]) (o: int) =
        (int64 d.[o] <<< 56)
        ||| (int64 d.[o + 1] <<< 48)
        ||| (int64 d.[o + 2] <<< 40)
        ||| (int64 d.[o + 3] <<< 32)
        ||| (int64 d.[o + 4] <<< 24)
        ||| (int64 d.[o + 5] <<< 16)
        ||| (int64 d.[o + 6] <<< 8)
        ||| int64 d.[o + 7]

    let private putBe64 (d: byte[]) (o: int) (v: int64) =
        d.[o] <- byte (v >>> 56)
        d.[o + 1] <- byte (v >>> 48)
        d.[o + 2] <- byte (v >>> 40)
        d.[o + 3] <- byte (v >>> 32)
        d.[o + 4] <- byte (v >>> 24)
        d.[o + 5] <- byte (v >>> 16)
        d.[o + 6] <- byte (v >>> 8)
        d.[o + 7] <- byte v

    let private putBe32 (d: byte[]) (o: int) (v: int) =
        d.[o] <- byte (v >>> 24)
        d.[o + 1] <- byte (v >>> 16)
        d.[o + 2] <- byte (v >>> 8)
        d.[o + 3] <- byte v

    let private readFully (s: Stream) (buf: byte[]) (off: int) (len: int) =
        let mutable done_ = 0

        while done_ < len do
            let n = s.Read(buf, off + done_, len - done_)

            if n = 0 then
                failwith "Fin prematuree du flux"

            done_ <- done_ + n

    let private hostOffsetMask (clusterBits: int) =
        let shift = clusterBits - 8

        if shift > 0 then
            0x00FFFFFFFFFFFFFE00L >>> shift
        else
            0x00FFFFFFFFFFFFFE00L

    let private clusterOffsetMask (clusterBits: int) = (1L <<< clusterBits) - 1L

    let readHeader (s: Stream) : Header =
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

        if clusterBits < 8 || clusterBits > 30 then
            failwithf "Cluster bits invalide : %d" clusterBits

        let clusterSize = 1L <<< clusterBits
        let virtualSize = be64 buf 28
        let cryptMethod = be32 buf 32

        if cryptMethod <> 0 then
            failwith "Chiffrement non supporte"

        let l1TableOffset = be64 buf 40
        let l1Size = be32 buf 48

        { Version = version
          ClusterBits = clusterBits
          ClusterSize = clusterSize
          VirtualSize = virtualSize
          L1Size = l1Size
          L1TableOffset = l1TableOffset
          CryptMethod = cryptMethod }

    let writeHeader (s: Stream) (h: Header) =
        let buf = Array.zeroCreate<byte> 72
        buf.[0] <- byte 'Q'
        buf.[1] <- byte 'F'
        buf.[2] <- byte 'I'
        buf.[3] <- 0xFEuy
        putBe32 buf 4 h.Version
        putBe32 buf 20 h.ClusterBits
        putBe64 buf 28 h.VirtualSize
        putBe64 buf 40 h.L1TableOffset
        putBe32 buf 48 h.L1Size
        s.Position <- 0L
        s.Write(buf, 0, 72)

    let private readUInt64At (s: Stream) (offset: int64) : int64 =
        let buf = Array.zeroCreate<byte> 8
        s.Position <- offset
        readFully s buf 0 8
        be64 buf 0

    let private writeUInt64At (s: Stream) (offset: int64) (v: int64) =
        let buf = Array.zeroCreate<byte> 8
        putBe64 buf 0 v
        s.Position <- offset
        s.Write(buf, 0, 8)

    let readBytesAt (s: Stream) (h: Header) (vOffset: int64) (count: int) (buf: byte[]) (bufOff: int) =
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
                let l1Index = int (vOff >>> clusterBits)
                let clusterOff = vOff &&& dataMask

                if l1Index >= h.L1Size then
                    Array.Clear(buf, bOff, remaining)
                    remaining <- 0
                else
                    let l1EntryOff = h.L1TableOffset + int64 l1Index * 8L
                    let descriptor = readUInt64At s l1EntryOff
                    let hostCluster = (descriptor &&& offMask) >>> (clusterBits - 8)
                    let toRead = min remaining (int (h.ClusterSize - clusterOff))

                    if hostCluster = 0L then
                        Array.Clear(buf, bOff, toRead)
                    else
                        let fileOffset = hostCluster * h.ClusterSize + clusterOff
                        s.Position <- fileOffset
                        readFully s buf bOff toRead

                    remaining <- remaining - toRead
                    bOff <- bOff + toRead
                    vOff <- vOff + int64 toRead

    let private findFreeCluster (_s: Stream) (h: Header) : int64 =
        let endOfL1 = h.L1TableOffset + int64 h.L1Size * 8L
        let lastCluster = (endOfL1 + h.ClusterSize - 1L) / h.ClusterSize
        lastCluster

    let writeBytesAt (s: Stream) (h: Header) (vOffset: int64) (data: byte[]) (dataOff: int) (count: int) =
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
            let hostCluster = (descriptor &&& offMask) >>> (clusterBits - 8)
            let toWrite = min remaining (int (h.ClusterSize - clusterOff))

            if hostCluster = 0L then
                let freeClus = findFreeCluster s h
                let newDesc = (freeClus <<< (clusterBits - 8)) &&& (~~~dataMask)
                writeUInt64At s l1EntryOff newDesc
                let fileOffset = freeClus * h.ClusterSize
                let zeroBuf = Array.zeroCreate<byte> (int h.ClusterSize)
                s.Position <- fileOffset
                s.Write(zeroBuf, 0, int h.ClusterSize)
                s.Position <- fileOffset + clusterOff
                s.Write(data, dOff, toWrite)
            else
                let fileOffset = hostCluster * h.ClusterSize + clusterOff
                s.Position <- fileOffset
                s.Write(data, dOff, toWrite)

            remaining <- remaining - toWrite
            dOff <- dOff + toWrite
            vOff <- vOff + int64 toWrite

type Qcow1Stream(path: string, access: FileAccess) =
    inherit Stream()
    let fs = new FileStream(path, FileMode.Open, access, FileShare.ReadWrite)
    let header = Qcow1.readHeader fs
    let mutable position = 0L
    override _.CanRead = true
    override _.CanSeek = true
    override _.CanWrite = access = FileAccess.ReadWrite || access = FileAccess.Write
    override _.Length = header.VirtualSize

    override _.Position
        with get () = position
        and set v = position <- v

    override _.Read(buf, offset, count) =
        let count = min count (int (header.VirtualSize - position))

        if count <= 0 then
            0
        else
            Qcow1.readBytesAt fs header position count buf offset
            position <- position + int64 count
            count

    override _.Write(buf, offset, count) =
        Qcow1.writeBytesAt fs header position buf offset count
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

    override _.Flush() = fs.Flush()

    override _.Dispose(disposing) =
        if disposing then
            fs.Dispose()
