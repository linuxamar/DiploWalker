namespace Diplo.Network.Tests

module NetworkServiceImplTests =

    open System
    open System.Collections.Generic
    open System.Threading
    open System.Threading.Tasks
    open Xunit
    open FsUnit.Xunit
    open Grpc.Core
    open Diplo.Grpc.Network
    open Diplo.Grpc
    open Diplo.Network.Plugins
    open Diplo.Network.Services

    let createService () =
        let mock = MockNetworkDriver()
        let drivers = Dictionary<NetworkDriver, INetworkDriver>()
        drivers.[NetworkDriver.Bridge] <- mock.Mock
        drivers.[NetworkDriver.``None``] <- NoneDriver() :> INetworkDriver
        let svc = NetworkServiceImpl(drivers :> IReadOnlyDictionary<_, _>)
        svc, mock

    let createCtx () = CancellationToken.None

    [<Fact>]
    let ``CreateNetwork avec nom retourne les informations du reseau`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Name = "mon-reseau"; Driver = NetworkDriver.Bridge; Subnet = "172.17.0.0/16"; Gateway = "172.17.0.1"; IpRange = ""; Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
        let result = (svc :> INetworkService).CreateNetwork(req, ctx).Result
        String.IsNullOrEmpty(result.Id) |> should equal false
        result.Name |> should equal "mon-reseau"
        result.Driver |> should equal NetworkDriver.Bridge
        result.Subnet |> should equal "172.17.0.0/16"
        result.Gateway |> should equal "172.17.0.1"

    [<Fact>]
    let ``CreateNetwork sans nom genere un id automatiquement`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Name = ""; Driver = NetworkDriver.Bridge; Subnet = ""; Gateway = ""; IpRange = ""; Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
        let result = (svc :> INetworkService).CreateNetwork(req, ctx).Result
        String.IsNullOrEmpty(result.Id) |> should equal false
        result.Id.Length |> should equal 32

    [<Fact>]
    let ``RemoveNetwork sur reseau existant retourne success`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let createReq = { Name = "to-delete"; Driver = NetworkDriver.Bridge; Subnet = ""; Gateway = ""; IpRange = ""; Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
        let createResult = (svc :> INetworkService).CreateNetwork(createReq, ctx).Result
        let req = { Id = createResult.Id; Force = false }
        let result = (svc :> INetworkService).RemoveNetwork(req, ctx).Result
        result.Success |> should equal true
        result.Message |> should equal "Réseau supprimé"

    [<Fact>]
    let ``RemoveNetwork sur reseau inexistant lance RpcException NotFound`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Id = "nonexistent"; Force = false }
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> INetworkService).RemoveNetwork(req, ctx).Result |> ignore)
        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.NotFound

    [<Fact>]
    let ``InspectNetwork retourne les metadonnees du reseau`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let createReq = { Name = "net-inspect"; Driver = NetworkDriver.Bridge; Subnet = "10.0.0.0/24"; Gateway = "10.0.0.1"; IpRange = ""; Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
        let createResult = (svc :> INetworkService).CreateNetwork(createReq, ctx).Result
        let req = { Id = createResult.Id }
        let result = (svc :> INetworkService).InspectNetwork(req, ctx).Result
        result.Id |> should equal createResult.Id
        result.Name |> should equal "net-inspect"
        result.Subnet |> should equal "10.0.0.0/24"
        result.Gateway |> should equal "10.0.0.1"

    [<Fact>]
    let ``InspectNetwork sur reseau inexistant lance RpcException NotFound`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Id = "nonexistent" }
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> INetworkService).InspectNetwork(req, ctx).Result |> ignore)
        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.NotFound

    [<Fact>]
    let ``ListNetworks retourne les reseaux crees`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let req1 = { Name = "net-1"; Driver = NetworkDriver.Bridge; Subnet = ""; Gateway = ""; IpRange = ""; Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
        let req2 = { Name = "net-2"; Driver = NetworkDriver.Bridge; Subnet = ""; Gateway = ""; IpRange = ""; Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
        (svc :> INetworkService).CreateNetwork(req1, ctx).Result |> ignore
        (svc :> INetworkService).CreateNetwork(req2, ctx).Result |> ignore
        let req = { Filters = Dictionary<string, string>() }
        let result = (svc :> INetworkService).ListNetworks(req, ctx).Result
        result.Networks.Count |> should equal 2
        result.Networks |> Seq.exists (fun n -> n.Name = "net-1") |> should equal true
        result.Networks |> Seq.exists (fun n -> n.Name = "net-2") |> should equal true

    [<Fact>]
    let ``ListNetworks retourne vide quand aucun reseau`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Filters = Dictionary<string, string>() }
        let result = (svc :> INetworkService).ListNetworks(req, ctx).Result
        result.Networks.Count |> should equal 0

    [<Fact>]
    let ``ConnectContainer retourne les informations de l'endpoint`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let createReq = { Name = "net-connect"; Driver = NetworkDriver.Bridge; Subnet = ""; Gateway = ""; IpRange = ""; Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
        let createResult = (svc :> INetworkService).CreateNetwork(createReq, ctx).Result
        let req = { NetworkId = createResult.Id; ContainerId = "container-123"; EndpointId = "ep-456"; Ipv4Address = "172.17.0.5"; Options = Dictionary() }
        let result = (svc :> INetworkService).ConnectContainer(req, ctx).Result
        result.EndpointId |> should equal "ep-456"
        result.Ipv4Address |> should equal "172.17.0.5"
        result.MacAddress |> should equal "02:42:ac:11:00:02"
        result.Message.Contains("container-123") |> should equal true

    [<Fact>]
    let ``ConnectContainer sans ipv4 utilise valeur par defaut`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let createReq = { Name = "net-connect2"; Driver = NetworkDriver.Bridge; Subnet = ""; Gateway = ""; IpRange = ""; Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
        let createResult = (svc :> INetworkService).CreateNetwork(createReq, ctx).Result
        let req = { NetworkId = createResult.Id; ContainerId = "container-789"; EndpointId = "ep-012"; Ipv4Address = ""; Options = Dictionary() }
        let result = (svc :> INetworkService).ConnectContainer(req, ctx).Result
        result.Ipv4Address |> should equal "172.17.0.2"
        result.Message.Contains("container-789") |> should equal true

    [<Fact>]
    let ``DisconnectContainer retourne success`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let createReq = { Name = "net-disconnect"; Driver = NetworkDriver.Bridge; Subnet = ""; Gateway = ""; IpRange = ""; Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
        let createResult = (svc :> INetworkService).CreateNetwork(createReq, ctx).Result
        let connectReq = { NetworkId = createResult.Id; ContainerId = "container-disc"; EndpointId = "ep-disc"; Ipv4Address = ""; Options = Dictionary() }
        (svc :> INetworkService).ConnectContainer(connectReq, ctx).Result |> ignore
        let req = { NetworkId = createResult.Id; ContainerId = "container-disc"; EndpointId = "ep-disc"; Force = false }
        let result = (svc :> INetworkService).DisconnectContainer(req, ctx).Result
        result.Success |> should equal true
        result.Message |> should equal "Déconnecté"

    [<Fact>]
    let ``DisconnectContainer sur reseau inexistant retourne error`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { NetworkId = "nonexistent"; ContainerId = "container-x"; EndpointId = "ep-x"; Force = false }
        let result = (svc :> INetworkService).DisconnectContainer(req, ctx).Result
        result.Success |> should equal false
        result.Message.Contains("introuvable") |> should equal true

    // --- PruneNetworks ---
    [<Fact>]
    let ``PruneNetworks retourne vide quand aucun reseau`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Placeholder = false }
        let result = (svc :> INetworkService).PruneNetworks(req, ctx).Result
        result.Count |> should equal 0
        result.NetworksDeleted.Count |> should equal 0
        result.Message |> should equal "0 réseau(x) supprimé(s)"

    // --- Sécurité : CreateNetwork ---
    [<Fact>]
    let ``CreateNetwork avec nom injection lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Name = "test; rm -rf /"; Driver = NetworkDriver.Bridge; Subnet = ""; Gateway = ""; IpRange = ""; Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> INetworkService).CreateNetwork(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "Le nom du réseau"

    [<Fact>]
    let ``CreateNetwork avec subnet invalide lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Name = "net-ok"; Driver = NetworkDriver.Bridge; Subnet = "999.999.999.999/24"; Gateway = ""; IpRange = ""; Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> INetworkService).CreateNetwork(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "Le sous-réseau"

    [<Fact>]
    let ``CreateNetwork avec gateway invalide lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Name = "net-ok"; Driver = NetworkDriver.Bridge; Subnet = ""; Gateway = "not-an-ip"; IpRange = ""; Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> INetworkService).CreateNetwork(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "La passerelle"

    [<Fact>]
    let ``CreateNetwork avec label clé invalide lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Name = "net-ok"; Driver = NetworkDriver.Bridge; Subnet = ""; Gateway = ""; IpRange = ""; Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
        req.Labels.Add("bad;key", "val")
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> INetworkService).CreateNetwork(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "La clé du label"

    [<Fact>]
    let ``CreateNetwork avec label valeur invalide lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Name = "net-ok"; Driver = NetworkDriver.Bridge; Subnet = ""; Gateway = ""; IpRange = ""; Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
        req.Labels.Add("env", "bad;value|pipe")
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> INetworkService).CreateNetwork(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "La valeur du label"

    // --- Sécurité : RemoveNetwork ---
    [<Fact>]
    let ``RemoveNetwork avec id injection lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Id = "id; rm -rf /"; Force = false }
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> INetworkService).RemoveNetwork(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "L'identifiant du réseau"

    [<Fact>]
    let ``RemoveNetwork avec id vide lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Id = ""; Force = false }
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> INetworkService).RemoveNetwork(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "L'identifiant du réseau"

    // --- Sécurité : InspectNetwork ---
    [<Fact>]
    let ``InspectNetwork avec id vide lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Id = "" }
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> INetworkService).InspectNetwork(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "L'identifiant du réseau"

    [<Fact>]
    let ``InspectNetwork avec id injection lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Id = "`whoami`" }
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> INetworkService).InspectNetwork(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "L'identifiant du réseau"

    // --- Sécurité : ConnectContainer ---
    [<Fact>]
    let ``ConnectContainer avec container id vide lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { NetworkId = "net123"; ContainerId = ""; EndpointId = ""; Ipv4Address = ""; Options = Dictionary() }
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> INetworkService).ConnectContainer(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "L'identifiant du conteneur"

    [<Fact>]
    let ``ConnectContainer avec container id injection lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { NetworkId = "net123"; ContainerId = "ct; ls -la"; EndpointId = ""; Ipv4Address = ""; Options = Dictionary() }
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> INetworkService).ConnectContainer(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "L'identifiant du conteneur"

    [<Fact>]
    let ``ConnectContainer avec ipv4 invalide lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { NetworkId = "net123"; ContainerId = "ct123"; EndpointId = ""; Ipv4Address = "999.999.999.999"; Options = Dictionary() }
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> INetworkService).ConnectContainer(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "L'adresse IPv4"

    // --- Sécurité : DisconnectContainer ---
    [<Fact>]
    let ``DisconnectContainer avec container id vide lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { NetworkId = "net123"; ContainerId = ""; EndpointId = ""; Force = false }
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> INetworkService).DisconnectContainer(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "L'identifiant du conteneur"

    [<Fact>]
    let ``DisconnectContainer avec network id injection lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { NetworkId = "net|cat /etc/passwd"; ContainerId = "ct123"; EndpointId = ""; Force = false }
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> INetworkService).DisconnectContainer(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "L'identifiant du réseau"

    // --- Sécurité : RunCniPlugin ---
    [<Fact>]
    let ``RunCniPlugin avec container id vide lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { PluginPath = ""; Command = ""; ContainerId = ""; NetnsPath = ""; Config = Unchecked.defaultof<CniConfiguration> }
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> INetworkService).RunCniPlugin(req, ctx).Result |> ignore)
        ex.InnerException.Message |> should haveSubstring "L'identifiant du conteneur"

    [<Fact>]
    let ``RunCniPlugin avec commande non autorisee retourne erreur`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { PluginPath = @"C:\Program Files\containerd\cni\bin\bridge.exe"; Command = "EXEC"; ContainerId = "ct123"; NetnsPath = ""; Config = Unchecked.defaultof<CniConfiguration> }
        let response = (svc :> INetworkService).RunCniPlugin(req, ctx).Result
        response.Success |> should equal false
        response.Message |> should haveSubstring "Erreur lors de l'exécution du plugin CNI"

    [<Fact>]
    let ``RunCniPlugin avec plugin hors repertoire autorise retourne erreur`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { PluginPath = @"C:\evil\malware.exe"; Command = "ADD"; ContainerId = "ct123"; NetnsPath = ""; Config = Unchecked.defaultof<CniConfiguration> }
        let response = (svc :> INetworkService).RunCniPlugin(req, ctx).Result
        response.Success |> should equal false
        response.Message |> should haveSubstring "Erreur lors de l'exécution du plugin CNI"

    [<Fact>]
    let ``PruneNetworks supprime tous les reseaux existants`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let req1 = { Name = "net-prune-1"; Driver = NetworkDriver.Bridge; Subnet = ""; Gateway = ""; IpRange = ""; Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
        let req2 = { Name = "net-prune-2"; Driver = NetworkDriver.Bridge; Subnet = ""; Gateway = ""; IpRange = ""; Options = Dictionary(); Labels = Dictionary(); CniPluginPath = "" }
        let r1 = (svc :> INetworkService).CreateNetwork(req1, ctx).Result
        let r2 = (svc :> INetworkService).CreateNetwork(req2, ctx).Result
        let req = { Placeholder = false }
        let result = (svc :> INetworkService).PruneNetworks(req, ctx).Result
        result.Count |> should equal 2
        result.NetworksDeleted.Count |> should equal 2
        result.NetworksDeleted |> should contain r1.Id
        result.NetworksDeleted |> should contain r2.Id
