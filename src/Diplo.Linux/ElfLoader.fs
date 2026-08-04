namespace Diplo.Linux

/// Image d'un programme ELF chargé en mémoire.
type LoadedImage =
    { EntryPoint : uint64
      EndOfData : uint64 }

/// Chargeur de fichiers exécutables ELF64 (little-endian, x86-64).
module ElfLoader =

    [<Literal>]
    let private Elfsig1 = 0x7Fuy

    [<Literal>]
    let private Elfsig2 = 0x45uy

    [<Literal>]
    let private Elfsig3 = 0x4Cuy

    [<Literal>]
    let private Elfsig4 = 0x46uy

    [<Literal>]
    let private ElfClass64 = 2uy

    [<Literal>]
    let private ElfDataLsb = 1uy

    [<Literal>]
    let private EmX86_64 = 62us

    [<Literal>]
    let private EtExec = 2us

    [<Literal>]
    let private EtDyn = 3us

    [<Literal>]
    let private PtLoad = 1u

    let private u16 (data : byte[]) (o : int) =
        uint16 data[o] ||| (uint16 data[o + 1] <<< 8)

    let private u32 (data : byte[]) (o : int) =
        uint32 data[o]
        ||| (uint32 data[o + 1] <<< 8)
        ||| (uint32 data[o + 2] <<< 16)
        ||| (uint32 data[o + 3] <<< 24)

    let private u64 (data : byte[]) (o : int) =
        uint64 data[o]
        ||| (uint64 data[o + 1] <<< 8)
        ||| (uint64 data[o + 2] <<< 16)
        ||| (uint64 data[o + 3] <<< 24)
        ||| (uint64 data[o + 4] <<< 32)
        ||| (uint64 data[o + 5] <<< 40)
        ||| (uint64 data[o + 6] <<< 48)
        ||| (uint64 data[o + 7] <<< 56)

    /// Valide l'en-tête ELF64 et retourne le type d'exécutable.
    let private validate (data : byte[]) : uint16 =
        if data.Length < 64 then
            invalidArg "data" "Fichier trop court pour être un ELF64"

        if data[0] <> Elfsig1 || data[1] <> Elfsig2 || data[2] <> Elfsig3 || data[3] <> Elfsig4 then
            invalidArg "data" "Signature ELF invalide (magic manquant)"

        if data[4] <> ElfClass64 then
            invalidArg "data" "Seuls les ELF64 sont pris en charge"

        if data[5] <> ElfDataLsb then
            invalidArg "data" "Seul le little-endian est pris en charge"

        let machine = u16 data 18
        if machine <> EmX86_64 then
            invalidArg "data" $"Machine non prise en charge : {machine} (attendu EM_X86_64=62)"

        u16 data 16

    /// Charge l'image ELF dans la mémoire virtuelle et retourne le point d'entrée.
    let load (mem : VirtualMemory) (data : byte[]) : LoadedImage =
        let etype = validate data
        let bias =
            if etype = EtDyn then 0x400000UL
            else if etype = EtExec then 0UL
            else invalidArg "data" $"Type ELF non pris en charge : {etype}"

        let entry = u64 data 24
        let phoff = u64 data 32
        let phentsize = int (u16 data 54)
        let phnum = int (u16 data 56)

        if phentsize < 56 then
            invalidArg "data" $"Taille d'en-tête de programme invalide : {phentsize}"

        let mutable maxEnd = 0UL

        for i in 0 .. phnum - 1 do
            let off = int (phoff + uint64 (i * phentsize))
            let ptype = u32 data off
            if ptype = PtLoad then
                let pflags = u32 data (off + 4)
                let p_offset = u64 data (off + 8)
                let p_vaddr = u64 data (off + 16)
                let p_filesz = u64 data (off + 32)
                let p_memsz = u64 data (off + 40)

                let dst = bias + p_vaddr

                if p_filesz > 0UL then
                    mem.WriteBytes dst (data[int p_offset .. int (p_offset + p_filesz - 1UL)])

                if p_memsz > p_filesz then
                    mem.Zero (dst + p_filesz) (p_memsz - p_filesz)

                let segmentEnd = dst + p_memsz
                if segmentEnd > maxEnd then
                    maxEnd <- segmentEnd

        let endOfData =
            if maxEnd = 0UL then 0UL
            else (maxEnd + 0xFFFUL) &&& ~~~0xFFFUL

        { EntryPoint = bias + entry; EndOfData = endOfData }
