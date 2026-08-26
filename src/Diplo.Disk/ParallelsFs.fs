namespace Diplo.Disk

open System
open System.IO

/// Pilote maison pour les images disque Parallels (.hdd, .hds).
/// Format simple : en-tete + table L1 + blocs de donnees.
/// Lecture/ecriture in-place, blocs non alloues lus comme zeros.
module Parallels =

    [<CLIMutable>]
    type Header =
        { mutable Magic: int
          mutable Version: int
          mutable Heads: int
          mutable Cylinders: int
          mutable Sectors: int
          mutable SectorSize: int
          mutable BlockSize: int
          mutable BlocksCount: int
          mutable L1Size: int
          mutable L1TableOffset: int64 }

    let private MAGIC = 0x30617261 // "ara\0" in little-endian (reversed: "para")

    let private readUInt32LE (d: byte[]) o =
        int d.[o]
        ||| (int d.[o + 1] <<< 8)
        ||| (int d.[o + 2] <<< 16)
        ||| (int d.[o + 3] <<< 24)

    let private readUInt64LE (d: byte[]) (o: int) =
        int64 d.[o]
        ||| (int64 d.[o + 1] <<< 8)
        ||| (int64 d.[o + 2] <<< 16)
        ||| (int64 d.[o + 3] <<< 24)
        ||| (int64 d.[o + 4] <<< 32)
        ||| (int64 d.[o + 5] <<< 40)
        ||| (int64 d.[o + 6] <<< 48)
        ||| (int64 d.[o + 7] <<< 56)

    let private putUInt32LE (d: byte[]) (o: int) (v: int) =
        d.[o] <- byte v
        d.[o + 1] <- byte (v >>> 8)
        d.[o + 2] <- byte (v >>> 16)
        d.[o + 3] <- byte (v >>> 24)

    let private putUInt64LE (d: byte[]) (o: int) (v: int64) =
        d.[o] <- byte v
        d.[o + 1] <- byte (v >>> 8)
        d.[o + 2] <- byte (v >>> 16)
        d.[o + 3] <- byte (v >>> 24)
        d.[o + 4] <- byte (v >>> 32)
        d.[o + 5] <- byte (v >>> 40)
        d.[o + 6] <- byte (v >>> 48)
        d.[o + 7] <- byte (v >>> 56)

    let private readFully (s: Stream) (buf: byte[]) (off: int) (len: int) =
        let mutable done_ = 0

        while done_ < len do
            let n = s.Read(buf, off + done_, len - done_)

            if n = 0 then
                failwith "Fin prematuree du flux Parallels"

            done_ <- done_ + n

    let virtualSize (h: Header) =
        int64 h.Cylinders * int64 h.Heads * int64 h.Sectors * int64 h.SectorSize

    let private blockIndex (h: Header) (vOffset: int64) = int (vOffset / int64 h.BlockSize)

    let private blockOffsetInSector (h: Header) (vOffset: int64) = vOffset % int64 h.BlockSize

    let private readHeaderCore (s: Stream) : Header =
        let buf = Array.zeroCreate<byte> 1024
        s.Position <- 0L
        readFully s buf 0 1024
        let magic = readUInt32LE buf 0

        if magic <> MAGIC then
            failwith "Magic Parallels invalide"

        let version = readUInt32LE buf 4
        let flags = readUInt32LE buf 8
        let _ = flags
        let heads = readUInt32LE buf 16
        let cylinders = readUInt32LE buf 20
        let sectors = readUInt32LE buf 24
        let sectorSize = readUInt32LE buf 28
        let blockMagic = readUInt32LE buf 32
        let _ = blockMagic
        let blockSize = readUInt32LE buf 36
        let blocksCount = readUInt32LE buf 40
        let l1Size = readUInt32LE buf 44
        let l1TableOffset = readUInt64LE buf 56

        if sectorSize = 0 then
            failwith "Sector size invalide"

        // Géométrie validée : heads/sectors à zéro provoqueraient une division
        // par zéro au SetLength ; blockSize non borné permettrait des
        // allocations géantes (ou négatives) sur image forgée.
        if heads < 1 || sectors < 1 || cylinders < 0 then
            failwith "Géométrie invalide (heads/sectors/cylinders)"

        if blockSize < 512 || blockSize > 8 * 1024 * 1024 || blockSize % sectorSize <> 0 then
            failwithf "Block size invalide : %d" blockSize

        { Magic = magic
          Version = version
          Heads = heads
          Cylinders = cylinders
          Sectors = sectors
          SectorSize = sectorSize
          BlockSize = blockSize
          BlocksCount = blocksCount
          L1Size = l1Size
          L1TableOffset = l1TableOffset }

    let readHeader (s: Stream) : Result<Header, string> =
        try Ok(readHeaderCore s) with ex -> Error ex.Message

    let writeHeader (s: Stream) (h: Header) =
        let buf = Array.zeroCreate<byte> 1024
        putUInt32LE buf 0 h.Magic
        putUInt32LE buf 4 h.Version
        putUInt32LE buf 16 h.Heads
        putUInt32LE buf 20 h.Cylinders
        putUInt32LE buf 24 h.Sectors
        putUInt32LE buf 28 h.SectorSize
        putUInt32LE buf 36 h.BlockSize
        putUInt32LE buf 40 h.BlocksCount
        putUInt32LE buf 44 h.L1Size
        putUInt64LE buf 56 h.L1TableOffset
        s.Position <- 0L
        s.Write(buf, 0, 1024)

    let private readBytesAtCore (s: Stream) (h: Header) (vOffset: int64) (count: int) (buf: byte[]) (bufOff: int) =
        let vSize = virtualSize h
        let mutable remaining = count
        let mutable vOff = vOffset
        let mutable bOff = bufOff

        while remaining > 0 do
            if vOff >= vSize then
                Array.Clear(buf, bOff, remaining)
                remaining <- 0
            else
                let bIdx = blockIndex h vOff
                let bOffInBlock = blockOffsetInSector h vOff

                if bIdx >= h.L1Size then
                    Array.Clear(buf, bOff, remaining)
                    remaining <- 0
                else
                    let l1Off = h.L1TableOffset + int64 bIdx * 4L
                    s.Position <- l1Off
                    let blockOffsetBytes = Array.zeroCreate<byte> 4
                    readFully s blockOffsetBytes 0 4
                    let blockOffset = int64 (readUInt32LE blockOffsetBytes 0) * 512L
                    let toRead = min remaining (h.BlockSize - int bOffInBlock)

                    if blockOffset = 0L then
                        Array.Clear(buf, bOff, toRead)
                    else
                        s.Position <- blockOffset + bOffInBlock
                        readFully s buf bOff toRead

                    remaining <- remaining - toRead
                    bOff <- bOff + toRead
                    vOff <- vOff + int64 toRead

    let readBytesAt (s: Stream) (h: Header) (vOffset: int64) (count: int) (buf: byte[]) (bufOff: int) : Result<unit, string> =
        try
            readBytesAtCore s h vOffset count buf bufOff
            Ok()
        with ex -> Error ex.Message

    let private findFreeBlock (s: Stream) (h: Header) : int =
        let mutable maxEnd = h.L1TableOffset + int64 h.L1Size * 4L

        for i in 0 .. h.BlocksCount - 1 do
            let l1Off = h.L1TableOffset + int64 i * 4L
            s.Position <- l1Off
            let buf = Array.zeroCreate<byte> 4
            readFully s buf 0 4
            let blockOff = int64 (readUInt32LE buf 0) * 512L

            if blockOff > 0L then
                let blockEnd = blockOff + int64 h.BlockSize

                if blockEnd > maxEnd then
                    maxEnd <- blockEnd

        int ((maxEnd + 511L) / 512L)

    let private writeBytesAtCore (s: Stream) (h: Header) (vOffset: int64) (data: byte[]) (dataOff: int) (count: int) =
        let vSize = virtualSize h
        let mutable remaining = count
        let mutable vOff = vOffset
        let mutable dOff = dataOff

        while remaining > 0 do
            if vOff >= vSize then
                failwith "Ecriture hors limites Parallels"

            let bIdx = blockIndex h vOff
            let bOffInBlock = blockOffsetInSector h vOff

            if bIdx >= h.L1Size then
                failwith "L1 index hors limites Parallels"

            let l1Off = h.L1TableOffset + int64 bIdx * 4L
            s.Position <- l1Off
            let blockOffsetBuf = Array.zeroCreate<byte> 4
            readFully s blockOffsetBuf 0 4
            let blockOffset = int64 (readUInt32LE blockOffsetBuf 0) * 512L
            let toWrite = min remaining (h.BlockSize - int bOffInBlock)

            if blockOffset = 0L then
                let freeBlock = findFreeBlock s h
                let newOffset = freeBlock
                s.Position <- l1Off
                let newBuf = Array.zeroCreate<byte> 4
                putUInt32LE newBuf 0 newOffset
                s.Write(newBuf, 0, 4)
                let fileOffset = int64 newOffset * 512L
                let zeroBuf = Array.zeroCreate<byte> h.BlockSize
                s.Position <- fileOffset
                s.Write(zeroBuf, 0, h.BlockSize)
                s.Position <- fileOffset + bOffInBlock
                s.Write(data, dOff, toWrite)
            else
                s.Position <- blockOffset + bOffInBlock
                s.Write(data, dOff, toWrite)

            remaining <- remaining - toWrite
            dOff <- dOff + toWrite
            vOff <- vOff + int64 toWrite

    let writeBytesAt (s: Stream) (h: Header) (vOffset: int64) (data: byte[]) (dataOff: int) (count: int) : Result<unit, string> =
        try
            writeBytesAtCore s h vOffset data dataOff count
            Ok()
        with ex -> Error ex.Message

