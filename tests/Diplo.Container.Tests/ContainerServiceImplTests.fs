namespace Diplo.Container.Tests

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
    open Diplo.Disk

    /// Faux mounter : retourne un volume dont le HostPath dérive de la source
    /// et enregistre les libérations (writeBack) pour les assertions.
    type MockDiskMounter() =
        let mutable mounted = ResizeArray<string * string * bool>()
        let mutable disposed = ResizeArray<string>()
        interface IDiskMounter with
            member _.Mount(source, destination, readOnly) =
                mounted.Add(source, destination, readOnly)
                { HostPath = source + "-staging"
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

    let shouldContain (substring: string) (text: string) =
        Assert.Contains(substring, text)

    let createCtx () = CancellationToken.None

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
        let req = { Name = "mon-conteneur"; Image = "mcr.microsoft.com/dotnet/runtime:10.0"; Env = Dictionary<string, string>(); Command = ResizeArray<string>(); Args = ResizeArray<string>(); Labels = Dictionary<string, string>(); PidLimit = 0; MemoryLimit = 0L; CpuShares = 0; Mounts = ResizeArray<ContainerMount>() }
        let result = (svc :> IContainerService).CreateContainer(req, ctx).Result
        result.Id |> should equal "mon-conteneur"
        result.Name |> should equal "mon-conteneur"
        result.State |> should equal ContainerState.Created

    [<Fact>]
    let ``CreateContainer sans nom genere un id automatiquement`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req = { Name = ""; Image = "nginx:latest"; Env = Dictionary<string, string>(); Command = ResizeArray<string>(); Args = ResizeArray<string>(); Labels = Dictionary<string, string>(); PidLimit = 0; MemoryLimit = 0L; CpuShares = 0; Mounts = ResizeArray<ContainerMount>() }
        let result = (svc :> IContainerService).CreateContainer(req, ctx).Result
        String.IsNullOrEmpty(result.Id) |> should equal false
        result.Id.Length |> should equal 32
        result.State |> should equal ContainerState.Created

    [<Fact>]
    let ``StartContainer retourne Running`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "c1", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u, []) |> ignore
        let req : StartContainerRequest = { Id = "c1" }
        let result = (svc :> IContainerService).StartContainer(req, ctx).Result
        result.State |> should equal ContainerState.Running
        result.Message |> should equal "Conteneur démarré"

    [<Fact>]
    let ``StopContainer avec timeout par defaut utilise 10`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "c1", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u, []) |> ignore
        let req = { Id = "c1"; TimeoutSeconds = 0 }
        let result = (svc :> IContainerService).StopContainer(req, ctx).Result
        result.State |> should equal ContainerState.Stopped
        result.Message |> should equal "Conteneur arrêté"
        mock.StopCalled.["c1"] |> should equal 10

    [<Fact>]
    let ``StopContainer avec timeout personnalise`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "c1", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u, []) |> ignore
        let req = { Id = "c1"; TimeoutSeconds = 30 }
        let result = (svc :> IContainerService).StopContainer(req, ctx).Result
        result.State |> should equal ContainerState.Stopped
        mock.StopCalled.["c1"] |> should equal 30

    [<Fact>]
    let ``DeleteContainer retourne success`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "c1", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u, []) |> ignore
        let req = { Id = "c1"; Force = false }
        let result = (svc :> IContainerService).DeleteContainer(req, ctx).Result
        result.Success |> should equal true
        result.Message |> should equal "Conteneur supprimé"
        mock.DeletedContainers |> should contain "c1"

    [<Fact>]
    let ``InspectContainer retourne les metadonnees du conteneur`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "my-app", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u, []) |> ignore
        mock.Mock.StartContainer("default", "my-app")
        let req : InspectContainerRequest = { Id = "my-app" }
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
        mock.Mock.CreateContainer("default", "stopped-app", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u, []) |> ignore
        let req : InspectContainerRequest = { Id = "stopped-app" }
        let result = (svc :> IContainerService).InspectContainer(req, ctx).Result
        result.State |> should equal ContainerState.Created
        result.Pid |> should equal 0

    [<Fact>]
    let ``ListContainers retourne les conteneurs crees`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "app-1", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u, []) |> ignore
        mock.Mock.CreateContainer("default", "app-2", "redis", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u, []) |> ignore
        let req = { All = true; Filters = Dictionary<string, string>() }
        let result = (svc :> IContainerService).ListContainers(req, ctx).Result
        result.Containers.Count |> should equal 2
        result.Containers |> Seq.exists (fun c -> c.Id = "app-1") |> should equal true
        result.Containers |> Seq.exists (fun c -> c.Id = "app-2") |> should equal true

    [<Fact>]
    let ``ListContainers retourne vide quand aucun conteneur`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req = { All = false; Filters = Dictionary<string, string>() }
        let result = (svc :> IContainerService).ListContainers(req, ctx).Result
        result.Containers.Count |> should equal 0

    [<Fact>]
    let ``GetVersion retourne la version containerd`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req : GetVersionRequest = { Placeholder = false }
        let result = (svc :> IContainerService).GetVersion(req, ctx).Result
        result.Version |> shouldContain "1.7.27"
        result.Version |> shouldContain "abc123"

    [<Fact>]
    let ``ListNamespaces retourne les namespaces`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req : ListNamespacesRequest = { Placeholder = false }
        let result = (svc :> IContainerService).ListNamespaces(req, ctx).Result
        result.Namespaces.Count |> should equal 2
        result.Namespaces |> should contain "default"
        result.Namespaces |> should contain "moby"

    [<Fact>]
    let ``GetContainerLogs ecrit les lignes dans le stream`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "c1", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u, []) |> ignore
        let req = { Id = "c1"; Tail = 10; Follow = false; Since = "" }
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
        mock.Mock.CreateContainer("default", "c1", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u, []) |> ignore
        let req = { Id = "c1"; Command = ResizeArray<string>(); AttachStdin = false; AttachStdout = true; AttachStderr = true }
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
        System.Text.Encoding.UTF8.GetString(items.[0].Data) |> should equal "Output of: echo hello"

    [<Fact>]
    let ``PullImage avec image valide retourne succes`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()
        let req = { Image = "mcr.microsoft.com/dotnet/runtime:10.0" }
        let result = (svc :> IContainerService).PullImage(req, ctx).Result
        result.Image |> should equal "mcr.microsoft.com/dotnet/runtime:10.0"
        result.Message |> shouldContain "image pulled"
        mock.PulledImages |> should contain "mcr.microsoft.com/dotnet/runtime:10.0"

    [<Fact>]
    let ``PullImage avec image vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req = { Image = "" }
        Assert.ThrowsAsync<RpcException>(fun () -> (svc :> IContainerService).PullImage(req, ctx)) |> ignore

    [<Fact>]
    let ``PullImage avec image servercore fonctionne`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()
        let req = { Image = "mcr.microsoft.com/windows/servercore:ltsc2022" }
        let result = (svc :> IContainerService).PullImage(req, ctx).Result
        result.Image |> should equal "mcr.microsoft.com/windows/servercore:ltsc2022"
        mock.PulledImages |> should contain "mcr.microsoft.com/windows/servercore:ltsc2022"

    // --- RenameContainer ---
    [<Fact>]
    let ``RenameContainer avec id et nom retourne success`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "c1", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u, []) |> ignore
        let req = { Id = "c1"; NewName = "nouveau-nom" }
        let result = (svc :> IContainerService).RenameContainer(req, ctx).Result
        result.Success |> should equal true
        result.Message |> shouldContain "nouveau-nom"

    [<Fact>]
    let ``RenameContainer avec id vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req = { Id = ""; NewName = "nom" }
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> IContainerService).RenameContainer(req, ctx).Result |> ignore)
        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    [<Fact>]
    let ``RenameContainer avec nom vide leve InvalidArgument`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "c1", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u, []) |> ignore
        let req = { Id = "c1"; NewName = "" }
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> IContainerService).RenameContainer(req, ctx).Result |> ignore)
        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    // --- TopContainer ---
    [<Fact>]
    let ``TopContainer retourne les processus`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "c1", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u, []) |> ignore
        mock.Mock.StartContainer("default", "c1")
        let req : TopContainerRequest = { Id = "c1" }
        let result = (svc :> IContainerService).TopContainer(req, ctx).Result
        result.Processes.Count |> should equal 1
        result.Processes.[0].Pid |> should equal 1234L
        result.Processes.[0].User |> should equal "root"
        result.Processes.[0].Command |> should equal "dotnet app.dll"

    [<Fact>]
    let ``TopContainer avec id vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req : TopContainerRequest = { Id = "" }
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> IContainerService).TopContainer(req, ctx).Result |> ignore)
        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    // --- GetContainerStats ---
    [<Fact>]
    let ``GetContainerStats retourne les metriques`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()
        mock.Mock.CreateContainer("default", "c1", "nginx", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u, []) |> ignore
        let req : GetContainerStatsRequest = { Id = "c1" }
        let result = (svc :> IContainerService).GetContainerStats(req, ctx).Result
        result.CpuUsage |> should equal 123456.0
        result.MemoryUsage |> should equal 1048576L
        result.MemoryLimit |> should equal 536870912L
        result.Pids |> should equal 3

    [<Fact>]
    let ``GetContainerStats avec id vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req : GetContainerStatsRequest = { Id = "" }
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> IContainerService).GetContainerStats(req, ctx).Result |> ignore)
        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    // --- ListImages ---
    [<Fact>]
    let ``ListImages retourne les images disponibles`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req : ListImagesRequest = { NamespaceName = "" }
        let result = (svc :> IContainerService).ListImages(req, ctx).Result
        result.Images.Count |> should equal 2
        result.Images |> Seq.exists (fun i -> i.Repository = "library/nginx") |> should equal true
        result.Images |> Seq.exists (fun i -> i.Repository = "library/redis") |> should equal true

    [<Fact>]
    let ``ListImages avec namespace vide utilise le namespace par defaut`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req : ListImagesRequest = { NamespaceName = "" }
        let result = (svc :> IContainerService).ListImages(req, ctx).Result
        result.Images.Count |> should equal 2

    // --- InspectImage ---
    [<Fact>]
    let ``InspectImage retourne les details de l'image`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req : InspectImageRequest = { Ref = "nginx:latest"; NamespaceName = "" }
        let result = (svc :> IContainerService).InspectImage(req, ctx).Result
        result.Ref |> should equal "nginx:latest"
        result.Repository |> should equal "library/nginx"
        result.Labels.["maintainer"] |> should equal "nginx"

    [<Fact>]
    let ``InspectImage avec ref vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req : InspectImageRequest = { Ref = ""; NamespaceName = "" }
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> IContainerService).InspectImage(req, ctx).Result |> ignore)
        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    // --- RemoveImage ---
    [<Fact>]
    let ``RemoveImage supprime l'image avec succes`` () =
        let svc, mock, _ = createService ()
        let ctx = createCtx ()
        let req : RemoveImageRequest = { Ref = "nginx:latest"; NamespaceName = "" }
        let result = (svc :> IContainerService).RemoveImage(req, ctx).Result
        result.Success |> should equal true
        result.Message |> shouldContain "nginx:latest"

    [<Fact>]
    let ``RemoveImage avec ref vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req : RemoveImageRequest = { Ref = ""; NamespaceName = "" }
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> IContainerService).RemoveImage(req, ctx).Result |> ignore)
        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    // --- TagImage ---
    [<Fact>]
    let ``TagImage tag l'image avec succes`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req = { Source = "nginx:latest"; Target = "myregistry.azurecr.io/nginx:v1"; NamespaceName = "" }
        let result = (svc :> IContainerService).TagImage(req, ctx).Result
        result.Source |> should equal "nginx:latest"
        result.Target |> should equal "myregistry.azurecr.io/nginx:v1"

    [<Fact>]
    let ``TagImage avec source vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req = { Source = ""; Target = "myregistry.azurecr.io/nginx:v1"; NamespaceName = "" }
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> IContainerService).TagImage(req, ctx).Result |> ignore)
        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    [<Fact>]
    let ``TagImage avec cible vide leve InvalidArgument`` () =
        let svc, _, _ = createService ()
        let ctx = createCtx ()
        let req = { Source = "nginx:latest"; Target = ""; NamespaceName = "" }
        let ex = Assert.Throws<AggregateException>(fun () -> (svc :> IContainerService).TagImage(req, ctx).Result |> ignore)
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
            { Name = "avec-volumes"; Image = "nginx:latest"
              Env = Dictionary<string, string>(); Command = ResizeArray<string>(); Args = ResizeArray<string>()
              Labels = Dictionary<string, string>(); PidLimit = 0; MemoryLimit = 0L; CpuShares = 0
              Mounts = ResizeArray([ { Source = dataSrc; Destination = "C:\\app"; ReadOnly = false }
                                     { Source = secretsSrc; Destination = "C:\\keys"; ReadOnly = true } ]) }
        let result = (svc :> IContainerService).CreateContainer(req, ctx).Result
        result.State |> should equal ContainerState.Created
        mounter.Mounted |> should equal [ (dataSrc, "C:\\app", false); (secretsSrc, "C:\\keys", true) ]
        mock.RecordedMounts.[result.Id] |> should equal [ (dataSrc + "-staging", "C:\\app", false); (secretsSrc + "-staging", "C:\\keys", true) ]

    [<Fact>]
    let ``CreateContainer sans montage ne fait aucun appel au mounter`` () =
        let svc, _, mounter = createService ()
        let ctx = createCtx ()
        let req =
            { Name = "sans-volume"; Image = "nginx:latest"
              Env = Dictionary<string, string>(); Command = ResizeArray<string>(); Args = ResizeArray<string>()
              Labels = Dictionary<string, string>(); PidLimit = 0; MemoryLimit = 0L; CpuShares = 0
              Mounts = ResizeArray<ContainerMount>() }
        let result = (svc :> IContainerService).CreateContainer(req, ctx).Result
        mounter.Mounted |> should be Empty

    [<Fact>]
    let ``DeleteContainer libere les volumes montes (writeBack)`` () =
        let dataSrc = tempVolume "diplo-data"
        let svc, _, mounter = createService ()
        let ctx = createCtx ()
        let req =
            { Name = "a-supprimer"; Image = "nginx:latest"
              Env = Dictionary<string, string>(); Command = ResizeArray<string>(); Args = ResizeArray<string>()
              Labels = Dictionary<string, string>(); PidLimit = 0; MemoryLimit = 0L; CpuShares = 0
              Mounts = ResizeArray([ { Source = dataSrc; Destination = "C:\\app"; ReadOnly = false } ]) }
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

