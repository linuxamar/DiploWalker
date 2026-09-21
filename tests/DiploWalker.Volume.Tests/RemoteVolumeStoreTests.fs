namespace DiploWalker.Volume.Tests

module RemoteVolumeStoreTests =

    open System
    open System.IO
    open Xunit
    open FsUnit.Xunit
    open Grpc.Core
    open DiploWalker.Abstractions
    open DiploWalker.Volume.Drivers

    let createTempDir () =
        let dir =
            Path.Combine(Path.GetTempPath(), "diplo-rstore-test-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory(dir) |> ignore
        dir

    let cleanupDir (dir: string) =
        try
            if Directory.Exists(dir) then
                Directory.Delete(dir, true)
        with _ ->
            ()

    // â”€â”€ CreateVolume â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``CreateVolume cree meta.json et retourne id et remotePath`` () =
        let root = createTempDir ()

        try
            let store = RemoteVolumeStore(root, "testdrv")
            let (id, remotePath) = store.CreateVolume("vol1", @"\\srv\share", Map.empty, Map.empty)

            id.Length |> should equal 32 // GUID au format N
            remotePath |> should equal @"\\srv\share"
            File.Exists(Path.Combine(root, "testdrv", id, "meta.json")) |> should be True
        finally
            cleanupDir root

    [<Fact>]
    let ``CreateVolume filtre les driverOpts sensibles du fichier persiste`` () =
        let root = createTempDir ()

        try
            let store = RemoteVolumeStore(root, "nfs")

            let opts =
                Map.ofList [ "storageKey", "SK-SECRET-123"; "vers", "4.1"; "password", "PW-SECRET-456" ]

            let (id, _) = store.CreateVolume("vol1", "srv:/export", Map.empty, opts)
            let meta = File.ReadAllText(Path.Combine(root, "nfs", id, "meta.json"))

            Assert.DoesNotContain("SK-SECRET-123", meta)
            Assert.DoesNotContain("PW-SECRET-456", meta)
            Assert.Contains("4.1", meta) // option non sensible conservÃ©e

            let opts =
                JsonHelpers.tryGetElement ((store.InspectVolume id).Value) "driverOpts"
                |> Option.get

            JsonHelpers.tryGetStringValue opts "storageKey" |> should equal None
            JsonHelpers.tryGetStringValue opts "password" |> should equal None
            JsonHelpers.tryGetStringValue opts "vers" |> should equal (Some "4.1")
        finally
            cleanupDir root

    [<Fact>]
    let ``CreateVolume filtre aussi les labels sensibles`` () =
        let root = createTempDir ()

        try
            let store = RemoteVolumeStore(root, "azure")

            let labels =
                Map.ofList [ "accountKey", "AK-SECRET-789"; "env", "prod" ]

            let (id, _) =
                store.CreateVolume(
                    "vol1",
                    @"\\acc.file.core.windows.net\share",
                    labels,
                    Map.ofList [ "storageAccount", "acc"; "shareName", "share" ]
                )
            let meta = File.ReadAllText(Path.Combine(root, "azure", id, "meta.json"))
            Assert.DoesNotContain("AK-SECRET-789", meta)

            let labels =
                JsonHelpers.tryGetElement ((store.InspectVolume id).Value) "labels"
                |> Option.get

            JsonHelpers.tryGetStringValue labels "accountKey" |> should equal None
            JsonHelpers.tryGetStringValue labels "env" |> should equal (Some "prod")
        finally
            cleanupDir root

    [<Fact>]
    let ``Le filtrage des cles sensibles est insensible a la casse`` () =
        let root = createTempDir ()

        try
            let store = RemoteVolumeStore(root, "testdrv")

            let (id, _) =
                store.CreateVolume(
                    "vol1",
                    "srv:/e",
                    Map.empty,
                    Map.ofList [ "StorageKey", "MIXEDCASE-SECRET" ]
                )

            let meta = File.ReadAllText(Path.Combine(root, "testdrv", id, "meta.json"))
            Assert.DoesNotContain("MIXEDCASE-SECRET", meta)
        finally
            cleanupDir root

    [<Fact>]
    let ``CreateVolume conserve les metadonnees attendues`` () =
        let root = createTempDir ()

        try
            let store = RemoteVolumeStore(root, "smb")

            let (id, remotePath) = store.CreateVolume("data", @"\\srv\data", Map.empty, Map.empty)
            let info = (store.InspectVolume id).Value

            JsonHelpers.tryGetStringValue info "id" |> should equal (Some id)
            JsonHelpers.tryGetStringValue info "name" |> should equal (Some "data")
            JsonHelpers.tryGetStringValue info "driver" |> should equal (Some "smb")
            JsonHelpers.tryGetStringValue info "remotePath" |> should equal (Some remotePath)
        finally
            cleanupDir root

    // â”€â”€ InspectVolume / VolumeExists / ListVolumes â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``InspectVolume retourne None pour un volume inconnu`` () =
        let root = createTempDir ()

        try
            let store = RemoteVolumeStore(root, "testdrv")
            store.InspectVolume "inconnu" |> should equal None
        finally
            cleanupDir root

    [<Fact>]
    let ``InspectVolume rejette un identifiant invalide`` () =
        let root = createTempDir ()

        try
            let store = RemoteVolumeStore(root, "testdrv")

            let ex = Assert.Throws<RpcException>(fun () -> store.InspectVolume "../escape" |> ignore)

            ex.StatusCode |> should equal StatusCode.InvalidArgument
        finally
            cleanupDir root

    [<Fact>]
    let ``VolumeExists reflÃ¨te la presence du volume`` () =
        let root = createTempDir ()

        try
            let store = RemoteVolumeStore(root, "testdrv")
            let (id, _) = store.CreateVolume("v", "p", Map.empty, Map.empty)

            store.VolumeExists id |> should be True
            store.VolumeExists "absent" |> should be False
        finally
            cleanupDir root

    [<Fact>]
    let ``ListVolumes liste les volumes et ignore les repertoires sans meta.json`` () =
        let root = createTempDir ()

        try
            let store = RemoteVolumeStore(root, "testdrv")

            let (id1, _) = store.CreateVolume("a", "pa", Map.empty, Map.empty)
            let (id2, _) = store.CreateVolume("b", "pb", Map.empty, Map.empty)

            // RÃ©pertoire orphelin sans meta.json : doit Ãªtre ignorÃ© par le listing.
            Directory.CreateDirectory(Path.Combine(root, "testdrv", "orphelin")) |> ignore

            let all = store.ListVolumes()
            all.Length |> should equal 2

            let ids = all |> List.choose (fun el -> JsonHelpers.tryGetStringValue el "id") |> Set.ofList

            ids.Contains id1 |> should be True
            ids.Contains id2 |> should be True
        finally
            cleanupDir root

    // â”€â”€ RemoveVolume / PruneAll â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``RemoveVolume supprime le volume puis retourne false`` () =
        let root = createTempDir ()

        try
            let store = RemoteVolumeStore(root, "testdrv")
            let (id, _) = store.CreateVolume("v", "p", Map.empty, Map.empty)

            store.RemoveVolume id |> should be True
            Directory.Exists(Path.Combine(root, "testdrv", id)) |> should be False
            store.RemoveVolume id |> should be False
        finally
            cleanupDir root

    [<Fact>]
    let ``RemoveVolume rejette un identifiant invalide`` () =
        let root = createTempDir ()

        try
            let store = RemoteVolumeStore(root, "testdrv")

            let ex = Assert.Throws<RpcException>(fun () -> store.RemoveVolume "bad;id" |> ignore)

            ex.StatusCode |> should equal StatusCode.InvalidArgument
        finally
            cleanupDir root

    [<Fact>]
    let ``PruneAll supprime tous les volumes et retourne leurs ids`` () =
        let root = createTempDir ()

        try
            let store = RemoteVolumeStore(root, "testdrv")
            store.PruneAll() |> should be Empty // vide au dÃ©part

            let (id1, _) = store.CreateVolume("a", "pa", Map.empty, Map.empty)
            let (id2, _) = store.CreateVolume("b", "pb", Map.empty, Map.empty)

            let removed = store.PruneAll() |> Set.ofList
            removed |> should equal (set [ id1; id2 ])

            Directory.GetDirectories(Path.Combine(root, "testdrv")).Length
            |> should equal 0
        finally
            cleanupDir root

