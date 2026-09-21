namespace DiploWalker.Disk.Tests

module IsoSourceTests =

    open System
    open System.IO
    open Xunit
    open FsUnit.Xunit
    open DiploWalker.Disk

    // â”€â”€ IsoSource.fromBytes â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``fromBytes retourne la bonne longueur`` () =
        let data = Array.create 1024 0xABuy
        let src = IsoSource.fromBytes data
        src.Length |> should equal 1024L

    [<Fact>]
    let ``fromBytes lit les bons octets`` () =
        let data = [| 0xDEuy; 0xADuy; 0xBEuy; 0xEFuy |]
        let src = IsoSource.fromBytes data
        let result = src.ReadBytes 0L 4
        result |> should equal data

    [<Fact>]
    let ``fromBytes lit avec un offset`` () =
        let data = [| 0x00uy; 0x01uy; 0x02uy; 0x03uy; 0x04uy |]
        let src = IsoSource.fromBytes data
        let result = src.ReadBytes 2L 2
        result |> should equal [| 0x02uy; 0x03uy |]

    [<Fact>]
    let ``fromBytes avec count depassant la fin tronque`` () =
        let data = [| 0x01uy; 0x02uy; 0x03uy |]
        let src = IsoSource.fromBytes data
        let result = src.ReadBytes 1L 100
        result.Length |> should equal 2
        result |> should equal [| 0x02uy; 0x03uy |]

    [<Fact>]
    let ``fromBytes avec offset negatif est clampe a zero`` () =
        let data = [| 0x01uy; 0x02uy; 0x03uy |]
        let src = IsoSource.fromBytes data
        let result = src.ReadBytes -10L 2
        result |> should equal data.[0..1]

    [<Fact>]
    let ``fromBytes avec count zero retourne tableau vide`` () =
        let data = [| 0x01uy; 0x02uy; 0x03uy |]
        let src = IsoSource.fromBytes data
        let result = src.ReadBytes 0L 0
        result.Length |> should equal 0

    [<Fact>]
    let ``fromBytes avec offset apres la fin retourne tableau vide`` () =
        let data = [| 0x01uy; 0x02uy; 0x03uy |]
        let src = IsoSource.fromBytes data
        let result = src.ReadBytes 100L 10
        result.Length |> should equal 0

    // â”€â”€ IsoSource.fromStream â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``fromStream retourne la bonne longueur`` () =
        let data = Array.create 2048 0xCDuy
        use stream = new MemoryStream(data)
        let src = IsoSource.fromStream stream
        src.Length |> should equal 2048L

    [<Fact>]
    let ``fromStream lit les bons octets`` () =
        let data = [| 0xAAuy; 0xBBuy; 0xCCuy; 0xDDuy |]
        use stream = new MemoryStream(data)
        let src = IsoSource.fromStream stream
        let result = src.ReadBytes 0L 4
        result |> should equal data

    [<Fact>]
    let ``fromStream lit avec un offset`` () =
        let data = [| 0x00uy; 0x10uy; 0x20uy; 0x30uy |]
        use stream = new MemoryStream(data)
        let src = IsoSource.fromStream stream
        let result = src.ReadBytes 1L 2
        result |> should equal [| 0x10uy; 0x20uy |]

    [<Fact>]
    let ``fromStream avec count depassant la fin tronque`` () =
        let data = [| 0x01uy; 0x02uy; 0x03uy |]
        use stream = new MemoryStream(data)
        let src = IsoSource.fromStream stream
        let result = src.ReadBytes 1L 100
        result.Length |> should equal 2

    [<Fact>]
    let ``fromStream avec count zero retourne tableau vide`` () =
        let data = [| 0x01uy; 0x02uy; 0x03uy |]
        use stream = new MemoryStream(data)
        let src = IsoSource.fromStream stream
        let result = src.ReadBytes 0L 0
        result.Length |> should equal 0

    [<Fact>]
    let ``fromStream avec offset apres la fin retourne tableau vide`` () =
        let data = [| 0x01uy; 0x02uy; 0x03uy |]
        use stream = new MemoryStream(data)
        let src = IsoSource.fromStream stream
        let result = src.ReadBytes 100L 10
        result.Length |> should equal 0

    // â”€â”€ IsoSource.sanitizeName â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``sanitizeName remplace les caracteres interdits`` () =
        IsoSource.sanitizeName "a/b\\c:d*e?f\"g<h>i|j"
        |> should equal "a_b_c_d_e_f_g_h_i_j"

    [<Fact>]
    let ``sanitizeName conserve les noms propres`` () =
        IsoSource.sanitizeName "HELLO.TXT" |> should equal "HELLO.TXT"

    [<Fact>]
    let ``sanitizeName remplace les controles`` () =
        IsoSource.sanitizeName "a\x01b\x02c" |> should equal "a_b_c"

    [<Fact>]
    let ``sanitizeName gere les underscores deja presents`` () =
        IsoSource.sanitizeName "a_b_c" |> should equal "a_b_c"

