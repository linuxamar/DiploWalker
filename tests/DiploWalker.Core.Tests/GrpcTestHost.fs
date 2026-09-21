namespace DiploWalker.Core.Tests

open System
open System.Collections.Generic
open System.ServiceModel
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.Extensions.DependencyInjection
open ProtoBuf.Grpc.Server
open DiploWalker.Grpc
open DiploWalker.Grpc.Container
open DiploWalker.Grpc.Network
open DiploWalker.Grpc.Volume

/// Infra de test : hÃ´te gRPC (protobuf-net, Kestrel Loopback sur un port
/// Ã©phÃ©mÃ¨re) avec des stubs de service qui conservent la derniÃ¨re requÃªte
/// reÃ§ue et renvoient des rÃ©ponses prÃ©visibles. Reproduit le pattern des
/// tests d'intÃ©gration (DiploWalker.Integration.Tests) sans aucun driver rÃ©el.
module GrpcTestHost =

    let private toAsyncEnumerable (items: seq<'T>) : IAsyncEnumerable<'T> =
        { new IAsyncEnumerable<'T> with
            member _.GetAsyncEnumerator(_ct: CancellationToken) =
                let e = items.GetEnumerator()

                { new System.Collections.Generic.IAsyncEnumerator<'T> with
                    member _.Current = e.Current

                    member _.MoveNextAsync() =
                        if e.MoveNext() then ValueTask<bool>(true)
                        else ValueTask<bool>(false)

                    member _.DisposeAsync() =
                        e.Dispose()
                        ValueTask() } }

    /// Stub IContainerService : les membres exercÃ©s par les wrappers renvoient
    /// une rÃ©ponse fixe et gardent la derniÃ¨re requÃªte ; le reste lÃ¨ve
    /// NotImplementedException pour rÃ©vÃ©ler toute dÃ©pendance inattendue.
    [<ServiceContract(Name = "IContainerService")>]
    type ContainerServiceStub() =

        let mutable lastCreate: CreateContainerRequest option = None
        let mutable lastStart: StartContainerRequest option = None
        let mutable lastStop: StopContainerRequest option = None
        let mutable lastDelete: DeleteContainerRequest option = None
        let mutable lastInspect: InspectContainerRequest option = None
        let mutable lastList: ListContainersRequest option = None
        let mutable lastLogs: GetContainerLogsRequest option = None
        let mutable lastExec: ExecInContainerRequest option = None
        let mutable lastPull: PullImageRequest option = None
        let mutable lastLogin: LoginRegistryRequest option = None
        let mutable lastLogout: LogoutRegistryRequest option = None
        let mutable lastRename: RenameContainerRequest option = None
        let mutable lastTop: TopContainerRequest option = None
        let mutable lastStats: GetContainerStatsRequest option = None
        let mutable lastListImages: ListImagesRequest option = None
        let mutable lastInspectImage: InspectImageRequest option = None
        let mutable lastRemoveImage: RemoveImageRequest option = None
        let mutable lastTagImage: TagImageRequest option = None

        member _.LastCreate = lastCreate
        member _.LastStart = lastStart
        member _.LastStop = lastStop
        member _.LastDelete = lastDelete
        member _.LastInspect = lastInspect
        member _.LastList = lastList
        member _.LastLogs = lastLogs
        member _.LastExec = lastExec
        member _.LastPull = lastPull
        member _.LastLogin = lastLogin
        member _.LastLogout = lastLogout
        member _.LastRename = lastRename
        member _.LastTop = lastTop
        member _.LastStats = lastStats
        member _.LastListImages = lastListImages
        member _.LastInspectImage = lastInspectImage
        member _.LastRemoveImage = lastRemoveImage
        member _.LastTagImage = lastTagImage

        member this.HasRequests =
            lastCreate.IsSome
            || lastStart.IsSome
            || lastStop.IsSome
            || lastDelete.IsSome
            || lastInspect.IsSome
            || lastList.IsSome
            || lastLogs.IsSome
            || lastExec.IsSome
            || lastPull.IsSome
            || lastLogin.IsSome
            || lastLogout.IsSome
            || lastRename.IsSome
            || lastTop.IsSome
            || lastStats.IsSome
            || lastListImages.IsSome
            || lastInspectImage.IsSome
            || lastRemoveImage.IsSome
            || lastTagImage.IsSome

        interface IContainerService with
            member _.CreateContainer(request, _ct) =
                lastCreate <- Some request

                Task.FromResult(
                    { CreateContainerResponse.Id = "ctr-" + request.Name
                      Name = request.Name
                      State = ContainerState.Created
                      CreatedAt = "2026-01-01T00:00:00Z" }
                )

            member _.StartContainer(request, _ct) =
                lastStart <- Some request
                Task.FromResult({ StartContainerResponse.State = ContainerState.Running; Message = "DÃ©marrÃ©" })

            member _.StopContainer(request, _ct) =
                lastStop <- Some request
                Task.FromResult({ StopContainerResponse.State = ContainerState.Stopped; Message = "ArrÃªtÃ©" })

            member _.DeleteContainer(request, _ct) =
                lastDelete <- Some request
                Task.FromResult({ DeleteContainerResponse.Success = true; Message = "SupprimÃ©" })

            member _.InspectContainer(request, _ct) =
                lastInspect <- Some request

                Task.FromResult(
                    { InspectContainerResponse.Id = request.Id
                      Name = "ctn-" + request.Id
                      Image = "nginx:latest"
                      State = ContainerState.Running
                      CreatedAt = "2026-01-01T00:00:00Z"
                      StartedAt = "2026-01-01T00:00:03Z"
                      FinishedAt = ""
                      Labels = Dictionary<string, string>()
                      Env = Dictionary<string, string>()
                      Pid = 1234
                      ExitCode = 0
                      RestartPolicy = ""
                      Ports = ResizeArray<PortMapping>()
                      Health = "healthy"
                      Mounts = ResizeArray<string>() }
                )

            member _.ListContainers(request, _ct) =
                lastList <- Some request
                Task.FromResult({ ListContainersResponse.Containers = ResizeArray<ContainerInfo>() })

            member _.GetContainerLogs(request, _ct) =
                lastLogs <- Some request

                toAsyncEnumerable
                    [ { ContainerLogEntry.Timestamp = "2026-01-01T00:00:00Z"
                        Stream = "stdout"
                        Log = "ligne 1" }
                      { Timestamp = "2026-01-01T00:00:01Z"
                        Stream = "stderr"
                        Log = "erreur" } ]

            member _.ExecInContainer(request, _ct) =
                lastExec <- Some request

                toAsyncEnumerable
                    [ { ExecOutput.Stream = "stdout"
                        Data = System.Text.Encoding.UTF8.GetBytes("hello") } ]

            member _.PullImage(request, _ct) =
                lastPull <- Some request
                Task.FromResult({ PullImageResponse.Image = request.Image; Message = "Image tÃ©lÃ©chargÃ©e" })

            member _.GetVersion(_request, _ct) =
                Task.FromResult(
                    { GetVersionResponse.Version = "1.0.0-test"
                      Revision = "abc123"
                      GoVersion = "1.24"
                      Os = "windows"
                      Arch = "amd64" }
                )

            member _.ListNamespaces(_request, _ct) =
                Task.FromResult(
                    { ListNamespacesResponse.Namespaces = ResizeArray<string>(seq { "default" }) }
                )

            member _.RenameContainer(request, _ct) =
                lastRename <- Some request
                Task.FromResult({ RenameContainerResponse.Success = true; Message = "RenommÃ©" })

            member _.TopContainer(request, _ct) =
                lastTop <- Some request
                let procs = ResizeArray<ProcessInfo>()
                procs.Add({ ProcessInfo.Pid = 1234L; User = "root"; Command = "nginx"; CpuPercent = 0.0; MemPercent = 0.0; Rss = 1024L })
                Task.FromResult({ TopContainerResponse.Processes = procs })

            member _.GetContainerStats(request, _ct) =
                lastStats <- Some request

                Task.FromResult(
                    { GetContainerStatsResponse.CpuUsage = 1.5
                      MemoryUsage = 2048L
                      MemoryLimit = 65536L
                      NetworkRx = 100L
                      NetworkTx = 50L
                      DiskRead = 0L
                      DiskWrite = 0L
                      Pids = 2 }
                )

            member _.ListImages(request, _ct) =
                lastListImages <- Some request
                let images = ResizeArray<ImageInfo>()
                images.Add({ ImageInfo.Ref = "nginx:latest"; Id = "img-1"; Repository = "nginx"; Tag = "latest"; Size = 100L; CreatedAt = "2026-01-01T00:00:00Z" })
                Task.FromResult({ ListImagesResponse.Images = images })

            member _.InspectImage(request, _ct) =
                lastInspectImage <- Some request

                Task.FromResult(
                    { InspectImageResponse.Ref = request.Ref
                      Id = "img-" + request.Ref
                      Repository = "nginx"
                      Tag = "latest"
                      Size = 100L
                      CreatedAt = "2026-01-01T00:00:00Z"
                      Labels = Dictionary<string, string>() }
                )

            member _.RemoveImage(request, _ct) =
                lastRemoveImage <- Some request
                Task.FromResult({ RemoveImageResponse.Success = true; Message = "Image supprimÃ©e" })

            member _.TagImage(request, _ct) =
                lastTagImage <- Some request
                Task.FromResult({ TagImageResponse.Source = request.Source; Target = request.Target; Message = "RÃ©Ã©tiquetÃ©e" })

            member _.SearchRegistry(request, _ct) =
                Task.FromResult(
                    { SearchRegistryResponse.Results = List<RegistrySearchResult>(); Message = "" }
                )

            member _.PauseContainer(_request, _ct) = raise (NotImplementedException())
            member _.UnpauseContainer(_request, _ct) = raise (NotImplementedException())
            member _.WaitContainer(_request, _ct) = raise (NotImplementedException())
            member _.UpdateContainer(_request, _ct) = raise (NotImplementedException())
            member _.PruneContainers(_request, _ct) = raise (NotImplementedException())
            member _.PruneImages(_request, _ct) = raise (NotImplementedException())
            member _.GetContainerStatsStream(_request, _ct) = raise (NotImplementedException())
            member _.WatchEvents(_request, _ct) = raise (NotImplementedException())
            member _.ExecContainerStream(_request, _ct) = raise (NotImplementedException())
            member _.ReadFile(_request, _ct) = raise (NotImplementedException())
            member _.WriteFile(_request, _ct) = raise (NotImplementedException())
            member _.CommitImage(_request, _ct) = raise (NotImplementedException())
            member _.ExportImage(_request, _ct) = raise (NotImplementedException())
            member _.ImportImage(_request, _ct) = raise (NotImplementedException())

            member _.LoginRegistry(request, _ct) =
                lastLogin <- Some request
                Task.FromResult({ LoginRegistryResponse.Success = true; Message = "Connexion rÃ©ussie" })

            member _.LogoutRegistry(request, _ct) =
                lastLogout <- Some request
                Task.FromResult({ LogoutRegistryResponse.Success = true; Message = "DÃ©connexion rÃ©ussie" })

            member _.CreateNamespace(_request, _ct) = raise (NotImplementedException())
            member _.DeleteNamespace(_request, _ct) = raise (NotImplementedException())

    /// Stub IVolumeService : mÃªmes conventions que ContainerServiceStub.
    [<ServiceContract(Name = "IVolumeService")>]
    type VolumeServiceStub() =

        let mutable lastCreate: CreateVolumeRequest option = None
        let mutable lastRemove: RemoveVolumeRequest option = None
        let mutable lastInspect: InspectVolumeRequest option = None
        let mutable lastList: ListVolumesRequest option = None
        let mutable lastMount: MountVolumeRequest option = None
        let mutable lastUnmount: UnmountVolumeRequest option = None

        member _.LastCreate = lastCreate
        member _.LastRemove = lastRemove
        member _.LastInspect = lastInspect
        member _.LastList = lastList
        member _.LastMount = lastMount
        member _.LastUnmount = lastUnmount

        interface IVolumeService with
            member _.CreateVolume(request, _ct) =
                lastCreate <- Some request

                Task.FromResult(
                    { CreateVolumeResponse.Id = "vol-" + request.Name
                      Name = request.Name
                      Driver = request.Driver
                      Mountpoint = "C:\\vol\\" + request.Name
                      CreatedAt = "2026-01-01T00:00:00Z" }
                )

            member _.RemoveVolume(request, _ct) =
                lastRemove <- Some request
                Task.FromResult({ RemoveVolumeResponse.Success = true; Message = "Volume supprimÃ©" })

            member _.InspectVolume(request, _ct) =
                lastInspect <- Some request

                Task.FromResult(
                    { InspectVolumeResponse.Id = request.Id
                      Name = request.Id
                      Driver = StorageDriverType.Local
                      Mountpoint = ""
                      State = MountState.Unmounted
                      Labels = Dictionary<string, string>()
                      DriverOpts = Dictionary<string, string>()
                      SizeBytes = 0L
                      CreatedAt = "" }
                )

            member _.ListVolumes(request, _ct) =
                lastList <- Some request
                Task.FromResult({ ListVolumesResponse.Volumes = ResizeArray<VolumeInfo>() })

            member _.MountVolume(request, _ct) =
                lastMount <- Some request

                Task.FromResult(
                    { MountVolumeResponse.State = MountState.Mounted
                      Mountpoint = "C:\\vol\\" + request.Id
                      Message = "Volume montÃ©" }
                )

            member _.UnmountVolume(request, _ct) =
                lastUnmount <- Some request
                Task.FromResult({ UnmountVolumeResponse.State = MountState.Unmounted; Message = "Volume démonté" })

            member _.PruneVolumes(_request, _ct) =
                Task.FromResult(
                    { PruneVolumesResponse.VolumesDeleted = ResizeArray<string>()
                      Count = 0
                      Message = "0 volume(s) supprimÃ©(s)" }
                )

    /// Stub INetworkService : mÃªmes conventions que ContainerServiceStub.
    [<ServiceContract(Name = "INetworkService")>]
    type NetworkServiceStub() =

        let mutable lastCreate: CreateNetworkRequest option = None
        let mutable lastRemove: RemoveNetworkRequest option = None
        let mutable lastInspect: InspectNetworkRequest option = None
        let mutable lastList: ListNetworksRequest option = None
        let mutable lastConnect: ConnectContainerRequest option = None
        let mutable lastDisconnect: DisconnectContainerRequest option = None
        let mutable lastRunCni: RunCniPluginRequest option = None

        member _.LastCreate = lastCreate
        member _.LastRemove = lastRemove
        member _.LastInspect = lastInspect
        member _.LastList = lastList
        member _.LastConnect = lastConnect
        member _.LastDisconnect = lastDisconnect
        member _.LastRunCni = lastRunCni

        interface INetworkService with
            member _.CreateNetwork(request, _ct) =
                lastCreate <- Some request

                Task.FromResult(
                    { CreateNetworkResponse.Id = "net-" + request.Name
                      Name = request.Name
                      Driver = request.Driver
                      Subnet = request.Subnet
                      Gateway = request.Gateway
                      CreatedAt = "2026-01-01T00:00:00Z" }
                )

            member _.RemoveNetwork(request, _ct) =
                lastRemove <- Some request
                Task.FromResult({ RemoveNetworkResponse.Success = true; Message = "RÃ©seau supprimÃ©" })

            member _.InspectNetwork(request, _ct) =
                lastInspect <- Some request

                Task.FromResult(
                    { InspectNetworkResponse.Id = request.Id
                      Name = "net-" + request.Id
                      Driver = NetworkDriver.Bridge
                      Subnet = ""
                      Gateway = ""
                      IpRange = ""
                      Options = Dictionary<string, string>()
                      Labels = Dictionary<string, string>()
                      Endpoints = ResizeArray<EndpointInfo>()
                      CreatedAt = "" }
                )

            member _.ListNetworks(request, _ct) =
                lastList <- Some request
                Task.FromResult({ ListNetworksResponse.Networks = ResizeArray<NetworkInfo>() })

            member _.ConnectContainer(request, _ct) =
                lastConnect <- Some request

                Task.FromResult(
                    { ConnectContainerResponse.EndpointId = "ep-" + request.ContainerId
                      Ipv4Address = request.Ipv4Address
                      MacAddress = ""
                      Message = "ConnectÃ©" }
                )

            member _.DisconnectContainer(request, _ct) =
                lastDisconnect <- Some request
                Task.FromResult({ DisconnectContainerResponse.Success = true; Message = "DÃ©connectÃ©" })

            member _.RunCniPlugin(request, _ct) =
                lastRunCni <- Some request

                Task.FromResult(
                    { RunCniPluginResponse.Success = true
                      Ifname = "eth0"
                      Ipv4Address = "10.0.0.2"
                      Gateway = "10.0.0.1"
                      Message = "Plugin exÃ©cutÃ©" }
                )

            member _.PruneNetworks(_request, _ct) =
                Task.FromResult(
                    { PruneNetworksResponse.NetworksDeleted = ResizeArray<string>()
                      Count = 0
                      Message = "0 rÃ©seau(x) supprimÃ©(s)" }
                )

    let private stopApp (app: WebApplication) =
        app.StopAsync().GetAwaiter().GetResult()
        (app :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()

    let private startApp<'T when 'T : not struct> (stub: 'T) () =
        let builder = WebApplication.CreateBuilder()
        builder.Services.AddCodeFirstGrpc() |> ignore
        builder.Services.AddSingleton<'T>(stub) |> ignore

        builder.WebHost.ConfigureKestrel(fun opts ->
            opts.Listen(System.Net.IPAddress.Loopback, 0, fun lo -> lo.Protocols <- HttpProtocols.Http2))
        |> ignore

        let app = builder.Build()
        app.MapGrpcService<'T>() |> ignore
        app.StartAsync().GetAwaiter().GetResult()
        let address = app.Urls |> Seq.head
        app, address

    /// DÃ©marre un hÃ´te gRPC branchÃ© sur le stub conteneur, exÃ©cute `f` avec
    /// l'adresse de l'hÃ´te puis arrÃªte proprement l'application.
    let withContainerApp (stub: ContainerServiceStub) (f: string -> 'a) =
        let app, address = startApp<ContainerServiceStub> stub ()

        try
            f address
        finally
            stopApp app

    /// DÃ©marre un hÃ´te gRPC branchÃ© sur le stub volume (cf. withContainerApp).
    let withVolumeApp (stub: VolumeServiceStub) (f: string -> 'a) =
        let app, address = startApp<VolumeServiceStub> stub ()

        try
            f address
        finally
            stopApp app

    /// DÃ©marre un hÃ´te gRPC branchÃ© sur le stub rÃ©seau (cf. withContainerApp).
    let withNetworkApp (stub: NetworkServiceStub) (f: string -> 'a) =
        let app, address = startApp<NetworkServiceStub> stub ()

        try
            f address
        finally
            stopApp app

