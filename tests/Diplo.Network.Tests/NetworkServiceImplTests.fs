namespace Diplo.Network.Tests

module NetworkServiceImplTests =

    open System
    open System.Collections.Generic
    open System.Threading
    open System.Threading.Tasks
    open Xunit
    open FsUnit.Xunit
    open Grpc.Core
    open Grpc.Core.Testing
    open Diplo.Grpc.Network
    open Diplo.Network.Plugins
    open Diplo.Network.Services

    let createService () =
        let mock = MockNetworkDriver()
        let drivers = Dictionary<NetworkDriver, INetworkDriver>()
        drivers.[NetworkDriver.Bridge] <- mock.Mock
        drivers.[NetworkDriver.``None``] <- NoneDriver() :> INetworkDriver
        let svc = NetworkServiceImpl(drivers :> IReadOnlyDictionary<_, _>)
        svc, mock

    let createCtx () =
        TestServerCallContext.Create(
            "test", "localhost", DateTime.UtcNow, Metadata(), CancellationToken.None,
            "peer", Unchecked.defaultof<AuthContext>, Unchecked.defaultof<ContextPropagationToken>,
            Unchecked.defaultof<System.Func<Metadata,Task>>, Unchecked.defaultof<System.Func<WriteOptions>>, Unchecked.defaultof<System.Action<WriteOptions>>)

    [<Fact>]
    let ``CreateNetwork avec nom retourne les informations du reseau`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = CreateNetworkRequest(Name = "mon-reseau", Driver = NetworkDriver.Bridge, Subnet = "172.17.0.0/16", Gateway = "172.17.0.1")
        let result = svc.CreateNetwork(req, ctx).Result
        String.IsNullOrEmpty(result.Id) |> should equal false
        result.Name |> should equal "mon-reseau"
        result.Driver |> should equal NetworkDriver.Bridge
        result.Subnet |> should equal "172.17.0.0/16"
        result.Gateway |> should equal "172.17.0.1"

    [<Fact>]
    let ``CreateNetwork sans nom genere un id automatiquement`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = CreateNetworkRequest(Name = "", Driver = NetworkDriver.Bridge)
        let result = svc.CreateNetwork(req, ctx).Result
        String.IsNullOrEmpty(result.Id) |> should equal false
        result.Id.Length |> should equal 32

    [<Fact>]
    let ``RemoveNetwork sur reseau existant retourne success`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let createReq = CreateNetworkRequest(Name = "to-delete", Driver = NetworkDriver.Bridge)
        let createResult = svc.CreateNetwork(createReq, ctx).Result
        let req = RemoveNetworkRequest(Id = createResult.Id, Force = false)
        let result = svc.RemoveNetwork(req, ctx).Result
        result.Success |> should equal true
        result.Message |> should equal "Réseau supprimé"

    [<Fact>]
    let ``RemoveNetwork sur reseau inexistant lance RpcException NotFound`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = RemoveNetworkRequest(Id = "nonexistent", Force = false)
        let ex = Assert.Throws<AggregateException>(fun () -> svc.RemoveNetwork(req, ctx).Result |> ignore)
        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.NotFound

    [<Fact>]
    let ``InspectNetwork retourne les metadonnees du reseau`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let createReq = CreateNetworkRequest(Name = "net-inspect", Driver = NetworkDriver.Bridge, Subnet = "10.0.0.0/24", Gateway = "10.0.0.1")
        let createResult = svc.CreateNetwork(createReq, ctx).Result
        let req = InspectNetworkRequest(Id = createResult.Id)
        let result = svc.InspectNetwork(req, ctx).Result
        result.Id |> should equal createResult.Id
        result.Name |> should equal "net-inspect"
        result.Subnet |> should equal "10.0.0.0/24"
        result.Gateway |> should equal "10.0.0.1"

    [<Fact>]
    let ``InspectNetwork sur reseau inexistant lance RpcException NotFound`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = InspectNetworkRequest(Id = "nonexistent")
        let ex = Assert.Throws<AggregateException>(fun () -> svc.InspectNetwork(req, ctx).Result |> ignore)
        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.NotFound

    [<Fact>]
    let ``ListNetworks retourne les reseaux crees`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let req1 = CreateNetworkRequest(Name = "net-1", Driver = NetworkDriver.Bridge)
        let req2 = CreateNetworkRequest(Name = "net-2", Driver = NetworkDriver.Bridge)
        svc.CreateNetwork(req1, ctx).Result |> ignore
        svc.CreateNetwork(req2, ctx).Result |> ignore
        let req = ListNetworksRequest()
        let result = svc.ListNetworks(req, ctx).Result
        result.Networks.Count |> should equal 2
        result.Networks |> Seq.exists (fun n -> n.Name = "net-1") |> should equal true
        result.Networks |> Seq.exists (fun n -> n.Name = "net-2") |> should equal true

    [<Fact>]
    let ``ListNetworks retourne vide quand aucun reseau`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = ListNetworksRequest()
        let result = svc.ListNetworks(req, ctx).Result
        result.Networks.Count |> should equal 0

    [<Fact>]
    let ``ConnectContainer retourne les informations de l'endpoint`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let createReq = CreateNetworkRequest(Name = "net-connect", Driver = NetworkDriver.Bridge)
        let createResult = svc.CreateNetwork(createReq, ctx).Result
        let req = ConnectContainerRequest(
            NetworkId = createResult.Id,
            ContainerId = "container-123",
            EndpointId = "ep-456",
            Ipv4Address = "172.17.0.5"
        )
        let result = svc.ConnectContainer(req, ctx).Result
        result.EndpointId |> should equal "ep-456"
        result.Ipv4Address |> should equal "172.17.0.5"
        result.MacAddress |> should equal "02:42:ac:11:00:02"
        result.Message.Contains("container-123") |> should equal true

    [<Fact>]
    let ``ConnectContainer sans ipv4 utilise valeur par defaut`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let createReq = CreateNetworkRequest(Name = "net-connect2", Driver = NetworkDriver.Bridge)
        let createResult = svc.CreateNetwork(createReq, ctx).Result
        let req = ConnectContainerRequest(
            NetworkId = createResult.Id,
            ContainerId = "container-789",
            EndpointId = "ep-012"
        )
        let result = svc.ConnectContainer(req, ctx).Result
        result.Ipv4Address |> should equal "172.17.0.2"
        result.Message.Contains("container-789") |> should equal true

    [<Fact>]
    let ``DisconnectContainer retourne success`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let createReq = CreateNetworkRequest(Name = "net-disconnect", Driver = NetworkDriver.Bridge)
        let createResult = svc.CreateNetwork(createReq, ctx).Result
        let connectReq = ConnectContainerRequest(
            NetworkId = createResult.Id,
            ContainerId = "container-disc",
            EndpointId = "ep-disc"
        )
        svc.ConnectContainer(connectReq, ctx).Result |> ignore
        let req = DisconnectContainerRequest(
            NetworkId = createResult.Id,
            ContainerId = "container-disc",
            EndpointId = "ep-disc",
            Force = false
        )
        let result = svc.DisconnectContainer(req, ctx).Result
        result.Success |> should equal true
        result.Message |> should equal "Déconnecté"

    [<Fact>]
    let ``DisconnectContainer sur reseau inexistant retourne error`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = DisconnectContainerRequest(
            NetworkId = "nonexistent",
            ContainerId = "container-x",
            EndpointId = "ep-x",
            Force = false
        )
        let result = svc.DisconnectContainer(req, ctx).Result
        result.Success |> should equal false
        result.Message.Contains("introuvable") |> should equal true
