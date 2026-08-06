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

        member this.RawBytes (b : byte[]) =
            this.Emit(b)

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
        member this.MovRdxImm64 (v : int64) = this.Emit(Array.append [| 0x48uy; 0xBAuy |] (this.Imm64 v))
        member this.MovRsiImm64 (v : int64) = this.Emit(Array.append [| 0x48uy; 0xBEuy |] (this.Imm64 v))

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
        member this.LeaRdxRip (label : string) =
            let start = bytes.Count
            this.Emit [| 0x48uy; 0x8Duy; 0x15uy; 0uy; 0uy; 0uy; 0uy |]
            ripRefs.Add(label, start)
        member this.LeaR10Rip (label : string) =
            let start = bytes.Count
            this.Emit [| 0x4Cuy; 0x8Duy; 0x15uy; 0uy; 0uy; 0uy; 0uy |]
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
        member this.MovEaxEdi () = this.Emit [| 0x89uy; 0xF8uy |]
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

    /// gettid doit retourner 1.
    let gettidCode : byte[] =
        let a = Asm()
        a.MovEaxImm 186
        a.Syscall()
        a.CmpEaxImm8 1
        a.Je "ok"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "ok"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Build()

    /// time(buf) écrit l'heure Unix (8 octets) dans buf puis sur stdout.
    let timeCode : byte[] =
        let a = Asm()
        a.MovEaxImm 201
        a.LeaRsiRip "buf"
        a.MovRdiRsi()
        a.Syscall()
        a.MovEaxImm 1
        a.MovEdiImm 1
        a.LeaRsiRip "buf"
        a.MovEdxImm 8
        a.Syscall()
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "buf"
        a.Zeros 8
        a.Build()

    /// setrlimit(RLIMIT_NOFILE, buf) doit réussir.
    let setrlimitCode : byte[] =
        let a = Asm()
        a.MovEaxImm 160
        a.MovEdiImm 7
        a.LeaRsiRip "buf"
        a.XorEdxEdx()
        a.Syscall()
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

    /// creat du fichier, écriture de « hi », puis fermeture.
    let creatCode (path : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 85
        a.LeaRdiRip "path"
        a.XorEsiEsi()
        a.Syscall()
        a.MovRdiRax()
        a.MovEaxImm 1
        a.LeaRsiRip "data"
        a.MovEdxImm 2
        a.Syscall()
        a.MovEaxImm 3
        a.Syscall()
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "path"
        a.Data path
        a.Label "data"
        a.Data "hi"
        a.Build()

    /// Ouvre (ou crée) le fichier, écrit « data », puis fsync.
    let fsyncCode (path : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 2
        a.LeaRdiRip "path"
        a.MovEsiImm 0x241
        a.Syscall()
        a.MovRdiRax()
        a.MovEaxImm 1
        a.LeaRsiRip "data"
        a.MovEdxImm 4
        a.Syscall()
        a.MovEaxImm 74
        a.Syscall()
        a.TestEaxEax()
        a.Je "ok"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "ok"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "path"
        a.Data path
        a.Label "data"
        a.Data "data"
        a.Build()

    /// Ouvre le fichier en écriture puis le tronque à la taille 0.
    let ftruncateCode (path : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 2
        a.LeaRdiRip "path"
        a.MovEsiImm 1
        a.Syscall()
        a.MovRdiRax()
        a.MovEaxImm 77
        a.XorEsiEsi()
        a.Syscall()
        a.TestEaxEax()
        a.Je "ok"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "ok"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "path"
        a.Data path
        a.Build()

    /// Ouvre le répertoire donné, fchdir, puis getcwd + écriture du chemin
    /// sur la sortie standard (longueur calculée par une boucle strlen guest).
    let fchdirCode (dir : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 2
        a.LeaRdiRip "path"
        a.XorEsiEsi()
        a.Syscall()
        a.MovRdiRax()
        a.MovEaxImm 81
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
        a.Data dir
        a.Label "buf"
        a.Zeros 256
        a.Build()

    let pollCode : byte[] =
        let a = Asm()
        a.MovEaxImm 7
        a.LeaRdiRip "fds"
        a.MovEsiImm 1
        a.XorEdxEdx()
        a.Syscall()
        a.CmpEaxImm8 1
        a.Jnz "fail"
        a.LeaRdiRip "fds"
        a.MovEdiMemRdiDisp8 6
        a.MovEaxEdi()
        a.AndEaxImm 0xFFFF
        a.CmpEaxImm8 4
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "fds"
        a.Data "\u0001\u0000\u0000\u0000\u0004"
        a.Zeros 8
        a.Build()

    let ppollCode : byte[] =
        let a = Asm()
        a.MovEaxImm 271
        a.LeaRdiRip "fds"
        a.MovEsiImm 1
        a.XorEdxEdx()
        a.MovR10Imm64 0L
        a.Syscall()
        a.CmpEaxImm8 1
        a.Jnz "fail"
        a.LeaRdiRip "fds"
        a.MovEdiMemRdiDisp8 6
        a.MovEaxEdi()
        a.AndEaxImm 0xFFFF
        a.CmpEaxImm8 4
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "fds"
        a.Data "\u0001\u0000\u0000\u0000\u0004"
        a.Zeros 8
        a.Build()

    let selectCode : byte[] =
        let a = Asm()
        a.MovEaxImm 23
        a.MovEdiImm 2
        a.XorEsiEsi()
        a.LeaRdxRip "wr"
        a.MovR10Imm64 0L
        a.MovR8Imm64 0L
        a.Syscall()
        a.CmpEaxImm8 1
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "wr"
        a.Data "\u0002"
        a.Build()

    let preadCode (path : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 2
        a.LeaRdiRip "path"
        a.XorEsiEsi()
        a.Syscall()
        a.MovRbxRax()
        a.MovEaxImm 17
        a.MovRdiRbx()
        a.LeaRsiRip "buf"
        a.MovEdxImm 5
        a.MovR10Imm64 2L
        a.Syscall()
        a.MovEaxImm 1
        a.MovEdiImm 1
        a.LeaRsiRip "buf"
        a.MovEdxImm 5
        a.Syscall()
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "path"
        a.Data path
        a.Label "buf"
        a.Zeros 8
        a.Build()

    let pwriteCode (path : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 2
        a.LeaRdiRip "path"
        a.MovEsiImm 1
        a.Syscall()
        a.MovRbxRax()
        a.MovEaxImm 18
        a.MovRdiRbx()
        a.LeaRsiRip "data"
        a.MovEdxImm 2
        a.MovR10Imm64 2L
        a.Syscall()
        a.CmpEaxImm8 2
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "path"
        a.Data path
        a.Label "data"
        a.Raw "hi"
        a.Build()

    let sendfileCode (path : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 2
        a.LeaRdiRip "path"
        a.XorEsiEsi()
        a.Syscall()
        a.MovRsiRax()
        a.MovEaxImm 40
        a.MovEdiImm 1
        a.XorEdxEdx()
        a.MovR10Imm64 7L
        a.Syscall()
        a.CmpEaxImm8 7
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "path"
        a.Data path
        a.Build()

    let getdentsCode (dir : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 2
        a.LeaRdiRip "path"
        a.XorEsiEsi()
        a.Syscall()
        a.MovRbxRax()
        a.MovEaxImm 78
        a.MovRdiRbx()
        a.LeaRsiRip "buf"
        a.MovEdxImm 512
        a.Syscall()
        a.CmpEaxImm8 24
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "path"
        a.Data dir
        a.Label "buf"
        a.Zeros 512
        a.Build()

    let truncateCode (path : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 76
        a.LeaRdiRip "path"
        a.MovEsiImm 3
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "path"
        a.Data path
        a.Build()

    let symlinkCode (target : string) (link : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 88
        a.LeaRdiRip "target"
        a.LeaRsiRip "link"
        a.Syscall()
        a.CmpEaxImm8 0
        a.Je "ok"
        a.CmpEaxImm8 -1
        a.Je "ok"
        a.CmpEaxImm8 -2
        a.Je "ok"
        a.CmpEaxImm8 -17
        a.Je "ok"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "ok"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "target"
        a.Data target
        a.Label "link"
        a.Data link
        a.Build()

    let readlinkCode (link : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 89
        a.LeaRdiRip "link"
        a.LeaRsiRip "buf"
        a.MovEdxImm 256
        a.Syscall()
        a.MovRdxRax()
        a.MovEaxImm 1
        a.MovEdiImm 1
        a.LeaRsiRip "buf"
        a.Syscall()
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "link"
        a.Data link
        a.Label "buf"
        a.Zeros 256
        a.Build()

    let devNullCode : byte[] =
        let a = Asm()
        a.MovEaxImm 2
        a.LeaRdiRip "path"
        a.MovEsiImm 2
        a.Syscall()
        a.MovRbxRax()
        a.MovEaxImm 0
        a.MovRdiRbx()
        a.LeaRsiRip "buf"
        a.MovEdxImm 16
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.MovEaxImm 1
        a.MovRdiRbx()
        a.LeaRsiRip "x"
        a.MovEdxImm 1
        a.Syscall()
        a.CmpEaxImm8 1
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "path"
        a.Data "/dev/null"
        a.Label "x"
        a.Data "x"
        a.Label "buf"
        a.Zeros 16
        a.Build()

    let devZeroCode : byte[] =
        let a = Asm()
        a.MovEaxImm 2
        a.LeaRdiRip "path"
        a.XorEsiEsi()
        a.Syscall()
        a.MovRbxRax()
        a.MovEaxImm 0
        a.MovRdiRbx()
        a.LeaRsiRip "buf"
        a.MovEdxImm 16
        a.Syscall()
        a.CmpEaxImm8 16
        a.Jnz "fail"
        a.LeaRdiRip "buf"
        a.MovAlBytePtrRdi()
        a.CmpAlImm 0
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "path"
        a.Data "/dev/zero"
        a.Label "buf"
        a.Zeros 16
        a.Build()

    let devRandomCode : byte[] =
        let a = Asm()
        a.MovEaxImm 2
        a.LeaRdiRip "path"
        a.XorEsiEsi()
        a.Syscall()
        a.MovRbxRax()
        a.MovEaxImm 0
        a.MovRdiRbx()
        a.LeaRsiRip "buf"
        a.MovEdxImm 16
        a.Syscall()
        a.CmpEaxImm8 16
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "path"
        a.Data "/dev/random"
        a.Label "buf"
        a.Zeros 16
        a.Build()

    let procSelfCmdlineCode : byte[] =
        let a = Asm()
        a.MovEaxImm 2
        a.LeaRdiRip "path"
        a.XorEsiEsi()
        a.Syscall()
        a.MovRbxRax()
        a.MovEaxImm 0
        a.MovRdiRbx()
        a.LeaRsiRip "buf"
        a.MovEdxImm 64
        a.Syscall()
        a.LeaRdiRip "buf"
        a.MovAlBytePtrRdi()
        a.CmpAlImm 47
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "path"
        a.Data "/proc/self/cmdline"
        a.Label "buf"
        a.Zeros 64
        a.Build()

    let procSelfGetdentsCode : byte[] =
        let a = Asm()
        a.MovEaxImm 2
        a.LeaRdiRip "path"
        a.XorEsiEsi()
        a.Syscall()
        a.MovRbxRax()
        a.MovEaxImm 217
        a.MovRdiRbx()
        a.LeaRsiRip "buf"
        a.MovEdxImm 512
        a.Syscall()
        a.TestEaxEax()
        a.Je "fail"
        a.CmpEaxImm8 -9
        a.Je "fail"
        a.CmpEaxImm8 -2
        a.Je "fail"
        a.CmpEaxImm8 -22
        a.Je "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "path"
        a.Data "/proc/self"
        a.Label "buf"
        a.Zeros 512
        a.Build()

    let readlinkExeCode : byte[] =
        let a = Asm()
        a.MovEaxImm 89
        a.LeaRdiRip "link"
        a.LeaRsiRip "buf"
        a.MovEdxImm 256
        a.Syscall()
        a.MovRdxRax()
        a.MovEaxImm 1
        a.MovEdiImm 1
        a.LeaRsiRip "buf"
        a.Syscall()
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "link"
        a.Data "/proc/self/exe"
        a.Label "buf"
        a.Zeros 256
        a.Build()

    let statDevNullCode : byte[] =
        let a = Asm()
        a.MovEaxImm 4
        a.LeaRdiRip "path"
        a.LeaRsiRip "buf"
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "path"
        a.Data "/dev/null"
        a.Label "buf"
        a.Zeros 144
        a.Build()

    let getresuidCode : byte[] =
        let a = Asm()
        a.MovEaxImm 118
        a.LeaRdiRip "ruid"
        a.LeaRsiRip "euid"
        a.XorEdxEdx()
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.LeaRdiRip "ruid"
        a.MovEdiMemRdi()
        a.MovEaxEdi()
        a.TestEaxEax()
        a.Jnz "fail"
        a.LeaRdiRip "euid"
        a.MovEdiMemRdi()
        a.MovEaxEdi()
        a.TestEaxEax()
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "ruid"
        a.Zeros 4
        a.Label "euid"
        a.Zeros 4
        a.Build()

    let getresgidCode : byte[] =
        let a = Asm()
        a.MovEaxImm 120
        a.LeaRdiRip "rgid"
        a.LeaRsiRip "egid"
        a.XorEdxEdx()
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.LeaRdiRip "rgid"
        a.MovEdiMemRdi()
        a.MovEaxEdi()
        a.TestEaxEax()
        a.Jnz "fail"
        a.LeaRdiRip "egid"
        a.MovEdiMemRdi()
        a.MovEaxEdi()
        a.TestEaxEax()
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "rgid"
        a.Zeros 4
        a.Label "egid"
        a.Zeros 4
        a.Build()

    let getcpuCode : byte[] =
        let a = Asm()
        a.MovEaxImm 309
        a.LeaRdiRip "cpu"
        a.LeaRsiRip "node"
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.LeaRdiRip "cpu"
        a.MovEdiMemRdi()
        a.MovEaxEdi()
        a.TestEaxEax()
        a.Jnz "fail"
        a.LeaRdiRip "node"
        a.MovEdiMemRdi()
        a.MovEaxEdi()
        a.TestEaxEax()
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "cpu"
        a.Zeros 4
        a.Label "node"
        a.Zeros 4
        a.Build()

    let umaskCode : byte[] =
        let a = Asm()
        a.MovEaxImm 95
        a.MovEdiImm 0x1B
        a.Syscall()
        a.CmpEaxImm8 0x12
        a.Jnz "fail"
        a.MovEaxImm 95
        a.MovEdiImm 0x12
        a.Syscall()
        a.CmpEaxImm8 0x1B
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Build()

    let getrusageCode : byte[] =
        let a = Asm()
        a.MovEaxImm 98
        a.XorEdiEdi()
        a.LeaRsiRip "usage"
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.LeaRdiRip "usage"
        a.MovEdiMemRdi()
        a.MovEaxEdi()
        a.TestEaxEax()
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "usage"
        a.Zeros 144
        a.Build()

    let sysinfoCode : byte[] =
        let a = Asm()
        a.MovEaxImm 99
        a.LeaRdiRip "buf"
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.LeaRdiRip "buf"
        a.MovEdiMemRdiDisp8 32
        a.MovEaxEdi()
        a.SubEaxImm 0x10000000
        a.TestEaxEax()
        a.Jnz "fail"
        a.LeaRdiRip "buf"
        a.MovEdiMemRdiDisp8 80
        a.MovEaxEdi()
        a.AndEaxImm 0xFFFF
        a.CmpEaxImm8 1
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "buf"
        a.Zeros 112
        a.Build()

    let timesCode : byte[] =
        let a = Asm()
        a.MovEaxImm 100
        a.LeaRdiRip "buf"
        a.Syscall()
        a.LeaRdiRip "buf"
        a.MovEdiMemRdi()
        a.MovEaxEdi()
        a.TestEaxEax()
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "buf"
        a.Zeros 32
        a.Build()

    let statfsCode (path : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 137
        a.LeaRdiRip "path"
        a.LeaRsiRip "buf"
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.LeaRdiRip "buf"
        a.MovEdiMemRdi()
        a.MovEaxEdi()
        a.SubEaxImm 0xEF53
        a.TestEaxEax()
        a.Jnz "fail"
        a.LeaRdiRip "buf"
        a.MovEdiMemRdiDisp8 8
        a.MovEaxEdi()
        a.SubEaxImm 4096
        a.TestEaxEax()
        a.Jnz "fail"
        a.LeaRdiRip "buf"
        a.MovEdiMemRdiDisp8 64
        a.MovEaxEdi()
        a.SubEaxImm 255
        a.TestEaxEax()
        a.Jnz "fail"
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
        a.Zeros 120
        a.Build()

    let fstatfsCode (path : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 2
        a.LeaRdiRip "path"
        a.XorEsiEsi()
        a.Syscall()
        a.MovRbxRax()
        a.MovEaxImm 138
        a.MovRdiRbx()
        a.LeaRsiRip "buf"
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.LeaRdiRip "buf"
        a.MovEdiMemRdi()
        a.MovEaxEdi()
        a.SubEaxImm 0xEF53
        a.TestEaxEax()
        a.Jnz "fail"
        a.LeaRdiRip "buf"
        a.MovEdiMemRdiDisp8 8
        a.MovEaxEdi()
        a.SubEaxImm 4096
        a.TestEaxEax()
        a.Jnz "fail"
        a.LeaRdiRip "buf"
        a.MovEdiMemRdiDisp8 64
        a.MovEaxEdi()
        a.SubEaxImm 255
        a.TestEaxEax()
        a.Jnz "fail"
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
        a.Zeros 120
        a.Build()

    let futexCode : byte[] =
        let a = Asm()
        a.MovEaxImm 202
        a.LeaRdiRip "uaddr"
        a.MovEsiImm 1
        a.XorEdxEdx()
        a.MovR10Imm64 0L
        a.MovR8Imm64 0L
        a.Syscall()
        a.CmpEaxImm8 1
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "uaddr"
        a.Zeros 4
        a.Build()

    let clockGetresCode : byte[] =
        let a = Asm()
        a.MovEaxImm 229
        a.XorEdiEdi()
        a.LeaRsiRip "res"
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.LeaRdiRip "res"
        a.MovEdiMemRdi()
        a.MovEaxEdi()
        a.TestEaxEax()
        a.Jnz "fail"
        a.LeaRdiRip "res"
        a.MovEdiMemRdiDisp8 8
        a.MovEaxEdi()
        a.CmpEaxImm8 1
        a.Jnz "fail"
        a.MovEaxImm 229
        a.MovEdiImm 99
        a.XorEsiEsi()
        a.Syscall()
        a.CmpEaxImm8 -22
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "res"
        a.Zeros 16
        a.Build()

    let clockNanosleepCode : byte[] =
        let a = Asm()
        a.MovEaxImm 230
        a.XorEdiEdi()
        a.XorEsiEsi()
        a.LeaRdxRip "req"
        a.MovR10Imm64 0L
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "req"
        a.Zeros 16
        a.Build()

    let prlimit64Code : byte[] =
        let a = Asm()
        a.MovEaxImm 302
        a.XorEdiEdi()
        a.MovEsiImm 7
        a.XorEdxEdx()
        a.LeaR10Rip "old"
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.LeaRdiRip "old"
        a.MovEdiMemRdi()
        a.MovEaxEdi()
        a.SubEaxImm 4096
        a.TestEaxEax()
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "old"
        a.Zeros 16
        a.Build()

    let schedGetaffinityCode : byte[] =
        let a = Asm()
        a.MovEaxImm 204
        a.XorEdiEdi()
        a.MovEsiImm 8
        a.LeaRdxRip "mask"
        a.Syscall()
        a.CmpEaxImm8 8
        a.Jnz "fail"
        a.LeaRdiRip "mask"
        a.MovAlBytePtrRdi()
        a.CmpAlImm 0xFF
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "mask"
        a.Zeros 8
        a.Build()

    let mincoreCode : byte[] =
        let a = Asm()
        a.MovEaxImm 9
        a.XorEdiEdi()
        a.MovEsiImm 0x1000
        a.MovEdxImm 3
        a.MovR10Imm64 0x22L
        a.MovR8Imm64 -1L
        a.XorR9dR9d()
        a.Syscall()
        a.MovRbxRax()
        a.MovEaxImm 27
        a.MovRdiRbx()
        a.MovEsiImm 0x1000
        a.LeaRdxRip "vec"
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "vec"
        a.Zeros 8
        a.Build()

    let madviseCode : byte[] =
        let a = Asm()
        a.MovEaxImm 9
        a.XorEdiEdi()
        a.MovEsiImm 0x1000
        a.MovEdxImm 3
        a.MovR10Imm64 0x22L
        a.MovR8Imm64 -1L
        a.XorR9dR9d()
        a.Syscall()
        a.MovRbxRax()
        a.MovEaxImm 28
        a.MovRdiRbx()
        a.MovEsiImm 0x1000
        a.XorEdxEdx()
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Build()

    let msyncCode : byte[] =
        let a = Asm()
        a.MovEaxImm 9
        a.XorEdiEdi()
        a.MovEsiImm 0x1000
        a.MovEdxImm 3
        a.MovR10Imm64 0x22L
        a.MovR8Imm64 -1L
        a.XorR9dR9d()
        a.Syscall()
        a.MovRbxRax()
        a.MovEaxImm 26
        a.MovRdiRbx()
        a.MovEsiImm 0x1000
        a.XorEdxEdx()
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Build()

    let mremapCode : byte[] =
        let a = Asm()
        a.MovEaxImm 9
        a.XorEdiEdi()
        a.MovEsiImm 0x1000
        a.MovEdxImm 3
        a.MovR10Imm64 0x22L
        a.MovR8Imm64 -1L
        a.XorR9dR9d()
        a.Syscall()
        a.MovRbxRax()
        a.LeaRdiRbxDisp32 0x100
        a.MovBytePtrRdiImm 0x42
        a.MovEaxImm 25
        a.MovRdiRbx()
        a.MovEsiImm 0x1000
        a.MovEdxImm 0x2000
        a.MovR10Imm64 1L
        a.MovR8Imm64 0L
        a.Syscall()
        a.MovRbxRax()
        a.LeaRdiRbxDisp32 0x100
        a.MovAlBytePtrRdi()
        a.CmpAlImm 0x42
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Build()

    let getitimerCode : byte[] =
        let a = Asm()
        a.MovEaxImm 36
        a.XorEdiEdi()
        a.LeaRsiRip "val"
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.LeaRdiRip "val"
        a.MovEdiMemRdi()
        a.MovEaxEdi()
        a.TestEaxEax()
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "val"
        a.Zeros 16
        a.Build()

    let fdatasyncCode (path : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 2
        a.LeaRdiRip "path"
        a.MovEsiImm 1
        a.Syscall()
        a.MovRbxRax()
        a.MovEaxImm 1
        a.MovRdiRbx()
        a.LeaRsiRip "data"
        a.MovEdxImm 4
        a.Syscall()
        a.MovEaxImm 75
        a.MovRdiRbx()
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "path"
        a.Data path
        a.Label "data"
        a.Raw "data"
        a.Build()

    let mkdiratCode (dir : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 258
        a.MovRdiImm64 -100L
        a.LeaRsiRip "path"
        a.MovEdxImm 0x1FF
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "path"
        a.Data dir
        a.Build()

    let unlinkatCode (path : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 263
        a.MovRdiImm64 -100L
        a.LeaRsiRip "path"
        a.XorEdxEdx()
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "path"
        a.Data path
        a.Build()

    let renameatCode (oldPath : string) (newPath : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 264
        a.MovRdiImm64 -100L
        a.LeaRsiRip "old"
        a.MovRdxImm64 -100L
        a.LeaR10Rip "new"
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "old"
        a.Data oldPath
        a.Label "new"
        a.Data newPath
        a.Build()

    let newfstatatCode (path : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 262
        a.MovRdiImm64 -100L
        a.LeaRsiRip "path"
        a.LeaRdxRip "st"
        a.MovR10Imm64 0L
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.LeaRdiRip "st"
        a.MovEdiMemRdiDisp8 48
        a.MovEaxEdi()
        a.SubEaxImm 7
        a.TestEaxEax()
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "path"
        a.Data path
        a.Label "st"
        a.Zeros 144
        a.Build()

    let symlinkatCode (target : string) (link : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 266
        a.LeaRdiRip "target"
        a.MovRsiImm64 -100L
        a.LeaRdxRip "link"
        a.Syscall()
        a.CmpEaxImm8 0
        a.Je "ok"
        a.CmpEaxImm8 -1
        a.Je "ok"
        a.CmpEaxImm8 -2
        a.Je "ok"
        a.CmpEaxImm8 -17
        a.Je "ok"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "ok"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "target"
        a.Data target
        a.Label "link"
        a.Data link
        a.Build()

    let readlinkatCode (link : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 267
        a.MovRdiImm64 -100L
        a.LeaRsiRip "link"
        a.LeaRdxRip "buf"
        a.MovR10Imm64 256L
        a.Syscall()
        a.MovRdxRax()
        a.MovEaxImm 1
        a.MovEdiImm 1
        a.LeaRsiRip "buf"
        a.Syscall()
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "link"
        a.Data link
        a.Label "buf"
        a.Zeros 256
        a.Build()

    let fchmodatCode (path : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 268
        a.MovRdiImm64 -100L
        a.LeaRsiRip "path"
        a.MovEdxImm 0x1FF
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "path"
        a.Data path
        a.Build()

    let faccessatCode (path : string) : byte[] =
        let a = Asm()
        a.MovEaxImm 269
        a.MovRdiImm64 -100L
        a.LeaRsiRip "path"
        a.XorEdxEdx()
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Label "path"
        a.Data path
        a.Build()

    let pselect6Code : byte[] =
        let a = Asm()
        a.MovEaxImm 270
        a.XorEdiEdi()
        a.XorEsiEsi()
        a.XorEdxEdx()
        a.MovR10Imm64 0L
        a.MovR8Imm64 0L
        a.Syscall()
        a.TestEaxEax()
        a.Jnz "fail"
        a.MovEaxImm 60
        a.XorEdiEdi()
        a.Syscall()
        a.Label "fail"
        a.MovEaxImm 60
        a.MovEdiImm 1
        a.Syscall()
        a.Build()
