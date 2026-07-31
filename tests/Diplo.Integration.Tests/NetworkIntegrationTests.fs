namespace Diplo.Integration.Tests

module NetworkIntegrationTests =

    open System
    open System.Collections.Generic
    open System.Threading
    open Xunit
    open FsUnit.Xunit
    open Microsoft.AspNetCore.Builder
    open Microsoft.AspNetCore.Hosting
    open Microsoft.AspNetCore.Server.Kestrel.Core
    open Microsoft.Extensions.DependencyInjection
    open Grpc.Net.Client
    open ProtoBuf.Grpc.Client
    open ProtoBuf.Grpc.Server
    open Diplo.Grpc
    open Diplo.Grpc.Network
    open Diplo.Network.Plugins
    open Diplo.Network.Services

    type private MockNetworkDriver() =

        let mutable networks = Map.empty<string, NetworkDriverInfo>
        let mutable endpoints = Map.empty<string, EndpointInfo>

        interface INetworkDriver with
            member _.DriverType = NetworkDriver.Bridge

            member _.Create(name, subnet, gateway, ipRange, _options, _labels) =
                let id = Guid.NewGuid().ToString("N")
                let info = { Id = id; Name = name; Driver = NetworkDriver.Bridge; Subnet = subnet; Gateway = gateway; Options = Map.empty; Labels = Map.empty; CreatedAt = DateTime.UtcNow.ToString("o") }
                networks <- networks |> Map.add id info
                Ok info

            member _.Remove(id, _force) =
                if networks |> Map.containsKey id then
                    networks <- networks |> Map.remove id
                    Ok ()
                else Error "introuvable"

            member _.Inspect(id) =
                match networks |> Map.tryFind id with
                | Some info -> Ok info
                | None -> Error "introuvable"

            member _.List() =
                networks |> Map.toList |> List.map snd |> Ok

            member _.Connect(networkId, containerId, endpointId, ipv4Address, _options) =
                if networks |> Map.containsKey networkId |> not then
                    Error "réseau introuvable"
                else
                    let ep = { EndpointId = endpointId; Ipv4Address = ipv4Address |> Option.defaultValue "172.17.0.2"; MacAddress = "02:42:ac:11:00:02"; Message = sprintf "Connecté à %s" containerId }
                    endpoints <- endpoints |> Map.add endpointId ep
                    Ok ep

            member _.Disconnect(networkId, _containerId, endpointId, _force) =
                if networks |> Map.containsKey networkId |> not then
                    Error "réseau introuvable"
                else
                    endpoints <- endpoints |> Map.remove endpointId
                    Ok ()

            member _.Prune() =
                let ids = networks |> Map.toList |> List.map fst
                networks <- Map.empty
                Ok ids

    let private createChannel (address: string) =
        GrpcChannel.ForAddress(address, GrpcChannelOptions())

    let private startApp () =
        let mock = MockNetworkDriver()
        let drivers = Dictionary<NetworkDriver, INetworkDriver>()
        drivers.[NetworkDriver.Bridge] <- mock
        drivers.[NetworkDriver.``None``] <- NoneDriver() :> INetworkDriver
        let builder = WebApplication.CreateBuilder()
        builder.Services.AddCodeFirstGrpc() |> ignore
        builder.Services.AddSingleton<IReadOnlyDictionary<NetworkDriver, INetworkDriver>>(drivers) |> ignore
        builder.Services.AddSingleton<NetworkServiceImpl>() |> ignore
        builder.WebHost.ConfigureKestrel(fun opts ->
            opts.Listen(System.Net.IPAddress.Loopback, 0, fun lo ->
                lo.Protocols <- HttpProtocols.Http2)) |> ignore
        let app = builder.Build()
        app.MapGrpcService<NetworkServiceImpl>() |> ignore
        app.StartAsync().GetAwaiter().GetResult()
        let address = app.Urls |> Seq.head
        app, address

    let private stopApp (app: WebApplication) =
        app.StopAsync().GetAwaiter().GetResult()
        (app :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()

    [<Fact>]
    let ``CreateNetwork via gRPC retourne les informations`` () =
        let app, address = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<INetworkService>()
            let req: CreateNetworkRequest =
                { Name = "net-grpc"; Driver = NetworkDriver.Bridge
                  Subnet = "10.0.0.0/24"; Gateway = "10.0.0.1"; IpRange = ""
                  Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
            let result = client.CreateNetwork(req, CancellationToken.None).Result
            String.IsNullOrEmpty(result.Id) |> should equal false
            result.Name |> should equal "net-grpc"
            result.Driver |> should equal NetworkDriver.Bridge
            result.Subnet |> should equal "10.0.0.0/24"
            result.Gateway |> should equal "10.0.0.1"
        finally
            stopApp app

    [<Fact>]
    let ``CreateNetwork sans nom genere un id automatiquement`` () =
        let app, address = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<INetworkService>()
            let req: CreateNetworkRequest =
                { Name = ""; Driver = NetworkDriver.Bridge
                  Subnet = ""; Gateway = ""; IpRange = ""
                  Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
            let result = client.CreateNetwork(req, CancellationToken.None).Result
            String.IsNullOrEmpty(result.Id) |> should equal false
            result.Id.Length |> should equal 32
        finally
            stopApp app

    [<Fact>]
    let ``CreateNetwork puis RemoveNetwork via gRPC`` () =
        let app, address = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<INetworkService>()
            let createReq: CreateNetworkRequest =
                { Name = "to-delete-net"; Driver = NetworkDriver.Bridge
                  Subnet = ""; Gateway = ""; IpRange = ""
                  Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
            let createResult = client.CreateNetwork(createReq, CancellationToken.None).Result
            let removeReq: RemoveNetworkRequest = { Id = createResult.Id; Force = false }
            let removeResult = client.RemoveNetwork(removeReq, CancellationToken.None).Result
            removeResult.Success |> should equal true
            removeResult.Message |> should equal "Réseau supprimé"
        finally
            stopApp app

    [<Fact>]
    let ``InspectNetwork via gRPC`` () =
        let app, address = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<INetworkService>()
            let createReq: CreateNetworkRequest =
                { Name = "net-inspect"; Driver = NetworkDriver.Bridge
                  Subnet = "192.168.1.0/24"; Gateway = "192.168.1.1"; IpRange = ""
                  Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
            let createResult = client.CreateNetwork(createReq, CancellationToken.None).Result
            let inspectReq: InspectNetworkRequest = { Id = createResult.Id }
            let inspectResult = client.InspectNetwork(inspectReq, CancellationToken.None).Result
            inspectResult.Id |> should equal createResult.Id
            inspectResult.Name |> should equal "net-inspect"
            inspectResult.Subnet |> should equal "192.168.1.0/24"
            inspectResult.Gateway |> should equal "192.168.1.1"
        finally
            stopApp app

    [<Fact>]
    let ``ListNetworks via gRPC`` () =
        let app, address = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<INetworkService>()
            let req1: CreateNetworkRequest =
                { Name = "net-list-1"; Driver = NetworkDriver.Bridge
                  Subnet = ""; Gateway = ""; IpRange = ""
                  Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
            let req2: CreateNetworkRequest =
                { Name = "net-list-2"; Driver = NetworkDriver.Bridge
                  Subnet = ""; Gateway = ""; IpRange = ""
                  Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
            client.CreateNetwork(req1, CancellationToken.None).Result |> ignore
            client.CreateNetwork(req2, CancellationToken.None).Result |> ignore
            let listReq: ListNetworksRequest = { Filters = Dictionary() }
            let listResult = client.ListNetworks(listReq, CancellationToken.None).Result
            listResult.Networks.Count |> should equal 2
            listResult.Networks |> Seq.exists (fun n -> n.Name = "net-list-1") |> should equal true
            listResult.Networks |> Seq.exists (fun n -> n.Name = "net-list-2") |> should equal true
        finally
            stopApp app

    [<Fact>]
    let ``ConnectContainer puis DisconnectContainer via gRPC`` () =
        let app, address = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<INetworkService>()
            let createReq: CreateNetworkRequest =
                { Name = "net-connect"; Driver = NetworkDriver.Bridge
                  Subnet = ""; Gateway = ""; IpRange = ""
                  Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
            let netResult = client.CreateNetwork(createReq, CancellationToken.None).Result
            let connectReq: ConnectContainerRequest =
                { NetworkId = netResult.Id; ContainerId = "ct-grpc"
                  EndpointId = "ep-grpc"; Ipv4Address = "10.0.0.5"
                  Options = Dictionary() }
            let connectResult = client.ConnectContainer(connectReq, CancellationToken.None).Result
            connectResult.EndpointId |> should equal "ep-grpc"
            connectResult.Ipv4Address |> should equal "10.0.0.5"
            let disconnectReq: DisconnectContainerRequest =
                { NetworkId = netResult.Id; ContainerId = "ct-grpc"
                  EndpointId = "ep-grpc"; Force = false }
            let disconnectResult = client.DisconnectContainer(disconnectReq, CancellationToken.None).Result
            disconnectResult.Success |> should equal true
        finally
            stopApp app

    [<Fact>]
    let ``PruneNetworks via gRPC`` () =
        let app, address = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<INetworkService>()
            let req1: CreateNetworkRequest =
                { Name = "net-prune-1"; Driver = NetworkDriver.Bridge
                  Subnet = ""; Gateway = ""; IpRange = ""
                  Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
            let req2: CreateNetworkRequest =
                { Name = "net-prune-2"; Driver = NetworkDriver.Bridge
                  Subnet = ""; Gateway = ""; IpRange = ""
                  Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
            client.CreateNetwork(req1, CancellationToken.None).Result |> ignore
            client.CreateNetwork(req2, CancellationToken.None).Result |> ignore
            let pruneReq: PruneNetworksRequest = { Placeholder = false }
            let pruneResult = client.PruneNetworks(pruneReq, CancellationToken.None).Result
            pruneResult.Count |> should equal 2
            pruneResult.NetworksDeleted.Count |> should equal 2
        finally
            stopApp app

    [<Fact>]
    let ``RemoveNetwork sur reseau inexistant via gRPC lance exception`` () =
        let app, address = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<INetworkService>()
            let req: RemoveNetworkRequest = { Id = "does-not-exist"; Force = false }
            let ex = Assert.Throws<AggregateException>(fun () -> client.RemoveNetwork(req, CancellationToken.None).Result |> ignore)
            ex.InnerException.Message |> should haveSubstring "introuvable"
        finally
            stopApp app

    [<Fact>]
    let ``CreateNetwork avec nom injection via gRPC lance exception`` () =
        let app, address = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<INetworkService>()
            let req: CreateNetworkRequest =
                { Name = "bad; rm -rf /"; Driver = NetworkDriver.Bridge
                  Subnet = ""; Gateway = ""; IpRange = ""
                  Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
            let ex = Assert.Throws<AggregateException>(fun () -> client.CreateNetwork(req, CancellationToken.None).Result |> ignore)
            ex.InnerException.Message |> should haveSubstring "Le nom du réseau"
        finally
            stopApp app