/// Flux System.IO presentant une image Parallels comme un disque brut virtuel.
type ParallelsStream(path: string, access: FileAccess) =
    inherit Stream()

    // FileShare.Read : un second accès en écriture doit échouer franchement.
    let fs = new FileStream(path, FileMode.Open, access, FileShare.Read)

    let header = Parallels.readHeader fs |> Result.defaultWith failwith
    let mutable position = 0L

    override _.CanRead = true
    override _.CanSeek = true
    override _.CanWrite = access = FileAccess.ReadWrite || access = FileAccess.Write
    override _.Length = Parallels.virtualSize header

    override _.Position
        with get () = position

        and set v =
            if v < 0L then
                raise (ArgumentOutOfRangeException(nameof position))

            position <- v

    override _.Read(buf, offset, count) =
        if position < 0L then
            raise (ArgumentOutOfRangeException(nameof position))

        let vSize = Parallels.virtualSize header
        let read = min count (int (vSize - position))

        if read <= 0 then
            0
        else
            Parallels.readBytesAt fs header position read buf offset |> Result.defaultWith failwith
            position <- position + int64 read
            read

    override _.Write(buf, offset, count) =
        if position < 0L then
            raise (ArgumentOutOfRangeException(nameof position))

        Parallels.writeBytesAt fs header position buf offset count |> Result.defaultWith failwith
        position <- position + int64 count

    override _.Seek(offset, origin) =
        position <-
            match origin with
            | SeekOrigin.Begin -> offset
            | SeekOrigin.Current -> position + offset
            | SeekOrigin.End -> Parallels.virtualSize header + offset
            | _ -> position

        if position < 0L then
            raise (ArgumentOutOfRangeException(nameof position))

        position

    override _.SetLength(value) =
        // Arrondi par excès : la division entière seule effondrerait la taille
        // virtuelle (voire à 0) pour un SetLength non aligné.
        let sectorPerCyl = int64 header.Heads * int64 header.Sectors * int64 header.SectorSize

        let cylinders =
            if value <= 0L then
                1L
            else
                (value + sectorPerCyl - 1L) / sectorPerCyl |> max 1L

        header.Cylinders <- int cylinders
        Parallels.writeHeader fs header

    override _.Flush() = fs.Flush()

    override _.Dispose(disposing) =
        if disposing then
            fs.Dispose()
