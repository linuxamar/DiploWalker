namespace Diplo.Disk.Tests

module FsImageTests =

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
    let ``extract recupere les fichiers et les repertoires de l'image`` () =
        run (fun root img ->
            TestImage.createFat img [ "hello.txt", "Bonjour Diplo"; @"dossier\sub.txt", "sous" ]
            let staging = Path.Combine(root, "staging")
            let n = FsImage.extract img staging false
            n |> should equal 2
            File.ReadAllText(Path.Combine(staging, "hello.txt")) |> should equal "Bonjour Diplo"
            File.ReadAllText(Path.Combine(staging, "dossier", "sub.txt")) |> should equal "sous")

    [<Fact>]
    let ``writeBack reecrit les fichiers modifies et ajoute les nouveaux`` () =
        run (fun root img ->
            TestImage.createFat img [ "hello.txt", "v1" ]
            let staging = Path.Combine(root, "staging")
            FsImage.extract img staging false |> ignore
            File.WriteAllText(Path.Combine(staging, "hello.txt"), "v2")
            File.WriteAllText(Path.Combine(staging, "nouveau.txt"), "nouveau")
            FsImage.writeBack img staging
            let re = Path.Combine(root, "re")
            FsImage.extract img re false |> ignore
            File.ReadAllText(Path.Combine(re, "hello.txt")) |> should equal "v2"
            File.ReadAllText(Path.Combine(re, "nouveau.txt")) |> should equal "nouveau")

    [<Fact>]
    let ``writeBack supprime les fichiers disparus du staging`` () =
        run (fun root img ->
            TestImage.createFat img [ "a.txt", "a"; "b.txt", "b" ]
            let staging = Path.Combine(root, "staging")
            FsImage.extract img staging false |> ignore
            File.Delete(Path.Combine(staging, "a.txt"))
            FsImage.writeBack img staging
            let re = Path.Combine(root, "re")
            FsImage.extract img re false |> ignore
            File.Exists(Path.Combine(re, "a.txt")) |> should equal false
            File.Exists(Path.Combine(re, "b.txt")) |> should equal true)

    [<Fact>]
    let ``writeBack vide la corbeille des repertoires supprimes du staging`` () =
        run (fun root img ->
            TestImage.createFat img [ @"dossier\a.txt", "a" ]
            let staging = Path.Combine(root, "staging")
            FsImage.extract img staging false |> ignore
            Directory.Delete(Path.Combine(staging, "dossier"), true)
            FsImage.writeBack img staging
            let re = Path.Combine(root, "re")
            FsImage.extract img re false |> ignore
            Directory.Exists(Path.Combine(re, "dossier")) |> should equal false)

    [<Fact>]
    let ``extract leve une exception sur un fichier sans systeme de fichiers`` () =
        run (fun root img ->
            File.WriteAllBytes(img, Array.create 4096 0uy)
            (fun () -> FsImage.extract img (Path.Combine(root, "s")) false |> ignore)
            |> should throw typeof<System.Exception>)

    [<Fact>]
    let ``writeBack leve une exception si l'image n'existe pas`` () =
        run (fun root _ ->
            let missing = Path.Combine(root, "missing.img")
            (fun () -> FsImage.writeBack missing (Path.Combine(root, "staging")))
            |> should throw typeof<System.IO.FileNotFoundException>)
