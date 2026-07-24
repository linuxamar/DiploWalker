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

    // --- PruneNetworks ---
    [<Fact>]
    let ``PruneNetworks retourne vide quand aucun reseau`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = PruneNetworksRequest()
        let result = svc.PruneNetworks(req, ctx).Result
        result.Count |> should equal 0
        result.NetworksDeleted.Count |> should equal 0
        result.Message |> should equal "0 réseau(x) supprimé(s)"

    // --- Sécurité : CreateNetwork ---
    [<Fact>]
    let ``CreateNetwork avec nom injection lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = CreateNetworkRequest(Name = "test; rm -rf /", Driver = NetworkDriver.Bridge)
        let ex = Assert.Throws<AggregateException>(fun () -> svc.CreateNetwork(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "Le nom du réseau"

    [<Fact>]
    let ``CreateNetwork avec subnet invalide lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = CreateNetworkRequest(Name = "net-ok", Driver = NetworkDriver.Bridge, Subnet = "999.999.999.999/24")
        let ex = Assert.Throws<AggregateException>(fun () -> svc.CreateNetwork(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "Le sous-réseau"

    [<Fact>]
    let ``CreateNetwork avec gateway invalide lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = CreateNetworkRequest(Name = "net-ok", Driver = NetworkDriver.Bridge, Gateway = "not-an-ip")
        let ex = Assert.Throws<AggregateException>(fun () -> svc.CreateNetwork(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "La passerelle"

    [<Fact>]
    let ``CreateNetwork avec label clé invalide lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = CreateNetworkRequest(Name = "net-ok", Driver = NetworkDriver.Bridge)
        req.Labels.Add("bad;key", "val")
        let ex = Assert.Throws<AggregateException>(fun () -> svc.CreateNetwork(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "La clé du label"

    [<Fact>]
    let ``CreateNetwork avec label valeur invalide lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = CreateNetworkRequest(Name = "net-ok", Driver = NetworkDriver.Bridge)
        req.Labels.Add("env", "bad;value|pipe")
        let ex = Assert.Throws<AggregateException>(fun () -> svc.CreateNetwork(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "La valeur du label"

    // --- Sécurité : RemoveNetwork ---
    [<Fact>]
    let ``RemoveNetwork avec id injection lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = RemoveNetworkRequest(Id = "id; rm -rf /", Force = false)
        let ex = Assert.Throws<AggregateException>(fun () -> svc.RemoveNetwork(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "L'identifiant du réseau"

    [<Fact>]
    let ``RemoveNetwork avec id vide lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = RemoveNetworkRequest(Id = "", Force = false)
        let ex = Assert.Throws<AggregateException>(fun () -> svc.RemoveNetwork(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "L'identifiant du réseau"

    // --- Sécurité : InspectNetwork ---
    [<Fact>]
    let ``InspectNetwork avec id vide lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = InspectNetworkRequest(Id = "")
        let ex = Assert.Throws<AggregateException>(fun () -> svc.InspectNetwork(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "L'identifiant du réseau"

    [<Fact>]
    let ``InspectNetwork avec id injection lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = InspectNetworkRequest(Id = "`whoami`")
        let ex = Assert.Throws<AggregateException>(fun () -> svc.InspectNetwork(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "L'identifiant du réseau"

    // --- Sécurité : ConnectContainer ---
    [<Fact>]
    let ``ConnectContainer avec container id vide lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = ConnectContainerRequest(NetworkId = "net123", ContainerId = "")
        let ex = Assert.Throws<AggregateException>(fun () -> svc.ConnectContainer(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "L'identifiant du conteneur"

    [<Fact>]
    let ``ConnectContainer avec container id injection lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = ConnectContainerRequest(NetworkId = "net123", ContainerId = "ct; ls -la")
        let ex = Assert.Throws<AggregateException>(fun () -> svc.ConnectContainer(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "L'identifiant du conteneur"

    [<Fact>]
    let ``ConnectContainer avec ipv4 invalide lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = ConnectContainerRequest(NetworkId = "net123", ContainerId = "ct123", Ipv4Address = "999.999.999.999")
        let ex = Assert.Throws<AggregateException>(fun () -> svc.ConnectContainer(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "L'adresse IPv4"

    // --- Sécurité : DisconnectContainer ---
    [<Fact>]
    let ``DisconnectContainer avec container id vide lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = DisconnectContainerRequest(NetworkId = "net123", ContainerId = "", Force = false)
        let ex = Assert.Throws<AggregateException>(fun () -> svc.DisconnectContainer(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "L'identifiant du conteneur"

    [<Fact>]
    let ``DisconnectContainer avec network id injection lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = DisconnectContainerRequest(NetworkId = "net|cat /etc/passwd", ContainerId = "ct123", Force = false)
        let ex = Assert.Throws<AggregateException>(fun () -> svc.DisconnectContainer(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "L'identifiant du réseau"

    // --- Sécurité : RunCniPlugin ---
    [<Fact>]
    let ``RunCniPlugin avec container id vide lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = RunCniPluginRequest(ContainerId = "")
        let ex = Assert.Throws<AggregateException>(fun () -> svc.RunCniPlugin(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "L'identifiant du conteneur"

    [<Fact>]
    let ``RunCniPlugin avec commande non autorisee retourne erreur`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = RunCniPluginRequest(ContainerId = "ct123", PluginPath = @"C:\Program Files\containerd\cni\bin\bridge.exe", Command = "EXEC")
        let response = svc.RunCniPlugin(req, ctx).Result
        response.Success |> should equal false
        response.Message |> should haveSubstring "Erreur lors de l'exécution du plugin CNI"

    [<Fact>]
    let ``RunCniPlugin avec plugin hors repertoire autorise retourne erreur`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = RunCniPluginRequest(ContainerId = "ct123", PluginPath = @"C:\evil\malware.exe", Command = "ADD")
        let response = svc.RunCniPlugin(req, ctx).Result
        response.Success |> should equal false
        response.Message |> should haveSubstring "Erreur lors de l'exécution du plugin CNI"

    [<Fact>]
    let ``PruneNetworks supprime tous les reseaux existants`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let req1 = CreateNetworkRequest(Name = "net-prune-1", Driver = NetworkDriver.Bridge)
        let req2 = CreateNetworkRequest(Name = "net-prune-2", Driver = NetworkDriver.Bridge)
        let r1 = svc.CreateNetwork(req1, ctx).Result
        let r2 = svc.CreateNetwork(req2, ctx).Result
        let req = PruneNetworksRequest()
        let result = svc.PruneNetworks(req, ctx).Result
        result.Count |> should equal 2
        result.NetworksDeleted.Count |> should equal 2
        result.NetworksDeleted |> should contain r1.Id
        result.NetworksDeleted |> should contain r2.Id
