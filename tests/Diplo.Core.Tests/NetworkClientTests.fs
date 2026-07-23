namespace Diplo.Core.Tests

module NetworkClientTests =

    open System
    open Xunit
    open FsUnit.Xunit
    open Grpc.Net.Client
    open Diplo.Core.Clients

    [<Fact>]
    let ``NetworkClient avec canal cree un client`` () =
        let options = GrpcChannelOptions()
        let channel = GrpcChannel.ForAddress("http://localhost:5003", options)
        use client = new NetworkClient(channel, false)
        client |> should not' (be Null)

    [<Fact>]
    let ``NetworkClient implemente IDisposable`` () =
        let options = GrpcChannelOptions()
        let channel = GrpcChannel.ForAddress("http://localhost:5003", options)
        let client = new NetworkClient(channel, false)
        (client :> IDisposable) |> should not' (be Null)
        (client :> IDisposable).Dispose()
        channel.Dispose()

    [<Fact>]
    let ``NetworkClient avec canal partage ne dispose pas le canal`` () =
        let options = GrpcChannelOptions()
        let channel = GrpcChannel.ForAddress("http://localhost:5003", options)
        let client = new NetworkClient(channel, false)
        (client :> IDisposable).Dispose()
        channel.Target |> should not' (be Null)
        channel.Dispose()
