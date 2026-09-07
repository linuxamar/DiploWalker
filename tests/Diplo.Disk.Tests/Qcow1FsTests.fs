namespace Diplo.Disk.Tests

module Qcow1FsTests =

    open System
    open System.IO
    open Xunit
    open FsUnit.Xunit
    open Diplo.Disk

    let private makeHeader () : Qcow1.Header =
        { Version = 1
          ClusterBits = 12
          ClusterSize = 4096L
          VirtualSize = 0x1_0000_0000L
          L1Size = 2
          L1TableOffset = 4096L
          CryptMethod = 0 }

    let private makeStream () =
        new MemoryStream(Array.zeroCreate<byte> 16_384)

    [<Fact>]
    let ``writeHeader puis readHeader conserve les valeurs`` () =
        use s = makeStream ()
        let h = makeHeader ()
        Qcow1.writeHeader s h

        match Qcow1.readHeader s with
        | Ok read ->
            read.Version |> should equal 1
            read.ClusterBits |> should equal 12
            read.ClusterSize |> should equal 4096L
            read.VirtualSize |> should equal 0x1_0000_0000L
            read.L1Size |> should equal 2
            read.L1TableOffset |> should equal 4096L
            read.CryptMethod |> should equal 0
        | Error msg -> failwith msg

    [<Fact>]
    let ``readHeader renvoie Error sur magic invalide`` () =
        use s = new MemoryStream(Array.zeroCreate<byte> 72)
        s.Write([| 1uy; 2uy; 3uy; 4uy |], 0, 4)
        s.Position <- 0L

        match Qcow1.readHeader s with
        | Error _ -> ()
        | Ok _ -> failwith "Attendu Error"

    [<Fact>]
    let ``readHeader renvoie Error sur version non supportee`` () =
        use s = makeStream ()
        let h = makeHeader ()
        Qcow1.writeHeader s h
        let buf = Array.zeroCreate<byte> 4
        BinaryIo.putBe32 2 buf 0
        s.Position <- 4L
        s.Write(buf, 0, 4)
        s.Position <- 0L

        match Qcow1.readHeader s with
        | Error _ -> ()
        | Ok _ -> failwith "Attendu Error"

    [<Fact>]
    let ``writeBytesAt puis readBytesAt realise un roundtrip`` () =
        use s = makeStream ()
        let h = makeHeader ()
        Qcow1.writeHeader s h

        let data = [| 0xAAuy; 0xBBuy; 0xCCuy; 0xDDuy; 0x11uy; 0x22uy |]

        Qcow1.writeBytesAt s h 100L data 0 data.Length |> Result.defaultWith failwith

        let buf = Array.zeroCreate<byte> data.Length
        Qcow1.readBytesAt s h 100L buf.Length buf 0 |> Result.defaultWith failwith

        buf |> should equal data

    [<Fact>]
    let ``readBytesAt lit des zeros sur zone non ecrite`` () =
        use s = makeStream ()
        let h = makeHeader ()
        Qcow1.writeHeader s h

        let buf = Array.zeroCreate<byte> 8
        Qcow1.readBytesAt s h 0L 8 buf 0 |> Result.defaultWith failwith

        buf |> should equal (Array.zeroCreate<byte> 8)

    [<Fact>]
    let ``readBytesAt ecrit des zeros au-dela de la taille virtuelle`` () =
        use s = makeStream ()
        let h = makeHeader ()
        Qcow1.writeHeader s h

        let buf = Array.zeroCreate<byte> 8
        Qcow1.readBytesAt s h (h.VirtualSize + 100L) 8 buf 0 |> Result.defaultWith failwith

        buf |> should equal (Array.zeroCreate<byte> 8)

    [<Fact>]
    let ``readHeader rejette un VirtualSize dont les 32 bits bas sont non nuls`` () =
        // Bug documente du module : writeHeader ecrit VirtualSize (putBe64) a
        // l'offset 28, ce qui chevauche le champ CryptMethod relu a be32 offset 32
        // (readHeader lit CryptMethod = VirtualSize &&& 0xFFFFFFFF). Un VirtualSize
        // non nul dans ses 32 bits bas fait donc echouer le re-lecture du header.
        use s = makeStream ()
        let h = { makeHeader () with VirtualSize = 1_048_576L }
        Qcow1.writeHeader s h

        match Qcow1.readHeader s with
        | Error _ -> ()
        | Ok _ -> failwith "Attendu Error (CryptMethod corrompu par VirtualSize)"

    [<Fact>]
    let ``readHeader renvoie Error si cluster bits hors bornes`` () =
        use s = makeStream ()
        let h = makeHeader ()
        Qcow1.writeHeader s h

        let buf = Array.zeroCreate<byte> 4
        BinaryIo.putBe32 30 buf 0
        s.Position <- 20L
        s.Write(buf, 0, 4)
        s.Position <- 0L

        match Qcow1.readHeader s with
        | Error _ -> ()
        | Ok _ -> failwith "Attendu Error"

    [<Fact>]
    let ``writeBytesAt sur des clusters distincts ne s'ecrase pas`` () =
        use s = makeStream ()
        let h = makeHeader ()
        Qcow1.writeHeader s h

        let a = [| 1uy; 2uy; 3uy |]
        let b = [| 9uy; 8uy; 7uy |]

        let offA = 100L
        let offB = 5000L

        Qcow1.writeBytesAt s h offA a 0 a.Length |> Result.defaultWith failwith
        Qcow1.writeBytesAt s h offB b 0 b.Length |> Result.defaultWith failwith

        let bufA = Array.zeroCreate<byte> a.Length
        let bufB = Array.zeroCreate<byte> b.Length
        Qcow1.readBytesAt s h offA bufA.Length bufA 0 |> Result.defaultWith failwith
        Qcow1.readBytesAt s h offB bufB.Length bufB 0 |> Result.defaultWith failwith

        bufA |> should equal a
        bufB |> should equal b
