namespace DiploWalker.Volume.Tests

module RemoteDriverHelpersTests =

    open System
    open System.IO
    open System.Text.Json
    open Xunit
    open FsUnit.Xunit
    open Grpc.Core
    open DiploWalker.Volume.Drivers
    open DiploWalker.Abstractions.SecurityValidation

    do addAllowedVolumeDir (Path.GetTempPath())

    let createTempDir () =
        let dir =
            Path.Combine(Path.GetTempPath(), "diplo-rhelpers-test-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory(dir) |> ignore
        dir

    let cleanupDir (dir: string) =
        try
            if Directory.Exists(dir) then
                Directory.Delete(dir, true)
        with _ ->
            ()

    let parseJson (json: string) = JsonDocument.Parse(json).RootElement

    // â”€â”€ extractRemotePathFromInfo â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``extractRemotePathFromInfo retourne le chemin distant`` () =
        let info = parseJson """{"id":"v1","remotePath":"srv:/export"}"""
        RemoteDriverHelpers.extractRemotePathFromInfo info "v1" |> should equal "srv:/export"

    [<Fact>]
    let ``extractRemotePathFromInfo leve NotFound si chemin manquant`` () =
        let info = parseJson """{"id":"v1"}"""

        let ex =
            Assert.Throws<RpcException>(fun () ->
                RemoteDriverHelpers.extractRemotePathFromInfo info "v1" |> ignore)

        ex.StatusCode |> should equal StatusCode.NotFound

    // â”€â”€ parseMergedOpts â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``parseMergedOpts avec options vides retourne uniquement driverOpts`` () =
        let info = parseJson """{"driverOpts":{"vers":"4.1","ro":""}}"""
        RemoteDriverHelpers.parseMergedOpts info "" |> should equal (Map.ofList [ "vers", "4.1"; "ro", "" ])

    [<Fact>]
    let ``parseMergedOpts fusionne options et driverOpts`` () =
        let info = parseJson """{"driverOpts":{"vers":"4.1"}}"""
        let merged = RemoteDriverHelpers.parseMergedOpts info "hard=true;timeo=100"
        merged |> should equal (Map.ofList [ "vers", "4.1"; "hard", "true"; "timeo", "100" ])

    [<Fact>]
    let ``parseMergedOpts donne la precedence aux options de la requete`` () =
        let info = parseJson """{"driverOpts":{"vers":"4.0"}}"""
        RemoteDriverHelpers.parseMergedOpts info "vers=4.2" |> should equal (Map.ofList [ "vers", "4.2" ])

    [<Fact>]
    let ``parseMergedOpts ignore les segments sans signe egal`` () =
        let info = parseJson """{"driverOpts":{}}"""
        RemoteDriverHelpers.parseMergedOpts info "ro=;malformed;timeo=50"
        |> should equal (Map.ofList [ "ro", ""; "timeo", "50" ])

    [<Fact>]
    let ``parseMergedOpts sans aucun element retourne vide`` () =
        let info = parseJson """{}"""
        RemoteDriverHelpers.parseMergedOpts info "" |> should equal Map.empty<string, string>

    // â”€â”€ mountVolume â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``mountVolume appelle mountFn avec le chemin distant et les options fusionnees`` () =
        let root = createTempDir ()

        try
            let store = RemoteVolumeStore(root, "nfs")
            let (id, _) = store.CreateVolume("v", "srv:/export", Map.empty, Map.ofList [ "vers", "4.1" ])
            let mutable captured = None
            let target = Path.Combine(Path.GetTempPath(), "diplo-mnt-" + Guid.NewGuid().ToString("N"))

            let mountFn (remotePath: string) (targetPath: string) (opts: Map<string, string>) =
                captured <- Some(remotePath, targetPath, opts)

            let (success, mountDir) =
                RemoteDriverHelpers.mountVolume store id target "hard=true" mountFn

            success |> should equal true
            mountDir |> should equal target

            let (rp, tp, opts) = captured.Value
            rp |> should equal "srv:/export"
            tp |> should equal target
            opts |> should equal (Map.ofList [ "vers", "4.1"; "hard", "true" ])
        finally
            cleanupDir root

    [<Fact>]
    let ``mountVolume leve NotFound si volume inconnu`` () =
        let root = createTempDir ()

        try
            let store = RemoteVolumeStore(root, "nfs")
            let target = Path.Combine(Path.GetTempPath(), "diplo-mnt-" + Guid.NewGuid().ToString("N"))

            let mountFn (_remotePath: string) (_targetPath: string) (_opts: Map<string, string>) = ()

            let ex =
                Assert.Throws<RpcException>(fun () ->
                    RemoteDriverHelpers.mountVolume store "inconnu" target "" mountFn
                    |> ignore)

            ex.StatusCode |> should equal StatusCode.NotFound
        finally
            cleanupDir root

    // â”€â”€ unmountVolume â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``unmountVolume appelle unmountFn et retourne Demonte`` () =
        let target = Path.Combine(Path.GetTempPath(), "diplo-mnt-" + Guid.NewGuid().ToString("N"))
        let mutable unmountedPath = ""

        let unmountFn (targetPath: string) = unmountedPath <- targetPath

        let (success, message) =
            RemoteDriverHelpers.unmountVolume "v1" target unmountFn

        success |> should equal true
        message |> should equal "Démonté"
        unmountedPath |> should equal target

    [<Fact>]
    let ``unmountVolume avec id vide leve InvalidArgument`` () =
        let unmountFn (_targetPath: string) = ()

        let ex =
            Assert.Throws<RpcException>(fun () ->
                RemoteDriverHelpers.unmountVolume "" "/mnt/v" unmountFn |> ignore)

        ex.StatusCode |> should equal StatusCode.InvalidArgument

    // â”€â”€ unmountNfsLike â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``unmountNfsLike propage RpcException si umount echoue`` () =
        let ex =
            Assert.Throws<RpcException>(fun () -> RemoteDriverHelpers.unmountNfsLike "/chemin/inexistant" |> ignore)

        ex.StatusCode |> should equal StatusCode.Internal

    [<Fact>]
    let ``unmountNfsLike propage RpcException si chemin inexistant`` () =
        let ex =
            Assert.Throws<RpcException>(fun () -> RemoteDriverHelpers.unmountNfsLike "/tmp/test-unmount" |> ignore)

        ex.StatusCode |> should equal StatusCode.Internal

