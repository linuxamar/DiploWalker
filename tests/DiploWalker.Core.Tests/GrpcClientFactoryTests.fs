namespace DiploWalker.Core.Tests

module GrpcClientFactoryTests =

    open Xunit
    open FsUnit.Xunit
    open DiploWalker.Abstractions
    open DiploWalker.Core.Connection

    [<Fact>]
    let ``resolveAddress avec une adresse configuree cible cette adresse`` () =
        // M1 : les canaux sont mutualisés (cache statique) — ne pas les
        // disposer ici, sinon les tests suivants retrouvent un canal fermé.
        let channel = GrpcClientFactory.resolveAddress (Some "http://localhost:7777") DiploWalkerPorts.Container
        channel.Target |> should equal "localhost:7777"

    [<Fact>]
    let ``resolveAddress sans configuration cible le port par defaut du service`` () =
        let conteneurs = GrpcClientFactory.resolveAddress None DiploWalkerPorts.Container
        conteneurs.Target |> should equal (sprintf "localhost:%d" DiploWalkerPorts.Container)

        let volumes = GrpcClientFactory.resolveAddress None DiploWalkerPorts.Volume
        volumes.Target |> should equal (sprintf "localhost:%d" DiploWalkerPorts.Volume)

        let reseaux = GrpcClientFactory.resolveAddress None DiploWalkerPorts.Network
        reseaux.Target |> should equal (sprintf "localhost:%d" DiploWalkerPorts.Network)

    [<Fact>]
    let ``resolveAddress rejette un hote distant`` () =
        (fun () ->
            GrpcClientFactory.resolveAddress (Some "http://machine-distante:7777") DiploWalkerPorts.Container
            |> ignore)
        |> should throw typeof<Grpc.Core.RpcException>

    [<Fact>]
    let ``resolveAddress rejette une adresse vide`` () =
        (fun () -> GrpcClientFactory.resolveAddress (Some "") DiploWalkerPorts.Container |> ignore)
        |> should throw typeof<Grpc.Core.RpcException>

