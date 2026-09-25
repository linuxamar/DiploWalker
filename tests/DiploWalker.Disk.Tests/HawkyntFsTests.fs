namespace DiploWalker.Disk.Tests

open System
open System.IO
open Xunit
open FsUnit.Xunit
open DiploWalker.Disk

/// Tests de l'adaptateur Hawkynt pour Btrfs, XFS et HFS+.
///
/// Les images Btrfs/XFS/HFS+ natives ne sont pas crÃ©ables
/// programmatiquement (pas de formatage DiscUtils pour ces FS).
/// Ces tests valident :
/// - La dÃ©tection de format et le fallback vers DiscUtils
/// - Le comportement de tryExtract/tryWriteBack hors format gÃ©rÃ©
/// - L'intÃ©gration FsImage â†’ Hawkynt â†’ DiscUtils pour FAT
module HawkyntFsTests =

    let private run (f: string -> string -> unit) =
        let root = TestImage.createTempDir ()

        try
            let img = Path.Combine(root, "test.img")
            f root img
        finally
            TestImage.cleanupDir root

    // â”€â”€ tryExtract â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``tryExtract retourne None pour un fichier brut sans FS`` () =
        run (fun root img ->
            File.WriteAllBytes(img, Array.create 4096 0uy)
            let staging = Path.Combine(root, "staging")
            Directory.CreateDirectory(staging) |> ignore
            HawkyntFs.tryExtract img staging |> should equal None)

    [<Fact>]
    let ``tryExtract retourne None pour un fichier inexistant`` () =
        run (fun root _ ->
            let missing = Path.Combine(root, "nope.img")
            let staging = Path.Combine(root, "staging")
            Directory.CreateDirectory(staging) |> ignore
            HawkyntFs.tryExtract missing staging |> should equal None)

    [<Fact>]
    let ``tryExtract retourne None pour un fichier vide`` () =
        run (fun root img ->
            File.WriteAllBytes(img, [||])
            let staging = Path.Combine(root, "staging")
            Directory.CreateDirectory(staging) |> ignore
            HawkyntFs.tryExtract img staging |> should equal None)

    // â”€â”€ tryWriteBack â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``tryWriteBack retourne false pour un fichier brut sans FS`` () =
        run (fun root img ->
            File.WriteAllBytes(img, Array.create 4096 0uy)
            let staging = Path.Combine(root, "staging")
            Directory.CreateDirectory(staging) |> ignore
            HawkyntFs.tryWriteBack img staging |> should equal false)

    [<Fact>]
    let ``tryWriteBack retourne false pour un fichier inexistant`` () =
        run (fun root _ ->
            let missing = Path.Combine(root, "nope.img")
            let staging = Path.Combine(root, "staging")
            Directory.CreateDirectory(staging) |> ignore
            HawkyntFs.tryWriteBack missing staging |> should equal false)

    [<Fact>]
    let ``tryWriteBack retourne false pour un fichier vide`` () =
        run (fun root img ->
            File.WriteAllBytes(img, [||])
            let staging = Path.Combine(root, "staging")
            Directory.CreateDirectory(staging) |> ignore
            HawkyntFs.tryWriteBack img staging |> should equal false)

    // â”€â”€ Fallback DiscUtils via FsImage â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``FsImage.extract sur FAT delegue bien a DiscUtils via fallback`` () =
        run (fun root img ->
            TestImage.createFat img [ "test.txt", "contenu Hawkynt" ]
            let staging = Path.Combine(root, "staging")
            let n = FsImage.extract img staging false |> Result.defaultWith failwith
            n |> should equal 1

            File.ReadAllText(Path.Combine(staging, "test.txt"))
            |> should equal "contenu Hawkynt")

    [<Fact>]
    let ``FsImage.writeBack sur FAT delegue bien a DiscUtils via fallback`` () =
        run (fun root img ->
            TestImage.createFat img [ "orig.txt", "v1" ]
            let staging = Path.Combine(root, "staging")
            FsImage.extract img staging false |> Result.defaultWith failwith |> ignore
            File.WriteAllText(Path.Combine(staging, "orig.txt"), "v2")
            File.WriteAllText(Path.Combine(staging, "ajout.txt"), "ajout")
            FsImage.writeBack img staging |> Result.defaultWith failwith
            let re = Path.Combine(root, "re")
            FsImage.extract img re false |> Result.defaultWith failwith |> ignore
            File.ReadAllText(Path.Combine(re, "orig.txt")) |> should equal "v2"
            File.ReadAllText(Path.Combine(re, "ajout.txt")) |> should equal "ajout")

    [<Fact>]
    let ``FsImage extract/writeBack round-trip FAT est coherent`` () =
        run (fun root img ->
            TestImage.createFat img [ "a.txt", "alpha"; "d/b.txt", "beta" ]
            let staging = Path.Combine(root, "staging")
            let n1 = FsImage.extract img staging false |> Result.defaultWith failwith
            n1 |> should equal 2
            File.Delete(Path.Combine(staging, "a.txt"))
            File.WriteAllText(Path.Combine(staging, "d", "b.txt"), "gamma")
            FsImage.writeBack img staging |> Result.defaultWith failwith
            let re = Path.Combine(root, "re")
            let n2 = FsImage.extract img re false |> Result.defaultWith failwith
            n2 |> should equal 1
            File.Exists(Path.Combine(re, "a.txt")) |> should equal false
            File.ReadAllText(Path.Combine(re, "d", "b.txt")) |> should equal "gamma")

