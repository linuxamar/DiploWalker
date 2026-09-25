namespace DiploWalker.Integration.Tests

module ContainerIntegrationTests =

    open System
    open System.Collections.Generic
    open System.IO
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
    open DiploWalker.Grpc
    open DiploWalker.Grpc.Container
    open DiploWalker.Container.Services
    open DiploWalker.Abstractions.Interfaces
    open DiploWalker.Disk
    open DiscUtils
    open DiscUtils.Fat
    open DiscUtils.Partitions
    open DiscUtils.Streams

    open DiploWalker.Core.Connection
    open DiploWalker.TestHelpers

    type private MockContainerdClient() =

        let mutable containers = Map.empty<string, Map<string, string>>
        let mutable started = Set.empty<string>
        let mutable mountsById = Map.empty<string, (string * string * bool) list>
        let ownedDocs = List<JsonDocument>()

        let keepDoc (doc: JsonDocument) =
            ownedDocs.Add(doc)
            doc

        member _.MountsById = mountsById

        interface IContainerdClient with
            member _.CreateContainer(_ns, id, image, _labels, _env, _command, _args, _mem, _cpu, _pid, mounts) =
                containers <- containers |> Map.add id (Map.ofList [ "image", image; "id", id ])
                mountsById <- mountsById |> Map.add id mounts
                id

            member _.StartContainer(_ns, id, _detach) = started <- started |> Set.add id

            member _.StopContainer(_ns, _id, _timeout) = Task.CompletedTask

            member _.DeleteContainer(_ns, _id, _force) = ()

            member _.InspectContainer(_ns, id) =
                let doc =
                    JsonDocument.Parse(
                        sprintf
                            """{"id":"%s","image":"test:latest","created_at":"2025-01-15T10:30:00Z","exit_code":0,"labels":{},"env":{}}"""
                            id
                    )
                    |> keepDoc

                doc.RootElement

            member _.TaskInfo(_ns, id) =
                let status = if started |> Set.contains id then "running" else "created"

                let doc =
                    JsonDocument.Parse(sprintf """{"pid":1234,"status":"%s"}""" status) |> keepDoc

                doc.RootElement

            member _.ListContainers(_ns, _all) =
                containers |> Map.toList |> List.map fst

            member _.GetContainerLogs(_ns, _id, _tail, _follow, _since) = [ "log line 1"; "log line 2" ]

            member _.GetContainerLogsStream(_ns, _id, _tail, _since, _ct) =
                let mutable index = 0

                { new System.Collections.Generic.IAsyncEnumerable<string> with
                    member _.GetAsyncEnumerator(_ct) =
                        { new System.Collections.Generic.IAsyncEnumerator<string> with
                            member _.Current = if index = 0 then "log line 1" else "log line 2"

                            member _.MoveNextAsync() =
                                if index >= 2 then
                                    ValueTask<bool>(false)
                                else
                                    index <- index + 1
                                    ValueTask<bool>(true)

                            member _.DisposeAsync() = ValueTask() } }

            member _.ExecInContainer(_ns, _id, command) =
                sprintf "exec: %s" (command |> String.concat " ")

            member _.Version() = "1.0.0-test"

            member _.PullImage(_image, _userArg) = "pulled"

            member _.Namespaces() = [ "default" ]

            member _.RenameContainer(_ns, _id, _newName) = ()

            member _.TopContainer(_ns, _id) = "1234 root test"

            member _.GetContainerStats(_ns, _id) =
                (JsonDocument.Parse(
                    """{"cpu":{"usage":100},"memory":{"usage":1024,"limit":65536},"pids":{"current":2}}"""
                 )
                 |> keepDoc)
                    .RootElement

            member _.ListImages(_ns) =
                [ (JsonDocument.Parse(
                      """{"id":"test:latest","ref":"test:latest","repository":"test","tag":"latest","size":1024,"created_at":"2025-01-10T08:00:00Z"}"""
                   )
                   |> keepDoc)
                      .RootElement ]

            member _.InspectImage(_ns, imageRef) =
                let doc =
                    JsonDocument.Parse(
                        sprintf
                            """{"id":"%s","ref":"%s","repository":"library/test","tag":"latest","size":1024,"created_at":"2025-01-10T08:00:00Z","labels":{}}"""
                            imageRef
                            imageRef
                    )
                    |> keepDoc

                doc.RootElement

            member _.RemoveImage(_ns, _imageRef) = "removed"

            member _.TagImage(_ns, _source, _target) = ()

            member _.StartContainerWithLogs(_ns, id, _logFile) = started <- started |> Set.add id

            member _.PauseContainer(_ns, _id) = ()

            member _.ResumeContainer(_ns, _id) = ()

            member _.WaitForContainerExit(_ns, _id, _timeoutSeconds, _ct) =
                System.Threading.Tasks.Task.FromResult(0)

            member _.UpdateContainer(_ns, _id, _mem, _cpu, _pids) = ()

            member _.CreateNamespace(_name) = ()

            member _.DeleteNamespace(_name) = ()

            member _.ExportImage(_ns, _imageRef, _tarFile) = ()

            member _.ImportImage(_ns, _tarFile) = [ "imported" ]

            member _.StartExec(_ns, _id, _command, stdin, stdout, _stderr) =
                stdin.CopyTo(stdout)
                0

    let private startAppWith (mock: MockContainerdClient) () =
        let builder = WebApplication.CreateBuilder()
        builder.Services.AddCodeFirstGrpc() |> ignore
        builder.Services.AddSingleton<IContainerdClient>(mock) |> ignore
        builder.Services.AddSingleton<IDiskMounter>(DiploWalker.Disk.DiskMounter()) |> ignore
        builder.Services.AddSingleton<System.Net.Http.HttpClient>(DiploWalker.Container.Services.RegistrySearch.sharedClient)
        |> ignore
        builder.Services.AddSingleton<ContainerServiceImpl>(fun sp ->
            ContainerServiceImpl(
                sp.GetRequiredService<IContainerdClient>(),
                sp.GetRequiredService<IDiskMounter>(),
                sp.GetRequiredService<System.Net.Http.HttpClient>()
            ))
        |> ignore

        builder.WebHost.ConfigureKestrel(fun opts ->
            opts.Listen(System.Net.IPAddress.Loopback, 0, fun lo -> lo.Protocols <- HttpProtocols.Http2))
        |> ignore

        let app = builder.Build()
        app.MapGrpcService<ContainerServiceImpl>() |> ignore
        app.StartAsync().GetAwaiter().GetResult()
        let address = app.Urls |> Seq.head
        app, address

    let private startApp () =
        let mock = MockContainerdClient()
        startAppWith mock ()

    let private startAppWithMock () =
        let mock = MockContainerdClient()
        let app, address = startAppWith mock ()
        app, address, mock

    let private stopApp (app: WebApplication) =
        app.StopAsync().GetAwaiter().GetResult()
        (app :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()

    let private withContainerApp (f: string -> 'a) =
        let app, address = startApp ()

        try
            f address
        finally
            stopApp app

    let private withContainerAppMock (f: string -> MockContainerdClient -> 'a) =
        let app, address, mock = startAppWithMock ()

        try
            f address mock
        finally
            stopApp app

    /// CrÃ©e une image disque FAT 64 Mo (table de partitions BIOS) contenant
    /// les fichiers (chemin relatif, contenu texte) fournis.
    let private createFatImage (path: string) (contents: (string * string) list) =
        use fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite)
        use disk = Raw.Disk.Initialize(fs, Ownership.None, 64L * 1024L * 1024L)
        BiosPartitionTable.Initialize(disk, WellKnownPartitionType.WindowsFat) |> ignore
        use fat = FatFileSystem.FormatPartition(disk, 0, "DIPLO")

        for (relPath, content) in contents do
            let parent = Path.GetDirectoryName(relPath)

            if not (String.IsNullOrEmpty parent) && not (fat.DirectoryExists parent) then
                fat.CreateDirectory(parent)

            use w = fat.OpenFile(relPath, FileMode.Create, FileAccess.ReadWrite)
            use sw = new StreamWriter(w)
            sw.Write(content)
            sw.Flush()

    [<Fact>]
    let ``CreateContainer via gRPC retourne l'ID`` () =
        withContainerApp (fun address ->
            use channel = DiploWalkerChannel.forAddress address
            let client = channel.CreateGrpcService<IContainerService>()

            let req: CreateContainerRequest =
                { Name = "ctn-grpc"
                  Image = "test:latest"
                  Command = List<string>()
                  Args = List<string>()
                  Env = Dictionary()
                  Labels = Dictionary()
                  MemoryLimit = 0L
                  CpuShares = 0
                  PidLimit = 0
                  Mounts = List<ContainerMount>()
                  RestartPolicy = ""
                  RestartMaxCount = 0
                  Ports = List<PortMapping>()
                  HealthCheck = Unchecked.defaultof<HealthCheckConfig> }

            let result = client.CreateContainer(req, CancellationToken.None).Result
            String.IsNullOrEmpty(result.Id) |> should equal false
            result.State |> should equal ContainerState.Created
            String.IsNullOrEmpty(result.CreatedAt) |> should equal false)

    [<Fact>]
    let ``CreateContainer puis StartContainer via gRPC`` () =
        withContainerApp (fun address ->
            use channel = DiploWalkerChannel.forAddress address
            let client = channel.CreateGrpcService<IContainerService>()

            let createReq: CreateContainerRequest =
                { Name = "to-start"
                  Image = "test:latest"
                  Command = List<string>()
                  Args = List<string>()
                  Env = Dictionary()
                  Labels = Dictionary()
                  MemoryLimit = 0L
                  CpuShares = 0
                  PidLimit = 0
                  Mounts = List<ContainerMount>()
                  RestartPolicy = ""
                  RestartMaxCount = 0
                  Ports = List<PortMapping>()
                  HealthCheck = Unchecked.defaultof<HealthCheckConfig> }

            let createResult = client.CreateContainer(createReq, CancellationToken.None).Result
            let startReq: StartContainerRequest = { Id = createResult.Id; Attach = false }
            let startResult = client.StartContainer(startReq, CancellationToken.None).Result
            startResult.State |> should equal ContainerState.Running
            startResult.Message |> should equal "Conteneur dÃ©marrÃ©")

    [<Fact>]
    let ``CreateContainer avec montage d'un repertoire via gRPC`` () =
        withContainerApp (fun address ->
            use channel = DiploWalkerChannel.forAddress address
            let client = channel.CreateGrpcService<IContainerService>()
            let mountDir = TestHelpers.createTempDir "mount"

            try
                let createReq: CreateContainerRequest =
                    { Name = "avec-mount"
                      Image = "test:latest"
                      Command = List<string>()
                      Args = List<string>()
                      Env = Dictionary()
                      Labels = Dictionary()
                      MemoryLimit = 0L
                      CpuShares = 0
                      PidLimit = 0
                      Mounts = List<ContainerMount>()
                      RestartPolicy = ""
                      RestartMaxCount = 0
                      Ports = List<PortMapping>()
                      HealthCheck = Unchecked.defaultof<HealthCheckConfig> }

                createReq.Mounts.Add(
                    { Source = mountDir
                      Destination = "C:\\data"
                      ReadOnly = false }
                )

                let createResult = client.CreateContainer(createReq, CancellationToken.None).Result
                String.IsNullOrEmpty(createResult.Id) |> should equal false
                createResult.State |> should equal ContainerState.Created
                let delReq: DeleteContainerRequest = { Id = createResult.Id; Force = false }
                let delResult = client.DeleteContainer(delReq, CancellationToken.None).Result
                delResult.Success |> should equal true
            finally
                TestHelpers.cleanupDir mountDir)

    [<Fact>]
    let ``InspectContainer via gRPC retourne les infos`` () =
        withContainerApp (fun address ->
            use channel = DiploWalkerChannel.forAddress address
            let client = channel.CreateGrpcService<IContainerService>()

            let createReq: CreateContainerRequest =
                { Name = "to-inspect"
                  Image = "test:latest"
                  Command = List<string>()
                  Args = List<string>()
                  Env = Dictionary()
                  Labels = Dictionary()
                  MemoryLimit = 0L
                  CpuShares = 0
                  PidLimit = 0
                  Mounts = List<ContainerMount>()
                  RestartPolicy = ""
                  RestartMaxCount = 0
                  Ports = List<PortMapping>()
                  HealthCheck = Unchecked.defaultof<HealthCheckConfig> }

            let createResult = client.CreateContainer(createReq, CancellationToken.None).Result
            let inspectReq: InspectContainerRequest = { Id = createResult.Id }

            let inspectResult =
                client.InspectContainer(inspectReq, CancellationToken.None).Result

            inspectResult.Id |> should equal createResult.Id
            inspectResult.Image |> should equal "test:latest")

    [<Fact>]
    let ``ListContainers via gRPC`` () =
        withContainerApp (fun address ->
            use channel = DiploWalkerChannel.forAddress address
            let client = channel.CreateGrpcService<IContainerService>()

            let createReq: CreateContainerRequest =
                { Name = ""
                  Image = "test:latest"
                  Command = List<string>()
                  Args = List<string>()
                  Env = Dictionary()
                  Labels = Dictionary()
                  MemoryLimit = 0L
                  CpuShares = 0
                  PidLimit = 0
                  Mounts = List<ContainerMount>()
                  RestartPolicy = ""
                  RestartMaxCount = 0
                  Ports = List<PortMapping>()
                  HealthCheck = Unchecked.defaultof<HealthCheckConfig> }

            client.CreateContainer(createReq, CancellationToken.None).Result |> ignore
            client.CreateContainer(createReq, CancellationToken.None).Result |> ignore
            let listReq: ListContainersRequest = { All = false; NamespaceName = ""; Filters = Dictionary() }
            let listResult = client.ListContainers(listReq, CancellationToken.None).Result
            listResult.Containers.Count |> should equal 2)

    [<Fact>]
    let ``ListNamespaces via gRPC`` () =
        withContainerApp (fun address ->
            use channel = DiploWalkerChannel.forAddress address
            let client = channel.CreateGrpcService<IContainerService>()
            let req: ListNamespacesRequest = { Placeholder = false }
            let result = client.ListNamespaces(req, CancellationToken.None).Result
            result.Namespaces |> should contain "default")

    [<Fact>]
    let ``GetVersion via gRPC`` () =
        withContainerApp (fun address ->
            use channel = DiploWalkerChannel.forAddress address
            let client = channel.CreateGrpcService<IContainerService>()
            let req: GetVersionRequest = { Placeholder = false }
            let result = client.GetVersion(req, CancellationToken.None).Result
            result.Version |> should equal "1.0.0-test")

    [<Fact>]
    let ``CreateContainer sans nom genere un id automatiquement`` () =
        withContainerApp (fun address ->
            use channel = DiploWalkerChannel.forAddress address
            let client = channel.CreateGrpcService<IContainerService>()

            let req: CreateContainerRequest =
                { Name = ""
                  Image = "test:latest"
                  Command = List<string>()
                  Args = List<string>()
                  Env = Dictionary()
                  Labels = Dictionary()
                  MemoryLimit = 0L
                  CpuShares = 0
                  PidLimit = 0
                  Mounts = List<ContainerMount>()
                  RestartPolicy = ""
                  RestartMaxCount = 0
                  Ports = List<PortMapping>()
                  HealthCheck = Unchecked.defaultof<HealthCheckConfig> }

            let result = client.CreateContainer(req, CancellationToken.None).Result
            String.IsNullOrEmpty(result.Id) |> should equal false)

    [<Fact>]
    let ``DeleteContainer via gRPC`` () =
        withContainerApp (fun address ->
            use channel = DiploWalkerChannel.forAddress address
            let client = channel.CreateGrpcService<IContainerService>()

            let createReq: CreateContainerRequest =
                { Name = "to-delete"
                  Image = "test:latest"
                  Command = List<string>()
                  Args = List<string>()
                  Env = Dictionary()
                  Labels = Dictionary()
                  MemoryLimit = 0L
                  CpuShares = 0
                  PidLimit = 0
                  Mounts = List<ContainerMount>()
                  RestartPolicy = ""
                  RestartMaxCount = 0
                  Ports = List<PortMapping>()
                  HealthCheck = Unchecked.defaultof<HealthCheckConfig> }

            let createResult = client.CreateContainer(createReq, CancellationToken.None).Result
            let deleteReq: DeleteContainerRequest = { Id = createResult.Id; Force = false }
            let deleteResult = client.DeleteContainer(deleteReq, CancellationToken.None).Result
            deleteResult.Success |> should equal true)

    [<Fact>]
    let ``StopContainer via gRPC`` () =
        withContainerApp (fun address ->
            use channel = DiploWalkerChannel.forAddress address
            let client = channel.CreateGrpcService<IContainerService>()

            let createReq: CreateContainerRequest =
                { Name = "to-stop"
                  Image = "test:latest"
                  Command = List<string>()
                  Args = List<string>()
                  Env = Dictionary()
                  Labels = Dictionary()
                  MemoryLimit = 0L
                  CpuShares = 0
                  PidLimit = 0
                  Mounts = List<ContainerMount>()
                  RestartPolicy = ""
                  RestartMaxCount = 0
                  Ports = List<PortMapping>()
                  HealthCheck = Unchecked.defaultof<HealthCheckConfig> }

            let createResult = client.CreateContainer(createReq, CancellationToken.None).Result

            let stopReq: StopContainerRequest =
                { Id = createResult.Id
                  TimeoutSeconds = 5 }

            let stopResult = client.StopContainer(stopReq, CancellationToken.None).Result
            stopResult.State |> should equal ContainerState.Stopped
            stopResult.Message |> should equal "Conteneur arrÃªtÃ©")

    [<Fact>]
    let ``CreateContainer avec image vide via gRPC lance exception`` () =
        withContainerApp (fun address ->
            use channel = DiploWalkerChannel.forAddress address
            let client = channel.CreateGrpcService<IContainerService>()

            let req: CreateContainerRequest =
                { Name = "no-image"
                  Image = ""
                  Command = List<string>()
                  Args = List<string>()
                  Env = Dictionary()
                  Labels = Dictionary()
                  MemoryLimit = 0L
                  CpuShares = 0
                  PidLimit = 0
                  Mounts = List<ContainerMount>()
                  RestartPolicy = ""
                  RestartMaxCount = 0
                  Ports = List<PortMapping>()
                  HealthCheck = Unchecked.defaultof<HealthCheckConfig> }

            let ex =
                Assert.Throws<AggregateException>(fun () ->
                    client.CreateContainer(req, CancellationToken.None).Result |> ignore)

            ex.InnerException.Message |> should haveSubstring "L'image du conteneur")

    [<Fact>]
    let ``CreateContainer avec une image disque montee via gRPC puis DeleteContainer reecrit l'image`` () =
        withContainerAppMock (fun address mock ->
            let root = TestHelpers.createTempDir "image"
            let imagePath = Path.Combine(root, "data.img")

            try
                createFatImage imagePath [ "hello.txt", "v1" ]
                use channel = DiploWalkerChannel.forAddress address
                let client = channel.CreateGrpcService<IContainerService>()

                let createReq: CreateContainerRequest =
                    { Name = "avec-image"
                      Image = "test:latest"
                      Command = List<string>()
                      Args = List<string>()
                      Env = Dictionary()
                      Labels = Dictionary()
                      MemoryLimit = 0L
                      CpuShares = 0
                      PidLimit = 0
                      Mounts = List<ContainerMount>()
                      RestartPolicy = ""
                      RestartMaxCount = 0
                      Ports = List<PortMapping>()
                      HealthCheck = Unchecked.defaultof<HealthCheckConfig> }

                createReq.Mounts.Add(
                    { Source = imagePath
                      Destination = "C:\\data"
                      ReadOnly = false }
                )

                let createResult = client.CreateContainer(createReq, CancellationToken.None).Result
                String.IsNullOrEmpty(createResult.Id) |> should equal false
                let mounts = mock.MountsById |> Map.tryFind createResult.Id
                mounts.IsSome |> should equal true
                let (hostPath, dest, ro) = mounts.Value |> List.head
                dest |> should equal "C:\\data"
                ro |> should equal false
                Directory.Exists hostPath |> should equal true
                File.ReadAllText(Path.Combine(hostPath, "hello.txt")) |> should equal "v1"
                File.WriteAllText(Path.Combine(hostPath, "hello.txt"), "v2")
                let delReq: DeleteContainerRequest = { Id = createResult.Id; Force = false }
                let delResult = client.DeleteContainer(delReq, CancellationToken.None).Result
                delResult.Success |> should equal true
                let re = Path.Combine(root, "re")
                DiploWalker.Disk.FsImage.extract imagePath re false |> Result.defaultWith failwith |> ignore
                File.ReadAllText(Path.Combine(re, "hello.txt")) |> should equal "v2"
            finally
                TestHelpers.cleanupDir root)



