namespace DiploWalker.Integration.Tests

module VolumeIntegrationTests =

    open System
    open System.IO
    open System.Collections.Generic
    open System.ServiceModel
    open System.Threading
    open Xunit
    open FsUnit.Xunit
    open Microsoft.AspNetCore.Builder
    open Microsoft.AspNetCore.Hosting
    open Microsoft.AspNetCore.Server.Kestrel.Core
    open Microsoft.Extensions.DependencyInjection
    open Grpc.Net.Client
    open Grpc.Core
    open ProtoBuf.Grpc.Client
    open ProtoBuf.Grpc.Server
    open DiploWalker.Grpc
    open DiploWalker.Grpc.Volume
    open DiploWalker.Core.Connection
    open DiploWalker.Volume.Drivers
    open DiploWalker.Volume.Services
    open DiploWalker.Abstractions
    open DiploWalker.Abstractions.SecurityValidation
    open DiploWalker.TestHelpers

    do addAllowedVolumeDir (Path.GetTempPath())

    let private startApp () =
        let dataRoot = TestHelpers.createTempDir "vol"
        let builder = WebApplication.CreateBuilder()
        builder.Services.AddCodeFirstGrpc() |> ignore

        builder.Services.AddSingleton<VolumeDriverRegistry>(fun _ ->
            let reg = VolumeDriverRegistry()
            reg.Register(StorageDriverType.Local, LocalVolumeDriver(dataRoot) :> Interfaces.IVolumeDriver)
            reg)
        |> ignore

        builder.Services.AddSingleton<VolumeServiceImpl>() |> ignore

        builder.WebHost.ConfigureKestrel(fun opts ->
            opts.Listen(System.Net.IPAddress.Loopback, 0, fun lo -> lo.Protocols <- HttpProtocols.Http2))
        |> ignore

        let app = builder.Build()
        app.MapGrpcService<VolumeServiceImpl>() |> ignore
        app.StartAsync().GetAwaiter().GetResult()
        let address = app.Urls |> Seq.head
        app, address, dataRoot

    let private stopApp (app: WebApplication) (dataRoot: string) =
        app.StopAsync().GetAwaiter().GetResult()
        (app :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()
        TestHelpers.cleanupDir dataRoot

    let private withVolumeApp (f: string -> 'a) =
        let app, address, dataRoot = startApp ()

        try
            f address
        finally
            stopApp app dataRoot

    let private startPipeApp (pipeName: string) =
        let dataRoot = TestHelpers.createTempDir "pipe"
        let builder = WebApplication.CreateBuilder()
        builder.Services.AddCodeFirstGrpc() |> ignore

        builder.Services.AddSingleton<VolumeDriverRegistry>(fun _ ->
            let reg = VolumeDriverRegistry()
            reg.Register(StorageDriverType.Local, LocalVolumeDriver(dataRoot) :> Interfaces.IVolumeDriver)
            reg)
        |> ignore

        builder.Services.AddSingleton<VolumeServiceImpl>() |> ignore

        builder.WebHost.ConfigureKestrel(fun opts ->
            opts.ListenNamedPipe(pipeName, fun lo -> lo.Protocols <- HttpProtocols.Http2))
        |> ignore

        let app = builder.Build()
        app.MapGrpcService<VolumeServiceImpl>() |> ignore
        app.StartAsync().GetAwaiter().GetResult()
        app, dataRoot

    [<ServiceContract(Name = "IVolumeService")>]
    type FailingOnceVolumeService(inner: IVolumeService) =
        let mutable _attempts = 0

        member _.Attempts = _attempts

        interface IVolumeService with
            member _.CreateVolume(request, ct) =
                _attempts <- _attempts + 1

                if _attempts = 1 then
                    raise (RpcException(Status(StatusCode.Unavailable, "panne simulée")))

                inner.CreateVolume(request, ct)

            member _.RemoveVolume(request, ct) = inner.RemoveVolume(request, ct)
            member _.InspectVolume(request, ct) = inner.InspectVolume(request, ct)
            member _.ListVolumes(request, ct) = inner.ListVolumes(request, ct)
            member _.MountVolume(request, ct) = inner.MountVolume(request, ct)
            member _.UnmountVolume(request, ct) = inner.UnmountVolume(request, ct)
            member _.PruneVolumes(request, ct) = inner.PruneVolumes(request, ct)

    let private startRetryApp (svc: FailingOnceVolumeService) =
        let builder = WebApplication.CreateBuilder()
        builder.Services.AddCodeFirstGrpc() |> ignore
        builder.Services.AddSingleton<FailingOnceVolumeService>(svc) |> ignore

        builder.WebHost.ConfigureKestrel(fun opts ->
            opts.Listen(System.Net.IPAddress.Loopback, 0, fun lo -> lo.Protocols <- HttpProtocols.Http2))
        |> ignore

        let app = builder.Build()
        app.MapGrpcService<FailingOnceVolumeService>() |> ignore
        app.StartAsync().GetAwaiter().GetResult()
        let address = app.Urls |> Seq.head
        app, address

    [<Fact>]
    let ``CreateVolume est relance par la politique de reprise apres un echec Unavailable`` () =
        let dataRoot = TestHelpers.createTempDir "retry"
        let reg = VolumeDriverRegistry()
        reg.Register(StorageDriverType.Local, LocalVolumeDriver(dataRoot) :> Interfaces.IVolumeDriver)
        let svc = FailingOnceVolumeService(VolumeServiceImpl(reg))
        let app, address = startRetryApp svc

        try
            use channel = DiploWalkerChannel.forAddress address
            let client = channel.CreateGrpcService<IVolumeService>()

            let req =
                { Name = "retried-volume"
                  Driver = StorageDriverType.Local
                  DriverOpts = Dictionary()
                  Labels = Dictionary() }

            let result = client.CreateVolume(req, CancellationToken.None).Result
            result.Name |> should equal "retried-volume"
            svc.Attempts |> should equal 2
        finally
            stopApp app dataRoot

    [<Fact>]
    // Le transport utilise ici est un named pipe Windows ; sur Unix l'adresse
    // equivalente est un socket de domaine, non couvert par ce test.
    [<Trait("Platform", "Windows")>]
    let ``CreateVolume via named pipe fonctionne de bout en bout`` () =
        let pipeName = "diplo-volume-test-" + Guid.NewGuid().ToString("N")
        let app, dataRoot = startPipeApp pipeName

        try
            use channel = DiploWalkerChannel.forAddress (sprintf "http://pipe:/%s" pipeName)
            let client = channel.CreateGrpcService<IVolumeService>()

            let req =
                { Name = "pipe-volume"
                  Driver = StorageDriverType.Local
                  DriverOpts = Dictionary()
                  Labels = Dictionary() }

            let result = client.CreateVolume(req, CancellationToken.None).Result
            String.IsNullOrEmpty(result.Id) |> should equal false
            result.Name |> should equal "pipe-volume"
        finally
            stopApp app dataRoot

    [<Fact>]
    let ``CreateVolume via gRPC retourne les informations du volume`` () =
        withVolumeApp (fun address ->
            use channel = DiploWalkerChannel.forAddress address
            let client = channel.CreateGrpcService<IVolumeService>()

            let req =
                { Name = "grpc-volume"
                  Driver = StorageDriverType.Local
                  DriverOpts = Dictionary()
                  Labels = Dictionary() }

            let result = client.CreateVolume(req, CancellationToken.None).Result
            String.IsNullOrEmpty(result.Id) |> should equal false
            result.Name |> should equal "grpc-volume"
            result.Driver |> should equal StorageDriverType.Local
            String.IsNullOrEmpty(result.Mountpoint) |> should equal false
            String.IsNullOrEmpty(result.CreatedAt) |> should equal false)

    [<Fact>]
    let ``CreateVolume puis RemoveVolume via gRPC`` () =
        withVolumeApp (fun address ->
            use channel = DiploWalkerChannel.forAddress address
            let client = channel.CreateGrpcService<IVolumeService>()

            let createReq =
                { Name = "to-delete-grpc"
                  Driver = StorageDriverType.Local
                  DriverOpts = Dictionary()
                  Labels = Dictionary() }

            let createResult = client.CreateVolume(createReq, CancellationToken.None).Result
            let removeReq = { Id = createResult.Id; Force = false }
            let removeResult = client.RemoveVolume(removeReq, CancellationToken.None).Result
            removeResult.Success |> should equal true
            removeResult.Message |> should equal "Volume supprimé")

    [<Fact>]
    let ``CreateVolume puis InspectVolume via gRPC`` () =
        withVolumeApp (fun address ->
            use channel = DiploWalkerChannel.forAddress address
            let client = channel.CreateGrpcService<IVolumeService>()

            let createReq =
                { Name = "inspect-me"
                  Driver = StorageDriverType.Local
                  DriverOpts = Dictionary()
                  Labels = Dictionary() }

            let createResult = client.CreateVolume(createReq, CancellationToken.None).Result
            let inspectReq = { Id = createResult.Id }
            let inspectResult = client.InspectVolume(inspectReq, CancellationToken.None).Result
            inspectResult.Id |> should equal createResult.Id
            inspectResult.Name |> should equal "inspect-me"
            inspectResult.Driver |> should equal StorageDriverType.Local)

    [<Fact>]
    let ``ListVolumes via gRPC retourne les volumes crees`` () =
        withVolumeApp (fun address ->
            use channel = DiploWalkerChannel.forAddress address
            let client = channel.CreateGrpcService<IVolumeService>()

            let req1 =
                { Name = "vol-a"
                  Driver = StorageDriverType.Local
                  DriverOpts = Dictionary()
                  Labels = Dictionary() }

            let req2 =
                { Name = "vol-b"
                  Driver = StorageDriverType.Local
                  DriverOpts = Dictionary()
                  Labels = Dictionary() }

            client.CreateVolume(req1, CancellationToken.None).Result |> ignore
            client.CreateVolume(req2, CancellationToken.None).Result |> ignore
            let listReq = { Filters = Dictionary() }
            let listResult = client.ListVolumes(listReq, CancellationToken.None).Result
            listResult.Volumes.Count |> should equal 2

            listResult.Volumes
            |> Seq.exists (fun v -> v.Name = "vol-a")
            |> should equal true

            listResult.Volumes
            |> Seq.exists (fun v -> v.Name = "vol-b")
            |> should equal true)

    [<Fact>]
    let ``RemoveVolume sur volume inexistant via gRPC retourne echec`` () =
        withVolumeApp (fun address ->
            use channel = DiploWalkerChannel.forAddress address
            let client = channel.CreateGrpcService<IVolumeService>()
            let req = { Id = "nonexistent"; Force = false }
            let result = client.RemoveVolume(req, CancellationToken.None).Result
            result.Success |> should equal false
            result.Message |> should haveSubstring "introuvable")

    [<Fact>]
    let ``InspectVolume sur volume inexistant via gRPC lance RpcException`` () =
        withVolumeApp (fun address ->
            use channel = DiploWalkerChannel.forAddress address
            let client = channel.CreateGrpcService<IVolumeService>()
            let req = { Id = "does-not-exist" }

            let ex =
                Assert.Throws<AggregateException>(fun () ->
                    client.InspectVolume(req, CancellationToken.None).Result |> ignore)

            ex.InnerException.Message |> should haveSubstring "introuvable")

    [<Fact>]
    let ``CreateVolume avec nom injection via gRPC lance exception`` () =
        withVolumeApp (fun address ->
            use channel = DiploWalkerChannel.forAddress address
            let client = channel.CreateGrpcService<IVolumeService>()

            let req =
                { Name = "test; rm -rf /"
                  Driver = StorageDriverType.Local
                  DriverOpts = Dictionary()
                  Labels = Dictionary() }

            let ex =
                Assert.Throws<AggregateException>(fun () ->
                    client.CreateVolume(req, CancellationToken.None).Result |> ignore)

            ex.InnerException.Message |> should haveSubstring "Le nom du volume")

    [<Fact>]
    let ``PruneVolumes via gRPC retourne zero quand aucun volume`` () =
        withVolumeApp (fun address ->
            use channel = DiploWalkerChannel.forAddress address
            let client = channel.CreateGrpcService<IVolumeService>()
            let req: PruneVolumesRequest = { Placeholder = false }
            let result = client.PruneVolumes(req, CancellationToken.None).Result
            result.Count |> should equal 0
            result.VolumesDeleted.Count |> should equal 0)


