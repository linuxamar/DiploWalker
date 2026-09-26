namespace DiploWalker.Cli.Tests

/// Tests des commandes `container catalog-*` (clip, insertion, mise à jour,
/// suppression) via fakes injectés. Un fichier catalogue temporaire est fourni
/// via l'option `--catalog` pour ne pas toucher au répertoire de travail.
module CatalogCommandTests =

    open System
    open System.IO
    open System.Threading
    open Xunit
    open FsUnit.Xunit
    open DiploWalker.Cli.Catalog
    open DiploWalker.Core
    open DiploWalker.TestHelpers

    let private run (cmd: Spectre.Console.Cli.ICommand<'T>) (settings: 'T) : int =
        cmd.ExecuteAsync(Unchecked.defaultof<Spectre.Console.Cli.CommandContext>, settings, CancellationToken.None)
            .Result

    let private container () = new FakeContainerClient()

    let private clients (c: FakeContainerClient) =
        FakeDiploClients(c, new FakeNetworkClient(), new FakeVolumeClient())

    let private catalogDir () =
        let dir = TestHelpers.createTempDir "catalog-cli"
        let path = Path.Combine(dir, "diplo-catalog.json")
        (dir, path)

    // ─── list ──────────────────────────────────────────────────────

    [<Fact>]
    let ``catalog list vide retourne 0 et prévient`` () =
        let output = MockOutputPort()
        let dir, path = catalogDir ()

        try
            let code = run (CatalogListCommand(output, clients(container()))) (CatalogListSettings(CatalogPath = path))
            code |> should equal 0
            output.Warnings |> should contain "Catalogue vide."
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``catalog list avec entrées retourne 0 et écrit une table`` () =
        let output = MockOutputPort()
        let dir, path = catalogDir ()
        ImageCatalog.add path "nginx:latest" (Some "web") |> ignore

        try
            let code = run (CatalogListCommand(output, clients(container()))) (CatalogListSettings(CatalogPath = path))
            code |> should equal 0
            output.Tables |> should not' (be Empty)
            ImageCatalog.load path |> should haveLength 1
        finally
            Directory.Delete(dir, true)

    // ─── add ───────────────────────────────────────────────────────

    [<Fact>]
    let ``catalog add télécharge puis inscrit au catalogue`` () =
        let output = MockOutputPort()
        let c = container()
        let dir, path = catalogDir ()

        try
            let code =
                run (CatalogAddCommand(output, clients c))
                    (CatalogAddSettings(Ref = "nginx:latest", Note = "web", CatalogPath = path))

            code |> should equal 0
            c.PullCalls |> should equal 1
            ImageCatalog.load path |> should haveLength 1
            ImageCatalog.load path |> List.head |> fun e -> e.Ref |> should equal "nginx:latest"
            ImageCatalog.load path |> List.head |> fun e -> e.Note |> should equal (Some "web")
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``catalog add --no-pull n'appelle pas Docker`` () =
        let output = MockOutputPort()
        let c = container()
        let dir, path = catalogDir ()

        try
            let code =
                run (CatalogAddCommand(output, clients c))
                    (CatalogAddSettings(Ref = "busybox:1.36", NoPull = true, CatalogPath = path))

            code |> should equal 0
            c.PullCalls |> should equal 0
            ImageCatalog.load path |> should haveLength 1
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``catalog add un doublon prévient et laisse le catalogue inchangé`` () =
        let output = MockOutputPort()
        let c = container()
        let dir, path = catalogDir ()
        ImageCatalog.add path "alpine:3.19" None |> ignore

        try
            let code =
                run (CatalogAddCommand(output, clients c))
                    (CatalogAddSettings(Ref = "alpine:3.19", CatalogPath = path))

            code |> should equal 0
            output.Warnings |> should contain "L'image alpine:3.19 est déjà au catalogue"
            ImageCatalog.load path |> should haveLength 1
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``catalog add sans référence retourne 1`` () =
        let output = MockOutputPort()
        let dir, path = catalogDir ()

        try
            let code = run (CatalogAddCommand(output, clients(container()))) (CatalogAddSettings(CatalogPath = path))
            code |> should equal 1
            output.Errors |> should contain "La référence de l'image est requise"
        finally
            Directory.Delete(dir, true)

    // ─── update ────────────────────────────────────────────────────

    [<Fact>]
    let ``catalog update étiquette puis met à jour le catalogue`` () =
        let output = MockOutputPort()
        let c = container()
        let dir, path = catalogDir ()
        ImageCatalog.add path "nginx:1.25" None |> ignore

        try
            let code =
                run (CatalogUpdateCommand(output, clients c))
                    (CatalogUpdateSettings(Ref = "nginx:1.25", Target = "nginx:1.27", CatalogPath = path))

            code |> should equal 0
            c.TagImageCalls |> should equal 1
            let e = ImageCatalog.load path |> List.head
            e.Ref |> should equal "nginx:1.27"
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``catalog update d'une référence absente retourne 1`` () =
        let output = MockOutputPort()
        let dir, path = catalogDir ()

        try
            let code =
                run (CatalogUpdateCommand(output, clients(container())))
                    (CatalogUpdateSettings(Ref = "introuvable:latest", CatalogPath = path))

            code |> should equal 1
            output.Errors |> should contain "L'image introuvable:latest n'est pas au catalogue"
        finally
            Directory.Delete(dir, true)

    // ─── delete ────────────────────────────────────────────────────

    [<Fact>]
    let ``catalog delete supprime l'image puis la retire du catalogue`` () =
        let output = MockOutputPort()
        let c = container()
        let dir, path = catalogDir ()
        ImageCatalog.add path "nginx:latest" None |> ignore

        try
            let code =
                run (CatalogDeleteCommand(output, clients c))
                    (CatalogDeleteSettings(Ref = "nginx:latest", CatalogPath = path))

            code |> should equal 0
            c.RemoveImageCalls |> should equal 1
            ImageCatalog.load path |> should be Empty
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``catalog delete --no-docker retire du catalogue sans Docker`` () =
        let output = MockOutputPort()
        let c = container()
        let dir, path = catalogDir ()
        ImageCatalog.add path "nginx:latest" None |> ignore

        try
            let code =
                run (CatalogDeleteCommand(output, clients c))
                    (CatalogDeleteSettings(Ref = "nginx:latest", NoDocker = true, CatalogPath = path))

            code |> should equal 0
            c.RemoveImageCalls |> should equal 0
            ImageCatalog.load path |> should be Empty
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``catalog delete d'une référence absente prévient et retourne 0`` () =
        let output = MockOutputPort()
        let dir, path = catalogDir ()

        try
            let code =
                run (CatalogDeleteCommand(output, clients(container())))
                    (CatalogDeleteSettings(Ref = "absente:latest", CatalogPath = path))

            code |> should equal 0
            output.Warnings |> should contain "L'image absente:latest n'est pas au catalogue"
        finally
            Directory.Delete(dir, true)
