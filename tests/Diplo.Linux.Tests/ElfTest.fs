namespace Diplo.Linux.Tests

/// Construction d'images ELF64 minimalistes pour les tests.
module ElfTest =

    let private u16 (v : uint16) : byte[] =
        [| byte v; byte (v >>> 8) |]

    let private u32 (v : uint32) : byte[] =
        [| byte v; byte (v >>> 8); byte (v >>> 16); byte (v >>> 24) |]

    let private u64 (v : uint64) : byte[] =
        [| byte v
           byte (v >>> 8)
           byte (v >>> 16)
           byte (v >>> 24)
           byte (v >>> 32)
           byte (v >>> 40)
           byte (v >>> 48)
           byte (v >>> 56) |]

    /// Construit un ELF64 ET_EXEC (x86-64) avec un unique segment PT_LOAD R|X.
    let create (code : byte[]) (entry : uint64) (baseAddr : uint64) : byte[] =
        let header = Array.zeroCreate 64
        header[0] <- 0x7Fuy
        header[1] <- 0x45uy
        header[2] <- 0x4Cuy
        header[3] <- 0x46uy
        header[4] <- 2uy
        header[5] <- 1uy
        header[6] <- 1uy

        let phdr = Array.zeroCreate 56
        let codeOffset = 64UL + 56UL
        Array.blit (u32 1u) 0 phdr 0 4
        Array.blit (u32 5u) 0 phdr 4 4
        Array.blit (u64 codeOffset) 0 phdr 8 8
        Array.blit (u64 baseAddr) 0 phdr 16 8
        Array.blit (u64 baseAddr) 0 phdr 24 8
        Array.blit (u64 (uint64 code.Length)) 0 phdr 32 8
        Array.blit (u64 (uint64 code.Length)) 0 phdr 40 8
        Array.blit (u64 0x1000UL) 0 phdr 48 8

        let out = Array.zeroCreate (64 + 56 + code.Length)
        Array.blit header 0 out 0 64
        Array.blit phdr 0 out 64 56
        Array.blit code 0 out 120 code.Length
        out[16] <- 2uy
        out[17] <- 0uy
        out[18] <- 62uy
        out[19] <- 0uy
        out[20] <- 1uy
        Array.blit (u64 entry) 0 out 24 8
        Array.blit (u64 64UL) 0 out 32 8
        out[52] <- 64uy
        out[53] <- 0uy
        out[54] <- 56uy
        out[55] <- 0uy
        out[56] <- 1uy
        out

    /// Écrit « Hello, Linux\n » (syscall write, fd=1) puis exit(42).
    let helloWorldCode : byte[] =
        [| 0xB8uy; 0x01uy; 0x00uy; 0x00uy; 0x00uy
           0xBFuy; 0x01uy; 0x00uy; 0x00uy; 0x00uy
           0x48uy; 0x8Duy; 0x35uy; 0x13uy; 0x00uy; 0x00uy; 0x00uy
           0xBAuy; 0x0Duy; 0x00uy; 0x00uy; 0x00uy
           0x0Fuy; 0x05uy
           0xB8uy; 0x3Cuy; 0x00uy; 0x00uy; 0x00uy
           0xBFuy; 0x2Auy; 0x00uy; 0x00uy; 0x00uy
           0x0Fuy; 0x05uy
           byte 'H'; byte 'e'; byte 'l'; byte 'l'; byte 'o'; byte ','; byte ' '
           byte 'L'; byte 'i'; byte 'n'; byte 'u'; byte 'x'; byte '\n' |]

    /// Réécrit le premier argument (argv[1]) via write(1) puis exit(0).
    let argvCode : byte[] =
        [| 0x48uy; 0x8Buy; 0x74uy; 0x24uy; 0x10uy
           0xBFuy; 0x01uy; 0x00uy; 0x00uy; 0x00uy
           0xB8uy; 0x01uy; 0x00uy; 0x00uy; 0x00uy
           0xBAuy; 0x07uy; 0x00uy; 0x00uy; 0x00uy
           0x0Fuy; 0x05uy
           0xB8uy; 0x3Cuy; 0x00uy; 0x00uy; 0x00uy
           0x31uy; 0xFFuy
           0x0Fuy; 0x05uy |]
