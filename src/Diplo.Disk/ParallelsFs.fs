namespace Diplo.Disk

open System
open System.IO

/// Pilote maison pour les images disque Parallels (.hdd, .hds).
/// Format simple : en-tete + table L1 + blocs de donnees.
/// Lecture/ecriture in-place, blocs non alloues lus comme zeros.
module Parallels =
    open BinaryIo

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

    let virtualSize (h: Header) =
        int64 h.Cylinders * int64 h.Heads * int64 h.Sectors * int64 h.SectorSize

    let private blockIndex64 (h: Header) (vOffset: int64) = vOffset / int64 h.BlockSize

    let private blockOffsetInSector (h: Header) (vOffset: int64) = vOffset % int64 h.BlockSize

    let private readHeaderCore (s: Stream) : Header =
        let buf = Array.zeroCreate<byte> 1024
        s.Position <- 0L
        readFully s buf 0 1024
        let magic = le32 buf 0

        if magic <> MAGIC then
            failwith "Magic Parallels invalide"

        let version = le32 buf 4
        let flags = le32 buf 8
        let _ = flags
        let heads = le32 buf 16
        let cylinders = le32 buf 20
        let sectors = le32 buf 24
        let sectorSize = le32 buf 28
        let blockMagic = le32 buf 32
        let _ = blockMagic
        let blockSize = le32 buf 36
        let blocksCount = le32 buf 40
        let l1Size = le32 buf 44
        let l1TableOffset = le64 buf 56

        if sectorSize = 0 then
            failwith "Sector size invalide"

        // Géométrie validée : heads/sectors à zéro provoqueraient une division
        // par zéro au SetLength ; blockSize non borné permettrait des
        // allocations géantes (ou négatives) sur image forgée.
        if heads < 1 || sectors < 1 || cylinders < 0 then
            failwith "Géométrie invalide (heads/sectors/cylinders)"

        if blockSize < 512 || blockSize > 8 * 1024 * 1024 || blockSize % sectorSize <> 0 then
            failwithf "Block size invalide : %d" blockSize

        // Bornes de sécurité sur une image forgée : un nombre de blocs ouvert
        // provoquerait des parcours énormes dans findFreeBlock, et une table L1
        // débordante fausserait les index (voir blockIndex64).
        if l1Size < 1 || l1Size > (1 <<< 24) then
            failwithf "Table L1 Parallels invalide (l1_size = %d)" l1Size

        if blocksCount < 0 || blocksCount > l1Size then
            failwithf "Nombre de blocs Parallels invalide (blocks = %d, l1_size = %d)" blocksCount l1Size

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
        protect (fun () -> readHeaderCore s)

    let writeHeader (s: Stream) (h: Header) =
        let buf = Array.zeroCreate<byte> 1024
        putLe32 h.Magic buf 0
        putLe32 h.Version buf 4
        putLe32 h.Heads buf 16
        putLe32 h.Cylinders buf 20
        putLe32 h.Sectors buf 24
        putLe32 h.SectorSize buf 28
        putLe32 h.BlockSize buf 36
        putLe32 h.BlocksCount buf 40
        putLe32 h.L1Size buf 44
        putLe64 h.L1TableOffset buf 56
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
                let bIdx = blockIndex64 h vOff
                let bOffInBlock = blockOffsetInSector h vOff

                if bIdx >= h.L1Size then
                    Array.Clear(buf, bOff, remaining)
                    remaining <- 0
                else
                    let l1Off = h.L1TableOffset + int64 bIdx * 4L
                    s.Position <- l1Off
                    let blockOffsetBytes = Array.zeroCreate<byte> 4
                    readFully s blockOffsetBytes 0 4
                    let blockOffset = int64 (le32 blockOffsetBytes 0) * 512L
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
        protect (fun () -> readBytesAtCore s h vOffset count buf bufOff)

    let private findFreeBlock (s: Stream) (h: Header) : int64 =
        let mutable maxEnd = h.L1TableOffset + int64 h.L1Size * 4L

        for i in 0 .. h.BlocksCount - 1 do
            let l1Off = h.L1TableOffset + int64 i * 4L
            s.Position <- l1Off
            let buf = Array.zeroCreate<byte> 4
            readFully s buf 0 4
            let blockOff = int64 (le32 buf 0) * 512L

            if blockOff > 0L then
                let blockEnd = blockOff + int64 h.BlockSize

                if blockEnd > maxEnd then
                    maxEnd <- blockEnd

        (maxEnd + 511L) / 512L

    let private writeBytesAtCore (s: Stream) (h: Header) (vOffset: int64) (data: byte[]) (dataOff: int) (count: int) =
        let vSize = virtualSize h
        let mutable remaining = count
        let mutable vOff = vOffset
        let mutable dOff = dataOff

        while remaining > 0 do
            if vOff >= vSize then
                failwith "Ecriture hors limites Parallels"

            let bIdx = blockIndex64 h vOff
            let bOffInBlock = blockOffsetInSector h vOff

            if bIdx >= h.L1Size then
                failwith "L1 index hors limites Parallels"

            let l1Off = h.L1TableOffset + int64 bIdx * 4L
            s.Position <- l1Off
            let blockOffsetBuf = Array.zeroCreate<byte> 4
            readFully s blockOffsetBuf 0 4
            let blockOffset = int64 (le32 blockOffsetBuf 0) * 512L
            let toWrite = min remaining (h.BlockSize - int bOffInBlock)

            if blockOffset = 0L then
                let freeBlock = findFreeBlock s h

                // Format stocke l'offset de secteur sur 32 bits : refuser de
                // déborder au-delà de 2 To plutôt que de corrompre la table L1.
                if freeBlock > int64 UInt32.MaxValue then
                    failwith "Image Parallels pleine : limite de 2 To dépassée"

                let newOffset = freeBlock
                s.Position <- l1Off
                let newBuf = Array.zeroCreate<byte> 4
                putLe32 (int newOffset) newBuf 0
                s.Write(newBuf, 0, 4)
                let fileOffset = newOffset * 512L
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
        protect (fun () -> writeBytesAtCore s h vOffset data dataOff count)

/// Flux System.IO presentant une image Parallels comme un disque brut virtuel.
type ParallelsStream(path: string, access: FileAccess) =
    inherit RawImageStream(path, access)

    let fs = base.UnderlyingStream
    let header = Parallels.readHeader fs |> Result.defaultWith failwith
    let mutable position = 0L

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
