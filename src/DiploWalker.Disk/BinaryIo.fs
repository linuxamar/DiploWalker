namespace DiploWalker.Disk

open System
open System.IO

/// Primitives binaires partagÃ©es par les pilotes d'images disque Â« maison Â»
/// (QCOW1, QCOW2, Parallels). Une seule dÃ©finition pour les lectures/Ã©critures
/// big-endian et little-endian ainsi que la lecture complÃ¨te d'un flux, afin
/// d'Ã©viter la duplication entre les pilotes.
module BinaryIo =

    // â”€â”€ big-endian â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    let be16 (d: byte[]) (o: int) = (int d.[o] <<< 8) ||| int d.[o + 1]

    let be32 (d: byte[]) (o: int) =
        (int d.[o] <<< 24)
        ||| (int d.[o + 1] <<< 16)
        ||| (int d.[o + 2] <<< 8)
        ||| int d.[o + 3]

    let be64 (d: byte[]) (o: int) =
        (int64 d.[o] <<< 56)
        ||| (int64 d.[o + 1] <<< 48)
        ||| (int64 d.[o + 2] <<< 40)
        ||| (int64 d.[o + 3] <<< 32)
        ||| (int64 d.[o + 4] <<< 24)
        ||| (int64 d.[o + 5] <<< 16)
        ||| (int64 d.[o + 6] <<< 8)
        ||| int64 d.[o + 7]

    let putBe16 (v: int) (d: byte[]) (o: int) =
        d.[o] <- byte (v >>> 8)
        d.[o + 1] <- byte v

    let putBe32 (v: int) (d: byte[]) (o: int) =
        d.[o] <- byte (v >>> 24)
        d.[o + 1] <- byte (v >>> 16)
        d.[o + 2] <- byte (v >>> 8)
        d.[o + 3] <- byte v

    let putBe64 (v: int64) (d: byte[]) (o: int) =
        d.[o] <- byte (v >>> 56)
        d.[o + 1] <- byte (v >>> 48)
        d.[o + 2] <- byte (v >>> 40)
        d.[o + 3] <- byte (v >>> 32)
        d.[o + 4] <- byte (v >>> 24)
        d.[o + 5] <- byte (v >>> 16)
        d.[o + 6] <- byte (v >>> 8)
        d.[o + 7] <- byte v

    // â”€â”€ little-endian â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    let le16 (d: byte[]) (o: int) = int d.[o] ||| (int d.[o + 1] <<< 8)

    let le32 (d: byte[]) (o: int) =
        int d.[o]
        ||| (int d.[o + 1] <<< 8)
        ||| (int d.[o + 2] <<< 16)
        ||| (int d.[o + 3] <<< 24)

    let le64 (d: byte[]) (o: int) =
        int64 d.[o]
        ||| (int64 d.[o + 1] <<< 8)
        ||| (int64 d.[o + 2] <<< 16)
        ||| (int64 d.[o + 3] <<< 24)
        ||| (int64 d.[o + 4] <<< 32)
        ||| (int64 d.[o + 5] <<< 40)
        ||| (int64 d.[o + 6] <<< 48)
        ||| (int64 d.[o + 7] <<< 56)

    let putLe16 (v: int) (d: byte[]) (o: int) =
        d.[o] <- byte v
        d.[o + 1] <- byte (v >>> 8)

    let putLe32 (v: int) (d: byte[]) (o: int) =
        d.[o] <- byte v
        d.[o + 1] <- byte (v >>> 8)
        d.[o + 2] <- byte (v >>> 16)
        d.[o + 3] <- byte (v >>> 24)

    let putLe64 (v: int64) (d: byte[]) (o: int) =
        d.[o] <- byte v
        d.[o + 1] <- byte (v >>> 8)
        d.[o + 2] <- byte (v >>> 16)
        d.[o + 3] <- byte (v >>> 24)
        d.[o + 4] <- byte (v >>> 32)
        d.[o + 5] <- byte (v >>> 40)
        d.[o + 6] <- byte (v >>> 48)
        d.[o + 7] <- byte (v >>> 56)

    // â”€â”€ lecture complÃ¨te d'un flux â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// Lit exactement `len` octets depuis `s` vers `buf` Ã  partir de `off`.
    /// LÃ¨ve une exception si le flux se termine avant la fin (Â« flux tronquÃ© Â»).
    let readFully (s: Stream) (buf: byte[]) (off: int) (len: int) =
        if len < 0 then
            invalidArg (nameof len) "La longueur ne peut Ãªtre nÃ©gative"

        let mutable doneCount = 0

        while doneCount < len do
            let n = s.Read(buf, off + doneCount, len - doneCount)

            if n = 0 then
                failwith "Fin prÃ©maturÃ©e du flux (fichier tronquÃ©)"

            doneCount <- doneCount + n

    // â”€â”€ exÃ©cution protÃ©gÃ©e retournant un Result â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// ExÃ©cute `f` et convertit toute exception en `Error ex.Message`.
    /// Centralise le schÃ©ma Â« try â€¦ Ok(â€¦) with e -> Error e.Message Â» des
    /// pilotes pour Ã©viter la duplication.
    let protect (f: unit -> 'a) : Result<'a, string> =
        try Ok(f()) with ex -> Error ex.Message


