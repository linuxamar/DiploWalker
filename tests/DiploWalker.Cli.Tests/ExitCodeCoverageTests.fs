namespace DiploWalker.Cli.Tests

/// Codes de sortie complémentaires : chemins de validation (arguments
/// manquants, format inconnu, etc.) non couverts dans ExitCodeTests.fs.
module ExitCodeCoverageTests =

    open System
    open System.IO
    open System.Threading
    open Xunit
    open FsUnit.Xunit
    open Spectre.Console.Cli
    open DiploWalker.TestHelpers

    let private run (cmd: ICommand<'T>) (settings: 'T) : int =
        cmd.ExecuteAsync(Unchecked.defaultof<CommandContext>, settings, CancellationToken.None).Result

    // ─── Conteneurs ───────────────────────────────────────────────────
    open DiploWalker.Cli.Container

    [<Fact>]
    let ``container inspect sans identifiant retourne 1`` () =
        let output = MockOutputPort()
        let code = run (InspectContainerCommand(output)) (InspectContainerSettings(Id = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``container start sans identifiant retourne 1`` () =
        let output = MockOutputPort()
        let code = run (StartContainerCommand(output)) (StartSettings(Id = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``container stop sans identifiant retourne 1`` () =
        let output = MockOutputPort()
        let code = run (StopContainerCommand(output)) (StopSettings(Id = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``image pull sans image retourne 1`` () =
        let output = MockOutputPort()
        let code = run (PullImageCommand(output)) (PullSettings(Image = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``container exec sans identifiant retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run (ExecContainerCommand(output)) (ExecContainerSettings(Id = null, Command = [| "ls" |]))

        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``container exec sans commande retourne 1`` () =
        let output = MockOutputPort()
        let code = run (ExecContainerCommand(output)) (ExecContainerSettings(Id = "c1", Command = [||]))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``container top sans identifiant retourne 1`` () =
        let output = MockOutputPort()
        let code = run (TopContainerCommand(output)) (TopContainerSettings(Id = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``container stats sans identifiant retourne 1`` () =
        let output = MockOutputPort()
        let code = run (StatsContainerCommand(output)) (StatsContainerSettings(Id = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``container logs sans identifiant retourne 1`` () =
        let output = MockOutputPort()
        let code = run (LogsContainerCommand(output)) (LogsContainerSettings(Id = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``image inspect sans référence retourne 1`` () =
        let output = MockOutputPort()
        let code = run (ImageInspectCommand(output)) (ImageInspectSettings(Ref = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``image remove sans référence retourne 1`` () =
        let output = MockOutputPort()
        let code = run (ImageRemoveCommand(output)) (ImageRemoveSettings(Ref = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``image tag sans source retourne 1`` () =
        let output = MockOutputPort()
        let code = run (ImageTagCommand(output)) (ImageTagSettings(Source = null, Target = "b"))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``image tag sans cible retourne 1`` () =
        let output = MockOutputPort()
        let code = run (ImageTagCommand(output)) (ImageTagSettings(Source = "a", Target = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``registry logout sans registre retourne 1`` () =
        let output = MockOutputPort()
        let code = run (RegistryLogoutCommand(output)) (RegistryLogoutSettings(Registry = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``container pause sans identifiant retourne 1`` () =
        let output = MockOutputPort()
        let code = run (PauseContainerCommand(output)) (PauseContainerSettings(Id = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``container unpause sans identifiant retourne 1`` () =
        let output = MockOutputPort()
        let code = run (UnpauseContainerCommand(output)) (UnpauseContainerSettings(Id = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``container wait sans identifiant retourne 1`` () =
        let output = MockOutputPort()
        let code = run (WaitContainerCommand(output)) (WaitContainerSettings(Id = null, Timeout = 5))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``container stats-stream sans identifiant retourne 1`` () =
        let output = MockOutputPort()
        let code = run (StatsStreamCommand(output)) (StatsStreamSettings(Id = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``image commit sans conteneur retourne 1`` () =
        let output = MockOutputPort()
        let code = run (ImageCommitCommand(output)) (ImageCommitSettings(ContainerId = null, ImageRef = "app"))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``image commit sans référence retourne 1`` () =
        let output = MockOutputPort()
        let code = run (ImageCommitCommand(output)) (ImageCommitSettings(ContainerId = "c1", ImageRef = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``image export sans référence retourne 1`` () =
        let output = MockOutputPort()
        let code = run (ImageExportCommand(output)) (ImageExportSettings(Ref = null, Output = "x.tar"))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``image export sans sortie retourne 1`` () =
        let output = MockOutputPort()
        let code = run (ImageExportCommand(output)) (ImageExportSettings(Ref = "nginx", Output = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``image import sans fichier retourne 1`` () =
        let output = MockOutputPort()
        let code = run (ImageImportCommand(output)) (ImageImportSettings(File = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``image import fichier introuvable retourne 1`` () =
        let output = MockOutputPort()
        let code = run (ImageImportCommand(output)) (ImageImportSettings(File = "C:\\nope\\img.tar"))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``container read-file sans identifiant retourne 1`` () =
        let output = MockOutputPort()
        let code = run (ReadFileCommand(output)) (ReadFileSettings(Id = null, Path = "/etc/hostname", Output = "o.txt"))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``container read-file sans chemin retourne 1`` () =
        let output = MockOutputPort()
        let code = run (ReadFileCommand(output)) (ReadFileSettings(Id = "c1", Path = null, Output = "o.txt"))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``container read-file sans sortie retourne 1`` () =
        let output = MockOutputPort()
        let code = run (ReadFileCommand(output)) (ReadFileSettings(Id = "c1", Path = "/etc/hostname", Output = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``container write-file sans identifiant retourne 1`` () =
        let output = MockOutputPort()
        let code = run (WriteFileCommand(output)) (WriteFileSettings(Id = null, Path = "/tmp/f", Input = "in.txt"))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``container write-file sans chemin retourne 1`` () =
        let output = MockOutputPort()
        let code = run (WriteFileCommand(output)) (WriteFileSettings(Id = "c1", Path = null, Input = "in.txt"))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``container write-file sans entrée retourne 1`` () =
        let output = MockOutputPort()
        let code = run (WriteFileCommand(output)) (WriteFileSettings(Id = "c1", Path = "/tmp/f", Input = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    // ─── Réseaux ──────────────────────────────────────────────────────
    open DiploWalker.Cli.Network

    [<Fact>]
    let ``network disconnect sans conteneur retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run (DisconnectCommand(output)) (DisconnectSettings(NetworkId = "n", ContainerId = null))

        code |> should equal 1
        output.Errors |> should not' (be Empty)

    // ─── Disk ─────────────────────────────────────────────────────────
    open DiploWalker.Cli.Disk

    [<Fact>]
    let ``disk create-image avec format inconnu retourne 1`` () =
        let output = MockOutputPort()
        let root = TestHelpers.createTempDir "cli-disk-format"
        let src = Path.Combine(root, "src")
        Directory.CreateDirectory(src) |> ignore

        try
            let code =
                run
                    (CreateImageCommand(output))
                    (CreateImageSettings(Source = src, Dest = Path.Combine(root, "out.iso"), Format = "qcow2"))

            code |> should equal 1
            output.Errors |> should not' (be Empty)
        finally
            TestHelpers.cleanupDir root

