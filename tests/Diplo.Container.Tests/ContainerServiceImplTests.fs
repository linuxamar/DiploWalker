namespace Diplo.Container.Tests

module ContainerServiceImplTests =

    open System
    open System.Collections.Generic
    open System.Text.Json
    open System.Threading
    open System.Threading.Tasks
    open Xunit
    open FsUnit.Xunit
    open Grpc.Core
    open Grpc.Core.Testing
    open Diplo.Grpc.Container
    open Diplo.Container.Services

    let createService () =
        let mock = MockContainerdClient()
        let svc = ContainerServiceImpl(mock.Mock)
        svc, mock

    let shouldContain (substring: string) (text: string) =
        Assert.Contains(substring, text)

    let createCtx () =
        TestServerCallContext.Create(
            "test", "localhost", DateTime.UtcNow, Metadata(), CancellationToken.None,
            "peer", Unchecked.defaultof<AuthContext>, Unchecked.defaultof<ContextPropagationToken>,
            Unchecked.defaultof<System.Func<Metadata,Task>>, Unchecked.defaultof<System.Func<WriteOptions>>, Unchecked.defaultof<System.Action<WriteOptions>>)

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
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = CreateContainerRequest(Name = "mon-conteneur", Image = "mcr.microsoft.com/dotnet/runtime:10.0")
        let result = svc.CreateContainer(req, ctx).Result
        result.Id |> should equal "mon-conteneur"
        result.Name |> should equal "mon-conteneur"
        result.State |> should equal ContainerState.Running

    [<Fact>]
    let ``CreateContainer sans nom genere un id automatiquement`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = CreateContainerRequest(Name = "", Image = "nginx:latest")
        let result = svc.CreateContainer(req, ctx).Result
        String.IsNullOrEmpty(result.Id) |> should equal false
        result.Id.Length |> should equal 32
        result.State |> should equal ContainerState.Running

    [<Fact>]
    let ``StartContainer retourne Running`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "c1", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u) |> ignore
        let req = StartContainerRequest(Id = "c1")
        let result = svc.StartContainer(req, ctx).Result
        result.State |> should equal ContainerState.Running
        result.Message |> should equal "Conteneur démarré"

    [<Fact>]
    let ``StopContainer avec timeout par defaut utilise 10`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "c1", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u) |> ignore
        let req = StopContainerRequest(Id = "c1", TimeoutSeconds = 0)
        let result = svc.StopContainer(req, ctx).Result
        result.State |> should equal ContainerState.Stopped
        result.Message |> should equal "Conteneur arrêté"
        mock.StopCalled.["c1"] |> should equal 10

    [<Fact>]
    let ``StopContainer avec timeout personnalise`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "c1", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u) |> ignore
        let req = StopContainerRequest(Id = "c1", TimeoutSeconds = 30)
        let result = svc.StopContainer(req, ctx).Result
        result.State |> should equal ContainerState.Stopped
        mock.StopCalled.["c1"] |> should equal 30

    [<Fact>]
    let ``DeleteContainer retourne success`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "c1", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u) |> ignore
        let req = DeleteContainerRequest(Id = "c1", Force = false)
        let result = svc.DeleteContainer(req, ctx).Result
        result.Success |> should equal true
        result.Message |> should equal "Conteneur supprimé"
        mock.DeletedContainers |> should contain "c1"

    [<Fact>]
    let ``InspectContainer retourne les metadonnees du conteneur`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "my-app", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u) |> ignore
        mock.Mock.StartContainer("default", "my-app")
        let req = InspectContainerRequest(Id = "my-app")
        let result = svc.InspectContainer(req, ctx).Result
        result.Id |> should equal "my-app"
        result.Name |> should equal "my-app"
        result.Image |> should equal "mcr.microsoft.com/dotnet/runtime:10.0"
        result.State |> should equal ContainerState.Running
        result.Pid |> should equal 1234L
        result.Labels.["app"] |> should equal "test"
        result.Labels.["env"] |> should equal "dev"
        result.Env.["ASPNETCORE_ENVIRONMENT"] |> should equal "Development"
        result.CreatedAt |> should not' (be NullOrEmptyString)

    [<Fact>]
    let ``InspectContainer sans demarrage retourne status created`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "stopped-app", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u) |> ignore
        let req = InspectContainerRequest(Id = "stopped-app")
        let result = svc.InspectContainer(req, ctx).Result
        result.State |> should equal ContainerState.Created
        result.Pid |> should equal 0L

    [<Fact>]
    let ``ListContainers retourne les conteneurs crees`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "app-1", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u) |> ignore
        mock.Mock.CreateContainer("default", "app-2", "redis", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u) |> ignore
        let req = ListContainersRequest(All = true)
        let result = svc.ListContainers(req, ctx).Result
        result.Containers.Count |> should equal 2
        result.Containers |> Seq.exists (fun c -> c.Id = "app-1") |> should equal true
        result.Containers |> Seq.exists (fun c -> c.Id = "app-2") |> should equal true

    [<Fact>]
    let ``ListContainers retourne vide quand aucun conteneur`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = ListContainersRequest(All = false)
        let result = svc.ListContainers(req, ctx).Result
        result.Containers.Count |> should equal 0

    [<Fact>]
    let ``GetVersion retourne la version containerd`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = GetVersionRequest()
        let result = svc.GetVersion(req, ctx).Result
        result.Version |> shouldContain "1.7.27"
        result.Version |> shouldContain "abc123"

    [<Fact>]
    let ``ListNamespaces retourne les namespaces`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = ListNamespacesRequest()
        let result = svc.ListNamespaces(req, ctx).Result
        result.Namespaces.Count |> should equal 2
        result.Namespaces |> should contain "default"
        result.Namespaces |> should contain "moby"

    [<Fact>]
    let ``GetContainerLogs ecrit les lignes dans le stream`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "c1", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u) |> ignore
        let writer = MockServerStreamWriter<ContainerLogEntry>()
        let req = GetContainerLogsRequest(Id = "c1", Tail = 10)
        svc.GetContainerLogs(req, writer, ctx).Wait()
        writer.Items.Count |> should equal 2
        writer.Items.[0].Log |> should equal "2025-01-15T10:30:01Z Application started"
        writer.Items.[1].Log |> should equal "2025-01-15T10:30:02Z Listening on port 8080"
        writer.Items |> Seq.forall (fun e -> e.Stream = "stdout") |> should equal true

    [<Fact>]
    let ``ExecInContainer ecrit la sortie dans le stream`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "c1", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u) |> ignore
        let writer = MockServerStreamWriter<ExecOutput>()
        let req = ExecInContainerRequest(Id = "c1")
        req.Command.Add("echo")
        req.Command.Add("hello")
        svc.ExecInContainer(req, writer, ctx).Wait()
        writer.Items.Count |> should equal 1
        writer.Items.[0].Stream |> should equal "stdout"
        writer.Items.[0].Data.ToStringUtf8() |> should equal "Output of: echo hello"

    [<Fact>]
    let ``PullImage avec image valide retourne succes`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let req = PullImageRequest(Image = "mcr.microsoft.com/dotnet/runtime:10.0")
        let result = svc.PullImage(req, ctx).Result
        result.Image |> should equal "mcr.microsoft.com/dotnet/runtime:10.0"
        result.Message |> shouldContain "image pulled"
        mock.PulledImages |> should contain "mcr.microsoft.com/dotnet/runtime:10.0"

    [<Fact>]
    let ``PullImage avec image vide leve InvalidArgument`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = PullImageRequest(Image = "")
        Assert.ThrowsAsync<RpcException>(fun () -> svc.PullImage(req, ctx)) |> ignore

    [<Fact>]
    let ``PullImage avec image servercore fonctionne`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let req = PullImageRequest(Image = "mcr.microsoft.com/windows/servercore:ltsc2022")
        let result = svc.PullImage(req, ctx).Result
        result.Image |> should equal "mcr.microsoft.com/windows/servercore:ltsc2022"
        mock.PulledImages |> should contain "mcr.microsoft.com/windows/servercore:ltsc2022"

    // --- RenameContainer ---
    [<Fact>]
    let ``RenameContainer avec id et nom retourne success`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "c1", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u) |> ignore
        let req = RenameContainerRequest(Id = "c1", NewName = "nouveau-nom")
        let result = svc.RenameContainer(req, ctx).Result
        result.Success |> should equal true
        result.Message |> shouldContain "nouveau-nom"

    [<Fact>]
    let ``RenameContainer avec id vide leve InvalidArgument`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = RenameContainerRequest(Id = "", NewName = "nom")
        let ex = Assert.Throws<AggregateException>(fun () -> svc.RenameContainer(req, ctx).Result |> ignore)
        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    [<Fact>]
    let ``RenameContainer avec nom vide leve InvalidArgument`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "c1", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u) |> ignore
        let req = RenameContainerRequest(Id = "c1", NewName = "")
        let ex = Assert.Throws<AggregateException>(fun () -> svc.RenameContainer(req, ctx).Result |> ignore)
        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    // --- TopContainer ---
    [<Fact>]
    let ``TopContainer retourne les processus`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "c1", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u) |> ignore
        mock.Mock.StartContainer("default", "c1")
        let req = TopContainerRequest(Id = "c1")
        let result = svc.TopContainer(req, ctx).Result
        result.Processes.Count |> should equal 1
        result.Processes.[0].Pid |> should equal 1234L
        result.Processes.[0].User |> should equal "root"
        result.Processes.[0].Command |> should equal "dotnet app.dll"

    [<Fact>]
    let ``TopContainer avec id vide leve InvalidArgument`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = TopContainerRequest(Id = "")
        let ex = Assert.Throws<AggregateException>(fun () -> svc.TopContainer(req, ctx).Result |> ignore)
        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    // --- GetContainerStats ---
    [<Fact>]
    let ``GetContainerStats retourne les metriques`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "c1", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u) |> ignore
        let req = GetContainerStatsRequest(Id = "c1")
        let result = svc.GetContainerStats(req, ctx).Result
        result.CpuUsage |> should equal 123456L
        result.MemoryUsage |> should equal 1048576L
        result.MemoryLimit |> should equal 536870912L
        result.Pids |> should equal 3.0

    [<Fact>]
    let ``GetContainerStats avec id vide leve InvalidArgument`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = GetContainerStatsRequest(Id = "")
        let ex = Assert.Throws<AggregateException>(fun () -> svc.GetContainerStats(req, ctx).Result |> ignore)
        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    // --- ListImages ---
    [<Fact>]
    let ``ListImages retourne les images disponibles`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = ListImagesRequest()
        let result = svc.ListImages(req, ctx).Result
        result.Images.Count |> should equal 2
        result.Images |> Seq.exists (fun i -> i.Repository = "library/nginx") |> should equal true
        result.Images |> Seq.exists (fun i -> i.Repository = "library/redis") |> should equal true

    [<Fact>]
    let ``ListImages avec namespace vide utilise le namespace par defaut`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = ListImagesRequest()
        let result = svc.ListImages(req, ctx).Result
        result.Images.Count |> should equal 2

    // --- InspectImage ---
    [<Fact>]
    let ``InspectImage retourne les details de l'image`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = InspectImageRequest(Ref = "nginx:latest")
        let result = svc.InspectImage(req, ctx).Result
        result.Ref |> should equal "nginx:latest"
        result.Repository |> should equal "library/nginx"
        result.Labels.["maintainer"] |> should equal "nginx"

    [<Fact>]
    let ``InspectImage avec ref vide leve InvalidArgument`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = InspectImageRequest(Ref = "")
        let ex = Assert.Throws<AggregateException>(fun () -> svc.InspectImage(req, ctx).Result |> ignore)
        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    // --- RemoveImage ---
    [<Fact>]
    let ``RemoveImage supprime l'image avec succes`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let req = RemoveImageRequest(Ref = "nginx:latest")
        let result = svc.RemoveImage(req, ctx).Result
        result.Success |> should equal true
        result.Message |> shouldContain "nginx:latest"

    [<Fact>]
    let ``RemoveImage avec ref vide leve InvalidArgument`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = RemoveImageRequest(Ref = "")
        let ex = Assert.Throws<AggregateException>(fun () -> svc.RemoveImage(req, ctx).Result |> ignore)
        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    // --- TagImage ---
    [<Fact>]
    let ``TagImage tag l'image avec succes`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = TagImageRequest(Source = "nginx:latest", Target = "myregistry.azurecr.io/nginx:v1")
        let result = svc.TagImage(req, ctx).Result
        result.Source |> should equal "nginx:latest"
        result.Target |> should equal "myregistry.azurecr.io/nginx:v1"

    [<Fact>]
    let ``TagImage avec source vide leve InvalidArgument`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = TagImageRequest(Source = "", Target = "myregistry.azurecr.io/nginx:v1")
        let ex = Assert.Throws<AggregateException>(fun () -> svc.TagImage(req, ctx).Result |> ignore)
        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    [<Fact>]
    let ``TagImage avec cible vide leve InvalidArgument`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = TagImageRequest(Source = "nginx:latest", Target = "")
        let ex = Assert.Throws<AggregateException>(fun () -> svc.TagImage(req, ctx).Result |> ignore)
        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument
