namespace Diplo.Disk.Tests

module ParallelsFsTests =

    open System
    open System.IO
    open Xunit
    open FsUnit.Xunit
    open Diplo.Disk

    let private makeHeader () : Parallels.Header =
        { Magic = 0x30617261
          Version = 1
          Heads = 16
          Cylinders = 16
          Sectors = 63
          SectorSize = 512
          BlockSize = 512
          BlocksCount = 4
          L1Size = 4
          L1TableOffset = 2048L }

    let private makeStream () =
        new MemoryStream(Array.zeroCreate<byte> 32_768)

    [<Fact>]
    let ``writeHeader puis readHeader conserve les valeurs`` () =
        use s = makeStream ()
        let h = makeHeader ()
        Parallels.writeHeader s h

        match Parallels.readHeader s with
        | Ok read ->
            read.Magic |> should equal 0x30617261
            read.Heads |> should equal 16
            read.Cylinders |> should equal 16
            read.Sectors |> should equal 63
            read.SectorSize |> should equal 512
            read.BlockSize |> should equal 512
            read.BlocksCount |> should equal 4
            read.L1Size |> should equal 4
            read.L1TableOffset |> should equal 2048L
        | Error msg -> failwith msg

    [<Fact>]
    let ``readHeader renvoie Error sur magic invalide`` () =
        use s = new MemoryStream(Array.zeroCreate<byte> 1024)
        s.Write([| 1uy; 2uy; 3uy; 4uy |], 0, 4)
        s.Position <- 0L

        match Parallels.readHeader s with
        | Error _ -> ()
        | Ok _ -> failwith "Attendu Error"

    [<Fact>]
    let ``readHeader renvoie Error sur geometrie invalide`` () =
        use s = makeStream ()
        let h = makeHeader ()
        Parallels.writeHeader s h

        let buf = Array.zeroCreate<byte> 4
        BinaryIo.putLe32 0 buf 0
        s.Position <- 16L
        s.Write(buf, 0, 4)
        s.Position <- 0L

        match Parallels.readHeader s with
        | Error _ -> ()
        | Ok _ -> failwith "Attendu Error"

    [<Fact>]
    let ``readHeader renvoie Error sur block size invalide`` () =
        use s = makeStream ()
        let h = makeHeader ()
        Parallels.writeHeader s h

        let buf = Array.zeroCreate<byte> 4
        BinaryIo.putLe32 128 buf 0
        s.Position <- 36L
        s.Write(buf, 0, 4)
        s.Position <- 0L

        match Parallels.readHeader s with
        | Error _ -> ()
        | Ok _ -> failwith "Attendu Error"

    [<Fact>]
    let ``virtualSize est le produit de la geometrie`` () =
        let h = makeHeader ()
        Parallels.virtualSize h |> should equal (16L * 16L * 63L * 512L)

    [<Fact>]
    let ``writeBytesAt puis readBytesAt realise un roundtrip`` () =
        use s = makeStream ()
        let h = makeHeader ()
        Parallels.writeHeader s h

        let data = [| 0x10uy; 0x20uy; 0x30uy; 0x40uy; 0x50uy |]

        Parallels.writeBytesAt s h 100L data 0 data.Length |> Result.defaultWith failwith

        let buf = Array.zeroCreate<byte> data.Length
        Parallels.readBytesAt s h 100L buf.Length buf 0 |> Result.defaultWith failwith

        buf |> should equal data

    [<Fact>]
    let ``readBytesAt lit des zeros sur bloc non alloue`` () =
        use s = makeStream ()
        let h = makeHeader ()
        Parallels.writeHeader s h

        let buf = Array.zeroCreate<byte> 8
        Parallels.readBytesAt s h 0L 8 buf 0 |> Result.defaultWith failwith

        buf |> should equal (Array.zeroCreate<byte> 8)

    [<Fact>]
    let ``readBytesAt ecrit des zeros au-dela de la taille virtuelle`` () =
        use s = makeStream ()
        let h = makeHeader ()
        Parallels.writeHeader s h

        let buf = Array.zeroCreate<byte> 8
        Parallels.readBytesAt s h (Parallels.virtualSize h + 100L) 8 buf 0 |> Result.defaultWith failwith

        buf |> should equal (Array.zeroCreate<byte> 8)

    [<Fact>]
    let ``writeBytesAt sur des blocs distincts ne s'ecrase pas`` () =
        use s = makeStream ()
        let h = makeHeader ()
        Parallels.writeHeader s h

        let a = [| 1uy; 2uy; 3uy; 4uy |]
        let b = [| 9uy; 8uy; 7uy; 6uy |]
        let offA = 20L
        let offB = 600L

        Parallels.writeBytesAt s h offA a 0 a.Length |> Result.defaultWith failwith
        Parallels.writeBytesAt s h offB b 0 b.Length |> Result.defaultWith failwith

        let bufA = Array.zeroCreate<byte> a.Length
        let bufB = Array.zeroCreate<byte> b.Length
        Parallels.readBytesAt s h offA bufA.Length bufA 0 |> Result.defaultWith failwith
        Parallels.readBytesAt s h offB bufB.Length bufB 0 |> Result.defaultWith failwith

        bufA |> should equal a
        bufB |> should equal b
