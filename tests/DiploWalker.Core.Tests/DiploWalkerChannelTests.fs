namespace DiploWalker.Core.Tests

module DiploWalkerChannelTests =

    open System
    open Xunit
    open FsUnit.Xunit
    open DiploWalker.Abstractions
    open DiploWalker.Core.Connection

    [<Fact>]
    let ``forContainer valide l'adresse localhost par defaut`` () =
        SecurityValidation.validateGrpcAddress "http://localhost:5001"

    [<Fact>]
    let ``forVolume valide l'adresse localhost par defaut`` () =
        SecurityValidation.validateGrpcAddress "http://localhost:5002"

    [<Fact>]
    let ``forNetwork valide l'adresse localhost par defaut`` () =
        SecurityValidation.validateGrpcAddress "http://localhost:5003"

    [<Fact>]
    let ``forContainer avec port personnalise est valide`` () =
        SecurityValidation.validateGrpcAddress "http://localhost:9999"

    [<Fact>]
    let ``forAddress avec adresse localhost est valide`` () =
        SecurityValidation.validateGrpcAddress "http://localhost:8080"

    [<Fact>]
    let ``forAddress avec 127.0.0.1 est valide`` () =
        SecurityValidation.validateGrpcAddress "http://127.0.0.1:3000"

    [<Fact>]
    let ``forContainer cree un canal TCP`` () =
        use channel = DiploWalkerChannel.forContainer 5001
        channel.Target |> should equal "localhost:5001"

    [<Fact>]
    let ``forVolume cree un canal TCP`` () =
        use channel = DiploWalkerChannel.forVolume 5002
        channel.Target |> should equal "localhost:5002"

    [<Fact>]
    let ``forNetwork cree un canal TCP`` () =
        use channel = DiploWalkerChannel.forNetwork 5003
        channel.Target |> should equal "localhost:5003"

    [<Fact>]
    let ``forAddress avec adresse non autorisee lance une exception`` () =
        (fun () -> SecurityValidation.validateGrpcAddress "http://remote-server:5000" |> ignore)
        |> should throw typeof<Exception>

    [<Fact>]
    let ``forAddress avec scheme ftp non supporte`` () =
        (fun () -> SecurityValidation.validateGrpcAddress "ftp://localhost:5000" |> ignore)
        |> should throw typeof<Exception>

    [<Fact>]
    let ``forAddress avec adresse vide lance une exception`` () =
        (fun () -> SecurityValidation.validateGrpcAddress "" |> ignore)
        |> should throw typeof<Exception>

    // ── Adresses par named pipe ─────────────────────────────────────

    [<Fact>]
    let ``forAddress avec adresse pipe valide ne lève pas`` () =
        DiploWalkerChannel.forAddress "http://pipe:/diplo-container"

    [<Fact>]
    let ``forAddress avec nom de pipe vide lance une exception`` () =
        (fun () -> DiploWalkerChannel.forAddress "http://pipe:/" |> ignore)
        |> should throw typeof<Exception>

    [<Fact>]
    let ``forAddress avec nom de pipe contenant backslash lance une exception`` () =
        (fun () -> DiploWalkerChannel.forAddress @"http://pipe:/diplo\evil" |> ignore)
        |> should throw typeof<Exception>


