namespace DiploWalker.Disk.Tests

module DiskMounterTests =

    open System
    open System.IO
    open Xunit
    open FsUnit.Xunit
    open DiploWalker.Disk

    let private run (f: string -> string -> unit) =
        let root = TestImage.createTempDir ()

        try
            let img = Path.Combine(root, "test.img")
            f root img
        finally
            TestImage.cleanupDir root

    [<Fact>]
    let ``mount sur un repertoire fait un bind direct sans staging`` () =
        run (fun root _ ->
            let src = Path.Combine(root, "src")
            Directory.CreateDirectory src |> ignore
            let vol = DiskMounter.mount src "/data" false
            vol.HostPath |> should equal src
            vol.Destination |> should equal "/data"
            vol.ReadOnly |> should equal false
            vol.Dispose())

    [<Fact>]
    [<Trait("Platform", "Windows")>]
    let ``mount sur une image expose son contenu et reecrit a la liberation`` () =
        run (fun root img ->
            TestImage.createFat img [ "hello.txt", "v1" ]
            let vol = DiskMounter.mount img "/data" false
            File.ReadAllText(Path.Combine(vol.HostPath, "hello.txt")) |> should equal "v1"
            File.WriteAllText(Path.Combine(vol.HostPath, "hello.txt"), "v2")
            vol.Dispose()
            let re = Path.Combine(root, "re")
            FsImage.extract img re false |> Result.defaultWith failwith |> ignore
            File.ReadAllText(Path.Combine(re, "hello.txt")) |> should equal "v2")

    [<Fact>]
    [<Trait("Platform", "Windows")>]
    let ``mount en lecture seule ne reecrit pas l'image a la liberation`` () =
        run (fun root img ->
            TestImage.createFat img [ "hello.txt", "v1" ]
            let vol = DiskMounter.mount img "/data" true
            vol.ReadOnly |> should equal true
            File.WriteAllText(Path.Combine(vol.HostPath, "hello.txt"), "v3")
            vol.Dispose()
            let re = Path.Combine(root, "re")
            FsImage.extract img re false |> Result.defaultWith failwith |> ignore
            File.ReadAllText(Path.Combine(re, "hello.txt")) |> should equal "v1")

    [<Fact>]
    let ``mount leve une exception si la source n'existe pas`` () =
        let missing =
            Path.Combine(Path.GetTempPath(), "diplo-absent-" + Guid.NewGuid().ToString("N"))

        (fun () -> DiskMounter.mount missing "/data" false |> ignore)
        |> should throw typeof<System.Exception>

    [<Fact>]
    let ``mount refuse les images qcow v1`` () =
        run (fun root img ->
            File.WriteAllBytes(img, [| 0x51uy; 0x46uy; 0x49uy; 0xFEuy |])

            (fun () -> DiskMounter.mount img "/data" false |> ignore)
            |> should throw typeof<System.Exception>)

    [<Fact>]
    let ``mount refuse un format inconnu`` () =
        run (fun root img ->
            File.WriteAllBytes(img, Array.create 1024 0xABuy)

            (fun () -> DiskMounter.mount img "/data" false |> ignore)
            |> should throw typeof<System.Exception>)

    [<Fact>]
    let ``mount en lecture seule libere sans ecrire sur un repertoire`` () =
        run (fun root _ ->
            let src = Path.Combine(root, "src")
            Directory.CreateDirectory src |> ignore
            let vol = DiskMounter.mount src "/data" true
            vol.Dispose()
            Directory.Exists src |> should equal true)

    [<Fact>]
    [<Trait("Platform", "Windows")>]
    let ``rehydrate restaure le write-back sans re-extraire l'image`` () =
        run (fun root img ->
            TestImage.createFat img [ "hello.txt", "v1" ]
            let vol = DiskMounter.mount img "/data" false

            let restored =
                DiskMounter.rehydrate vol.Source vol.HostPath vol.Destination vol.ReadOnly

            File.WriteAllText(Path.Combine(restored.HostPath, "hello.txt"), "v2")
            restored.Dispose()
            let re = Path.Combine(root, "re")
            FsImage.extract img re false |> Result.defaultWith failwith |> ignore
            File.ReadAllText(Path.Combine(re, "hello.txt")) |> should equal "v2")

    [<Fact>]
    let ``rehydrate d'un bind mount de repertoire est sans effet sur la source`` () =
        run (fun root _ ->
            let src = Path.Combine(root, "src")
            Directory.CreateDirectory src |> ignore
            let vol = DiskMounter.mount src "/data" false

            let restored =
                DiskMounter.rehydrate vol.Source vol.HostPath vol.Destination vol.ReadOnly

            restored.Dispose()
            Directory.Exists src |> should equal true)

    [<Fact>]
    [<Trait("Platform", "Windows")>]
    let ``rehydrate d'un staging disparu est sans effet`` () =
        run (fun root img ->
            TestImage.createFat img [ "hello.txt", "v1" ]
            let vol = DiskMounter.mount img "/data" false
            let hostPath = vol.HostPath
            Directory.Delete(hostPath, true)

            let restored =
                DiskMounter.rehydrate vol.Source hostPath vol.Destination vol.ReadOnly

            restored.Dispose())

    [<Fact>]
    let ``MountState fait un aller-retour de persistance`` () =
        run (fun root _ ->
            let path = Path.Combine(root, "state.json")

            let entries: (string * MountState.MountEntry list) list =
                [ "c1",
                  [ { Source = "C:\\data.img"
                      HostPath = "C:\\staging-c1"
                      Destination = "C:\\app"
                      ReadOnly = false } ]
                  "c2",
                  [ { Source = "C:\\keys"
                      HostPath = "C:\\keys"
                      Destination = "C:\\keys"
                      ReadOnly = true } ] ]

            MountState.save path entries
            let loaded = MountState.load path
            loaded.Count |> should equal 2
            loaded.["c1"].Head.HostPath |> should equal "C:\\staging-c1")

    // â”€â”€ pruneStaleStaging â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    [<Trait("Platform", "Windows")>]
    let ``pruneStaleStaging supprime les dossiers orphelins anciens`` () =
        let staging = DiskMounter.stagingRoot ()

        try
            let guid = Guid.NewGuid().ToString("N")
            let dir = Path.Combine(staging, guid)
            Directory.CreateDirectory dir |> ignore
            // Forcer une date de creation ancienne (>24h)
            let old = DateTime.UtcNow - TimeSpan.FromHours 25.0
            Directory.SetCreationTimeUtc(dir, old)
            DiskMounter.pruneStaleStaging (TimeSpan.FromHours 24.0)
            Directory.Exists dir |> should equal false
        finally
            if Directory.Exists staging then
                for d in Directory.GetDirectories staging do
                    try
                        Directory.Delete(d, true)
                    with _ -> ()

    [<Fact>]
    [<Trait("Platform", "Windows")>]
    let ``pruneStaleStaging conserve les dossiers recents`` () =
        let staging = DiskMounter.stagingRoot ()

        try
            let guid = Guid.NewGuid().ToString("N")
            let dir = Path.Combine(staging, guid)
            Directory.CreateDirectory dir |> ignore
            DiskMounter.pruneStaleStaging (TimeSpan.FromHours 24.0)
            Directory.Exists dir |> should equal true
        finally
            if Directory.Exists staging then
                for d in Directory.GetDirectories staging do
                    try
                        Directory.Delete(d, true)
                    with _ -> ()

    [<Fact>]
    [<Trait("Platform", "Windows")>]
    let ``pruneStaleStaging ne supprime pas les dossiers non-GUID`` () =
        let staging = DiskMounter.stagingRoot ()

        try
            let dir = Path.Combine(staging, "not-a-guid")
            Directory.CreateDirectory dir |> ignore
            let old = DateTime.UtcNow - TimeSpan.FromHours 48.0
            Directory.SetCreationTimeUtc(dir, old)
            DiskMounter.pruneStaleStaging (TimeSpan.FromHours 24.0)
            Directory.Exists dir |> should equal true
        finally
            if Directory.Exists staging then
                for d in Directory.GetDirectories staging do
                    try
                        Directory.Delete(d, true)
                    with _ -> ()

