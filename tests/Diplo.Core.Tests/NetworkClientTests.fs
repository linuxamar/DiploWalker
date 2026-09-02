namespace Diplo.Core.Tests

module NetworkClientTests =

    open System
    open System.Collections.Generic
    open System.Threading.Tasks
    open Xunit
    open FsUnit.Xunit
    open Grpc.Net.Client
    open Diplo.Core.Clients
    open Diplo.Core.Connection
    open Diplo.Grpc
    open Diplo.Grpc.Network

    let private run (t: Task<'T>) = t.GetAwaiter().GetResult()

    let private assertArgumentError (f: unit -> unit) =
        (fun () -> f ()) |> should throw typeof<ArgumentException>

    let private newClient (address: string) =
        let channel = DiploChannel.forAddress address
        new NetworkClient(channel, true)

    let private offlineClient () =
        let options = GrpcChannelOptions()
        let channel = GrpcChannel.ForAddress("http://localhost:5003", options)
        new NetworkClient(channel, true)

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

    [<Fact>]
    let ``CreateAsync cree un reseau et retourne son ID`` () =
        let stub = GrpcTestHost.NetworkServiceStub()
        GrpcTestHost.withNetworkApp stub (fun address ->
            use c = newClient address

            let response =
                c.CreateAsync("front", driver = NetworkDriver.Bridge, subnet = "192.168.50.0/24", gateway = "192.168.50.1")
                |> run

            response.Id |> should equal "net-front"
            response.Subnet |> should equal "192.168.50.0/24"
            let req = stub.LastCreate.Value
            req.Name |> should equal "front"
            req.Driver |> should equal NetworkDriver.Bridge
            req.Gateway |> should equal "192.168.50.1")

    [<Fact>]
    let ``CreateAsync transmet plage ip, options et etiquettes`` () =
        let stub = GrpcTestHost.NetworkServiceStub()
        GrpcTestHost.withNetworkApp stub (fun address ->
            use c = newClient address

            c.CreateAsync(
                "host",
                ipRange = "192.168.50.128/25",
                options = (dict [ "mtu", "1500" ]),
                labels = (dict [ "app", "demo" ]),
                cniPluginPath = "C:\\cni\\plugin.cni"
            )
            |> run
            |> ignore

            let req = stub.LastCreate.Value
            req.IpRange |> should equal "192.168.50.128/25"
            req.Options.["mtu"] |> should equal "1500"
            req.Labels.["app"] |> should equal "demo"
            req.CniPluginPath |> should equal "C:\\cni\\plugin.cni")

    [<Fact>]
    let ``InspectAsync retourne les infos du reseau`` () =
        let stub = GrpcTestHost.NetworkServiceStub()
        GrpcTestHost.withNetworkApp stub (fun address ->
            use c = newClient address
            let response = c.InspectAsync("net-front") |> run
            response.Id |> should equal "net-front"
            response.Driver |> should equal NetworkDriver.Bridge
            stub.LastInspect.Value.Id |> should equal "net-front")

    [<Fact>]
    let ``ListAsync retourne les reseaux`` () =
        let stub = GrpcTestHost.NetworkServiceStub()
        GrpcTestHost.withNetworkApp stub (fun address ->
            use c = newClient address
            let response = c.ListAsync(filters = (dict [ "label", "x" ])) |> run
            response.Networks |> should be Empty
            stub.LastList.Value.Filters.["label"] |> should equal "x")

    [<Fact>]
    let ``RemoveAsync supprime un reseau`` () =
        let stub = GrpcTestHost.NetworkServiceStub()
        GrpcTestHost.withNetworkApp stub (fun address ->
            use c = newClient address
            let response = c.RemoveAsync("net-front", force = true) |> run
            response.Success |> should equal true
            let req = stub.LastRemove.Value
            req.Id |> should equal "net-front"
            req.Force |> should equal true)

    [<Fact>]
    let ``ConnectAsync connecte un conteneur au reseau`` () =
        let stub = GrpcTestHost.NetworkServiceStub()
        GrpcTestHost.withNetworkApp stub (fun address ->
            use c = newClient address
            let response = c.ConnectAsync("net-front", "ctr-1", ipv4Address = "10.0.0.5") |> run
            response.Ipv4Address |> should equal "10.0.0.5"
            let req = stub.LastConnect.Value
            req.ContainerId |> should equal "ctr-1"
            req.NetworkId |> should equal "net-front"
            req.Ipv4Address |> should equal "10.0.0.5")

    [<Fact>]
    let ``DisconnectAsync deconnecte un conteneur du reseau`` () =
        let stub = GrpcTestHost.NetworkServiceStub()
        GrpcTestHost.withNetworkApp stub (fun address ->
            use c = newClient address
            let response = c.DisconnectAsync("net-front", "ctr-1") |> run
            response.Success |> should equal true
            let req = stub.LastDisconnect.Value
            req.ContainerId |> should equal "ctr-1"
            req.NetworkId |> should equal "net-front")

    [<Fact>]
    let ``RunCniPluginAsync execute le plugin et retourne l'interface`` () =
        let stub = GrpcTestHost.NetworkServiceStub()
        GrpcTestHost.withNetworkApp stub (fun address ->
            use c = newClient address
            let response = c.RunCniPluginAsync("C:\\plugins\\host-local.exe", "ADD", "ctr-1", "C:\\netns\\ns1") |> run
            response.Success |> should equal true
            response.Ifname |> should equal "eth0"
            let req = stub.LastRunCni.Value
            req.PluginPath |> should equal "C:\\plugins\\host-local.exe"
            req.Command |> should equal "ADD"
            req.NetnsPath |> should equal "C:\\netns\\ns1")

    [<Fact>]
    let ``PruneNetworksAsync supprime les reseaux non utilises`` () =
        let stub = GrpcTestHost.NetworkServiceStub()
        GrpcTestHost.withNetworkApp stub (fun address ->
            use c = newClient address
            let response = c.PruneNetworksAsync() |> run
            response.Count |> should equal 0
            response.Message |> should equal "0 réseau(x) supprimé(s)")

    [<Fact>]
    let ``Les gardes rejettent nome et identifiants vides`` () =
        use c = offlineClient ()
        assertArgumentError (fun () -> c.CreateAsync("") |> run |> ignore)
        assertArgumentError (fun () -> c.InspectAsync("") |> run |> ignore)
        assertArgumentError (fun () -> c.RemoveAsync("") |> run |> ignore)
        assertArgumentError (fun () -> c.ConnectAsync("", "net-front") |> run |> ignore)
        assertArgumentError (fun () -> c.ConnectAsync("ctr-1", "") |> run |> ignore)
        assertArgumentError (fun () -> c.DisconnectAsync("", "net-front") |> run |> ignore)
        assertArgumentError (fun () -> c.DisconnectAsync("ctr-1", "") |> run |> ignore)