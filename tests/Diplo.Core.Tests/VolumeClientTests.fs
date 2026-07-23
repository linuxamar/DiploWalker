namespace Diplo.Core.Tests

module VolumeClientTests =

    open System
    open Xunit
    open FsUnit.Xunit
    open Grpc.Net.Client
    open Diplo.Core.Clients

    [<Fact>]
    let ``VolumeClient avec canal cree un client`` () =
        let options = GrpcChannelOptions()
        let channel = GrpcChannel.ForAddress("http://localhost:5002", options)
        use client = new VolumeClient(channel, false)
        client |> should not' (be Null)

    [<Fact>]
    let ``VolumeClient implemente IDisposable`` () =
        let options = GrpcChannelOptions()
        let channel = GrpcChannel.ForAddress("http://localhost:5002", options)
        let client = new VolumeClient(channel, false)
        (client :> IDisposable) |> should not' (be Null)
        (client :> IDisposable).Dispose()
        channel.Dispose()

    [<Fact>]
    let ``VolumeClient avec canal partage ne dispose pas le canal`` () =
        let options = GrpcChannelOptions()
        let channel = GrpcChannel.ForAddress("http://localhost:5002", options)
        let client = new VolumeClient(channel, false)
        (client :> IDisposable).Dispose()
        channel.Target |> should not' (be Null)
        channel.Dispose()
