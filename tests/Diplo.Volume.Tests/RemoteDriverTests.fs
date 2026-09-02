namespace Diplo.Volume.Tests

module RemoteDriverTests =

    open System
    open System.IO
    open Xunit
    open Grpc.Core
    open FsUnit.Xunit
    open Diplo.Volume.Drivers

    let createTempDir () =
        let dir =
            Path.Combine(Path.GetTempPath(), "diplo-rdriver-test-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory(dir) |> ignore
        dir

    let cleanupDir (dir: string) =
        try
            if Directory.Exists(dir) then
                Directory.Delete(dir, true)
        with _ ->
            ()

    let assertInvalidArg (f: unit -> unit) =
        let ex = Assert.Throws<RpcException>(fun () -> f ())
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode)

    // ── SmbDriver ────────────────────────────────────────────────
    [<Fact>]
    let ``SmbDriver CreateVolume sans server lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()

        try
            let driver = SmbDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver

            assertInvalidArg (fun () ->
                driver.CreateVolume("test", Map.ofList [ "share", "myshare" ], Map.empty)
                |> ignore)
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``SmbDriver CreateVolume sans share lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()

        try
            let driver = SmbDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver

            assertInvalidArg (fun () ->
                driver.CreateVolume("test", Map.ofList [ "server", "srv01" ], Map.empty)
                |> ignore)
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``SmbDriver CreateVolume sans options lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()

        try
            let driver = SmbDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver
            assertInvalidArg (fun () -> driver.CreateVolume("test", Map.empty, Map.empty) |> ignore)
        finally
            cleanupDir tempRoot

    // ── NfsDriver ────────────────────────────────────────────────
    [<Fact>]
    let ``NfsDriver CreateVolume sans server lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()

        try
            let driver = NfsDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver

            assertInvalidArg (fun () ->
                driver.CreateVolume("test", Map.ofList [ "export", "share" ], Map.empty)
                |> ignore)
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``NfsDriver CreateVolume sans export lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()

        try
            let driver = NfsDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver

            assertInvalidArg (fun () ->
                driver.CreateVolume("test", Map.ofList [ "server", "10.0.0.1" ], Map.empty)
                |> ignore)
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``NfsDriver CreateVolume sans options lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()

        try
            let driver = NfsDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver
            assertInvalidArg (fun () -> driver.CreateVolume("test", Map.empty, Map.empty) |> ignore)
        finally
            cleanupDir tempRoot

    // ── CloudAwsDriver ──────────────────────────────────────────
    [<Fact>]
    let ``CloudAwsDriver CreateVolume sans fsId lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()

        try
            let driver = CloudAwsDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver

            assertInvalidArg (fun () ->
                driver.CreateVolume("test", Map.ofList [ "region", "eu-west-1" ], Map.empty)
                |> ignore)
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``CloudAwsDriver CreateVolume sans region lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()

        try
            let driver = CloudAwsDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver

            assertInvalidArg (fun () ->
                driver.CreateVolume("test", Map.ofList [ "fsId", "fs-12345" ], Map.empty)
                |> ignore)
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``CloudAwsDriver CreateVolume sans options lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()

        try
            let driver = CloudAwsDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver
            assertInvalidArg (fun () -> driver.CreateVolume("test", Map.empty, Map.empty) |> ignore)
        finally
            cleanupDir tempRoot

    // ── CloudGcpDriver ──────────────────────────────────────────
    [<Fact>]
    let ``CloudGcpDriver CreateVolume sans ipAddress lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()

        try
            let driver = CloudGcpDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver

            assertInvalidArg (fun () ->
                driver.CreateVolume("test", Map.ofList [ "volumeName", "vol1" ], Map.empty)
                |> ignore)
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``CloudGcpDriver CreateVolume sans volumeName lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()

        try
            let driver = CloudGcpDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver

            assertInvalidArg (fun () ->
                driver.CreateVolume("test", Map.ofList [ "ipAddress", "10.0.0.1" ], Map.empty)
                |> ignore)
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``CloudGcpDriver CreateVolume sans options lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()

        try
            let driver = CloudGcpDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver
            assertInvalidArg (fun () -> driver.CreateVolume("test", Map.empty, Map.empty) |> ignore)
        finally
            cleanupDir tempRoot

    // ── CloudAzureDriver ────────────────────────────────────────
    [<Fact>]
    let ``CloudAzureDriver CreateVolume sans storageAccount lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()

        try
            let driver =
                CloudAzureDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver

            assertInvalidArg (fun () ->
                driver.CreateVolume("test", Map.ofList [ "shareName", "myfileshare" ], Map.empty)
                |> ignore)
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``CloudAzureDriver CreateVolume sans shareName lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()

        try
            let driver =
                CloudAzureDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver

            assertInvalidArg (fun () ->
                driver.CreateVolume("test", Map.ofList [ "storageAccount", "mystorage" ], Map.empty)
                |> ignore)
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``CloudAzureDriver CreateVolume sans options lève RpcException InvalidArgument`` () =
        let tempRoot = createTempDir ()

        try
            let driver =
                CloudAzureDriver(tempRoot) :> Diplo.Abstractions.Interfaces.IVolumeDriver

            assertInvalidArg (fun () -> driver.CreateVolume("test", Map.empty, Map.empty) |> ignore)
        finally
            cleanupDir tempRoot

    // ── AzureNetUse.buildArgs : la clé ne transite jamais par argv ──

    [<Fact>]
    let ``AzureNetUse buildArgs place la cible le partage et l'utilisateur AZURE`` () =
        let args =
            AzureNetUse.buildArgs
                @"\\acc.file.core.windows.net\share"
                @"X:\mount"
                (Map.ofList [ "storageAccount", "acc"; "storageKey", "BASE64KEY==" ])

        args
        |> should equal
            [ @"X:\mount"
              @"\\acc.file.core.windows.net\share"
              @"/user:AZURE\acc"
              "*"
              "/persistent:no" ]

    [<Fact>]
    let ``AzureNetUse buildArgs n'expose jamais la cle dans les arguments`` () =
        let args =
            AzureNetUse.buildArgs
                @"\\acc.file.core.windows.net\share"
                @"X:\mount"
                (Map.ofList [ "storageAccount", "acc"; "storageKey", "SECRET-VALUE-123" ])

        Assert.DoesNotContain("SECRET-VALUE-123", args)
        // « * » force la lecture du secret sur l'entrée standard.
        Assert.Contains("*", args)

    [<Fact>]
    let ``AzureNetUse buildArgs sans storageKey lève RpcException InvalidArgument`` () =
        assertInvalidArg (fun () ->
            AzureNetUse.buildArgs @"\\a\f\s" @"X:\m" (Map.ofList [ "storageAccount", "a" ])
            |> ignore)

    [<Fact>]
    let ``AzureNetUse buildArgs sans storageAccount lève RpcException InvalidArgument`` () =
        assertInvalidArg (fun () ->
            AzureNetUse.buildArgs @"\\a\f\s" @"X:\m" (Map.ofList [ "storageKey", "K" ])
            |> ignore)

    // ── NfsMountOptions.buildOptions : allow-list stricte ───────

    [<Fact>]
    let ``NfsMountOptions buildOptions ajoute toujours nolock`` () =
        NfsMountOptions.buildOptions Map.empty |> should equal "nolock"

    [<Fact>]
    let ``NfsMountOptions buildOptions conserve uniquement les options autorisées`` () =
        // Map itérée par ordre de clés : ro < timeo < vers.
        let opts =
            Map.ofList [ "ro", ""
                         "vers", "4.1"
                         "timeo", "100"
                         "intr", "1"
                         "exec", "true"
                         "credentials", "/etc/passwd" ]

        NfsMountOptions.buildOptions opts
        |> should equal "nolock,ro,timeo=100,vers=4.1"

    [<Fact>]
    let ``NfsMountOptions buildOptions traite les flags sans valeur`` () =
        NfsMountOptions.buildOptions (Map.ofList [ "hard", "true"; "soft", "" ])
        |> should equal "nolock,hard,soft"

    [<Fact>]
    let ``NfsMountOptions buildOptions normalise la casse des clés`` () =
        NfsMountOptions.buildOptions (Map.ofList [ "RO", ""; "Vers", "3" ])
        |> should equal "nolock,ro,vers=3"

    [<Fact>]
    let ``NfsMountOptions buildOptions rejette une option utilisateur non autorisée avec valeur sensible`` () =
        let opts = Map.ofList [ "sec", "krb5i"; "port", "2049;rm" ]

        NfsMountOptions.buildOptions opts |> should equal "nolock"

    // ── RemotePath : branches de succès ──────────────────────────

    [<Fact>]
    let ``NfsDriver RemotePath construit server:export`` () =
        let tempRoot = createTempDir ()

        try
            let driver = NfsDriver(tempRoot)
            driver.RemotePath (Map.ofList [ "server", "10.0.0.1"; "export", "data" ])
            |> should equal "10.0.0.1:/data"
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``SmbDriver RemotePath construit unc share`` () =
        let tempRoot = createTempDir ()

        try
            let driver = SmbDriver(tempRoot)
            driver.RemotePath (Map.ofList [ "server", "srv01"; "share", "myshare" ])
            |> should equal @"\\srv01\myshare"
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``CloudAwsDriver RemotePath construit efs dns`` () =
        let tempRoot = createTempDir ()

        try
            let driver = CloudAwsDriver(tempRoot)

            driver.RemotePath (Map.ofList [ "fsId", "fs-12345"; "region", "eu-west-1" ])
            |> should equal "fs-12345.efs.eu-west-1.amazonaws.com:/"
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``CloudAzureDriver RemotePath construit unc azure`` () =
        let tempRoot = createTempDir ()

        try
            let driver = CloudAzureDriver(tempRoot)

            driver.RemotePath (Map.ofList [ "storageAccount", "acc"; "shareName", "share" ])
            |> should equal @"\\acc.file.core.windows.net\share"
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``CloudGcpDriver RemotePath construit ip:volume`` () =
        let tempRoot = createTempDir ()

        try
            let driver = CloudGcpDriver(tempRoot)

            driver.RemotePath (Map.ofList [ "ipAddress", "10.0.0.1"; "volumeName", "vol1" ])
            |> should equal "10.0.0.1:/vol1"
        finally
            cleanupDir tempRoot
