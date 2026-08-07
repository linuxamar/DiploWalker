namespace Diplo.Linux

open System

/// Extraction et décompression du noyau Linux compressé (zstd) contenu dans une image de démarrage.
module KernelImage =

    /// Magic d'une frame zstd : 28 B5 2F FD.
    let private frameMagic = [| 0x28uy; 0xB5uy; 0x2Fuy; 0xFDuy |]

    /// Taille initiale du tampon de destination : assez grande pour un noyau Linux décompressé.
    let private initialDestinationSize = 256_000_000

    /// Recherche le début de la frame zstd (magic 28 B5 2F FD) dans les données
    /// et retourne son offset. Lève ArgumentException si le magic est introuvable.
    let findFrameStart (data : byte[]) : int =
        let rec scan i =
            if i + frameMagic.Length > data.Length then
                invalidArg (nameof data) "Magic zstd (28 B5 2F FD) introuvable."
            elif data[i] = frameMagic[0] && data[i + 1] = frameMagic[1]
                 && data[i + 2] = frameMagic[2] && data[i + 3] = frameMagic[3] then
                i
            else
                scan (i + 1)

        scan 0

    /// Décompresse la frame zstd commençant à frameStart dans data et retourne
    /// les octets décompressés (ex. le noyau ELF). Les données éventuelles après
    /// la fin de la frame (résidu) sont ignorées. Lève ArgumentException si la
    /// frame est tronquée ou inexploitable.
    let decompress (data : byte[]) (frameStart : int) : byte[] =
        let input = data[frameStart .. data.Length - 1]
        let mutable destination = Array.zeroCreate<byte> initialDestinationSize
        let mutable consumed = 0
        let mutable written = 0

        use decompressor = new ZstdSharp.Decompressor()

        let status =
            decompressor.UnwrapStream(
                ReadOnlySpan<byte> input,
                Span<byte> destination,
                &consumed,
                &written
            )

        match status with
        | System.Buffers.OperationStatus.Done
        | System.Buffers.OperationStatus.InvalidData when written > 0 ->
            destination[0 .. written - 1]
        | System.Buffers.OperationStatus.NeedMoreData ->
            invalidArg (nameof data) "Frame zstd tronquée : données insuffisantes."
        | System.Buffers.OperationStatus.DestinationTooSmall ->
            invalidArg (nameof data) "Tampon de destination trop petit pour la décompression."
        | _ ->
            invalidArg (nameof data) "Décompression zstd impossible."

    /// Recherche le payload zstd dans data puis le décompresse en noyau ELF.
    let decompressKernel (data : byte[]) : byte[] =
        let frameStart = findFrameStart data
        decompress data frameStart
