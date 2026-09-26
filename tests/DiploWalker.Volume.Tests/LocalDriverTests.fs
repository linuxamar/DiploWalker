namespace DiploWalker.Volume.Tests

module LocalDriverTests =

    open System
    open System.IO
    open Xunit
    open FsUnit.Xunit
    open DiploWalker.Volume.Drivers
    open DiploWalker.Abstractions.SecurityValidation

    do addAllowedVolumeDir (Path.GetTempPath())

    let ensureCwdAllowed () =
        addAllowedVolumeDir (Directory.GetCurrentDirectory())

    let createTempDir () =
        let dir =
            Path.Combine(Path.GetTempPath(), "diplo-vol-test-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory(dir) |> ignore
        dir

    let cleanupDir (dir: string) =
        try
            if Directory.Exists(dir) then
                Directory.Delete(dir, true)
        with _ ->
            ()

    [<Fact>]
    let ``CreateVolume cree un repertoire et retourne un id et un mountpoint`` () =
        let tempRoot = createTempDir ()

        try
            let driver = LocalVolumeDriver(tempRoot)
            let (id, mountpoint) = driver.CreateVolume("test-vol", Map.empty, Map.empty)

            String.IsNullOrEmpty(id) |> should equal false
            String.IsNullOrEmpty(mountpoint) |> should equal false
            Directory.Exists(mountpoint) |> should equal true
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``InspectVolume retourne Some pour un volume existant`` () =
        let tempRoot = createTempDir ()

        try
            let driver = LocalVolumeDriver(tempRoot)
            let (id, _) = driver.CreateVolume("inspect-me", Map.empty, Map.empty)
            let result = driver.InspectVolume(id)
            result.IsSome |> should equal true
            result.Value.GetProperty("name").GetString() |> should equal "inspect-me"
            result.Value.GetProperty("driver").GetString() |> should equal "local"
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``InspectVolume retourne None pour un id inexistant`` () =
        let tempRoot = createTempDir ()

        try
            let driver = LocalVolumeDriver(tempRoot)
            let result = driver.InspectVolume("nonexistent-id")
            result.IsNone |> should equal true
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``RemoveVolume supprime le repertoire et retourne true`` () =
        let tempRoot = createTempDir ()

        try
            let driver = LocalVolumeDriver(tempRoot)
            let (id, _) = driver.CreateVolume("to-delete", Map.empty, Map.empty)
            let result = driver.RemoveVolume(id, false)
            result |> should equal true
            driver.InspectVolume(id).IsNone |> should equal true
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``RemoveVolume retourne false pour un id inexistant`` () =
        let tempRoot = createTempDir ()

        try
            let driver = LocalVolumeDriver(tempRoot)
            let result = driver.RemoveVolume("nonexistent", false)
            result |> should equal false
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``ListVolumes retourne tous les volumes crees`` () =
        let tempRoot = createTempDir ()

        try
            let driver = LocalVolumeDriver(tempRoot)
            let (id1, _) = driver.CreateVolume("vol-a", Map.empty, Map.empty)
            let (id2, _) = driver.CreateVolume("vol-b", Map.empty, Map.empty)
            let volumes = driver.ListVolumes(Map.empty)
            volumes.Length |> should equal 2
            let names = volumes |> List.map (fun v -> v.GetProperty("name").GetString())
            names |> should contain "vol-a"
            names |> should contain "vol-b"
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``ListVolumes retourne liste vide quand aucun volume`` () =
        let tempRoot = createTempDir ()

        try
            let driver = LocalVolumeDriver(tempRoot)
            let volumes = driver.ListVolumes(Map.empty)
            volumes.Length |> should equal 0
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``CreateVolume avec labels les stocke dans meta`` () =
        let tempRoot = createTempDir ()

        try
            let driver = LocalVolumeDriver(tempRoot)
            let labels = Map [ ("env", "test"); ("team", "infra") ]
            let (id, _) = driver.CreateVolume("labeled", Map.empty, labels)
            let result = driver.InspectVolume(id)
            result.IsSome |> should equal true
            let lbl = result.Value.GetProperty("labels")
            lbl.GetProperty("env").GetString() |> should equal "test"
            lbl.GetProperty("team").GetString() |> should equal "infra"
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``MountVolume cree un repertoire de montage et copie les fichiers`` () =
        ensureCwdAllowed ()
        let tempRoot = createTempDir ()

        try
            let driver = LocalVolumeDriver(tempRoot)
            let (id, mountpoint) = driver.CreateVolume("mount-test", Map.empty, Map.empty)
            File.WriteAllText(Path.Combine(mountpoint, "test.txt"), "contenu")
            let (success, mountDir) = driver.MountVolume(id, "target", "")
            success |> should equal true
            Directory.Exists(mountDir) |> should equal true
            File.ReadAllText(Path.Combine(mountDir, "test.txt")) |> should equal "contenu"
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``UnmountVolume supprime le repertoire de montage`` () =
        ensureCwdAllowed ()
        let tempRoot = createTempDir ()

        try
            let driver = LocalVolumeDriver(tempRoot)
            let (id, mountpoint) = driver.CreateVolume("unmount-test", Map.empty, Map.empty)
            File.WriteAllText(Path.Combine(mountpoint, "file.txt"), "data")
            let (_, mountDir) = driver.MountVolume(id, "target", "")
            Directory.Exists(mountDir) |> should equal true
            let (success, msg) = driver.UnmountVolume(id, "target")
            success |> should equal true
            msg |> should equal "Démonté"
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``GetVolumeSize retourne la taille totale des fichiers`` () =
        let tempRoot = createTempDir ()

        try
            let driver = LocalVolumeDriver(tempRoot)
            let (id, mountpoint) = driver.CreateVolume("size-test", Map.empty, Map.empty)
            File.WriteAllText(Path.Combine(mountpoint, "a.txt"), "12345")
            File.WriteAllText(Path.Combine(mountpoint, "b.txt"), "abcde")
            let size = driver.GetVolumeSize(id)
            size |> should equal 10L
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``GetVolumeSize retourne 0 pour un id inexistant`` () =
        let tempRoot = createTempDir ()

        try
            let driver = LocalVolumeDriver(tempRoot)
            driver.GetVolumeSize("inexistant") |> should equal 0L
        finally
            cleanupDir tempRoot

    // ── RemoveVolume monté ───────────────────────────────────────

    [<Fact>]
    let ``RemoveVolume sans force sur volume monte leve FailedPrecondition`` () =
        ensureCwdAllowed ()
        let tempRoot = createTempDir ()

        try
            let driver = LocalVolumeDriver(tempRoot)
            let (id, _) = driver.CreateVolume("mounted-del", Map.empty, Map.empty)
            driver.MountVolume(id, "target", "") |> ignore
            let ex = Assert.Throws<Grpc.Core.RpcException>(fun () -> driver.RemoveVolume(id, false) |> ignore)
            ex.StatusCode |> should equal Grpc.Core.StatusCode.FailedPrecondition
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``RemoveVolume avec force sur volume monte supprime`` () =
        ensureCwdAllowed ()
        let tempRoot = createTempDir ()

        try
            let driver = LocalVolumeDriver(tempRoot)
            let (id, _) = driver.CreateVolume("mounted-force", Map.empty, Map.empty)
            driver.MountVolume(id, "target", "") |> ignore
            driver.RemoveVolume(id, true) |> should equal true
            driver.InspectVolume(id).IsNone |> should equal true
        finally
            cleanupDir tempRoot

    // ── ListVolumes filtres ──────────────────────────────────────

    [<Fact>]
    let ``ListVolumes filtre par nom (insensible a la casse)`` () =
        let tempRoot = createTempDir ()

        try
            let driver = LocalVolumeDriver(tempRoot)
            driver.CreateVolume("AlphaVol", Map.empty, Map.empty) |> ignore
            driver.CreateVolume("BetaVol", Map.empty, Map.empty) |> ignore

            let names =
                driver.ListVolumes(Map.ofList [ "name", "alpha" ])
                |> List.map (fun v -> v.GetProperty("name").GetString())

            names.Length |> should equal 1
            names.Head |> should equal "AlphaVol"
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``ListVolumes filtre par presence de label`` () =
        let tempRoot = createTempDir ()

        try
            let driver = LocalVolumeDriver(tempRoot)
            driver.CreateVolume("labeled", Map.empty, Map.ofList [ "env", "test" ]) |> ignore
            driver.CreateVolume("plain", Map.empty, Map.empty) |> ignore

            let names =
                driver.ListVolumes(Map.ofList [ "label", "env" ])
                |> List.map (fun v -> v.GetProperty("name").GetString())

            names.Length |> should equal 1
            names.Head |> should equal "labeled"
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``ListVolumes filtre par valeur de label`` () =
        let tempRoot = createTempDir ()

        try
            let driver = LocalVolumeDriver(tempRoot)
            driver.CreateVolume("prod", Map.empty, Map.ofList [ "env", "prod" ]) |> ignore
            driver.CreateVolume("dev", Map.empty, Map.ofList [ "env", "dev" ]) |> ignore

            let names =
                driver.ListVolumes(Map.ofList [ "label", "env=prod" ])
                |> List.map (fun v -> v.GetProperty("name").GetString())

            names.Length |> should equal 1
            names.Head |> should equal "prod"
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``ListVolumes combine les filtres name et label`` () =
        let tempRoot = createTempDir ()

        try
            let driver = LocalVolumeDriver(tempRoot)
            driver.CreateVolume("web-prod", Map.empty, Map.ofList [ "env", "prod" ]) |> ignore
            driver.CreateVolume("web-dev", Map.empty, Map.ofList [ "env", "dev" ]) |> ignore
            driver.CreateVolume("api-prod", Map.empty, Map.ofList [ "env", "prod" ]) |> ignore

            let names =
                driver.ListVolumes(Map.ofList [ "name", "web"; "label", "env=prod" ])
                |> List.map (fun v -> v.GetProperty("name").GetString())

            names.Length |> should equal 1
            names.Head |> should equal "web-prod"
        finally
            cleanupDir tempRoot

    // ── CreateVolume/MountVolume avec chemin externe (path) ──────

    [<Fact>]
    let ``CreateVolume avec option path pointe sur le repertoire externe`` () =
        ensureCwdAllowed ()
        let tempRoot = createTempDir ()

        try
            let driver = LocalVolumeDriver(tempRoot)
            let external = Path.Combine(tempRoot, "extern-data")
            let (id, mountpoint) = driver.CreateVolume("path-vol", Map.ofList [ "path", external ], Map.empty)
            mountpoint |> should equal external
            Directory.Exists(external) |> should equal true
            // Le _data interne n'est pas créé pour un volume référencé.
            let (_, mountDir) = driver.MountVolume(id, "target", "")
            Directory.Exists(mountDir) |> should equal true
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``MountVolume avec volume externe copie son contenu`` () =
        ensureCwdAllowed ()
        let tempRoot = createTempDir ()

        try
            let driver = LocalVolumeDriver(tempRoot)
            let external = Path.Combine(tempRoot, "extern-content")
            let (id, mountpoint) = driver.CreateVolume("path-content", Map.ofList [ "path", external ], Map.empty)
            File.WriteAllText(Path.Combine(mountpoint, "fichier.txt"), "externe")
            let (success, mountDir) = driver.MountVolume(id, "target", "")
            success |> should equal true
            File.ReadAllText(Path.Combine(mountDir, "fichier.txt")) |> should equal "externe"
        finally
            cleanupDir tempRoot

    // ── PruneVolumes ─────────────────────────────────────────────

    [<Fact>]
    let ``PruneVolumes supprime les volumes non montes et ignore les montes`` () =
        ensureCwdAllowed ()
        let tempRoot = createTempDir ()

        try
            let driver = LocalVolumeDriver(tempRoot)
            let (idMount, _) = driver.CreateVolume("keep-mounted", Map.empty, Map.empty)
            driver.MountVolume(idMount, "target", "") |> ignore
            let (idFree, _) = driver.CreateVolume("to-prune", Map.empty, Map.empty)

            let removed = driver.PruneVolumes()
            removed |> should contain idFree
            removed |> should not' (contain idMount)
            driver.InspectVolume(idFree).IsNone |> should equal true
            driver.InspectVolume(idMount).IsSome |> should equal true
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``PruneVolumes retourne vide quand aucun volume`` () =
        let tempRoot = createTempDir ()

        try
            let driver = LocalVolumeDriver(tempRoot)
            driver.PruneVolumes() |> should be Empty
        finally
            cleanupDir tempRoot

