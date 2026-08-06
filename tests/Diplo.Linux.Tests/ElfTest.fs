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

    /// Assembleur minimaliste : quelques instructions x86-64 avec résolution
    /// automatique des adresses RIP-relatives (lea) et des sauts courts (je/jne).
    type Asm () =
        let bytes = ResizeArray<byte>()
        let labels = System.Collections.Generic.Dictionary<string, int>()
        let ripRefs = ResizeArray<string * int>()
        let jumpRefs = ResizeArray<string * int>()

        member private _.Emit (b : byte[]) = bytes.AddRange b

        member this.Label (name : string) = labels[name] <- bytes.Count

        member this.Data (s : string) =
            this.Emit(System.Text.Encoding.UTF8.GetBytes(s + "\u0000"))

        member this.Raw (s : string) =
            this.Emit(System.Text.Encoding.UTF8.GetBytes s)

        member this.Zeros (n : int) = this.Emit(Array.zeroCreate n)

        member private _.Imm32 (v : int) : byte[] =
            [| byte v; byte (v >>> 8); byte (v >>> 16); byte (v >>> 24) |]

        member private _.Imm64 (v : int64) : byte[] =
            [| byte v
               byte (v >>> 8)
               byte (v >>> 16)
               byte (v >>> 24)
               byte (v >>> 32)
               byte (v >>> 40)
               byte (v >>> 48)
               byte (v >>> 56) |]

        member this.MovEaxImm (v : int) = this.Emit(Array.append [| 0xB8uy |] (this.Imm32 v))
        member this.MovEdiImm (v : int) = this.Emit(Array.append [| 0xBFuy |] (this.Imm32 v))
        member this.MovEsiImm (v : int) = this.Emit(Array.append [| 0xBEuy |] (this.Imm32 v))
        member this.MovEdxImm (v : int) = this.Emit(Array.append [| 0xBAuy |] (this.Imm32 v))
        member this.MovRdiImm64 (v : int64) = this.Emit(Array.append [| 0x48uy; 0xBFuy |] (this.Imm64 v))
        member this.MovR10Imm64 (v : int64) = this.Emit(Array.append [| 0x49uy; 0xBAuy |] (this.Imm64 v))
        member this.MovR8Imm64 (v : int64) = this.Emit(Array.append [| 0x49uy; 0xB8uy |] (this.Imm64 v))

        member this.MovRdiRax () = this.Emit [| 0x48uy; 0x89uy; 0xC7uy |]
        member this.MovRdiRbx () = this.Emit [| 0x48uy; 0x89uy; 0xDFuy |]
        member this.MovRsiRax () = this.Emit [| 0x48uy; 0x89uy; 0xC6uy |]
        member this.MovRbxRax () = this.Emit [| 0x48uy; 0x89uy; 0xC3uy |]
        member this.MovR8Rax () = this.Emit [| 0x49uy; 0x89uy; 0xC0uy |]
        member this.XorEaxEax () = this.Emit [| 0x31uy; 0xC0uy |]
        member this.XorEdiEdi () = this.Emit [| 0x31uy; 0xFFuy |]
        member this.XorEsiEsi () = this.Emit [| 0x31uy; 0xF6uy |]
        member this.XorEdxEdx () = this.Emit [| 0x31uy; 0xD2uy |]
        member this.XorR9dR9d () = this.Emit [| 0x45uy; 0x31uy; 0xC9uy |]
        member this.Syscall () = this.Emit [| 0x0Fuy; 0x05uy |]
        member this.TestEaxEax () = this.Emit [| 0x85uy; 0xC0uy |]
        member this.AndEaxImm (v : int) = this.Emit(Array.append [| 0x25uy |] (this.Imm32 v))
        member this.CmpEaxImm8 (v : int) = this.Emit [| 0x83uy; 0xF8uy; byte v |]
        member this.CmpAlImm (v : int) = this.Emit [| 0x3Cuy; byte v |]
        member this.MovAlBytePtrRdi () = this.Emit [| 0x8Auy; 0x07uy |]
        member this.MovBytePtrRdiImm (v : int) = this.Emit [| 0xC6uy; 0x07uy; byte v |]
        member this.LeaRdiRbxDisp32 (disp : int) =
            this.Emit [| 0x48uy; 0x8Duy; 0xBBuy; byte disp; byte (disp >>> 8); byte (disp >>> 16); byte (disp >>> 24) |]
        member this.LeaRdiRip (label : string) =
            let start = bytes.Count
            this.Emit [| 0x48uy; 0x8Duy; 0x3Duy; 0uy; 0uy; 0uy; 0uy |]
            ripRefs.Add(label, start)
        member this.LeaRsiRip (label : string) =
            let start = bytes.Count
            this.Emit [| 0x48uy; 0x8Duy; 0x35uy; 0uy; 0uy; 0uy; 0uy |]
            ripRefs.Add(label, start)
        member this.Jnz (label : string) =
            let start = bytes.Count
            this.Emit [| 0x75uy; 0uy |]
            jumpRefs.Add(label, start)
        member this.Je (label : string) =
            let start = bytes.Count
            this.Emit [| 0x74uy; 0uy |]
            jumpRefs.Add(label, start)
        member this.Jmp (label : string) =
            let start = bytes.Count
            this.Emit [| 0xEBuy; 0uy |]
            jumpRefs.Add(label, start)

        member this.MovRdiRsi () = this.Emit [| 0x48uy; 0x89uy; 0xF7uy |]
        member this.MovRdxRax () = this.Emit [| 0x48uy; 0x89uy; 0xC2uy |]
        member this.MovRdxRcx () = this.Emit [| 0x48uy; 0x89uy; 0xCAuy |]
        member this.MovRaxMemRsi () = this.Emit [| 0x48uy; 0x8Buy; 0x06uy |]
        member this.MovEdiMemRdi () = this.Emit [| 0x8Buy; 0x3Fuy |]
        member this.MovEdiMemRdiDisp8 (v : int) = this.Emit [| 0x8Buy; 0x7Fuy; byte v |]
        member this.SubEaxImm (v : int) = this.Emit(Array.append [| 0x2Duy |] (this.Imm32 v))
        member this.XorEcxEcx () = this.Emit [| 0x31uy; 0xC9uy |]
        member this.IncEcx () = this.Emit [| 0xFFuy; 0xC1uy |]
        member this.CmpBytePtrRsiRcxImm (v : int) = this.Emit [| 0x80uy; 0x3Cuy; 0x0Euy; byte v |]

        member this.Build () : byte[] =
            for (label, start) in ripRefs do
                let disp = labels[label] - (start + 7)
                bytes[start + 3] <- byte disp
                bytes[start + 4] <- byte (disp >>> 8)
                bytes[start + 5] <- byte (disp >>> 16)
                bytes[start + 6] <- byte (disp >>> 24)
            for (label, start) in jumpRefs do
                bytes[start + 1] <- byte (labels[label] - (start + 2))
            bytes.ToArray()

    /// Ouvre /dev/console en écriture, y écrit « Bonjour console\n », puis exit(0).
    let consoleWriteCode : byte[] =
        let a = Asm()
        a.MovEaxImm 2
        a.LeaRdiRip "path"
        a.MovEsiImm 1
        a.Syscall()
        a.MovRdiRax()
        a.MovEaxImm 1
        a.LeaRsiRip "text"
        a.MovEdxImm 16
        a.Syscall()
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "path"
        a.Data "/dev/console"
        a.Label "text"
        a.Raw "Bonjour console\n"
        a.Build()

    /// Lit 5 octets depuis /dev/console puis les réécrit sur le même descripteur.
    let consoleEchoCode : byte[] =
        let a = Asm()
        a.MovEaxImm 2
        a.LeaRdiRip "path"
        a.XorEsiEsi()
        a.Syscall()
        a.MovRdiRax()
        a.XorEaxEax()
        a.LeaRsiRip "buf"
        a.MovEdxImm 5
        a.Syscall()
        a.MovEaxImm 1
        a.MovEdxImm 5
        a.Syscall()
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "path"
        a.Data "/dev/console"
        a.Label "buf"
        a.Raw "ECHO\n"
        a.Build()

    /// mmap anonyme de 0x5000 puis exit(dest &&& 0xFFF) : le code de sortie
    /// vaut 0 si l'adresse retournée est alignée sur une page.
    let mmapAnonCode : byte[] =
        let a = Asm()
        a.MovEaxImm 9
        a.XorEdiEdi()
        a.MovEsiImm 0x5000
        a.MovEdxImm 3
        a.MovR10Imm64 0x22L
        a.MovR8Imm64 -1L
        a.XorR9dR9d()
        a.Syscall()
        a.AndEaxImm 0xFFF
        a.MovRdiRax()
        a.MovEaxImm 60
        a.Syscall()
        a.Build()

    /// mmap, écriture/lecture dans la région, mprotect puis munmap ;
    /// le code de sortie distingue chaque échec.
    let mmapLifecycleCode : byte[] =
        let a = Asm()
        a.MovEaxImm 9
        a.XorEdiEdi()
        a.MovEsiImm 0x5000
        a.MovEdxImm 3
        a.MovR10Imm64 0x22L
        a.MovR8Imm64 -1L
        a.XorR9dR9d()
        a.Syscall()
        a.MovRbxRax()
        a.LeaRdiRbxDisp32 0x1000
        a.MovBytePtrRdiImm 0x42
        a.MovAlBytePtrRdi()
        a.CmpAlImm 0x42
        a.Jnz "fail1"
        a.MovRdiRbx()
        a.MovEaxImm 10
        a.MovEsiImm 0x5000
        a.MovEdxImm 1
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail2"
        a.MovRdiRbx()
        a.MovEaxImm 11
        a.MovEsiImm 0x5000
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail3"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail1"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "fail2"
        a.MovEaxImm 60
        a.MovEdiImm 2
        a.Syscall()
        a.Label "fail3"
        a.MovEaxImm 60
        a.MovEdiImm 3
        a.Syscall()
        a.Build()

    /// Ouvre un fichier, le mmappe en lecture puis réécrit les 6 premiers
    /// octets mappés sur la sortie standard.
    let mmapFileCode (path : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 2
        a.LeaRdiRip "path"
        a.XorEsiEsi()
        a.Syscall()
        a.MovR8Rax()
        a.MovEaxImm 9
        a.XorEdiEdi()
        a.MovEsiImm 0x1000
        a.MovEdxImm 1
        a.MovR10Imm64 0x2L
        a.XorR9dR9d()
        a.Syscall()
        a.MovRsiRax()
        a.MovEdiImm 1
        a.MovEaxImm 1
        a.MovEdxImm 6
        a.Syscall()
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "path"
        a.Data path
        a.Build()

    /// fork : l'enfant sort avec 3, le parent avec 7.
    let forkCode : byte[] =
        let a = Asm()
        a.MovEaxImm 57
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "child"
        a.MovEaxImm 60
        a.MovEdiImm 7
        a.Syscall()
        a.Label "child"
        a.MovEaxImm 60
        a.MovEdiImm 3
        a.Syscall()
        a.Build()

    /// wait4 sans enfant terminé doit retourner -ECHILD (-10).
    let wait4EmptyCode : byte[] =
        let a = Asm()
        a.MovEaxImm 61
        a.MovRdiImm64 -1L
        a.XorEsiEsi()
        a.XorEdxEdx()
        a.Syscall()
        a.CmpEaxImm8 -10
        a.Je "ok"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "ok"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Build()

    /// getrandom de 16 octets : doit retourner la longueur demandée.
    let getrandomCode : byte[] =
        let a = Asm()
        a.MovEaxImm 318
        a.LeaRdiRip "buf"
        a.MovEsiImm 16
        a.XorEdxEdx()
        a.Syscall()
        a.CmpEaxImm8 16
        a.Je "ok"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "ok"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "buf"
        a.Zeros 16
        a.Build()

    /// pipe2 puis aller-retour : écrit « ping » sur l'extrémité d'écriture,
    /// relit sur l'extrémité de lecture et réécrit le résultat sur stdout.
    let pipe2Code : byte[] =
        let a = Asm()
        a.MovEaxImm 293
        a.LeaRdiRip "fds"
        a.XorEsiEsi()
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.LeaRdiRip "fds"
        a.MovEdiMemRdiDisp8 4
        a.MovEaxImm 1
        a.LeaRsiRip "msg"
        a.MovEdxImm 4
        a.Syscall()
        a.LeaRdiRip "fds"
        a.MovEdiMemRdi ()
        a.XorEaxEax()
        a.LeaRsiRip "buf"
        a.MovEdxImm 4
        a.Syscall()
        a.MovEaxImm 1
        a.MovEdiImm 1
        a.LeaRsiRip "buf"
        a.MovEdxImm 4
        a.Syscall()
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "fds"
        a.Zeros 8
        a.Label "msg"
        a.Raw "ping"
        a.Label "buf"
        a.Raw "xxxx"
        a.Build()

    /// dup2(1, 7) puis écriture de « duplo » sur le descripteur 7.
    let dup2Code : byte[] =
        let a = Asm()
        a.MovEaxImm 33
        a.MovEdiImm 1
        a.MovEsiImm 7
        a.Syscall()
        a.MovEaxImm 1
        a.MovEdiImm 7
        a.LeaRsiRip "msg"
        a.MovEdxImm 5
        a.Syscall()
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "msg"
        a.Raw "duplo"
        a.Build()

    /// fcntl F_DUPFD de la sortie standard (fd minimal 10) puis écriture
    /// de « fd » via le descripteur retourné.
    let fcntlDupCode : byte[] =
        let a = Asm()
        a.MovEaxImm 72
        a.MovEdiImm 1
        a.XorEsiEsi()
        a.MovEdxImm 10
        a.Syscall()
        a.MovRdiRax()
        a.MovEaxImm 1
        a.LeaRsiRip "msg"
        a.MovEdxImm 2
        a.Syscall()
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "msg"
        a.Raw "fd"
        a.Build()

    /// ioctl(0, TCGETS, 0) : la requête n'est pas gérée, il faut -ENOTTY (-25).
    let ioctlCode : byte[] =
        let a = Asm()
        a.MovEaxImm 16
        a.XorEdiEdi()
        a.MovEsiImm 0x5401
        a.XorEdxEdx()
        a.Syscall()
        a.CmpEaxImm8 -25
        a.Je "ok"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "ok"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Build()

    /// getrlimit(RLIMIT_NOFILE) : la limite courante doit valoir 4096.
    let getrlimitCode : byte[] =
        let a = Asm()
        a.MovEaxImm 97
        a.MovEdiImm 7
        a.LeaRsiRip "buf"
        a.Syscall()
        a.MovRaxMemRsi()
        a.SubEaxImm 4096
        a.TestEaxEax()
        a.Je "ok"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "ok"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "buf"
        a.Zeros 16
        a.Build()

    /// chdir vers le chemin donné, getcwd, puis écriture du répertoire courant
    /// sur la sortie standard (longueur calculée par une boucle strlen guest).
    let chdirCode (path : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 80
        a.LeaRdiRip "path"
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.MovEaxImm 79
        a.LeaRdiRip "buf"
        a.MovEsiImm 256
        a.Syscall()
        a.MovRsiRax()
        a.XorEcxEcx()
        a.Label "loop"
        a.CmpBytePtrRsiRcxImm 0
        a.Je "done"
        a.IncEcx()
        a.Jmp "loop"
        a.Label "done"
        a.MovEaxImm 1
        a.MovEdiImm 1
        a.MovRdxRcx()
        a.Syscall()
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "path"
        a.Data path
        a.Label "buf"
        a.Zeros 256
        a.Build()
