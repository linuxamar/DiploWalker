namespace Diplo.Core.Tests

module GrpcClientFactoryTests =

    open Xunit
    open FsUnit.Xunit
    open Diplo.Abstractions
    open Diplo.Core.Connection

    [<Fact>]
    let ``resolveAddress avec une adresse configuree cible cette adresse`` () =
        use channel = GrpcClientFactory.resolveAddress (Some "http://localhost:7777") DiploPorts.Container
        channel.Target |> should equal "localhost:7777"

    [<Fact>]
    let ``resolveAddress sans configuration cible le port par defaut du service`` () =
        use conteneurs = GrpcClientFactory.resolveAddress None DiploPorts.Container
        conteneurs.Target |> should equal (sprintf "localhost:%d" DiploPorts.Container)

        use volumes = GrpcClientFactory.resolveAddress None DiploPorts.Volume
        volumes.Target |> should equal (sprintf "localhost:%d" DiploPorts.Volume)

        use reseaux = GrpcClientFactory.resolveAddress None DiploPorts.Network
        reseaux.Target |> should equal (sprintf "localhost:%d" DiploPorts.Network)

    [<Fact>]
    let ``resolveAddress rejette un hote distant`` () =
        (fun () ->
            GrpcClientFactory.resolveAddress (Some "http://machine-distante:7777") DiploPorts.Container
            |> ignore)
        |> should throw typeof<Grpc.Core.RpcException>

    [<Fact>]
    let ``resolveAddress rejette une adresse vide`` () =
        (fun () -> GrpcClientFactory.resolveAddress (Some "") DiploPorts.Container |> ignore)
        |> should throw typeof<Grpc.Core.RpcException>