namespace Diplo.Volume.Tests

module VolumeServiceImplTests =

    open System
    open System.IO
    open System.Collections.Generic
    open System.Threading
    open System.Threading.Tasks
    open Xunit
    open FsUnit.Xunit
    open Grpc.Core
    open Grpc.Core.Testing
    open Diplo.Grpc.Volume
    open Diplo.Volume.Services
    open Diplo.Abstractions.SecurityValidation

    do addAllowedVolumeDir(Path.GetTempPath())
    do addAllowedVolumeDir(Directory.GetCurrentDirectory())

    let createService () =
        let mock = MockVolumeDriver()
        let svc = VolumeServiceImpl(mock.Mock)
        svc, mock

    let createCtx () =
        TestServerCallContext.Create(
            "test", "localhost", DateTime.UtcNow, Metadata(), CancellationToken.None,
            "peer", Unchecked.defaultof<AuthContext>, Unchecked.defaultof<ContextPropagationToken>,
            Unchecked.defaultof<System.Func<Metadata,Task>>, Unchecked.defaultof<System.Func<WriteOptions>>, Unchecked.defaultof<System.Action<WriteOptions>>)

    [<Fact>]
    let ``CreateVolume avec nom retourne le nom et le mountpoint`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let req = CreateVolumeRequest(Name = "mon-volume", Driver = StorageDriverType.Local)
        let result = svc.CreateVolume(req, ctx).Result
        String.IsNullOrEmpty(result.Id) |> should equal false
        result.Name |> should equal "mon-volume"
        result.Driver |> should equal StorageDriverType.Local
        String.IsNullOrEmpty(result.Mountpoint) |> should equal false
        String.IsNullOrEmpty(result.CreatedAt) |> should equal false
        mock.Volumes.Count |> should equal 1

    [<Fact>]
    let ``CreateVolume sans nom genere un id automatiquement`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = CreateVolumeRequest(Name = "", Driver = StorageDriverType.Local)
        let result = svc.CreateVolume(req, ctx).Result
        String.IsNullOrEmpty(result.Id) |> should equal false
        result.Id.Length |> should equal 32

    [<Fact>]
    let ``CreateVolume avec labels retourne le nom`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let req = CreateVolumeRequest(Name = "vol-labels", Driver = StorageDriverType.Local)
        req.Labels.Add("env", "test")
        req.Labels.Add("app", "web")
        let result = svc.CreateVolume(req, ctx).Result
        result.Name |> should equal "vol-labels"
        mock.Volumes.Count |> should equal 1

    [<Fact>]
    let ``RemoveVolume sur volume existant retourne success`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let createReq = CreateVolumeRequest(Name = "to-delete", Driver = StorageDriverType.Local)
        let createResult = svc.CreateVolume(createReq, ctx).Result
        let req = RemoveVolumeRequest(Id = createResult.Id, Force = false)
        let result = svc.RemoveVolume(req, ctx).Result
        result.Success |> should equal true
        result.Message |> should equal "Volume supprimé"

    [<Fact>]
    let ``RemoveVolume sur volume inexistant lance RpcException NotFound`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = RemoveVolumeRequest(Id = "nonexistent", Force = false)
        let ex = Assert.Throws<AggregateException>(fun () -> svc.RemoveVolume(req, ctx).Result |> ignore)
        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.NotFound

    [<Fact>]
    let ``InspectVolume retourne les metadonnees du volume`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let createReq = CreateVolumeRequest(Name = "vol-inspect", Driver = StorageDriverType.Local)
        let createResult = svc.CreateVolume(createReq, ctx).Result
        let req = InspectVolumeRequest(Id = createResult.Id)
        let result = svc.InspectVolume(req, ctx).Result
        result.Id |> should equal createResult.Id
        result.Name |> should equal "vol-inspect"
        result.Driver |> should equal StorageDriverType.Local
        result.State |> should equal MountState.Unmounted
        result.SizeBytes |> should equal 1024L

    [<Fact>]
    let ``InspectVolume sur volume inexistant lance RpcException NotFound`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = InspectVolumeRequest(Id = "nonexistent")
        let ex = Assert.Throws<AggregateException>(fun () -> svc.InspectVolume(req, ctx).Result |> ignore)
        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.NotFound

    [<Fact>]
    let ``ListVolumes retourne les volumes crees`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let req1 = CreateVolumeRequest(Name = "vol-1", Driver = StorageDriverType.Local)
        let req2 = CreateVolumeRequest(Name = "vol-2", Driver = StorageDriverType.Local)
        svc.CreateVolume(req1, ctx).Result |> ignore
        svc.CreateVolume(req2, ctx).Result |> ignore
        let req = ListVolumesRequest()
        let result = svc.ListVolumes(req, ctx).Result
        result.Volumes.Count |> should equal 2
        result.Volumes |> Seq.exists (fun v -> v.Name = "vol-1") |> should equal true
        result.Volumes |> Seq.exists (fun v -> v.Name = "vol-2") |> should equal true

    [<Fact>]
    let ``ListVolumes retourne vide quand aucun volume`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = ListVolumesRequest()
        let result = svc.ListVolumes(req, ctx).Result
        result.Volumes.Count |> should equal 0

    [<Fact>]
    let ``MountVolume retourne MountState Mounted`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let createReq = CreateVolumeRequest(Name = "vol-mount", Driver = StorageDriverType.Local)
        let createResult = svc.CreateVolume(createReq, ctx).Result
        let targetPath = Path.Combine(Path.GetTempPath(), "diplo-mount-test")
        let req = MountVolumeRequest(Id = createResult.Id, TargetPath = targetPath)
        let result = svc.MountVolume(req, ctx).Result
        result.State |> should equal MountState.Mounted
        String.IsNullOrEmpty(result.Mountpoint) |> should equal false
        result.Message |> should equal "Volume monté"

    [<Fact>]
    let ``UnmountVolume retourne MountState Unmounted`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        let createReq = CreateVolumeRequest(Name = "vol-unmount", Driver = StorageDriverType.Local)
        let createResult = svc.CreateVolume(createReq, ctx).Result
        let targetPath = Path.Combine(Path.GetTempPath(), "diplo-unmount-test")
        let mountReq = MountVolumeRequest(Id = createResult.Id, TargetPath = targetPath)
        svc.MountVolume(mountReq, ctx).Result |> ignore
        let req = UnmountVolumeRequest(Id = createResult.Id, TargetPath = targetPath)
        let result = svc.UnmountVolume(req, ctx).Result
        result.State |> should equal MountState.Unmounted
        result.Message |> should equal "Démonté"
