namespace Diplo.Core.Tests

module ContainerClientTests =

    open System
    open Xunit
    open FsUnit.Xunit
    open Grpc.Net.Client
    open Diplo.Core.Clients

    [<Fact>]
    let ``ContainerClient avec canal cree un client`` () =
        let options = GrpcChannelOptions()
        let channel = GrpcChannel.ForAddress("http://localhost:5001", options)
        use client = new ContainerClient(channel, false)
        client |> should not' (be Null)

    [<Fact>]
    let ``ContainerClient implemente IDisposable`` () =
        let options = GrpcChannelOptions()
        let channel = GrpcChannel.ForAddress("http://localhost:5001", options)
        let client = new ContainerClient(channel, false)
        (client :> IDisposable) |> should not' (be Null)
        (client :> IDisposable).Dispose()
        channel.Dispose()

    [<Fact>]
    let ``ContainerClient avec canal partage ne dispose pas le canal`` () =
        let options = GrpcChannelOptions()
        let channel = GrpcChannel.ForAddress("http://localhost:5001", options)
        let client = new ContainerClient(channel, false)
        (client :> IDisposable).Dispose()
        // Le canal ne doit pas être disposé — pas d'exception en accédant au canal après dispose
        channel.Target |> should not' (be Null)
        channel.Dispose()
