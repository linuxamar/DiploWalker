namespace DiploWalker.Core.Tests

module ImageCatalogTests =

    open System
    open System.IO
    open Xunit
    open FsUnit.Xunit
    open DiploWalker.Core

    // â”€â”€ catalogPath â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``catalogPath sans variable d'environnement pointe vers diplo-catalog.json du rÃ©pertoire courant`` () =
        let old = Environment.GetEnvironmentVariable("DIPLO_CONFIG_HOME")
        Environment.SetEnvironmentVariable("DIPLO_CONFIG_HOME", null)

        try
            ImageCatalog.catalogPath () |> should equal (Path.Combine(Directory.GetCurrentDirectory(), "diplo-catalog.json"))
        finally
            Environment.SetEnvironmentVariable("DIPLO_CONFIG_HOME", old)

    [<Fact>]
    let ``catalogPath honore la variable d'environnement DIPLO_CONFIG_HOME`` () =
        let old = Environment.GetEnvironmentVariable("DIPLO_CONFIG_HOME")
        let custom = Path.Combine(Path.GetTempPath(), "diplo-catalog-home")

        Environment.SetEnvironmentVariable("DIPLO_CONFIG_HOME", custom)

        try
            ImageCatalog.catalogPath () |> should equal (Path.Combine(custom, "diplo-catalog.json"))
        finally
            Environment.SetEnvironmentVariable("DIPLO_CONFIG_HOME", old)

    // â”€â”€ load â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    let private tempDir () =
        let dir =
            Path.Combine(Path.GetTempPath(), "diplo-catalog-test-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory(dir) |> ignore
        dir

    let private catalogPath dir = Path.Combine(dir, "diplo-catalog.json")

    [<Fact>]
    let ``load avec fichier absent renvoie un catalogue vide`` () =
        let dir = tempDir ()

        try
            ImageCatalog.load (catalogPath dir) |> should be Empty
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``load avec fichier malformÃ© renvoie un catalogue vide (repli)`` () =
        let dir = tempDir ()
        let path = catalogPath dir
        File.WriteAllText(path, "{ pas du json ]")

        try
            ImageCatalog.load path |> should be Empty
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``load relit une entrÃ©e Ã©crite par add`` () =
        let dir = tempDir ()
        let path = catalogPath dir
        ImageCatalog.add path "nginx:latest" (Some "image web") |> should equal true

        try
            let entries = ImageCatalog.load path
            entries |> should haveLength 1
            entries |> List.head |> fun e -> e.Ref |> should equal "nginx:latest"
            entries |> List.head |> fun e -> e.Note |> should equal (Some "image web")
            entries |> List.head |> fun e -> e.AddedAt |> should not' (be Empty)
        finally
            Directory.Delete(dir, true)

    // â”€â”€ add â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``add refuse le doublon et laisse le catalogue inchangÃ©`` () =
        let dir = tempDir ()
        let path = catalogPath dir
        ImageCatalog.add path "alpine:3.19" None |> should equal true

        try
            ImageCatalog.add path "alpine:3.19" (Some "doublon") |> should equal false
            ImageCatalog.load path |> should haveLength 1
            ImageCatalog.load path |> List.head |> fun e -> e.Note |> should equal None
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``add nettoie une note vide en None`` () =
        let dir = tempDir ()
        let path = catalogPath dir

        try
            ImageCatalog.add path "busybox:1.36" (Some "   ") |> should equal true
            ImageCatalog.load path |> List.head |> fun e -> e.Note |> should equal None
        finally
            Directory.Delete(dir, true)

    // â”€â”€ update â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``update met Ã  jour la note et le nom de l'entrÃ©e`` () =
        let dir = tempDir ()
        let path = catalogPath dir
        ImageCatalog.add path "alpine:3.19" (Some "ancienne") |> ignore

        try
            ImageCatalog.update path "alpine:3.19" (Some "alpine:3.20") (Some "nouvelle note")
            |> should equal true

            let e = ImageCatalog.load path |> List.head
            e.Ref |> should equal "alpine:3.20"
            e.Note |> should equal (Some "nouvelle note")
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``update prÃ©serve la note quand elle n'est pas fournie`` () =
        let dir = tempDir ()
        let path = catalogPath dir
        ImageCatalog.add path "alpine:3.19" (Some "gardÃ©e") |> ignore

        try
            ImageCatalog.update path "alpine:3.19" None None |> should equal true
            ImageCatalog.load path |> List.head |> fun e -> e.Note |> should equal (Some "gardÃ©e")
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``update d'une rÃ©fÃ©rence absente renvoie false`` () =
        let dir = tempDir ()
        let path = catalogPath dir

        try
            ImageCatalog.update path "introuvable:latest" None None |> should equal false
        finally
            Directory.Delete(dir, true)

    // â”€â”€ remove â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``remove retire l'entrÃ©e existante et renvoie true`` () =
        let dir = tempDir ()
        let path = catalogPath dir
        ImageCatalog.add path "nginx:latest" None |> ignore

        try
            ImageCatalog.remove path "nginx:latest" |> should equal true
            ImageCatalog.load path |> should be Empty
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``remove d'une rÃ©fÃ©rence absente renvoie false`` () =
        let dir = tempDir ()
        let path = catalogPath dir

        try
            ImageCatalog.remove path "absente:latest" |> should equal false
        finally
            Directory.Delete(dir, true)

    // â”€â”€ save â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``save crÃ©e le rÃ©pertoire parent manquant`` () =
        let dir = tempDir ()

        try
            let nested = Path.Combine(dir, "sous", "dossier")
            let path = catalogPath nested
            ImageCatalog.add path "redis:7" None |> should equal true
            File.Exists path |> should equal true
        finally
            Directory.Delete(dir, true)
