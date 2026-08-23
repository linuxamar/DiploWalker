namespace Diplo.Volume.Tests

module RemoteDriverTests =

    open System
    open System.IO
    open Xunit
    open Grpc.Core
    open Diplo.Volume.Drivers

    let createTempDir () =
        let dir = Path.Combine(Path.GetTempPath(), "diplo-rdriver-test-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(dir) |> ignore
        dir

    let cleanupDir (dir: string) =
        try if Directory.Exists(dir) then Directory.Delete(dir, true) with _ -> ()

    let assertInvalidArg (f: unit -> unit) =
        let ex = Assert.Throws<RpcException>(fun () -> f())
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode)

    // ── SmbDriver ────────────────────────────────────────────────
    [<Fact>]
    let ``SmbDriver CreateVolume sans server lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()
        try
            let driver = SmbDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver
            assertInvalidArg (fun () -> driver.CreateVolume("test", Map.ofList [ "share", "myshare" ], Map.empty) |> ignore)
        finally cleanupDir tempRoot

    [<Fact>]
    let ``SmbDriver CreateVolume sans share lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()
        try
            let driver = SmbDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver
            assertInvalidArg (fun () -> driver.CreateVolume("test", Map.ofList [ "server", "srv01" ], Map.empty) |> ignore)
        finally cleanupDir tempRoot

    [<Fact>]
    let ``SmbDriver CreateVolume sans options lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()
        try
            let driver = SmbDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver
            assertInvalidArg (fun () -> driver.CreateVolume("test", Map.empty, Map.empty) |> ignore)
        finally cleanupDir tempRoot

    // ── NfsDriver ────────────────────────────────────────────────
    [<Fact>]
    let ``NfsDriver CreateVolume sans server lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()
        try
            let driver = NfsDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver
            assertInvalidArg (fun () -> driver.CreateVolume("test", Map.ofList [ "export", "share" ], Map.empty) |> ignore)
        finally cleanupDir tempRoot

    [<Fact>]
    let ``NfsDriver CreateVolume sans export lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()
        try
            let driver = NfsDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver
            assertInvalidArg (fun () -> driver.CreateVolume("test", Map.ofList [ "server", "10.0.0.1" ], Map.empty) |> ignore)
        finally cleanupDir tempRoot

    [<Fact>]
    let ``NfsDriver CreateVolume sans options lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()
        try
            let driver = NfsDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver
            assertInvalidArg (fun () -> driver.CreateVolume("test", Map.empty, Map.empty) |> ignore)
        finally cleanupDir tempRoot

    // ── CloudAwsDriver ──────────────────────────────────────────
    [<Fact>]
    let ``CloudAwsDriver CreateVolume sans fsId lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()
        try
            let driver = CloudAwsDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver
            assertInvalidArg (fun () -> driver.CreateVolume("test", Map.ofList [ "region", "eu-west-1" ], Map.empty) |> ignore)
        finally cleanupDir tempRoot

    [<Fact>]
    let ``CloudAwsDriver CreateVolume sans region lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()
        try
            let driver = CloudAwsDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver
            assertInvalidArg (fun () -> driver.CreateVolume("test", Map.ofList [ "fsId", "fs-12345" ], Map.empty) |> ignore)
        finally cleanupDir tempRoot

    [<Fact>]
    let ``CloudAwsDriver CreateVolume sans options lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()
        try
            let driver = CloudAwsDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver
            assertInvalidArg (fun () -> driver.CreateVolume("test", Map.empty, Map.empty) |> ignore)
        finally cleanupDir tempRoot

    // ── CloudGcpDriver ──────────────────────────────────────────
    [<Fact>]
    let ``CloudGcpDriver CreateVolume sans ipAddress lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()
        try
            let driver = CloudGcpDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver
            assertInvalidArg (fun () -> driver.CreateVolume("test", Map.ofList [ "volumeName", "vol1" ], Map.empty) |> ignore)
        finally cleanupDir tempRoot

    [<Fact>]
    let ``CloudGcpDriver CreateVolume sans volumeName lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()
        try
            let driver = CloudGcpDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver
            assertInvalidArg (fun () -> driver.CreateVolume("test", Map.ofList [ "ipAddress", "10.0.0.1" ], Map.empty) |> ignore)
        finally cleanupDir tempRoot

    [<Fact>]
    let ``CloudGcpDriver CreateVolume sans options lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()
        try
            let driver = CloudGcpDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver
            assertInvalidArg (fun () -> driver.CreateVolume("test", Map.empty, Map.empty) |> ignore)
        finally cleanupDir tempRoot

    // ── CloudAzureDriver ────────────────────────────────────────
    [<Fact>]
    let ``CloudAzureDriver CreateVolume sans storageAccount lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()
        try
            let driver = CloudAzureDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver
            assertInvalidArg (fun () -> driver.CreateVolume("test", Map.ofList [ "shareName", "myfileshare" ], Map.empty) |> ignore)
        finally cleanupDir tempRoot

    [<Fact>]
    let ``CloudAzureDriver CreateVolume sans shareName lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()
        try
            let driver = CloudAzureDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver
            assertInvalidArg (fun () -> driver.CreateVolume("test", Map.ofList [ "storageAccount", "mystorage" ], Map.empty) |> ignore)
        finally cleanupDir tempRoot

    [<Fact>]
    let ``CloudAzureDriver CreateVolume sans options lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()
        try
            let driver = CloudAzureDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver
            assertInvalidArg (fun () -> driver.CreateVolume("test", Map.empty, Map.empty) |> ignore)
        finally cleanupDir tempRoot
