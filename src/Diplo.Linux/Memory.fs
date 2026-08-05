namespace Diplo.Linux

open System

/// Espace d'adressage virtuel plat de la machine émulée.
type VirtualMemory(size : uint64) =
    let buffer : byte[] = Array.zeroCreate (int size)

    let check (addr : uint64) (count : uint64) =
        if addr + count > size then
            invalidArg "addr" $"Adresse hors mémoire : 0x{addr:X} + 0x{count:X} > 0x{size:X}"

    /// Taille totale de la mémoire virtuelle, en octets.
    member _.Size = size

    member _.ReadByte(addr : uint64) : byte =
        check addr 1UL
        buffer[int addr]

    member _.ReadUInt16(addr : uint64) : uint16 =
        check addr 2UL
        let i = int addr
        uint16 buffer[i] ||| (uint16 buffer[i + 1] <<< 8)

    member _.ReadUInt32(addr : uint64) : uint32 =
        check addr 4UL
        let i = int addr
        uint32 buffer[i]
        ||| (uint32 buffer[i + 1] <<< 8)
        ||| (uint32 buffer[i + 2] <<< 16)
        ||| (uint32 buffer[i + 3] <<< 24)

    member _.ReadUInt64(addr : uint64) : uint64 =
        check addr 8UL
        let i = int addr
        uint64 buffer[i]
        ||| (uint64 buffer[i + 1] <<< 8)
        ||| (uint64 buffer[i + 2] <<< 16)
        ||| (uint64 buffer[i + 3] <<< 24)
        ||| (uint64 buffer[i + 4] <<< 32)
        ||| (uint64 buffer[i + 5] <<< 40)
        ||| (uint64 buffer[i + 6] <<< 48)
        ||| (uint64 buffer[i + 7] <<< 56)

    member _.WriteByte(addr : uint64) (value : byte) =
        check addr 1UL
        buffer[int addr] <- value

    member _.WriteUInt16(addr : uint64) (value : uint16) =
        check addr 2UL
        let i = int addr
        buffer[i] <- byte value
        buffer[i + 1] <- byte (value >>> 8)

    member _.WriteUInt32(addr : uint64) (value : uint32) =
        check addr 4UL
        let i = int addr
        buffer[i] <- byte value
        buffer[i + 1] <- byte (value >>> 8)
        buffer[i + 2] <- byte (value >>> 16)
        buffer[i + 3] <- byte (value >>> 24)

    member _.WriteUInt64(addr : uint64) (value : uint64) =
        check addr 8UL
        let i = int addr
        buffer[i] <- byte value
        buffer[i + 1] <- byte (value >>> 8)
        buffer[i + 2] <- byte (value >>> 16)
        buffer[i + 3] <- byte (value >>> 24)
        buffer[i + 4] <- byte (value >>> 32)
        buffer[i + 5] <- byte (value >>> 40)
        buffer[i + 6] <- byte (value >>> 48)
        buffer[i + 7] <- byte (value >>> 56)

    /// Lit un bloc d'octets (copie).
    member _.ReadBytes(addr : uint64) (count : int) : byte[] =
        check addr (uint64 count)
        let dst = Array.zeroCreate count
        Array.Copy(buffer, int addr, dst, 0, count)
        dst

    /// Écrit un bloc d'octets.
    member _.WriteBytes(addr : uint64) (data : byte[]) =
        check addr (uint64 data.Length)
        Array.Copy(data, 0, buffer, int addr, data.Length)

    /// Met à zéro une zone mémoire.
    member _.Zero(addr : uint64) (count : uint64) =
        check addr count
        Array.Clear(buffer, int addr, int count)

    /// Prend une copie complète de la mémoire (pour la sauvegarde d'une tâche).
    member _.Snapshot() : byte[] =
        let dst = Array.zeroCreate buffer.Length
        Array.Copy(buffer, dst, buffer.Length)
        dst

    /// Restaure le contenu complet de la mémoire depuis une copie.
    member _.Restore(data : byte[]) =
        if data.Length <> buffer.Length then
            invalidArg "data" $"Taille mémoire incompatible : {data.Length} <> {buffer.Length}"
        Array.Copy(data, buffer, buffer.Length)
