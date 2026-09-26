namespace DiploWalker.Disk.Tests

module BinaryIoTests =

    open System
    open System.IO
    open Xunit
    open FsUnit.Xunit
    open DiploWalker.Disk

    // ── Big-endian read ────────────────────────────────────────────────

    [<Fact>]
    let ``be16 lit un entier 16 bits big-endian`` () =
        let data = [| 0x01uy; 0x02uy |]
        BinaryIo.be16 data 0 |> should equal 0x0102

    [<Fact>]
    let ``be32 lit un entier 32 bits big-endian`` () =
        let data = [| 0x01uy; 0x02uy; 0x03uy; 0x04uy |]
        BinaryIo.be32 data 0 |> should equal 0x01020304

    [<Fact>]
    let ``be64 lit un entier 64 bits big-endian`` () =
        let data = [| 0x01uy; 0x02uy; 0x03uy; 0x04uy; 0x05uy; 0x06uy; 0x07uy; 0x08uy |]
        BinaryIo.be64 data 0 |> should equal 0x0102030405060708L

    [<Fact>]
    let ``be32 lit avec un offset`` () =
        let data = [| 0x00uy; 0x00uy; 0x01uy; 0x02uy; 0x03uy; 0x04uy |]
        BinaryIo.be32 data 2 |> should equal 0x01020304

    [<Fact>]
    let ``be16 lit zero`` () =
        let data = [| 0x00uy; 0x00uy |]
        BinaryIo.be16 data 0 |> should equal 0

    [<Fact>]
    let ``be32 lit valeur maximale`` () =
        let data = [| 0xFFuy; 0xFFuy; 0xFFuy; 0xFFuy |]
        BinaryIo.be32 data 0 |> should equal -1

    // ── Big-endian write ───────────────────────────────────────────────

    [<Fact>]
    let ``putBe16 ecrit un entier 16 bits big-endian`` () =
        let data = Array.zeroCreate<byte> 2
        BinaryIo.putBe16 0x0102 data 0
        data.[0] |> should equal 0x01uy
        data.[1] |> should equal 0x02uy

    [<Fact>]
    let ``putBe32 ecrit un entier 32 bits big-endian`` () =
        let data = Array.zeroCreate<byte> 4
        BinaryIo.putBe32 0x01020304 data 0
        data.[0] |> should equal 0x01uy
        data.[3] |> should equal 0x04uy

    [<Fact>]
    let ``putBe64 ecrit un entier 64 bits big-endian`` () =
        let data = Array.zeroCreate<byte> 8
        BinaryIo.putBe64 0x0102030405060708L data 0
        data.[0] |> should equal 0x01uy
        data.[7] |> should equal 0x08uy

    // ── Little-endian read ─────────────────────────────────────────────

    [<Fact>]
    let ``le16 lit un entier 16 bits little-endian`` () =
        let data = [| 0x02uy; 0x01uy |]
        BinaryIo.le16 data 0 |> should equal 0x0102

    [<Fact>]
    let ``le32 lit un entier 32 bits little-endian`` () =
        let data = [| 0x04uy; 0x03uy; 0x02uy; 0x01uy |]
        BinaryIo.le32 data 0 |> should equal 0x01020304

    [<Fact>]
    let ``le64 lit un entier 64 bits little-endian`` () =
        let data = [| 0x08uy; 0x07uy; 0x06uy; 0x05uy; 0x04uy; 0x03uy; 0x02uy; 0x01uy |]
        BinaryIo.le64 data 0 |> should equal 0x0102030405060708L

    [<Fact>]
    let ``le32 lit avec un offset`` () =
        let data = [| 0x00uy; 0x00uy; 0x04uy; 0x03uy; 0x02uy; 0x01uy |]
        BinaryIo.le32 data 2 |> should equal 0x01020304

    [<Fact>]
    let ``le16 lit zero`` () =
        let data = [| 0x00uy; 0x00uy |]
        BinaryIo.le16 data 0 |> should equal 0

    // ── Little-endian write ────────────────────────────────────────────

    [<Fact>]
    let ``putLe16 ecrit un entier 16 bits little-endian`` () =
        let data = Array.zeroCreate<byte> 2
        BinaryIo.putLe16 0x0102 data 0
        data.[0] |> should equal 0x02uy
        data.[1] |> should equal 0x01uy

    [<Fact>]
    let ``putLe32 ecrit un entier 32 bits little-endian`` () =
        let data = Array.zeroCreate<byte> 4
        BinaryIo.putLe32 0x01020304 data 0
        data.[0] |> should equal 0x04uy
        data.[3] |> should equal 0x01uy

    [<Fact>]
    let ``putLe64 ecrit un entier 64 bits little-endian`` () =
        let data = Array.zeroCreate<byte> 8
        BinaryIo.putLe64 0x0102030405060708L data 0
        data.[0] |> should equal 0x08uy
        data.[7] |> should equal 0x01uy

    // ── Roundtrip ──────────────────────────────────────────────────────

    [<Fact>]
    let ``be16/putBe16 roundtrip`` () =
        let data = Array.zeroCreate<byte> 2
        BinaryIo.putBe16 12345 data 0
        BinaryIo.be16 data 0 |> should equal 12345

    [<Fact>]
    let ``be32/putBe32 roundtrip`` () =
        let data = Array.zeroCreate<byte> 4
        BinaryIo.putBe32 1234567890 data 0
        BinaryIo.be32 data 0 |> should equal 1234567890

    [<Fact>]
    let ``be64/putBe64 roundtrip`` () =
        let data = Array.zeroCreate<byte> 8
        BinaryIo.putBe64 123456789012345L data 0
        BinaryIo.be64 data 0 |> should equal 123456789012345L

    [<Fact>]
    let ``le16/putLe16 roundtrip`` () =
        let data = Array.zeroCreate<byte> 2
        BinaryIo.putLe16 12345 data 0
        BinaryIo.le16 data 0 |> should equal 12345

    [<Fact>]
    let ``le32/putLe32 roundtrip`` () =
        let data = Array.zeroCreate<byte> 4
        BinaryIo.putLe32 1234567890 data 0
        BinaryIo.le32 data 0 |> should equal 1234567890

    [<Fact>]
    let ``le64/putLe64 roundtrip`` () =
        let data = Array.zeroCreate<byte> 8
        BinaryIo.putLe64 123456789012345L data 0
        BinaryIo.le64 data 0 |> should equal 123456789012345L

    [<Fact>]
    let ``putBe puis le retourne la meme valeur`` () =
        let be = Array.zeroCreate<byte> 8
        let le = Array.zeroCreate<byte> 8
        BinaryIo.putBe64 0x0102030405060708L be 0
        BinaryIo.putLe64 0x0102030405060708L le 0
        BinaryIo.be64 be 0 |> should equal (BinaryIo.le64 le 0)

    // ── readFully ──────────────────────────────────────────────────────

    [<Fact>]
    let ``readFully lit exactement len octets`` () =
        use stream = new MemoryStream [| 1uy; 2uy; 3uy; 4uy; 5uy |]
        let buf = Array.zeroCreate<byte> 3
        BinaryIo.readFully stream buf 0 3
        buf.[0] |> should equal 1uy
        buf.[2] |> should equal 3uy

    [<Fact>]
    let ``readFully avec offset dans le buffer`` () =
        use stream = new MemoryStream [| 10uy; 20uy; 30uy |]
        let buf = Array.zeroCreate<byte> 5
        BinaryIo.readFully stream buf 2 3
        buf.[0] |> should equal 0uy
        buf.[2] |> should equal 10uy
        buf.[4] |> should equal 30uy

    [<Fact>]
    let ``readFully leve une exception si le flux est tronque`` () =
        use stream = new MemoryStream [| 1uy; 2uy |]
        let buf = Array.zeroCreate<byte> 5

        (fun () -> BinaryIo.readFully stream buf 0 5 |> ignore)
        |> should throw typeof<System.Exception>

    [<Fact>]
    let ``readFully leve une exception si len est negatif`` () =
        use stream = new MemoryStream [| 1uy; 2uy |]
        let buf = Array.zeroCreate<byte> 5

        (fun () -> BinaryIo.readFully stream buf 0 -1 |> ignore)
        |> should throw typeof<System.ArgumentException>

    [<Fact>]
    let ``readFully avec len zero ne lit rien`` () =
        use stream = new MemoryStream [| 1uy; 2uy; 3uy |]
        let buf = Array.zeroCreate<byte> 5
        BinaryIo.readFully stream buf 0 0
        buf |> should equal (Array.zeroCreate<byte> 5)

    // ── protect ────────────────────────────────────────────────────────

    [<Fact>]
    let ``protect retourne Ok quand pas d'exception`` () =
        match BinaryIo.protect (fun () -> 42) with
        | Ok v -> v |> should equal 42
        | Error _ -> failwith "Attendu Ok"

    [<Fact>]
    let ``protect retourne Error quand exception`` () =
        match BinaryIo.protect (fun () -> failwith "boom") with
        | Error msg -> msg |> should equal "boom"
        | Ok _ -> failwith "Attendu Error"

