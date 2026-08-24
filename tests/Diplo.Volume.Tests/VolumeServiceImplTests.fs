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
    open Diplo.Grpc
    open Diplo.Grpc.Volume
    open Diplo.Volume.Drivers
    open Diplo.Volume.Services
    open Diplo.Abstractions.SecurityValidation

    do addAllowedVolumeDir (Path.GetTempPath())
    do addAllowedVolumeDir (Directory.GetCurrentDirectory())

    let createService () =
        let mock = MockVolumeDriver()
        let registry = VolumeDriverRegistry()
        registry.Register(StorageDriverType.Local, mock.Mock)
        let svc = VolumeServiceImpl(registry)
        svc, mock

    let createCtx () = CancellationToken.None

    [<Fact>]
    let ``CreateVolume avec nom retourne le nom et le mountpoint`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()

        let req =
            { Name = "mon-volume"
              Driver = StorageDriverType.Local
              DriverOpts = Dictionary<string, string>()
              Labels = Dictionary<string, string>() }

        let result = (svc :> IVolumeService).CreateVolume(req, ctx).Result
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

        let req =
            { Name = ""
              Driver = StorageDriverType.Local
              DriverOpts = Dictionary<string, string>()
              Labels = Dictionary<string, string>() }

        let result = (svc :> IVolumeService).CreateVolume(req, ctx).Result
        String.IsNullOrEmpty(result.Id) |> should equal false
        result.Id.Length |> should equal 32

    [<Fact>]
    let ``CreateVolume avec labels retourne le nom`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()

        let req =
            { Name = "vol-labels"
              Driver = StorageDriverType.Local
              DriverOpts = Dictionary<string, string>()
              Labels = Dictionary<string, string>() }

        req.Labels.Add("env", "test")
        req.Labels.Add("app", "web")
        let result = (svc :> IVolumeService).CreateVolume(req, ctx).Result
        result.Name |> should equal "vol-labels"
        mock.Volumes.Count |> should equal 1

    [<Fact>]
    let ``RemoveVolume sur volume existant retourne success`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()

        let createReq =
            { Name = "to-delete"
              Driver = StorageDriverType.Local
              DriverOpts = Dictionary<string, string>()
              Labels = Dictionary<string, string>() }

        let createResult = (svc :> IVolumeService).CreateVolume(createReq, ctx).Result
        let req = { Id = createResult.Id; Force = false }
        let result = (svc :> IVolumeService).RemoveVolume(req, ctx).Result
        result.Success |> should equal true
        result.Message |> should equal "Volume supprimé"

    [<Fact>]
    let ``RemoveVolume sur volume inexistant retourne success false`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Id = "nonexistent"; Force = false }
        let result = (svc :> IVolumeService).RemoveVolume(req, ctx).Result
        result.Success |> should equal false
        result.Message |> should haveSubstring "introuvable"

    [<Fact>]
    let ``InspectVolume retourne les metadonnees du volume`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()

        let createReq =
            { Name = "vol-inspect"
              Driver = StorageDriverType.Local
              DriverOpts = Dictionary<string, string>()
              Labels = Dictionary<string, string>() }

        let createResult = (svc :> IVolumeService).CreateVolume(createReq, ctx).Result
        let req = { Id = createResult.Id }
        let result = (svc :> IVolumeService).InspectVolume(req, ctx).Result
        result.Id |> should equal createResult.Id
        result.Name |> should equal "vol-inspect"
        result.Driver |> should equal StorageDriverType.Local
        result.State |> should equal MountState.Unmounted
        result.SizeBytes |> should equal 1024L

    [<Fact>]
    let ``InspectVolume sur volume inexistant lance RpcException NotFound`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Id = "nonexistent" }

        let ex =
            Assert.Throws<AggregateException>(fun () ->
                (svc :> IVolumeService).InspectVolume(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.NotFound

    [<Fact>]
    let ``ListVolumes retourne les volumes crees`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()

        let req1 =
            { Name = "vol-1"
              Driver = StorageDriverType.Local
              DriverOpts = Dictionary<string, string>()
              Labels = Dictionary<string, string>() }

        let req2 =
            { Name = "vol-2"
              Driver = StorageDriverType.Local
              DriverOpts = Dictionary<string, string>()
              Labels = Dictionary<string, string>() }

        (svc :> IVolumeService).CreateVolume(req1, ctx).Result |> ignore
        (svc :> IVolumeService).CreateVolume(req2, ctx).Result |> ignore
        let req = { Filters = Dictionary<string, string>() }
        let result = (svc :> IVolumeService).ListVolumes(req, ctx).Result
        result.Volumes.Count |> should equal 2
        result.Volumes |> Seq.exists (fun v -> v.Name = "vol-1") |> should equal true
        result.Volumes |> Seq.exists (fun v -> v.Name = "vol-2") |> should equal true

    [<Fact>]
    let ``ListVolumes retourne vide quand aucun volume`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Filters = Dictionary<string, string>() }
        let result = (svc :> IVolumeService).ListVolumes(req, ctx).Result
        result.Volumes.Count |> should equal 0

    [<Fact>]
    let ``MountVolume retourne MountState Mounted`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()

        let createReq =
            { Name = "vol-mount"
              Driver = StorageDriverType.Local
              DriverOpts = Dictionary<string, string>()
              Labels = Dictionary<string, string>() }

        let createResult = (svc :> IVolumeService).CreateVolume(createReq, ctx).Result
        let targetPath = Path.Combine(Path.GetTempPath(), "diplo-mount-test")

        let req =
            { Id = createResult.Id
              TargetPath = targetPath
              Options = Dictionary<string, string>() }

        let result = (svc :> IVolumeService).MountVolume(req, ctx).Result
        result.State |> should equal MountState.Mounted
        String.IsNullOrEmpty(result.Mountpoint) |> should equal false
        result.Message |> should equal "Volume monté"

    [<Fact>]
    let ``UnmountVolume retourne MountState Unmounted`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()

        let createReq =
            { Name = "vol-unmount"
              Driver = StorageDriverType.Local
              DriverOpts = Dictionary<string, string>()
              Labels = Dictionary<string, string>() }

        let createResult = (svc :> IVolumeService).CreateVolume(createReq, ctx).Result
        let targetPath = Path.Combine(Path.GetTempPath(), "diplo-unmount-test")

        let mountReq =
            { Id = createResult.Id
              TargetPath = targetPath
              Options = Dictionary<string, string>() }

        (svc :> IVolumeService).MountVolume(mountReq, ctx).Result |> ignore

        let req: UnmountVolumeRequest =
            { Id = createResult.Id
              TargetPath = targetPath }

        let result = (svc :> IVolumeService).UnmountVolume(req, ctx).Result
        result.State |> should equal MountState.Unmounted
        result.Message |> should equal "Démonté"

    // --- Sécurité : CreateVolume ---
    [<Fact>]
    let ``CreateVolume avec nom injection lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()

        let req =
            { Name = "test; rm -rf /"
              Driver = StorageDriverType.Local
              DriverOpts = Dictionary<string, string>()
              Labels = Dictionary<string, string>() }

        let ex =
            Assert.Throws<AggregateException>(fun () -> (svc :> IVolumeService).CreateVolume(req, ctx).Result |> ignore)

        ex.InnerException.Message |> should haveSubstring "Le nom du volume"

    [<Fact>]
    let ``CreateVolume avec label clé invalide lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()

        let req =
            { Name = "vol-ok"
              Driver = StorageDriverType.Local
              DriverOpts = Dictionary<string, string>()
              Labels = Dictionary<string, string>() }

        req.Labels.Add("bad;key", "val")

        let ex =
            Assert.Throws<AggregateException>(fun () -> (svc :> IVolumeService).CreateVolume(req, ctx).Result |> ignore)

        ex.InnerException.Message |> should haveSubstring "La clé du label"

    [<Fact>]
    let ``CreateVolume avec label valeur invalide lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()

        let req =
            { Name = "vol-ok"
              Driver = StorageDriverType.Local
              DriverOpts = Dictionary<string, string>()
              Labels = Dictionary<string, string>() }

        req.Labels.Add("env", "bad|value")

        let ex =
            Assert.Throws<AggregateException>(fun () -> (svc :> IVolumeService).CreateVolume(req, ctx).Result |> ignore)

        ex.InnerException.Message |> should haveSubstring "La valeur du label"

    // --- Sécurité : RemoveVolume ---
    [<Fact>]
    let ``RemoveVolume avec id vide lance RpcException`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Id = ""; Force = false }

        let ex =
            Assert.Throws<AggregateException>(fun () -> (svc :> IVolumeService).RemoveVolume(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    [<Fact>]
    let ``RemoveVolume avec id injection lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Id = "id; rm -rf /"; Force = false }

        let ex =
            Assert.Throws<AggregateException>(fun () -> (svc :> IVolumeService).RemoveVolume(req, ctx).Result |> ignore)

        ex.InnerException.Message |> should haveSubstring "L'identifiant du volume"

    // --- Sécurité : InspectVolume ---
    [<Fact>]
    let ``InspectVolume avec id vide lance RpcException`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Id = "" }

        let ex =
            Assert.Throws<AggregateException>(fun () ->
                (svc :> IVolumeService).InspectVolume(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    [<Fact>]
    let ``InspectVolume avec id injection lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Id = "`whoami`" }

        let ex =
            Assert.Throws<AggregateException>(fun () ->
                (svc :> IVolumeService).InspectVolume(req, ctx).Result |> ignore)

        ex.InnerException.Message |> should haveSubstring "L'identifiant du volume"

    // --- Sécurité : MountVolume ---
    [<Fact>]
    let ``MountVolume avec id vide lance RpcException`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()

        let req: MountVolumeRequest =
            { Id = ""
              TargetPath = Path.Combine(Path.GetTempPath(), "test")
              Options = Dictionary<string, string>() }

        let ex =
            Assert.Throws<AggregateException>(fun () -> (svc :> IVolumeService).MountVolume(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    [<Fact>]
    let ``MountVolume avec target vide lance RpcException`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()

        let req =
            { Id = "vol123"
              TargetPath = ""
              Options = Dictionary<string, string>() }

        let ex =
            Assert.Throws<AggregateException>(fun () -> (svc :> IVolumeService).MountVolume(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    [<Fact>]
    let ``MountVolume avec chemin traversal lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()

        let req =
            { Id = "vol123"
              TargetPath = "/etc/../etc/passwd"
              Options = Dictionary<string, string>() }

        let ex =
            Assert.Throws<AggregateException>(fun () -> (svc :> IVolumeService).MountVolume(req, ctx).Result |> ignore)

        ex.InnerException.Message |> should haveSubstring "Le chemin cible"

    [<Fact>]
    let ``MountVolume avec chemin non autorise lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()

        let req =
            { Id = "vol123"
              TargetPath = @"C:\Windows\System32\evil"
              Options = Dictionary<string, string>() }

        let ex =
            Assert.Throws<AggregateException>(fun () -> (svc :> IVolumeService).MountVolume(req, ctx).Result |> ignore)

        ex.InnerException.Message |> should haveSubstring "Le chemin cible"

    // --- Sécurité : UnmountVolume ---
    [<Fact>]
    let ``UnmountVolume avec id vide lance RpcException`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()

        let req =
            { Id = ""
              TargetPath = Path.Combine(Path.GetTempPath(), "test") }

        let ex =
            Assert.Throws<AggregateException>(fun () ->
                (svc :> IVolumeService).UnmountVolume(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    [<Fact>]
    let ``UnmountVolume avec target vide lance RpcException`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Id = "vol123"; TargetPath = "" }

        let ex =
            Assert.Throws<AggregateException>(fun () ->
                (svc :> IVolumeService).UnmountVolume(req, ctx).Result |> ignore)

        let rpcEx = ex.InnerException :?> RpcException
        rpcEx.StatusCode |> should equal StatusCode.InvalidArgument

    [<Fact>]
    let ``UnmountVolume avec chemin traversal lance exception`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()

        let req =
            { Id = "vol123"
              TargetPath = @"C:\tmp\..\..\Windows\System32" }

        let ex =
            Assert.Throws<AggregateException>(fun () ->
                (svc :> IVolumeService).UnmountVolume(req, ctx).Result |> ignore)

        ex.InnerException.Message |> should haveSubstring "Le chemin cible"

    // --- PruneVolumes ---
    [<Fact>]
    let ``PruneVolumes retourne vide quand aucun volume a supprimer`` () =
        let svc, _ = createService ()
        let ctx = createCtx ()
        let req = { Placeholder = false }
        let result = (svc :> IVolumeService).PruneVolumes(req, ctx).Result
        result.Count |> should equal 0
        result.VolumesDeleted.Count |> should equal 0
        result.Message |> should equal "0 volume(s) supprimé(s)"

    [<Fact>]
    let ``PruneVolumes retourne les ids supprimes`` () =
        let svc, mock = createService ()
        let ctx = createCtx ()
        mock.PruneResult <- [ "vol-1"; "vol-2"; "vol-3" ]
        let req = { Placeholder = false }
        let result = (svc :> IVolumeService).PruneVolumes(req, ctx).Result
        result.Count |> should equal 3
        result.VolumesDeleted.Count |> should equal 3
        result.VolumesDeleted |> should contain "vol-1"
        result.VolumesDeleted |> should contain "vol-2"
        result.VolumesDeleted |> should contain "vol-3"
        result.Message |> should equal "3 volume(s) supprimé(s)"
