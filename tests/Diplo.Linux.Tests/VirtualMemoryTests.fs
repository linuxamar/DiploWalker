namespace Diplo.Linux.Tests

open Xunit
open FsUnit.Xunit
open Diplo.Linux

/// Tests du gestionnaire de régions mémoire (mmap/munmap/mprotect/mremap).
module VirtualMemoryTests =

    let private create () = VirtualMemory(256UL <<< 20)

    [<Fact>]
    let ``MapRegion enregistre la région avec sa protection`` () =
        let mem = create ()
        mem.MapRegion 0x1000UL 0x2000UL 0x3UL
        mem.Regions |> Seq.length |> should equal 1
        let r = mem.Regions[0]
        r.Start |> should equal 0x1000UL
        r.Length |> should equal 0x2000UL
        r.Prot |> should equal 0x3UL

    [<Fact>]
    let ``MapRegion en MAP_FIXED remplace la région qui se chevauche`` () =
        let mem = create ()
        mem.MapRegion 0x0000UL 0x3000UL 0x3UL
        mem.MapRegion 0x1000UL 0x1000UL 0x1UL
        mem.Regions |> Seq.length |> should equal 1
        let r = mem.Regions[0]
        r.Start |> should equal 0x1000UL
        r.Length |> should equal 0x1000UL
        r.Prot |> should equal 0x1UL

    [<Fact>]
    let ``MapRegion préserve les régions disjointes`` () =
        let mem = create ()
        mem.MapRegion 0x0000UL 0x1000UL 0x3UL
        mem.MapRegion 0x2000UL 0x1000UL 0x3UL
        mem.Regions |> Seq.length |> should equal 2

    [<Fact>]
    let ``UnmapRange au milieu découpe la région en deux`` () =
        let mem = create ()
        mem.MapRegion 0x0000UL 0x3000UL 0x3UL
        mem.UnmapRange 0x1000UL 0x1000UL
        mem.Regions |> Seq.length |> should equal 2
        let gauche = mem.Regions |> Seq.find (fun r -> r.Start = 0x0000UL)
        gauche.Length |> should equal 0x1000UL
        let droite = mem.Regions |> Seq.find (fun r -> r.Start = 0x2000UL)
        droite.Length |> should equal 0x1000UL
        droite.Prot |> should equal 0x3UL

    [<Fact>]
    let ``UnmapRange total supprime la région`` () =
        let mem = create ()
        mem.MapRegion 0x0000UL 0x3000UL 0x3UL
        mem.UnmapRange 0x0000UL 0x3000UL
        mem.Regions |> Seq.isEmpty |> should be True

    [<Fact>]
    let ``UnmapRange sur le bord gauche ne garde que la droite`` () =
        let mem = create ()
        mem.MapRegion 0x0000UL 0x3000UL 0x3UL
        mem.UnmapRange 0x0000UL 0x1000UL
        mem.Regions |> Seq.length |> should equal 1
        let r = mem.Regions[0]
        r.Start |> should equal 0x1000UL
        r.Length |> should equal 0x2000UL

    [<Fact>]
    let ``UnmapRange traversant deux régions les découpe toutes les deux`` () =
        let mem = create ()
        mem.MapRegion 0x0000UL 0x1000UL 0x3UL
        mem.MapRegion 0x2000UL 0x1000UL 0x3UL
        mem.UnmapRange 0x0500UL 0x2000UL   // 0x0500..0x2500
        mem.Regions |> Seq.length |> should equal 2
        let gauche = mem.Regions |> Seq.find (fun r -> r.Start = 0x0000UL)
        gauche.Length |> should equal 0x0500UL
        let droite = mem.Regions |> Seq.find (fun r -> r.Start = 0x2500UL)
        droite.Length |> should equal 0x0B00UL

    [<Fact>]
    let ``ProtectRange découpe et applique la protection au centre`` () =
        let mem = create ()
        mem.MapRegion 0x0000UL 0x3000UL 0x3UL
        mem.ProtectRange 0x1000UL 0x1000UL 0x1UL
        mem.Regions |> Seq.length |> should equal 3
        let centre = mem.Regions |> Seq.find (fun r -> r.Start = 0x1000UL)
        centre.Prot |> should equal 0x1UL
        centre.Length |> should equal 0x1000UL
        mem.Regions
        |> Seq.filter (fun r -> r.Start <> 0x1000UL)
        |> Seq.forall (fun r -> r.Prot = 0x3UL)
        |> should be True

    [<Fact>]
    let ``ProtectRange sur toute la région change sa protection`` () =
        let mem = create ()
        mem.MapRegion 0x0000UL 0x2000UL 0x3UL
        mem.ProtectRange 0x0000UL 0x2000UL 0x1UL
        mem.Regions |> Seq.length |> should equal 1
        mem.Regions[0].Prot |> should equal 0x1UL

    let private mmapFixedCode =
        let a = ElfTest.Asm()
        // mmap(MAP_FIXED|MAP_PRIVATE|MAP_ANONYMOUS, addr=0x0E001000, len=0x2000, prot=RW)
        a.MovEaxImm 9
        a.MovRdiImm64 0x0E001000L
        a.MovEsiImm 0x2000
        a.MovEdxImm 0x3
        a.MovR10Imm64 0x32L
        a.MovR8Imm64 -1L
        a.XorR9dR9d()
        a.Syscall()
        // écrit puis relit une valeur à (retour)+0x1000
        a.MovRbxRax()
        a.LeaRdiRbxDisp32 0x1000
        a.MovBytePtrRdiImm 0x42
        a.MovAlBytePtrRdi()
        a.CmpAlImm 0x42
        a.Jnz "mmapFixedFail"
        a.XorEdiEdi()
        a.MovEaxImm 60
        a.Syscall()
        a.Label "mmapFixedFail"
        a.MovEdiImm 1
        a.MovEaxImm 60
        a.Syscall()
        ElfTest.create (a.Build()) 0x400000UL 0x400000UL

    [<Fact>]
    let ``mmap MAP_FIXED mappe à l'adresse demandée et reste utilisable`` () =
        use machine = new LinuxMachine(mmapFixedCode, [||])
        machine.Run() |> should equal 0
