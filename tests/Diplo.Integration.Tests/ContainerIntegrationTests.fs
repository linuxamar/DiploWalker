namespace Diplo.Integration.Tests

module ContainerIntegrationTests =

    open System
    open System.Collections.Generic
    open System.Threading
    open System.Text.Json
    open System.Threading.Tasks
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
    open Diplo.Grpc.Container
    open Diplo.Container.Services
    open Diplo.Abstractions.Interfaces

    type private MockContainerdClient() =

        let mutable containers = Map.empty<string, Map<string, string>>
        let mutable started = Set.empty<string>
        let ownedDocs = List<JsonDocument>()

        let keepDoc (doc: JsonDocument) =
            ownedDocs.Add(doc)
            doc

        interface IContainerdClient with
            member _.CreateContainer(_ns, id, image, _labels, _env, _command, _args, _mem, _cpu, _pid) =
                containers <- containers |> Map.add id (Map.ofList ["image", image; "id", id])
                id

            member _.StartContainer(_ns, id) =
                started <- started |> Set.add id

            member _.StopContainer(_ns, _id, _timeout) =
                Task.CompletedTask

            member _.DeleteContainer(_ns, _id, _force) = ()

            member _.InspectContainer(_ns, id) =
                let doc = JsonDocument.Parse(sprintf """{"id":"%s","image":"test:latest","created_at":"2025-01-15T10:30:00Z","exit_code":0,"labels":{},"env":{}}""" id) |> keepDoc
                doc.RootElement

            member _.TaskInfo(_ns, id) =
                let status = if started |> Set.contains id then "running" else "created"
                let doc = JsonDocument.Parse(sprintf """{"pid":1234,"status":"%s"}""" status) |> keepDoc
                doc.RootElement

            member _.ListContainers(_ns, _all) =
                containers |> Map.toList |> List.map fst

            member _.GetContainerLogs(_ns, _id, _tail, _follow, _since) =
                ["log line 1"; "log line 2"]

            member _.ExecInContainer(_ns, _id, command) =
                sprintf "exec: %s" (command |> String.concat " ")

            member _.Version() = "1.0.0-test"

            member _.PullImage(_image) = "pulled"

            member _.Namespaces() = ["default"]

            member _.RenameContainer(_ns, _id, _newName) = ()

            member _.TopContainer(_ns, _id) = "1234 root test"

            member _.GetContainerStats(_ns, _id) =
                (JsonDocument.Parse("""{"cpu":{"usage":100},"memory":{"usage":1024,"limit":65536},"pids":{"current":2}}""") |> keepDoc).RootElement

            member _.ListImages(_ns) =
                [(JsonDocument.Parse("""{"id":"test:latest","ref":"test:latest","repository":"test","tag":"latest","size":1024,"created_at":"2025-01-10T08:00:00Z"}""") |> keepDoc).RootElement]

            member _.InspectImage(_ns, imageRef) =
                let doc = JsonDocument.Parse(sprintf """{"id":"%s","ref":"%s","repository":"library/test","tag":"latest","size":1024,"created_at":"2025-01-10T08:00:00Z","labels":{}}""" imageRef imageRef) |> keepDoc
                doc.RootElement

            member _.RemoveImage(_ns, _imageRef) = "removed"

            member _.TagImage(_ns, _source, _target) = ()

    let private createChannel (address: string) =
        GrpcChannel.ForAddress(address, GrpcChannelOptions())

    let private startApp () =
        let mock = MockContainerdClient()
        let builder = WebApplication.CreateBuilder()
        builder.Services.AddCodeFirstGrpc() |> ignore
        builder.Services.AddSingleton<IContainerdClient>(mock) |> ignore
        builder.Services.AddSingleton<ContainerServiceImpl>() |> ignore
        builder.WebHost.ConfigureKestrel(fun opts ->
            opts.Listen(System.Net.IPAddress.Loopback, 0, fun lo ->
                lo.Protocols <- HttpProtocols.Http2)) |> ignore
        let app = builder.Build()
        app.MapGrpcService<ContainerServiceImpl>() |> ignore
        app.StartAsync().GetAwaiter().GetResult()
        let address = app.Urls |> Seq.head
        app, address

    let private stopApp (app: WebApplication) =
        app.StopAsync().GetAwaiter().GetResult()
        (app :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()

    [<Fact>]
    let ``CreateContainer via gRPC retourne l'ID`` () =
        let app, address = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<IContainerService>()
            let req: CreateContainerRequest =
                { Name = "ctn-grpc"; Image = "test:latest"
                  Command = List<string>(); Args = List<string>()
                  Env = Dictionary(); Labels = Dictionary()
                  MemoryLimit = 0L; CpuShares = 0; PidLimit = 0 }
            let result = client.CreateContainer(req, CancellationToken.None).Result
            String.IsNullOrEmpty(result.Id) |> should equal false
            result.State |> should equal ContainerState.Created
            String.IsNullOrEmpty(result.CreatedAt) |> should equal false
        finally
            stopApp app

    [<Fact>]
    let ``CreateContainer puis StartContainer via gRPC`` () =
        let app, address = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<IContainerService>()
            let createReq: CreateContainerRequest =
                { Name = "to-start"; Image = "test:latest"
                  Command = List<string>(); Args = List<string>()
                  Env = Dictionary(); Labels = Dictionary()
                  MemoryLimit = 0L; CpuShares = 0; PidLimit = 0 }
            let createResult = client.CreateContainer(createReq, CancellationToken.None).Result
            let startReq: StartContainerRequest = { Id = createResult.Id }
            let startResult = client.StartContainer(startReq, CancellationToken.None).Result
            startResult.State |> should equal ContainerState.Running
            startResult.Message |> should equal "Conteneur démarré"
        finally
            stopApp app

    [<Fact>]
    let ``InspectContainer via gRPC retourne les infos`` () =
        let app, address = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<IContainerService>()
            let createReq: CreateContainerRequest =
                { Name = "to-inspect"; Image = "test:latest"
                  Command = List<string>(); Args = List<string>()
                  Env = Dictionary(); Labels = Dictionary()
                  MemoryLimit = 0L; CpuShares = 0; PidLimit = 0 }
            let createResult = client.CreateContainer(createReq, CancellationToken.None).Result
            let inspectReq: InspectContainerRequest = { Id = createResult.Id }
            let inspectResult = client.InspectContainer(inspectReq, CancellationToken.None).Result
            inspectResult.Id |> should equal createResult.Id
            inspectResult.Image |> should equal "test:latest"
        finally
            stopApp app

    [<Fact>]
    let ``ListContainers via gRPC`` () =
        let app, address = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<IContainerService>()
            let createReq: CreateContainerRequest =
                { Name = ""; Image = "test:latest"
                  Command = List<string>(); Args = List<string>()
                  Env = Dictionary(); Labels = Dictionary()
                  MemoryLimit = 0L; CpuShares = 0; PidLimit = 0 }
            client.CreateContainer(createReq, CancellationToken.None).Result |> ignore
            client.CreateContainer(createReq, CancellationToken.None).Result |> ignore
            let listReq: ListContainersRequest = { All = false; Filters = Dictionary() }
            let listResult = client.ListContainers(listReq, CancellationToken.None).Result
            listResult.Containers.Count |> should equal 2
        finally
            stopApp app

    [<Fact>]
    let ``ListNamespaces via gRPC`` () =
        let app, address = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<IContainerService>()
            let req: ListNamespacesRequest = { Placeholder = false }
            let result = client.ListNamespaces(req, CancellationToken.None).Result
            result.Namespaces |> should contain "default"
        finally
            stopApp app

    [<Fact>]
    let ``GetVersion via gRPC`` () =
        let app, address = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<IContainerService>()
            let req: GetVersionRequest = { Placeholder = false }
            let result = client.GetVersion(req, CancellationToken.None).Result
            result.Version |> should equal "1.0.0-test"
        finally
            stopApp app

    [<Fact>]
    let ``CreateContainer sans nom genere un id automatiquement`` () =
        let app, address = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<IContainerService>()
            let req: CreateContainerRequest =
                { Name = ""; Image = "test:latest"
                  Command = List<string>(); Args = List<string>()
                  Env = Dictionary(); Labels = Dictionary()
                  MemoryLimit = 0L; CpuShares = 0; PidLimit = 0 }
            let result = client.CreateContainer(req, CancellationToken.None).Result
            String.IsNullOrEmpty(result.Id) |> should equal false
        finally
            stopApp app

    [<Fact>]
    let ``DeleteContainer via gRPC`` () =
        let app, address = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<IContainerService>()
            let createReq: CreateContainerRequest =
                { Name = "to-delete"; Image = "test:latest"
                  Command = List<string>(); Args = List<string>()
                  Env = Dictionary(); Labels = Dictionary()
                  MemoryLimit = 0L; CpuShares = 0; PidLimit = 0 }
            let createResult = client.CreateContainer(createReq, CancellationToken.None).Result
            let deleteReq: DeleteContainerRequest = { Id = createResult.Id; Force = false }
            let deleteResult = client.DeleteContainer(deleteReq, CancellationToken.None).Result
            deleteResult.Success |> should equal true
        finally
            stopApp app

    [<Fact>]
    let ``StopContainer via gRPC`` () =
        let app, address = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<IContainerService>()
            let createReq: CreateContainerRequest =
                { Name = "to-stop"; Image = "test:latest"
                  Command = List<string>(); Args = List<string>()
                  Env = Dictionary(); Labels = Dictionary()
                  MemoryLimit = 0L; CpuShares = 0; PidLimit = 0 }
            let createResult = client.CreateContainer(createReq, CancellationToken.None).Result
            let stopReq: StopContainerRequest = { Id = createResult.Id; TimeoutSeconds = 5 }
            let stopResult = client.StopContainer(stopReq, CancellationToken.None).Result
            stopResult.State |> should equal ContainerState.Stopped
            stopResult.Message |> should equal "Conteneur arrêté"
        finally
            stopApp app

    [<Fact>]
    let ``CreateContainer avec image vide via gRPC lance exception`` () =
        let app, address = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<IContainerService>()
            let req: CreateContainerRequest =
                { Name = "no-image"; Image = ""
                  Command = List<string>(); Args = List<string>()
                  Env = Dictionary(); Labels = Dictionary()
                  MemoryLimit = 0L; CpuShares = 0; PidLimit = 0 }
            let ex = Assert.Throws<AggregateException>(fun () -> client.CreateContainer(req, CancellationToken.None).Result |> ignore)
            ex.InnerException.Message |> should haveSubstring "L'image du conteneur"
        finally
            stopApp app
