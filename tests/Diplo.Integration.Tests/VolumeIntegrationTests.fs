namespace Diplo.Integration.Tests

module VolumeIntegrationTests =

    open System
    open System.IO
    open System.Collections.Generic
    open System.Threading
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
    open Diplo.Grpc.Volume
    open Diplo.Volume.Drivers
    open Diplo.Volume.Services
    open Diplo.Abstractions
    open Diplo.Abstractions.SecurityValidation

    do addAllowedVolumeDir(Path.GetTempPath())

    let private createChannel (address: string) =
        GrpcChannel.ForAddress(address, GrpcChannelOptions())

    let private startApp () =
        let dataRoot = Path.Combine(Path.GetTempPath(), "diplo-int-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(dataRoot) |> ignore
        let builder = WebApplication.CreateBuilder()
        builder.Services.AddCodeFirstGrpc() |> ignore
        builder.Services.AddSingleton<VolumeDriverRegistry>(fun _ ->
            let reg = VolumeDriverRegistry()
            reg.Register(StorageDriverType.Local, LocalVolumeDriver(dataRoot) :> Interfaces.IVolumeDriver)
            reg) |> ignore
        builder.Services.AddSingleton<VolumeServiceImpl>() |> ignore
        builder.WebHost.ConfigureKestrel(fun opts ->
            opts.Listen(System.Net.IPAddress.Loopback, 0, fun lo ->
                lo.Protocols <- HttpProtocols.Http2)) |> ignore
        let app = builder.Build()
        app.MapGrpcService<VolumeServiceImpl>() |> ignore
        app.StartAsync().GetAwaiter().GetResult()
        let address = app.Urls |> Seq.head
        app, address, dataRoot

    let private stopApp (app: WebApplication) (dataRoot: string) =
        app.StopAsync().GetAwaiter().GetResult()
        (app :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()
        try Directory.Delete(dataRoot, true) with _ -> ()

    [<Fact>]
    let ``CreateVolume via gRPC retourne les informations du volume`` () =
        let app, address, dataRoot = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<IVolumeService>()
            let req = { Name = "grpc-volume"; Driver = StorageDriverType.Local; DriverOpts = Dictionary(); Labels = Dictionary() }
            let result = client.CreateVolume(req, CancellationToken.None).Result
            String.IsNullOrEmpty(result.Id) |> should equal false
            result.Name |> should equal "grpc-volume"
            result.Driver |> should equal StorageDriverType.Local
            String.IsNullOrEmpty(result.Mountpoint) |> should equal false
            String.IsNullOrEmpty(result.CreatedAt) |> should equal false
        finally
            stopApp app dataRoot

    [<Fact>]
    let ``CreateVolume puis RemoveVolume via gRPC`` () =
        let app, address, dataRoot = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<IVolumeService>()
            let createReq = { Name = "to-delete-grpc"; Driver = StorageDriverType.Local; DriverOpts = Dictionary(); Labels = Dictionary() }
            let createResult = client.CreateVolume(createReq, CancellationToken.None).Result
            let removeReq = { Id = createResult.Id; Force = false }
            let removeResult = client.RemoveVolume(removeReq, CancellationToken.None).Result
            removeResult.Success |> should equal true
            removeResult.Message |> should equal "Volume supprimé"
        finally
            stopApp app dataRoot

    [<Fact>]
    let ``CreateVolume puis InspectVolume via gRPC`` () =
        let app, address, dataRoot = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<IVolumeService>()
            let createReq = { Name = "inspect-me"; Driver = StorageDriverType.Local; DriverOpts = Dictionary(); Labels = Dictionary() }
            let createResult = client.CreateVolume(createReq, CancellationToken.None).Result
            let inspectReq = { Id = createResult.Id }
            let inspectResult = client.InspectVolume(inspectReq, CancellationToken.None).Result
            inspectResult.Id |> should equal createResult.Id
            inspectResult.Name |> should equal "inspect-me"
            inspectResult.Driver |> should equal StorageDriverType.Local
        finally
            stopApp app dataRoot

    [<Fact>]
    let ``ListVolumes via gRPC retourne les volumes crees`` () =
        let app, address, dataRoot = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<IVolumeService>()
            let req1 = { Name = "vol-a"; Driver = StorageDriverType.Local; DriverOpts = Dictionary(); Labels = Dictionary() }
            let req2 = { Name = "vol-b"; Driver = StorageDriverType.Local; DriverOpts = Dictionary(); Labels = Dictionary() }
            client.CreateVolume(req1, CancellationToken.None).Result |> ignore
            client.CreateVolume(req2, CancellationToken.None).Result |> ignore
            let listReq = { Filters = Dictionary() }
            let listResult = client.ListVolumes(listReq, CancellationToken.None).Result
            listResult.Volumes.Count |> should equal 2
            listResult.Volumes |> Seq.exists (fun v -> v.Name = "vol-a") |> should equal true
            listResult.Volumes |> Seq.exists (fun v -> v.Name = "vol-b") |> should equal true
        finally
            stopApp app dataRoot

    [<Fact>]
    let ``RemoveVolume sur volume inexistant via gRPC retourne echec`` () =
        let app, address, dataRoot = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<IVolumeService>()
            let req = { Id = "nonexistent"; Force = false }
            let result = client.RemoveVolume(req, CancellationToken.None).Result
            result.Success |> should equal false
            result.Message |> should haveSubstring "introuvable"
        finally
            stopApp app dataRoot

    [<Fact>]
    let ``InspectVolume sur volume inexistant via gRPC lance RpcException`` () =
        let app, address, dataRoot = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<IVolumeService>()
            let req = { Id = "does-not-exist" }
            let ex = Assert.Throws<AggregateException>(fun () -> client.InspectVolume(req, CancellationToken.None).Result |> ignore)
            ex.InnerException.Message |> should haveSubstring "introuvable"
        finally
            stopApp app dataRoot

    [<Fact>]
    let ``CreateVolume avec nom injection via gRPC lance exception`` () =
        let app, address, dataRoot = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<IVolumeService>()
            let req = { Name = "test; rm -rf /"; Driver = StorageDriverType.Local; DriverOpts = Dictionary(); Labels = Dictionary() }
            let ex = Assert.Throws<AggregateException>(fun () -> client.CreateVolume(req, CancellationToken.None).Result |> ignore)
            ex.InnerException.Message |> should haveSubstring "Le nom du volume"
        finally
            stopApp app dataRoot

    [<Fact>]
    let ``PruneVolumes via gRPC retourne zero quand aucun volume`` () =
        let app, address, dataRoot = startApp ()
        try
            use channel = createChannel address
            let client = channel.CreateGrpcService<IVolumeService>()
            let req: PruneVolumesRequest = { Placeholder = false }
            let result = client.PruneVolumes(req, CancellationToken.None).Result
            result.Count |> should equal 0
            result.VolumesDeleted.Count |> should equal 0
        finally
            stopApp app dataRoot
