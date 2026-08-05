namespace Diplo.Linux

/// Construction d'images de démarrage (mini noyaux) exécutables par l'émulateur x86-64.
module BootImage =

    /// Point d'entrée du noyau : adresse conventionnelle de 1 Mo, comme un noyau Linux chargé en mémoire.
    [<Literal>]
    let KernelEntryPoint = 0x100000UL

    let private u16 (v : uint16) : byte[] =
        [| byte v; byte (v >>> 8) |]

    let private u32 (v : uint32) : byte[] =
        [| byte v; byte (v >>> 8); byte (v >>> 16); byte (v >>> 24) |]

    let private u64 (v : uint64) : byte[] =
        [| byte v; byte (v >>> 8); byte (v >>> 16); byte (v >>> 24)
           byte (v >>> 32); byte (v >>> 40); byte (v >>> 48); byte (v >>> 56) |]

    let private writeAt (dest : byte[]) (offset : int) (src : byte[]) =
        Array.blit src 0 dest offset src.Length

    /// Assemble le code du mini noyau : pour chaque message, un appel write(1, msg, len)
    /// (bannière de démarrage sur la sortie standard), puis une instruction HLT (0xF4)
    /// qui arrête proprement l'émulateur (ExitStatus = 0).
    let private buildKernelCode (messages : string list) : byte[] =
        let code = ResizeArray<byte>()
        let dispPositions = ResizeArray<int>()
        let messageOffsets = ResizeArray<int>()
        let mutable messageTotal = 0

        let emit (bytes : byte[]) =
            code.AddRange bytes

        for message in messages do
            let messageBytes = System.Text.Encoding.UTF8.GetBytes message

            emit [| 0xB8uy; 1uy; 0uy; 0uy; 0uy |]    // mov eax, 1  (syscall write)
            emit [| 0xBFuy; 1uy; 0uy; 0uy; 0uy |]    // mov edi, 1  (fd = sortie standard)
            emit [| 0x48uy; 0x8Duy; 0x35uy |]        // lea rsi, [rip + disp32]
            let dispPos = code.Count
            dispPositions.Add dispPos
            emit (u32 0u)                            // disp32 (patché plus loin)
            emit [| 0xBAuy |]                        // opcode mov edx, imm32
            emit (u32 (uint32 messageBytes.Length))  // imm32 = longueur du message
            emit [| 0x0Fuy; 0x05uy |]                // syscall

            messageOffsets.Add messageTotal
            messageTotal <- messageTotal + messageBytes.Length

        emit [| 0xF4uy |]                            // hlt

        let messageBase = code.Count
        let codeArray = code.ToArray ()
        for i = 0 to dispPositions.Count - 1 do
            let dispPos = dispPositions[i]
            let target = uint64 (messageBase + messageOffsets[i])
            let disp = uint32 (target - uint64 (dispPos + 4))
            Array.blit (u32 disp) 0 codeArray dispPos 4

        let messageBytes = messages |> Seq.collect (fun m -> System.Text.Encoding.UTF8.GetBytes m) |> Seq.toArray
        Array.append codeArray messageBytes

    /// Construit un ELF64 exécutable (ET_EXEC) minimal : un en-tête 64 o + un programme
    /// header 56 o (un segment PT_LOAD R|X) + le code, mappé à l'adresse de base donnée.
    let private buildExecutable (code : byte[]) (entryPoint : uint64) (baseAddress : uint64) : byte[] =
        let header = Array.zeroCreate 64
        header[0] <- 0x7Fuy
        header[1] <- 0x45uy  // E
        header[2] <- 0x4Cuy  // L
        header[3] <- 0x46uy  // F
        header[4] <- 2uy     // classe ELF64
        header[5] <- 1uy     // little-endian
        header[6] <- 1uy     // version
        writeAt header 16 (u16 2us)     // e_type = ET_EXEC
        writeAt header 18 (u16 62us)    // e_machine = x86-64
        writeAt header 20 (u32 1u)      // e_version
        writeAt header 24 (u64 entryPoint)
        writeAt header 32 (u64 64UL)    // e_phoff
        writeAt header 52 (u16 64us)    // e_ehsize
        writeAt header 54 (u16 56us)    // e_phentsize
        writeAt header 56 (u16 1us)     // e_phnum

        let phdr = Array.zeroCreate 56
        let codeOffset = 64 + 56
        writeAt phdr 0 (u32 1u)                     // p_type = PT_LOAD
        writeAt phdr 4 (u32 5u)                     // p_flags = R | X
        writeAt phdr 8 (u64 (uint64 codeOffset))    // p_offset
        writeAt phdr 16 (u64 baseAddress)           // p_vaddr
        writeAt phdr 24 (u64 baseAddress)           // p_paddr
        writeAt phdr 32 (u64 (uint64 code.Length))  // p_filesz
        writeAt phdr 40 (u64 (uint64 code.Length))  // p_memsz
        writeAt phdr 48 (u64 0x1000UL)              // p_align

        Array.concat [ header; phdr; code ]

    /// Construit une image de démarrage (mini noyau) : le code écrit chaque message sur la
    /// sortie standard (séquence de boot) puis s'arrête par HLT, avec un code de sortie 0.
    let createKernel (messages : string seq) : byte[] =
        let messageList = List.ofSeq messages
        let code = buildKernelCode messageList
        buildExecutable code KernelEntryPoint KernelEntryPoint
