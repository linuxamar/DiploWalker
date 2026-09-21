namespace DiploWalker.Disk.Tests

module DiscFsHelperTests =

    open System
    open System.IO
    open Xunit
    open FsUnit.Xunit
    open DiploWalker.Disk

    // â”€â”€ toRealRel â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``toRealRel remplace les slashes avantants par le separateur hote`` () =
        let result = DiscFsHelper.toRealRel "a/b/c"
        result.Length |> should be (greaterThan 0)

    [<Fact>]
    let ``toRealRel supprime les backslashes en tete`` () =
        let result = DiscFsHelper.toRealRel "\\\\server\\share"
        result.StartsWith(Path.DirectorySeparatorChar.ToString()) |> should equal false

    [<Fact>]
    let ``toRealRel supprime les slashes en tete`` () =
        let result = DiscFsHelper.toRealRel "/usr/local/bin"
        result.StartsWith(Path.DirectorySeparatorChar.ToString()) |> should equal false

    [<Fact>]
    let ``toRealRel normalise les doubles slashes`` () =
        let result = DiscFsHelper.toRealRel "a//b///c"
        result.Contains "//" |> should equal false

    [<Fact>]
    let ``toRealRel gere les noms simples`` () =
        DiscFsHelper.toRealRel "file.txt" |> should equal "file.txt"

    // â”€â”€ realFrom : chemins valides â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``realFrom accepte un chemin simple`` () =
        let root = TestImage.createTempDir ()

        try
            let result = DiscFsHelper.realFrom root "fichier.txt"
            result.StartsWith(Path.GetFullPath root) |> should equal true
        finally
            TestImage.cleanupDir root

    [<Fact>]
    let ``realFrom accepte un sous-repertoire`` () =
        let root = TestImage.createTempDir ()

        try
            let result = DiscFsHelper.realFrom root "sous/dossier/fichier.txt"
            result.StartsWith(Path.GetFullPath root) |> should equal true
        finally
            TestImage.cleanupDir root

    [<Fact>]
    let ``realFrom accepte la racine elle-meme`` () =
        let root = TestImage.createTempDir ()

        try
            let result = DiscFsHelper.realFrom root ""
            result |> should equal (Path.GetFullPath root)
        finally
            TestImage.cleanupDir root

    [<Fact>]
    let ``realFrom conserve les espaces dans les noms`` () =
        let root = TestImage.createTempDir ()

        try
            let result = DiscFsHelper.realFrom root "mon dossier"
            Path.GetFileName result |> should equal "mon dossier"
        finally
            TestImage.cleanupDir root

    // â”€â”€ realFrom : traversal â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``realFrom rejette le traversal avec ..`` () =
        let root = TestImage.createTempDir ()

        try
            (fun () -> DiscFsHelper.realFrom root "../etc/passwd" |> ignore)
            |> should throw typeof<ArgumentException>
        finally
            TestImage.cleanupDir root

    [<Fact>]
    let ``realFrom rejette un chemin enracine Windows`` () =
        let root = TestImage.createTempDir ()

        try
            (fun () -> DiscFsHelper.realFrom root "C:\\Windows\\System32" |> ignore)
            |> should throw typeof<ArgumentException>
        finally
            TestImage.cleanupDir root

    [<Fact>]
    let ``realFrom rejette un chemin avec drive letter Windows`` () =
        let root = TestImage.createTempDir ()

        try
            (fun () -> DiscFsHelper.realFrom root "C:\\Windows\\System32" |> ignore)
            |> should throw typeof<ArgumentException>
        finally
            TestImage.cleanupDir root

    [<Fact>]
    let ``realFrom rejette un traversal cache`` () =
        let root = TestImage.createTempDir ()

        try
            (fun () -> DiscFsHelper.realFrom root "a/../../b" |> ignore)
            |> should throw typeof<ArgumentException>
        finally
            TestImage.cleanupDir root

