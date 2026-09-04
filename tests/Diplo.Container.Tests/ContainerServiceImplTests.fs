namespace Diplo.Container.Tests

[<Xunit.Collection("registry-state")>]
module ContainerServiceImplTests =

    open System
    open System.Collections.Generic
    open System.IO
    open System.Text.Json
    open System.Threading
    open System.Threading.Tasks
    open Xunit
    open FsUnit.Xunit
    open Grpc.Core
    open Diplo.Grpc
    open Diplo.Grpc.Container
    open Diplo.Container.Services
    open Diplo.Abstractions.Interfaces
    open Diplo.Disk
    open Diplo.Container

    /// Faux mounter : retourne un volume dont le HostPath dérive de la source
    /// et enregistre les libérations (writeBack) pour les assertions.
    type MockDiskMounter() =
        let mutable mounted = ResizeArray<string * string * bool>()
        let mutable disposed = ResizeArray<string>()

        interface IDiskMounter with
            member _.Mount(source, destination, readOnly) =
                mounted.Add(source, destination, readOnly)

                { Source = source
                  HostPath = source + "-staging"
                  Destination = destination
                  ReadOnly = readOnly
                  Dispose = fun () -> disposed.Add(source) }

        member _.Mounted = mounted |> Seq.toList
        member _.Disposed = disposed |> Seq.toList

    let createService () =
        let mock = MockContainerdClient()
        let mounter = MockDiskMounter()
        let svc = ContainerServiceImpl(mock.Mock, mounter)
        svc, mock, mounter

    let shouldContain (substring: string) (text: string) = Assert.Contains(substring, text)

    let shouldNotContain (substring: string) (text: string) = Assert.DoesNotContain(substring, text)

    let createCtx () = CancellationToken.None

    /// Convertit une liste en IAsyncEnumerable (flux entrant gRPC bidirectionnel).
    let toAsyncSeq (items: 'T list) : IAsyncEnumerable<'T> =
        { new IAsyncEnumerable<'T> with
            member _.GetAsyncEnumerator(_ct) =
                let e = (items :> seq<'T>).GetEnumerator()

                { new IAsyncEnumerator<'T> with
                    member _.Current = e.Current
                    member _.MoveNextAsync() = ValueTask<bool>(e.MoveNext())

                    member _.DisposeAsync() =
                        e.Dispose()
                        ValueTask() } }

    type MockServerStreamWriter<'T>() =
        let items = List<'T>()
        member _.Items = items

        interface IServerStreamWriter<'T> with
            member _.WriteAsync(message: 'T) =
                items.Add(message)
                Task.CompletedTask

            member _.WriteAsync(message: 'T, _cancellationToken: CancellationToken) =
                items.Add(message)
                Task.CompletedTask

            member val WriteOptions = null with get, set

    [<Fact>]
    let ``CreateContainer avec nom retourne l'id et le nom`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()

        let req =
            { Name = "mon-conteneur"
              Image = "mcr.microsoft.com/dotnet/runtime:10.0"
              Env = Dictionary<string, string>()
              Command = ResizeArray<string>()
              Args = ResizeArray<string>()
              Labels = Dictionary<string, string>()
              PidLimit = 0
              MemoryLimit = 0L
              CpuShares = 0
              Mounts = ResizeArray<ContainerMount>()
              RestartPolicy = ""
              RestartMaxCount = 0
              Ports = ResizeArray<PortMapping>()
              HealthCheck = Unchecked.defaultof<HealthCheckConfig> }

        let result = (svc :> IContainerService).CreateContainer(req, ctx).Result
        result.Id |> should equal "mon-conteneur"
        result.Name |> should equal "mon-conteneur"
        result.State |> should equal ContainerState.Created

    [<Fact>]
    let ``CreateContainer sans nom genere un id automatiquement`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()

        let req =
            { Name = ""
              Image = "nginx:latest"
              Env = Dictionary<string, string>()
              Command = ResizeArray<string>()
              Args = ResizeArray<string>()
              Labels = Dictionary<string, string>()
              PidLimit = 0
              MemoryLimit = 0L
              CpuShares = 0
              Mounts = ResizeArray<ContainerMount>()
              RestartPolicy = ""
              RestartMaxCount = 0
              Ports = ResizeArray<PortMapping>()
              HealthCheck = Unchecked.defaultof<HealthCheckConfig> }

        let result = (svc :> IContainerService).CreateContainer(req, ctx).Result
        String.IsNullOrEmpty(result.Id) |> should equal false
        result.Id.Length |> should equal 32
        result.State |> should equal ContainerState.Created

    [<Fact>]
    let ``StartContainer retourne Running`` () =
        let dir =
            Path.Combine(Path.GetTempPath(), "diplo-logs-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory dir |> ignore

        try
            ContainerLogs.setLogsDir dir
            let svc, mock, _ = createService ()
            let ctx = createCtx ()

            mock.Mock.CreateContainer(
                "default",
                "c1",
                "nginx",
                Map.empty,
                Map.empty,
                Array.empty,
                Array.empty,
                0L,
                0L,
                0u,
                []
            )
            |> ignore

            let req: StartContainerRequest = { Id = "c1"; Attach = false }
            let result = (svc :> IContainerService).StartContainer(req, ctx).Result
            result.State |> should equal ContainerState.Running
            result.Message |> should equal "Conteneur démarré"
        finally
            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    [<Fact>]
    let ``StartContainer detache capture les logs du conteneur`` () =
        let dir =
            Path.Combine(Path.GetTempPath(), "diplo-logs-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory dir |> ignore

        try
            ContainerLogs.setLogsDir dir
            let svc, mock, _ = createService ()
            let ctx = createCtx ()

            mock.Mock.CreateContainer(
                "default",
                "c1",
                "nginx",
                Map.empty,
                Map.empty,
                Array.empty,
                Array.empty,
                0L,
                0L,
                0u,
                []
            )
            |> ignore

            let req: StartContainerRequest = { Id = "c1"; Attach = false }
            (svc :> IContainerService).StartContainer(req, ctx).Result |> ignore
            let logFile = ContainerLogs.fileFor "c1"
            File.Exists logFile |> should be True
            let content = File.ReadAllText logFile
            content |> shouldContain "Application started"
        finally
            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    [<Fact>]
    let ``StartContainer attache ne cree pas de journal`` () =
        let dir =
            Path.Combine(Path.GetTempPath(), "diplo-logs-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory dir |> ignore

        try
            ContainerLogs.setLogsDir dir
            let svc, mock, _ = createService ()
            let ctx = createCtx ()

            mock.Mock.CreateContainer(
                "default",
                "c1",
                "nginx",
                Map.empty,
                Map.empty,
                Array.empty,
                Array.empty,
                0L,
                0L,
                0u,
                []
            )
            |> ignore

            let req: StartContainerRequest = { Id = "c1"; Attach = true }
            let result = (svc :> IContainerService).StartContainer(req, ctx).Result
            result.State |> should equal ContainerState.Running
            File.Exists(ContainerLogs.fileFor "c1") |> should be False
        finally
            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    [<Fact>]
    let ``StopContainer avec timeout par defaut utilise 10`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        mock.Mock.CreateContainer(
            "default",
            "c1",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        let req: StopContainerRequest = { Id = "c1"; TimeoutSeconds = 0 }
        let result = (svc :> IContainerService).StopContainer(req, ctx).Result
        result.State |> should equal ContainerState.Stopped
        result.Message |> should equal "Conteneur arrêté"
        mock.StopCalled.["c1"] |> should equal 10

    [<Fact>]
    let ``StopContainer avec timeout personnalise`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        mock.Mock.CreateContainer(
            "default",
            "c1",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        let req: StopContainerRequest = { Id = "c1"; TimeoutSeconds = 30 }
        let result = (svc :> IContainerService).StopContainer(req, ctx).Result
        result.State |> should equal ContainerState.Stopped
        mock.StopCalled.["c1"] |> should equal 30

    [<Fact>]
    let ``DeleteContainer retourne success`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        mock.Mock.CreateContainer(
            "default",
            "c1",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        let req = { Id = "c1"; Force = false }
        let result = (svc :> IContainerService).DeleteContainer(req, ctx).Result
        result.Success |> should equal true
        result.Message |> should equal "Conteneur supprimé"
        mock.DeletedContainers |> should contain "c1"

    [<Fact>]
    let ``InspectContainer retourne les metadonnees du conteneur`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        mock.Mock.CreateContainer(
            "default",
            "my-app",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        mock.Mock.StartContainer("default", "my-app", true)
        let req: InspectContainerRequest = { Id = "my-app" }
        let result = (svc :> IContainerService).InspectContainer(req, ctx).Result
        result.Id |> should equal "my-app"
        result.Name |> should equal "my-app"
        result.Image |> should equal "mcr.microsoft.com/dotnet/runtime:10.0"
        result.State |> should equal ContainerState.Running
        result.Pid |> should equal 1234
        result.Labels.["app"] |> should equal "test"
        result.Labels.["env"] |> should equal "dev"
        result.Env.["ASPNETCORE_ENVIRONMENT"] |> should equal "Development"
        result.CreatedAt |> should not' (be NullOrEmptyString)

    [<Fact>]
    let ``InspectContainer sans demarrage retourne status created`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        mock.Mock.CreateContainer(
            "default",
            "stopped-app",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        let req: InspectContainerRequest = { Id = "stopped-app" }
        let result = (svc :> IContainerService).InspectContainer(req, ctx).Result
        result.State |> should equal ContainerState.Created
        result.Pid |> should equal 0

    [<Fact>]
    let ``ListContainers retourne les conteneurs crees`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        mock.Mock.CreateContainer(
            "default",
            "app-1",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        mock.Mock.CreateContainer(
            "default",
            "app-2",
            "redis",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        let req =
            { All = true
              Filters = Dictionary<string, string>() }

        let result = (svc :> IContainerService).ListContainers(req, ctx).Result
        result.Containers.Count |> should equal 2
        result.Containers |> Seq.exists (fun c -> c.Id = "app-1") |> should equal true
        result.Containers |> Seq.exists (fun c -> c.Id = "app-2") |> should equal true

    [<Fact>]
    let ``ListContainers retourne vide quand aucun conteneur`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()

        let req =
            { All = false
              Filters = Dictionary<string, string>() }

        let result = (svc :> IContainerService).ListContainers(req, ctx).Result
        result.Containers.Count |> should equal 0

    [<Fact>]
    let ``GetVersion retourne la version containerd`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req: GetVersionRequest = { Placeholder = false }
        let result = (svc :> IContainerService).GetVersion(req, ctx).Result
        result.Version |> shouldContain "1.7.27"
        result.Revision |> shouldContain "abc123"

    [<Fact>]
    let ``ListNamespaces retourne les namespaces`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req: ListNamespacesRequest = { Placeholder = false }
        let result = (svc :> IContainerService).ListNamespaces(req, ctx).Result
        result.Namespaces.Count |> should equal 2
        result.Namespaces |> should contain "default"
        result.Namespaces |> should contain "moby"

    [<Fact>]
    let ``GetContainerLogs ecrit les lignes dans le stream`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        mock.Mock.CreateContainer(
            "default",
            "c1",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        let req =
            { Id = "c1"
              Tail = 10
              Follow = false
              Since = "" }

        let enumerable = (svc :> IContainerService).GetContainerLogs(req, ctx)
        let enumerator = enumerable.GetAsyncEnumerator(CancellationToken.None)
        let items = List<ContainerLogEntry>()
        let mutable hasNext = enumerator.MoveNextAsync().Result

        while hasNext do
            items.Add(enumerator.Current)
            hasNext <- enumerator.MoveNextAsync().Result

        items.Count |> should equal 2
        items.[0].Log |> should equal "2025-01-15T10:30:01Z Application started"
        items.[1].Log |> should equal "2025-01-15T10:30:02Z Listening on port 8080"
        items |> Seq.forall (fun e -> e.Stream = "stdout") |> should equal true

    [<Fact>]
    let ``ExecInContainer ecrit la sortie dans le stream`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        mock.Mock.CreateContainer(
            "default",
            "c1",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        let req =
            { Id = "c1"
              Command = ResizeArray<string>()
              AttachStdin = false
              AttachStdout = true
              AttachStderr = true }

        req.Command.Add("echo")
        req.Command.Add("hello")
        let enumerable = (svc :> IContainerService).ExecInContainer(req, ctx)
        let enumerator = enumerable.GetAsyncEnumerator(CancellationToken.None)
        let items = List<ExecOutput>()
        let mutable hasNext = enumerator.MoveNextAsync().Result

        while hasNext do
            items.Add(enumerator.Current)
            hasNext <- enumerator.MoveNextAsync().Result

        items.Count |> should equal 1
        items.[0].Stream |> should equal "stdout"

        System.Text.Encoding.UTF8.GetString(items.[0].Data)
        |> should equal "Output of: echo hello"

    [<Fact>]
    let ``PullImage avec image valide retourne succes`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        let req: PullImageRequest =
            { Image = "mcr.microsoft.com/dotnet/runtime:10.0"
              User = "" }

        let result = (svc :> IContainerService).PullImage(req, ctx).Result
        result.Image |> should equal "mcr.microsoft.com/dotnet/runtime:10.0"
        result.Message |> shouldContain "image pulled"
        mock.PulledImages |> should contain "mcr.microsoft.com/dotnet/runtime:10.0"

    [<Fact>]
    let ``PullImage transmet le --user inline au client`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()

        let req: PullImageRequest =
            { Image = "myregistry.azurecr.io/team/app:latest"
              User = "inline:secret" }

        let result = (svc :> IContainerService).PullImage(req, ctx).Result
        result.Message |> shouldContain "image pulled"

    [<Fact>]
    let ``PullImage avec image vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req: PullImageRequest = { Image = ""; User = "" }

        Assert.ThrowsAsync<RpcException>(fun () -> (svc :> IContainerService).PullImage(req, ctx))
        |> ignore

    [<Fact>]
    let ``PullImage avec image servercore fonctionne`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        let req: PullImageRequest =
            { Image = "mcr.microsoft.com/windows/servercore:ltsc2022"
              User = "" }

        let result = (svc :> IContainerService).PullImage(req, ctx).Result
        result.Image |> should equal "mcr.microsoft.com/windows/servercore:ltsc2022"

        mock.PulledImages
        |> should contain "mcr.microsoft.com/windows/servercore:ltsc2022"

    // --- RenameContainer ---
    [<Fact>]
    let ``RenameContainer avec id et nom retourne success`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        mock.Mock.CreateContainer(
            "default",
            "c1",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        let req = { Id = "c1"; NewName = "nouveau-nom" }
        let result = (svc :> IContainerService).RenameContainer(req, ctx).Result
        result.Success |> should equal true
        result.Message |> shouldContain "nouveau-nom"

    [<Fact>]
    let ``RenameContainer avec id vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req = { Id = ""; NewName = "nom" }

        let ex =
            Assert.Throws<AggregateException>(fun () ->
                (svc :> IContainerService).RenameContainer(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    [<Fact>]
    let ``RenameContainer avec nom vide leve InvalidArgument`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        mock.Mock.CreateContainer(
            "default",
            "c1",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        let req = { Id = "c1"; NewName = "" }

        let ex =
            Assert.Throws<AggregateException>(fun () ->
                (svc :> IContainerService).RenameContainer(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    // --- TopContainer ---
    [<Fact>]
    let ``TopContainer retourne les processus`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        mock.Mock.CreateContainer(
            "default",
            "c1",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        mock.Mock.StartContainer("default", "c1", true)
        let req: TopContainerRequest = { Id = "c1" }
        let result = (svc :> IContainerService).TopContainer(req, ctx).Result
        result.Processes.Count |> should equal 1
        result.Processes.[0].Pid |> should equal 1234L
        result.Processes.[0].User |> should equal "root"
        result.Processes.[0].Command |> should equal "dotnet app.dll"

    [<Fact>]
    let ``TopContainer avec id vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req: TopContainerRequest = { Id = "" }

        let ex =
            Assert.Throws<AggregateException>(fun () ->
                (svc :> IContainerService).TopContainer(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    // --- GetContainerStats ---
    [<Fact>]
    let ``GetContainerStats retourne les metriques`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        mock.Mock.CreateContainer(
            "default",
            "c1",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        let req: GetContainerStatsRequest = { Id = "c1" }
        let result = (svc :> IContainerService).GetContainerStats(req, ctx).Result
        result.CpuUsage |> should equal 123456.0
        result.MemoryUsage |> should equal 1048576L
        result.MemoryLimit |> should equal 536870912L
        result.Pids |> should equal 3

    [<Fact>]
    let ``GetContainerStats avec id vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req: GetContainerStatsRequest = { Id = "" }

        let ex =
            Assert.Throws<AggregateException>(fun () ->
                (svc :> IContainerService).GetContainerStats(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    // --- ListImages ---
    [<Fact>]
    let ``ListImages retourne les images disponibles`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req: ListImagesRequest = { NamespaceName = "" }
        let result = (svc :> IContainerService).ListImages(req, ctx).Result
        result.Images.Count |> should equal 2

        result.Images
        |> Seq.exists (fun i -> i.Repository = "library/nginx")
        |> should equal true

        result.Images
        |> Seq.exists (fun i -> i.Repository = "library/redis")
        |> should equal true

    [<Fact>]
    let ``ListImages avec namespace vide utilise le namespace par defaut`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req: ListImagesRequest = { NamespaceName = "" }
        let result = (svc :> IContainerService).ListImages(req, ctx).Result
        result.Images.Count |> should equal 2

    // --- InspectImage ---
    [<Fact>]
    let ``InspectImage retourne les details de l'image`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()

        let req: InspectImageRequest =
            { Ref = "nginx:latest"
              NamespaceName = "" }

        let result = (svc :> IContainerService).InspectImage(req, ctx).Result
        result.Ref |> should equal "nginx:latest"
        result.Repository |> should equal "library/nginx"
        result.Labels.["maintainer"] |> should equal "nginx"

    [<Fact>]
    let ``InspectImage avec ref vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req: InspectImageRequest = { Ref = ""; NamespaceName = "" }

        let ex =
            Assert.Throws<AggregateException>(fun () ->
                (svc :> IContainerService).InspectImage(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    // --- RemoveImage ---
    [<Fact>]
    let ``RemoveImage supprime l'image avec succes`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        let req: RemoveImageRequest =
            { Ref = "nginx:latest"
              NamespaceName = "" }

        let result = (svc :> IContainerService).RemoveImage(req, ctx).Result
        result.Success |> should equal true
        result.Message |> shouldContain "nginx:latest"

    [<Fact>]
    let ``RemoveImage avec ref vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req: RemoveImageRequest = { Ref = ""; NamespaceName = "" }

        let ex =
            Assert.Throws<AggregateException>(fun () ->
                (svc :> IContainerService).RemoveImage(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    // --- TagImage ---
    [<Fact>]
    let ``TagImage tag l'image avec succes`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()

        let req =
            { Source = "nginx:latest"
              Target = "myregistry.azurecr.io/nginx:v1"
              NamespaceName = "" }

        let result = (svc :> IContainerService).TagImage(req, ctx).Result
        result.Source |> should equal "nginx:latest"
        result.Target |> should equal "myregistry.azurecr.io/nginx:v1"

    [<Fact>]
    let ``TagImage avec source vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()

        let req =
            { Source = ""
              Target = "myregistry.azurecr.io/nginx:v1"
              NamespaceName = "" }

        let ex =
            Assert.Throws<AggregateException>(fun () -> (svc :> IContainerService).TagImage(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    [<Fact>]
    let ``TagImage avec cible vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()

        let req =
            { Source = "nginx:latest"
              Target = ""
              NamespaceName = "" }

        let ex =
            Assert.Throws<AggregateException>(fun () -> (svc :> IContainerService).TagImage(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    // ─── Montages de volumes ───────────────────────────────────────────────

    let tempVolume (name: string) = Path.Combine(Path.GetTempPath(), name)

    [<Fact>]
    let ``CreateContainer monte les volumes avant la creation containerd`` () =
        let dataSrc = tempVolume "diplo-data"
        let secretsSrc = tempVolume "diplo-secrets"
        let svc, mock, mounter = createService ()
        let ctx = createCtx ()

        let req =
            { Name = "avec-volumes"
              Image = "nginx:latest"
              Env = Dictionary<string, string>()
              Command = ResizeArray<string>()
              Args = ResizeArray<string>()
              Labels = Dictionary<string, string>()
              PidLimit = 0
              MemoryLimit = 0L
              CpuShares = 0
              Mounts =
                ResizeArray(
                    [ { Source = dataSrc
                        Destination = "C:\\app"
                        ReadOnly = false }
                      { Source = secretsSrc
                        Destination = "C:\\keys"
                        ReadOnly = true } ]
                )
              RestartPolicy = ""
              RestartMaxCount = 0
              Ports = ResizeArray<PortMapping>()
              HealthCheck = Unchecked.defaultof<HealthCheckConfig> }

        let result = (svc :> IContainerService).CreateContainer(req, ctx).Result
        result.State |> should equal ContainerState.Created

        mounter.Mounted
        |> should equal [ (dataSrc, "C:\\app", false); (secretsSrc, "C:\\keys", true) ]

        mock.RecordedMounts.[result.Id]
        |> should
            equal
            [ (dataSrc + "-staging", "C:\\app", false)
              (secretsSrc + "-staging", "C:\\keys", true) ]

    [<Fact>]
    let ``CreateContainer sans montage ne fait aucun appel au mounter`` () =
        let svc, _, mounter = createService ()
        let ctx = createCtx ()

        let req =
            { Name = "sans-volume"
              Image = "nginx:latest"
              Env = Dictionary<string, string>()
              Command = ResizeArray<string>()
              Args = ResizeArray<string>()
              Labels = Dictionary<string, string>()
              PidLimit = 0
              MemoryLimit = 0L
              CpuShares = 0
              Mounts = ResizeArray<ContainerMount>()
              RestartPolicy = ""
              RestartMaxCount = 0
              Ports = ResizeArray<PortMapping>()
              HealthCheck = Unchecked.defaultof<HealthCheckConfig> }

        let result = (svc :> IContainerService).CreateContainer(req, ctx).Result
        mounter.Mounted |> should be Empty

    [<Fact>]
    let ``DeleteContainer libere les volumes montes (writeBack)`` () =
        let dataSrc = tempVolume "diplo-data"
        let svc, _, mounter = createService ()
        let ctx = createCtx ()

        let req =
            { Name = "a-supprimer"
              Image = "nginx:latest"
              Env = Dictionary<string, string>()
              Command = ResizeArray<string>()
              Args = ResizeArray<string>()
              Labels = Dictionary<string, string>()
              PidLimit = 0
              MemoryLimit = 0L
              CpuShares = 0
              Mounts =
                ResizeArray(
                    [ { Source = dataSrc
                        Destination = "C:\\app"
                        ReadOnly = false } ]
                )
              RestartPolicy = ""
              RestartMaxCount = 0
              Ports = ResizeArray<PortMapping>()
              HealthCheck = Unchecked.defaultof<HealthCheckConfig> }

        let result = (svc :> IContainerService).CreateContainer(req, ctx).Result
        mounter.Disposed |> should be Empty
        let delReq = { Id = result.Id; Force = false }
        (svc :> IContainerService).DeleteContainer(delReq, ctx).Result |> ignore
        mounter.Disposed |> should equal [ dataSrc ]

    [<Fact>]
    let ``DeleteContainer sans volume ne leve pas d'erreur`` () =
        let svc, _, mounter = createService ()
        let ctx = createCtx ()
        let req = { Id = "jamais-monte"; Force = false }
        (svc :> IContainerService).DeleteContainer(req, ctx).Result |> ignore
        mounter.Disposed |> should be Empty

    // ─── Pause / Unpause ────────────────────────────────────────────────────

    [<Fact>]
    let ``PauseContainer retourne Paused et appelle le client`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        mock.Mock.CreateContainer(
            "default",
            "c1",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        mock.Mock.StartContainer("default", "c1", true)
        let req: PauseContainerRequest = { Id = "c1" }
        let result = (svc :> IContainerService).PauseContainer(req, ctx).Result
        result.State |> should equal ContainerState.Paused
        result.Message |> shouldContain "pause"
        mock.PausedContainers |> should contain "c1"

    [<Fact>]
    let ``PauseContainer avec id vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req: PauseContainerRequest = { Id = "" }

        let ex =
            Assert.Throws<AggregateException>(fun () ->
                (svc :> IContainerService).PauseContainer(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    [<Fact>]
    let ``UnpauseContainer retourne Running et retire de la pause`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        mock.Mock.CreateContainer(
            "default",
            "c1",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        mock.Mock.StartContainer("default", "c1", true)
        mock.Mock.PauseContainer("default", "c1")
        let req: UnpauseContainerRequest = { Id = "c1" }
        let result = (svc :> IContainerService).UnpauseContainer(req, ctx).Result
        result.State |> should equal ContainerState.Running
        result.Message |> shouldContain "repris"
        mock.PausedContainers |> should not' (contain "c1")

    [<Fact>]
    let ``UnpauseContainer avec id vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req: UnpauseContainerRequest = { Id = "" }

        let ex =
            Assert.Throws<AggregateException>(fun () ->
                (svc :> IContainerService).UnpauseContainer(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    // ─── Wait ───────────────────────────────────────────────────────────────

    [<Fact>]
    let ``WaitContainer retourne le code de sortie quand le conteneur s'arrete`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        mock.Mock.CreateContainer(
            "default",
            "c1",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        mock.Mock.StartContainer("default", "c1", true)
        let req: WaitContainerRequest = { Id = "c1"; TimeoutSeconds = 5 }
        let result = (svc :> IContainerService).WaitContainer(req, ctx).Result
        result.ExitCode |> should equal 0
        result.State |> should equal ContainerState.Stopped
        result.Message |> shouldContain "terminé"

    [<Fact>]
    let ``WaitContainer retourne un timeout quand le conteneur tourne toujours`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        mock.Mock.CreateContainer(
            "default",
            "c1",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        let req: WaitContainerRequest = { Id = "c1"; TimeoutSeconds = 5 }
        let result = (svc :> IContainerService).WaitContainer(req, ctx).Result
        result.ExitCode |> should equal -1
        result.State |> should equal ContainerState.Running
        result.Message |> shouldContain "Timeout"

    [<Fact>]
    let ``WaitContainer avec id vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req: WaitContainerRequest = { Id = ""; TimeoutSeconds = 5 }

        let ex =
            Assert.Throws<AggregateException>(fun () ->
                (svc :> IContainerService).WaitContainer(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    // ─── Update ─────────────────────────────────────────────────────────────

    [<Fact>]
    let ``UpdateContainer retourne success et met a jour les limites`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        mock.Mock.CreateContainer(
            "default",
            "c1",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        let req: UpdateContainerRequest =
            { Id = "c1"
              MemoryLimit = 512L
              CpuShares = 1024
              PidLimit = 64
              RestartPolicy = "always" }

        let result = (svc :> IContainerService).UpdateContainer(req, ctx).Result
        result.Success |> should equal true
        result.Message |> shouldContain "mis à jour"
        mock.UpdatedContainers |> should contain "c1"

    [<Fact>]
    let ``UpdateContainer avec id vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()

        let req: UpdateContainerRequest =
            { Id = ""
              MemoryLimit = 0L
              CpuShares = 0
              PidLimit = 0
              RestartPolicy = "" }

        let ex =
            Assert.Throws<AggregateException>(fun () ->
                (svc :> IContainerService).UpdateContainer(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    // ─── Prune ──────────────────────────────────────────────────────────────

    [<Fact>]
    let ``PruneContainers supprime uniquement les conteneurs arretes`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        mock.Mock.CreateContainer(
            "default",
            "c1",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        mock.Mock.CreateContainer(
            "default",
            "c2",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        mock.Mock.StartContainer("default", "c2", true)
        mock.Mock.StopContainer("default", "c1", 10).GetAwaiter().GetResult()
        let req: PruneContainersRequest = { Placeholder = false }
        let result = (svc :> IContainerService).PruneContainers(req, ctx).Result
        result.Deleted.Count |> should equal 1
        result.Deleted |> should contain "c1"
        mock.DeletedContainers |> should contain "c1"
        mock.DeletedContainers |> should not' (contain "c2")

    [<Fact>]
    let ``PruneImages ne supprime aucune image taguee`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req: PruneImagesRequest = { Placeholder = false }
        let result = (svc :> IContainerService).PruneImages(req, ctx).Result
        result.Deleted.Count |> should equal 0

    // ─── Stats en streaming ─────────────────────────────────────────────────

    [<Fact>]
    let ``GetContainerStatsStream ecrit les metriques dans le stream`` () =
        let svc, mock, _ = createService ()

        mock.Mock.CreateContainer(
            "default",
            "c1",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        use cts = new CancellationTokenSource()
        let req: GetContainerStatsStreamRequest = { Id = "c1"; IntervalSeconds = 1 }
        let enumerable = (svc :> IContainerService).GetContainerStatsStream(req, cts.Token)
        let enumerator = enumerable.GetAsyncEnumerator(CancellationToken.None)
        let mutable hasNext = enumerator.MoveNextAsync().Result

        let first =
            if hasNext then
                enumerator.Current
            else
                Unchecked.defaultof<GetContainerStatsResponse>

        cts.Cancel()

        while hasNext do
            hasNext <- enumerator.MoveNextAsync().Result

        first.CpuUsage |> should equal 123456.0
        first.MemoryUsage |> should equal 1048576L
        first.MemoryLimit |> should equal 536870912L
        first.Pids |> should equal 3

    [<Fact>]
    let ``GetContainerStatsStream avec id vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req: GetContainerStatsStreamRequest = { Id = ""; IntervalSeconds = 0 }

        Assert.Throws<RpcException>(fun () -> (svc :> IContainerService).GetContainerStatsStream(req, ctx) |> ignore)
        |> ignore

    // ─── Événements ─────────────────────────────────────────────────────────

    [<Fact>]
    let ``WatchEvents emet un evenement create pour un conteneur existant`` () =
        let svc, mock, _ = createService ()

        mock.Mock.CreateContainer(
            "default",
            "c1",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        use cts = new CancellationTokenSource()
        let req: WatchEventsRequest = { Placeholder = false }
        let enumerable = (svc :> IContainerService).WatchEvents(req, cts.Token)
        let enumerator = enumerable.GetAsyncEnumerator(CancellationToken.None)
        let mutable hasNext = enumerator.MoveNextAsync().Result

        let first =
            if hasNext then
                enumerator.Current
            else
                Unchecked.defaultof<ContainerEvent>

        cts.Cancel()

        while hasNext do
            hasNext <- enumerator.MoveNextAsync().Result

        first.EventType |> should equal "create"
        first.Id |> should equal "c1"
        first.Status |> should equal "created"

    // ─── Exec bidirectionnel ────────────────────────────────────────────────

    [<Fact>]
    let ``ExecContainerStream renvoie la sortie du processus`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        mock.Mock.CreateContainer(
            "default",
            "c1",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        let data = System.Text.Encoding.UTF8.GetBytes("bonjour-exec")

        let messages =
            [ { ExecMessage.Id = "c1"
                Command = ResizeArray([ "echo"; "hello" ])
                Data = data
                Eof = false }
              { ExecMessage.Id = "c1"
                Command = ResizeArray()
                Data = Array.empty
                Eof = true } ]

        let outputs = List<ExecOutput>()

        let enumerable =
            (svc :> IContainerService).ExecContainerStream(toAsyncSeq messages, ctx)

        let enumerator = enumerable.GetAsyncEnumerator(CancellationToken.None)
        let mutable hasNext = enumerator.MoveNextAsync().Result

        while hasNext do
            outputs.Add(enumerator.Current)
            hasNext <- enumerator.MoveNextAsync().Result

        outputs.Count |> should equal 1
        outputs.[0].Stream |> should equal "stdout"

        System.Text.Encoding.UTF8.GetString(outputs.[0].Data)
        |> should equal "bonjour-exec"

    // ─── Copie de fichiers ──────────────────────────────────────────────────

    [<Fact>]
    let ``ReadFile retourne les donnees decodees en base64`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        mock.Mock.CreateContainer(
            "default",
            "c1",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        let req: ReadFileRequest =
            { Id = "c1"
              Path = "C:\\app\\config.json" }

        let result = (svc :> IContainerService).ReadFile(req, ctx).Result
        result.Success |> should equal true

        System.Text.Encoding.UTF8.GetString(result.Data)
        |> should equal "contenu-du-fichier"

    [<Fact>]
    let ``ReadFile avec id vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req: ReadFileRequest = { Id = ""; Path = "C:\\app" }

        let ex =
            Assert.Throws<AggregateException>(fun () -> (svc :> IContainerService).ReadFile(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    [<Fact>]
    let ``WriteFile ecrit le fichier avec succes`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        mock.Mock.CreateContainer(
            "default",
            "c1",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        let data = System.Text.Encoding.UTF8.GetBytes("contenu")

        let req: WriteFileRequest =
            { Id = "c1"
              Path = "C:\\app\\f.txt"
              Data = data }

        let result = (svc :> IContainerService).WriteFile(req, ctx).Result
        result.Success |> should equal true
        result.Message |> should equal "Fichier écrit"

    [<Fact>]
    let ``WriteFile avec chemin vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()

        let req: WriteFileRequest =
            { Id = "c1"
              Path = ""
              Data = Array.empty }

        let ex =
            Assert.Throws<AggregateException>(fun () -> (svc :> IContainerService).WriteFile(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    // ─── Commit / Export / Import ───────────────────────────────────────────

    [<Fact>]
    let ``CommitImage cree l'image depuis le conteneur`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()

        mock.Mock.CreateContainer(
            "default",
            "c1",
            "nginx",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        let req: CommitImageRequest =
            { ContainerId = "c1"
              ImageRef = "myregistry.azurecr.io/app:v1"
              Message = ""
              Author = "" }

        let result = (svc :> IContainerService).CommitImage(req, ctx).Result
        result.Success |> should equal true
        result.ImageRef |> should equal "myregistry.azurecr.io/app:v1"

    [<Fact>]
    let ``CommitImage avec reference vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()

        let req: CommitImageRequest =
            { ContainerId = "c1"
              ImageRef = ""
              Message = ""
              Author = "" }

        let ex =
            Assert.Throws<AggregateException>(fun () ->
                (svc :> IContainerService).CommitImage(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    [<Fact>]
    let ``ExportImage envoie les morceaux de l'image`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()

        let req: ExportImageRequest =
            { ImageRef = "nginx:latest"
              NamespaceName = "" }

        let enumerable = (svc :> IContainerService).ExportImage(req, ctx)
        let enumerator = enumerable.GetAsyncEnumerator(CancellationToken.None)
        let sb = System.Text.StringBuilder()
        let mutable hasNext = enumerator.MoveNextAsync().Result

        while hasNext do
            System.Text.Encoding.UTF8.GetString(enumerator.Current.Data)
            |> sb.Append
            |> ignore

            hasNext <- enumerator.MoveNextAsync().Result

        sb.ToString() |> should equal "fake-image-archive"

    [<Fact>]
    let ``ExportImage avec reference vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req: ExportImageRequest = { ImageRef = ""; NamespaceName = "" }

        let ex =
            Assert.Throws<RpcException>(fun () -> (svc :> IContainerService).ExportImage(req, ctx) |> ignore)

        ex.StatusCode |> should equal StatusCode.InvalidArgument

    [<Fact>]
    let ``ImportImage importe les morceaux et retourne les references`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let chunk = System.Text.Encoding.UTF8.GetBytes("archive-import")
        let chunks = [ { ImageChunk.Data = chunk } ]
        let result = (svc :> IContainerService).ImportImage(toAsyncSeq chunks, ctx).Result
        result.ImageRefs.Count |> should equal 1
        result.ImageRefs.[0] |> should equal "archive-import"
        result.Message |> shouldContain "1 image"

    // ─── Registres ──────────────────────────────────────────────────────────

    [<Fact>]
    let ``LoginRegistry retourne success`` () =
        let stateFile =
            Path.Combine(Path.GetTempPath(), "diplo-login-ok-" + Guid.NewGuid().ToString("N") + ".json")

        try
            RegistryAuth.setStateFile stateFile
            let svc, _, _ = createService ()
            let ctx = createCtx ()

            let req: LoginRegistryRequest =
                { Registry = "myregistry.azurecr.io"
                  Username = "user"
                  Password = "secret" }

            let result = (svc :> IContainerService).LoginRegistry(req, ctx).Result
            result.Success |> should equal true
            result.Message |> shouldContain "registre"
        finally
            try
                File.Delete stateFile
            with _ ->
                ()

    [<Fact>]
    let ``LoginRegistry sans registre leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()

        let req: LoginRegistryRequest =
            { Registry = ""
              Username = "user"
              Password = "" }

        let ex =
            Assert.Throws<AggregateException>(fun () ->
                (svc :> IContainerService).LoginRegistry(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    [<Fact>]
    let ``LogoutRegistry retourne success`` () =
        let stateFile =
            Path.Combine(Path.GetTempPath(), "diplo-logout-ok-" + Guid.NewGuid().ToString("N") + ".json")

        try
            RegistryAuth.setStateFile stateFile
            let svc, _, _ = createService ()
            let ctx = createCtx ()
            let req: LogoutRegistryRequest = { Registry = "myregistry.azurecr.io" }
            let result = (svc :> IContainerService).LogoutRegistry(req, ctx).Result
            result.Success |> should equal true
        finally
            try
                File.Delete stateFile
            with _ ->
                ()

    [<Fact>]
    let ``LoginRegistry persiste l'identifiant de maniere chiffree`` () =
        let stateFile =
            Path.Combine(Path.GetTempPath(), "diplo-login-" + Guid.NewGuid().ToString("N") + ".json")

        try
            RegistryAuth.setStateFile stateFile
            let svc, _, _ = createService ()
            let ctx = createCtx ()

            let req: LoginRegistryRequest =
                { Registry = "myregistry.azurecr.io"
                  Username = "user"
                  Password = "secret" }

            let result = (svc :> IContainerService).LoginRegistry(req, ctx).Result
            result.Success |> should equal true

            RegistryAuth.tryGetUserArg (RegistryAuth.stateFile ()) "myregistry.azurecr.io"
            |> should equal (Some "user:secret")

            let raw = File.ReadAllText stateFile
            raw |> shouldNotContain "secret"
        finally
            try
                File.Delete stateFile
            with _ ->
                ()

    [<Fact>]
    let ``LogoutRegistry retire l'identifiant persiste`` () =
        let stateFile =
            Path.Combine(Path.GetTempPath(), "diplo-logout-" + Guid.NewGuid().ToString("N") + ".json")

        try
            RegistryAuth.setStateFile stateFile
            let svc, _, _ = createService ()
            let ctx = createCtx ()

            let login: LoginRegistryRequest =
                { Registry = "myregistry.azurecr.io"
                  Username = "user"
                  Password = "secret" }

            (svc :> IContainerService).LoginRegistry(login, ctx).Result |> ignore
            let logout: LogoutRegistryRequest = { Registry = "myregistry.azurecr.io" }
            let result = (svc :> IContainerService).LogoutRegistry(logout, ctx).Result
            result.Success |> should equal true

            RegistryAuth.tryGetUserArg (RegistryAuth.stateFile ()) "myregistry.azurecr.io"
            |> should equal None
        finally
            try
                File.Delete stateFile
            with _ ->
                ()

    // ─── Namespaces ─────────────────────────────────────────────────────────

    [<Fact>]
    let ``CreateNamespace retourne success et appelle le client`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()
        let req: CreateNamespaceRequest = { Name = "prod" }
        let result = (svc :> IContainerService).CreateNamespace(req, ctx).Result
        result.Success |> should equal true
        result.Message |> shouldContain "prod"
        mock.CreatedNamespaces |> should contain "prod"

    [<Fact>]
    let ``CreateNamespace sans nom leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req: CreateNamespaceRequest = { Name = "" }

        let ex =
            Assert.Throws<AggregateException>(fun () ->
                (svc :> IContainerService).CreateNamespace(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    [<Fact>]
    let ``DeleteNamespace retourne success et appelle le client`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()
        let req: DeleteNamespaceRequest = { Name = "prod" }
        let result = (svc :> IContainerService).DeleteNamespace(req, ctx).Result
        result.Success |> should equal true
        result.Message |> shouldContain "prod"
        mock.RemovedNamespaces |> should contain "prod"

    [<Fact>]
    let ``DeleteNamespace sans nom leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req: DeleteNamespaceRequest = { Name = "" }

        let ex =
            Assert.Throws<AggregateException>(fun () ->
                (svc :> IContainerService).DeleteNamespace(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument
