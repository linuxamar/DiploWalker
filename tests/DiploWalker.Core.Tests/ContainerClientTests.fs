namespace DiploWalker.Core.Tests

module ContainerClientTests =

    open System
    open System.Collections.Generic
    open System.Threading
    open System.Threading.Tasks
    open Xunit
    open FsUnit.Xunit
    open Grpc.Net.Client
    open DiploWalker.Core.Clients
    open DiploWalker.Core.Connection
    open DiploWalker.Grpc
    open DiploWalker.Grpc.Container

    let private run (t: Task<'T>) = t.GetAwaiter().GetResult()

    let private assertArgumentError (f: unit -> unit) =
        (fun () -> f ()) |> should throw typeof<ArgumentException>

    let private newClient (address: string) =
        let channel = DiploWalkerChannel.forAddress address
        new ContainerClient(channel, true)

    let private offlineClient () =
        let options = GrpcChannelOptions()
        let channel = GrpcChannel.ForAddress("http://localhost:5001", options)
        new ContainerClient(channel, true)

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
        channel.Target |> should not' (be Null)
        channel.Dispose()

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ CrÃ©ation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``CreateAsync cree un conteneur et retourne son ID`` () =
        let stub = GrpcTestHost.ContainerServiceStub()
        GrpcTestHost.withContainerApp stub (fun address ->
            use c = newClient address
            let response = c.CreateAsync("web", "nginx:latest") |> run
            response.Id |> should equal "ctr-web"
            response.State |> should equal ContainerState.Created
            let req = stub.LastCreate.Value
            req.Name |> should equal "web"
            req.Image |> should equal "nginx:latest")

    [<Fact>]
    let ``CreateAsync transmet les ports declares`` () =
        let stub = GrpcTestHost.ContainerServiceStub()
        GrpcTestHost.withContainerApp stub (fun address ->
            use c = newClient address

            c.CreateAsync("web", "nginx:latest", ports = [ (8080, 80, "tcp"); (8443, 443, "tcp") ])
            |> run
            |> ignore

            let req = stub.LastCreate.Value
            req.Ports |> should haveCount 2
            req.Ports.[0].HostPort |> should equal 8080
            req.Ports.[0].ContainerPort |> should equal 80
            req.Ports.[0].Protocol |> should equal "tcp"
            req.Ports.[1].HostPort |> should equal 8443)

    [<Fact>]
    let ``CreateAsync transmet env, labels, commande, arguments, montages et limites`` () =
        let stub = GrpcTestHost.ContainerServiceStub()
        GrpcTestHost.withContainerApp stub (fun address ->
            use c = newClient address

            c.CreateAsync(
                "web",
                "nginx:latest",
                env = (dict [ "FOO", "bar" ]),
                labels = (dict [ "app", "demo" ]),
                command = [ "sh" ],
                args = [ "-c"; "echo ok" ],
                mounts = [ ("C:\\src", "C:\\dst", true) ],
                pidLimit = 10,
                memoryLimit = 512L,
                cpuShares = 2
            )
            |> run
            |> ignore

            let req = stub.LastCreate.Value
            req.Env.["FOO"] |> should equal "bar"
            req.Labels.["app"] |> should equal "demo"
            req.Command |> should contain "sh"
            req.Args |> should haveCount 2
            req.Mounts.[0].Source |> should equal "C:\\src"
            req.Mounts.[0].Destination |> should equal "C:\\dst"
            req.Mounts.[0].ReadOnly |> should equal true
            req.PidLimit |> should equal 10
            req.MemoryLimit |> should equal 512L
            req.CpuShares |> should equal 2)

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ Cycle de vie â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``StartAsync demarre un conteneur`` () =
        let stub = GrpcTestHost.ContainerServiceStub()
        GrpcTestHost.withContainerApp stub (fun address ->
            use c = newClient address
            let response = c.StartAsync("c1", attach = true) |> run
            response.State |> should equal ContainerState.Running
            let req = stub.LastStart.Value
            req.Id |> should equal "c1"
            req.Attach |> should equal true)

    [<Fact>]
    let ``StopAsync arrete un conteneur avec le delai transmis`` () =
        let stub = GrpcTestHost.ContainerServiceStub()
        GrpcTestHost.withContainerApp stub (fun address ->
            use c = newClient address
            let response = c.StopAsync("c1", timeoutSeconds = 42) |> run
            response.State |> should equal ContainerState.Stopped
            let req = stub.LastStop.Value
            req.Id |> should equal "c1"
            req.TimeoutSeconds |> should equal 42)

    [<Fact>]
    let ``DeleteAsync supprime un conteneur`` () =
        let stub = GrpcTestHost.ContainerServiceStub()
        GrpcTestHost.withContainerApp stub (fun address ->
            use c = newClient address
            let response = c.DeleteAsync("c1", force = true) |> run
            response.Success |> should equal true
            let req = stub.LastDelete.Value
            req.Id |> should equal "c1"
            req.Force |> should equal true)

    [<Fact>]
    let ``InspectAsync retourne les infos du conteneur`` () =
        let stub = GrpcTestHost.ContainerServiceStub()
        GrpcTestHost.withContainerApp stub (fun address ->
            use c = newClient address
            let response = c.InspectAsync("c1") |> run
            response.Id |> should equal "c1"
            response.State |> should equal ContainerState.Running
            response.Image |> should equal "nginx:latest")

    [<Fact>]
    let ``ListAsync transmet le filtre all`` () =
        let stub = GrpcTestHost.ContainerServiceStub()
        GrpcTestHost.withContainerApp stub (fun address ->
            use c = newClient address
            let response = c.ListAsync(all = true, filters = (dict [ "label", "x" ])) |> run
            response.Containers |> should be Empty
            let req = stub.LastList.Value
            req.All |> should equal true
            req.Filters.["label"] |> should equal "x")

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ Journaux â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``GetLogsStream emet les entrees du service`` () =
        let stub = GrpcTestHost.ContainerServiceStub()
        GrpcTestHost.withContainerApp stub (fun address ->
            use c = newClient address
            let entries = ResizeArray()

            let enumerator =
                c
                    .GetLogsStream("c1", follow = true, tail = 10, since = "2026-01-01T00:00:00Z")
                    .GetAsyncEnumerator(CancellationToken.None)

            try
                let mutable moving = true

                while moving do
                    if enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult() then
                        entries.Add(enumerator.Current)
                    else
                        moving <- false
            finally
                enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult()

            entries |> should haveCount 2
            entries.[0].Stream |> should equal "stdout"
            entries.[0].Log |> should equal "ligne 1"
            entries.[1].Stream |> should equal "stderr"
            let req = stub.LastLogs.Value
            req.Id |> should equal "c1"
            req.Follow |> should equal true
            req.Tail |> should equal 10
            req.Since |> should equal "2026-01-01T00:00:00Z")

    [<Fact>]
    let ``GetLogs agrege le flux en une sequence`` () =
        let stub = GrpcTestHost.ContainerServiceStub()
        GrpcTestHost.withContainerApp stub (fun address ->
            use c = newClient address
            let logs = c.GetLogs("c1", follow = false, tail = 50) |> run
            logs |> should haveCount 2
            logs |> Seq.map (fun e -> e.Log) |> List.ofSeq |> should equal [ "ligne 1"; "erreur" ])

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ ExÃ©cution â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``Exec envoie la commande et retourne la sortie`` () =
        let stub = GrpcTestHost.ContainerServiceStub()
        GrpcTestHost.withContainerApp stub (fun address ->
            use c = newClient address
            let outputs = c.Exec("c1", [ "ls"; "-la" ]) |> run
            let req = stub.LastExec.Value
            req.Id |> should equal "c1"
            req.Command |> should contain "ls"
            outputs |> should haveCount 1
            (outputs |> Seq.head).Stream |> should equal "stdout"
            System.Text.Encoding.UTF8.GetString((outputs |> Seq.head).Data) |> should equal "hello")

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ Images â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``PullImageAsync telecharge une image`` () =
        let stub = GrpcTestHost.ContainerServiceStub()
        GrpcTestHost.withContainerApp stub (fun address ->
            use c = newClient address
            let response = c.PullImageAsync("nginx:latest", user = "u") |> run
            response.Image |> should equal "nginx:latest"
            let req = stub.LastPull.Value
            req.Image |> should equal "nginx:latest"
            req.User |> should equal "u")

    [<Fact>]
    let ``ListImagesAsync retourne les images`` () =
        let stub = GrpcTestHost.ContainerServiceStub()
        GrpcTestHost.withContainerApp stub (fun address ->
            use c = newClient address
            let response = c.ListImagesAsync(namespaceName = "k8s") |> run
            response.Images |> should haveCount 1
            response.Images.[0].Ref |> should equal "nginx:latest"
            stub.LastListImages.Value.NamespaceName |> should equal "k8s")

    [<Fact>]
    let ``InspectImageAsync retourne les infos de l'image`` () =
        let stub = GrpcTestHost.ContainerServiceStub()
        GrpcTestHost.withContainerApp stub (fun address ->
            use c = newClient address
            let response = c.InspectImageAsync("nginx:latest") |> run
            response.Ref |> should equal "nginx:latest"
            response.Repository |> should equal "nginx"
            stub.LastInspectImage.Value.Ref |> should equal "nginx:latest")

    [<Fact>]
    let ``RemoveImageAsync supprime une image`` () =
        let stub = GrpcTestHost.ContainerServiceStub()
        GrpcTestHost.withContainerApp stub (fun address ->
            use c = newClient address
            let response = c.RemoveImageAsync("nginx:latest") |> run
            response.Success |> should equal true
            stub.LastRemoveImage.Value.Ref |> should equal "nginx:latest")

    [<Fact>]
    let ``TagImageAsync reetiquette une image`` () =
        let stub = GrpcTestHost.ContainerServiceStub()
        GrpcTestHost.withContainerApp stub (fun address ->
            use c = newClient address
            let response = c.TagImageAsync("nginx:latest", "registry.local/nginx:2") |> run
            response.Target |> should equal "registry.local/nginx:2"
            let req = stub.LastTagImage.Value
            req.Source |> should equal "nginx:latest"
            req.Target |> should equal "registry.local/nginx:2")

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ Registres â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``LoginRegistryAsync se connecte au registre`` () =
        let stub = GrpcTestHost.ContainerServiceStub()
        GrpcTestHost.withContainerApp stub (fun address ->
            use c = newClient address
            let response = c.LoginRegistryAsync("registry.local", "user", "secret") |> run
            response.Success |> should equal true
            let req = stub.LastLogin.Value
            req.Registry |> should equal "registry.local"
            req.Username |> should equal "user"
            req.Password |> should equal "secret")

    [<Fact>]
    let ``LogoutRegistryAsync se deconnecte du registre`` () =
        let stub = GrpcTestHost.ContainerServiceStub()
        GrpcTestHost.withContainerApp stub (fun address ->
            use c = newClient address
            let response = c.LogoutRegistryAsync("registry.local") |> run
            response.Success |> should equal true
            stub.LastLogout.Value.Registry |> should equal "registry.local")

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ Divers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``GetVersionAsync retourne la version du service`` () =
        let stub = GrpcTestHost.ContainerServiceStub()
        GrpcTestHost.withContainerApp stub (fun address ->
            use c = newClient address
            let response = c.GetVersionAsync() |> run
            response.Version |> should equal "1.0.0-test")

    [<Fact>]
    let ``ListNamespacesAsync retourne les espaces de noms`` () =
        let stub = GrpcTestHost.ContainerServiceStub()
        GrpcTestHost.withContainerApp stub (fun address ->
            use c = newClient address
            let response = c.ListNamespacesAsync() |> run
            response.Namespaces |> should contain "default")

    [<Fact>]
    let ``RenameContainerAsync renomme un conteneur`` () =
        let stub = GrpcTestHost.ContainerServiceStub()
        GrpcTestHost.withContainerApp stub (fun address ->
            use c = newClient address
            let response = c.RenameContainerAsync("c1", "nouveau") |> run
            response.Success |> should equal true
            let req = stub.LastRename.Value
            req.Id |> should equal "c1"
            req.NewName |> should equal "nouveau")

    [<Fact>]
    let ``TopContainerAsync retourne les processus`` () =
        let stub = GrpcTestHost.ContainerServiceStub()
        GrpcTestHost.withContainerApp stub (fun address ->
            use c = newClient address
            let response = c.TopContainerAsync("c1") |> run
            response.Processes |> should haveCount 1
            response.Processes.[0].Pid |> should equal 1234L
            stub.LastTop.Value.Id |> should equal "c1")

    [<Fact>]
    let ``GetContainerStatsAsync retourne les statistiques`` () =
        let stub = GrpcTestHost.ContainerServiceStub()
        GrpcTestHost.withContainerApp stub (fun address ->
            use c = newClient address
            let response = c.GetContainerStatsAsync("c1") |> run
            response.CpuUsage |> should equal 1.5
            response.MemoryLimit |> should equal 65536L
            stub.LastStats.Value.Id |> should equal "c1")

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ Gardes â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``Les gardes de creation rejettent nom et image vides`` () =
        use c = offlineClient ()
        assertArgumentError (fun () -> c.CreateAsync("", "nginx:latest") |> run |> ignore)
        assertArgumentError (fun () -> c.CreateAsync("web", "") |> run |> ignore)

    [<Fact>]
    let ``Les gardes rejettent les identifiants vides`` () =
        use c = offlineClient ()
        assertArgumentError (fun () -> c.StartAsync("") |> run |> ignore)
        assertArgumentError (fun () -> c.StopAsync("") |> run |> ignore)
        assertArgumentError (fun () -> c.DeleteAsync("") |> run |> ignore)
        assertArgumentError (fun () -> c.InspectAsync("") |> run |> ignore)
        assertArgumentError (fun () -> c.GetLogs("") |> run |> ignore)
        assertArgumentError (fun () -> c.GetLogsStream("") |> ignore)
        assertArgumentError (fun () -> c.Exec("", [ "ls" ]) |> run |> ignore)
        assertArgumentError (fun () -> c.TopContainerAsync("") |> run |> ignore)
        assertArgumentError (fun () -> c.GetContainerStatsAsync("") |> run |> ignore)
        assertArgumentError (fun () -> c.RenameContainerAsync("", "nouveau") |> run |> ignore)

    [<Fact>]
    let ``Exec rejette une commande nulle`` () =
        use c = offlineClient ()
        assertArgumentError (fun () -> c.Exec("c1", (null : IEnumerable<string>)) |> run |> ignore)

    [<Fact>]
    let ``Les gardes des images et registres rejettent les valeurs vides`` () =
        use c = offlineClient ()
        assertArgumentError (fun () -> c.PullImageAsync("") |> run |> ignore)
        assertArgumentError (fun () -> c.InspectImageAsync("") |> run |> ignore)
        assertArgumentError (fun () -> c.RemoveImageAsync("") |> run |> ignore)
        assertArgumentError (fun () -> c.TagImageAsync("", "cible") |> run |> ignore)
        assertArgumentError (fun () -> c.TagImageAsync("source", "") |> run |> ignore)
        assertArgumentError (fun () -> c.LoginRegistryAsync("", "u", "p") |> run |> ignore)
        assertArgumentError (fun () -> c.LoginRegistryAsync("r", "", "p") |> run |> ignore)
        assertArgumentError (fun () -> c.LoginRegistryAsync("r", "u", "") |> run |> ignore)
        assertArgumentError (fun () -> c.LogoutRegistryAsync("") |> run |> ignore)
        assertArgumentError (fun () -> c.RenameContainerAsync("id", "") |> run |> ignore)

