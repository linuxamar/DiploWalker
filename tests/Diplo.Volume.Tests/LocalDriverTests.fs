namespace Diplo.Volume.Tests

module LocalDriverTests =

    open System
    open System.IO
    open Xunit
    open FsUnit.Xunit
    open Diplo.Volume.Drivers
    open Diplo.Abstractions.SecurityValidation

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
