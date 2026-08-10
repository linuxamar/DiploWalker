namespace Diplo.Disk.Tests

module DiskMounterTests =

    open System
    open System.IO
    open Xunit
    open FsUnit.Xunit
    open Diplo.Disk

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
    let ``mount sur une image expose son contenu et reecrit a la liberation`` () =
        run (fun root img ->
            TestImage.createFat img [ "hello.txt", "v1" ]
            let vol = DiskMounter.mount img "/data" false
            File.ReadAllText(Path.Combine(vol.HostPath, "hello.txt")) |> should equal "v1"
            File.WriteAllText(Path.Combine(vol.HostPath, "hello.txt"), "v2")
            vol.Dispose()
            let re = Path.Combine(root, "re")
            FsImage.extract img re false |> ignore
            File.ReadAllText(Path.Combine(re, "hello.txt")) |> should equal "v2")

    [<Fact>]
    let ``mount en lecture seule ne reecrit pas l'image a la liberation`` () =
        run (fun root img ->
            TestImage.createFat img [ "hello.txt", "v1" ]
            let vol = DiskMounter.mount img "/data" true
            vol.ReadOnly |> should equal true
            File.WriteAllText(Path.Combine(vol.HostPath, "hello.txt"), "v3")
            vol.Dispose()
            let re = Path.Combine(root, "re")
            FsImage.extract img re false |> ignore
            File.ReadAllText(Path.Combine(re, "hello.txt")) |> should equal "v1")

    [<Fact>]
    let ``mount leve une exception si la source n'existe pas`` () =
        let missing = Path.Combine(Path.GetTempPath(), "diplo-absent-" + Guid.NewGuid().ToString("N"))
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
