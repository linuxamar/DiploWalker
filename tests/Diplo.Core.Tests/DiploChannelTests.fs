namespace Diplo.Core.Tests

module DiploChannelTests =

    open System
    open Xunit
    open FsUnit.Xunit
    open Diplo.Abstractions
    open Diplo.Core.Connection

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
        DiploChannel.forAddress "http://pipe:/diplo-container"

    [<Fact>]
    let ``forAddress avec nom de pipe vide lance une exception`` () =
        (fun () -> DiploChannel.forAddress "http://pipe:/" |> ignore)
        |> should throw typeof<Exception>

    [<Fact>]
    let ``forAddress avec nom de pipe contenant backslash lance une exception`` () =
        (fun () -> DiploChannel.forAddress @"http://pipe:/diplo\evil" |> ignore)
        |> should throw typeof<Exception>
