namespace Diplo.Cli.Tests

/// Codes de sortie : les chemins de validation (arguments manquants, driver
/// inconnu, etc.) doivent retourner 1 SANS contacter le serveur gRPC.
module ExitCodeTests =

    open System.Threading
    open Xunit
    open FsUnit.Xunit
    open Diplo.Core.Output
    open Spectre.Console.Cli
    open Diplo.TestHelpers

    let private run (cmd: AsyncCommand<'T>) (settings: 'T) : int =
        let command = cmd :> ICommand<'T>
        command.ExecuteAsync(Unchecked.defaultof<CommandContext>, settings, CancellationToken.None).Result

    // ─── Conteneurs ───────────────────────────────────────────────────
    open Diplo.Cli.Container

    [<Fact>]
    let ``container delete sans identifiant retourne 1`` () =
        let output = MockOutputPort()
        let code = run (DeleteContainerCommand(output)) (DeleteSettings(Id = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``container create sans nom retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run (CreateContainerCommand(output)) (CreateContainerSettings(Name = null, Image = "image"))

        code |> should equal 1

    [<Fact>]
    let ``container create avec nom mais sans image retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run (CreateContainerCommand(output)) (CreateContainerSettings(Name = "app", Image = null))

        code |> should equal 1

    [<Fact>]
    let ``container rename sans identifiant retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run (RenameContainerCommand(output)) (RenameContainerSettings(Id = null, NewName = "x"))

        code |> should equal 1

    [<Fact>]
    let ``container rename sans nouveau nom retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run (RenameContainerCommand(output)) (RenameContainerSettings(Id = "abc", NewName = null))

        code |> should equal 1

    [<Fact>]
    let ``registry login sans registre retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run (RegistryLoginCommand(output)) (RegistryLoginSettings(Registry = null, Username = "u", Password = "p"))

        code |> should equal 1

    [<Fact>]
    let ``registry login sans utilisateur retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run
                (RegistryLoginCommand(output))
                (RegistryLoginSettings(Registry = "reg", Username = null, Password = "p"))

        code |> should equal 1

    // ─── Volumes ──────────────────────────────────────────────────────
    open Diplo.Cli.Volume

    [<Fact>]
    let ``volume remove sans identifiant retourne 1`` () =
        let output = MockOutputPort()
        let code = run (RemoveVolumeCommand(output)) (RemoveVolumeSettings(Id = null))
        code |> should equal 1

    [<Fact>]
    let ``volume create sans nom retourne 1`` () =
        let output = MockOutputPort()
        let code = run (CreateVolumeCommand(output)) (CreateVolumeSettings(Name = null))
        code |> should equal 1

    [<Fact>]
    let ``volume create avec driver inconnu retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run (CreateVolumeCommand(output)) (CreateVolumeSettings(Name = "v", Driver = "ext4"))

        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``volume create smb sans serveur retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run
                (CreateVolumeCommand(output))
                (CreateVolumeSettings(Name = "v", Driver = "smb", Server = null, Share = "partage"))

        code |> should equal 1

    [<Fact>]
    let ``volume create nfs sans export retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run
                (CreateVolumeCommand(output))
                (CreateVolumeSettings(Name = "v", Driver = "nfs", Server = "srv", Export = null))

        code |> should equal 1

    // ─── Réseaux ──────────────────────────────────────────────────────
    open Diplo.Cli.Network

    [<Fact>]
    let ``network remove sans identifiant retourne 1`` () =
        let output = MockOutputPort()
        let code = run (RemoveNetworkCommand(output)) (RemoveNetworkSettings(Id = null))
        code |> should equal 1

    [<Fact>]
    let ``network create sans nom retourne 1`` () =
        let output = MockOutputPort()
        let code = run (CreateNetworkCommand(output)) (CreateNetworkSettings(Name = null))
        code |> should equal 1

    [<Fact>]
    let ``network connect sans reseau retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run (ConnectCommand(output)) (ConnectSettings(NetworkId = null, ContainerId = "c"))

        code |> should equal 1

    [<Fact>]
    let ``network connect sans conteneur retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run (ConnectCommand(output)) (ConnectSettings(NetworkId = "n", ContainerId = null))

        code |> should equal 1

    [<Fact>]
    let ``network disconnect sans reseau retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run (DisconnectCommand(output)) (DisconnectSettings(NetworkId = null, ContainerId = "c"))

        code |> should equal 1

    [<Fact>]
    let ``network run-cni-plugin sans plugin retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run (RunCniPluginCommand(output)) (RunCniPluginSettings(PluginPath = null))

        code |> should equal 1

    [<Fact>]
    let ``network run-cni-plugin avec plugin relatif retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run (RunCniPluginCommand(output)) (RunCniPluginSettings(PluginPath = "mon-plugin.exe"))

        code |> should equal 1

    [<Fact>]
    let ``network run-cni-plugin sans container-id retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run
                (RunCniPluginCommand(output))
                (RunCniPluginSettings(
                    PluginPath = "C:\\plugins\\mon-plugin.exe",
                    ContainerId = null,
                    NetnsPath = "C:\\ns\\1"
                ))

        code |> should equal 1

    [<Fact>]
    let ``network run-cni-plugin sans netns retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run
                (RunCniPluginCommand(output))
                (RunCniPluginSettings(PluginPath = "C:\\plugins\\mon-plugin.exe", ContainerId = "c", NetnsPath = null))

        code |> should equal 1

    // ─── Disk ─────────────────────────────────────────────────────────
    open Diplo.Cli.Disk

    [<Fact>]
    let ``disk create-image sans source retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run (CreateImageCommand(output)) (CreateImageSettings(Source = null, Dest = "C:\\img.vhd"))

        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``disk create-image sans destination retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run (CreateImageCommand(output)) (CreateImageSettings(Source = "C:\\src", Dest = null))

        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``disk create-image avec source inexistante retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run (CreateImageCommand(output)) (CreateImageSettings(Source = "Z:\\n'existe\\pas", Dest = "C:\\img.vhd"))

        code |> should equal 1
        output.Errors |> should not' (be Empty)

    // ─── Volume inspect / mount / unmount ─────────────────────────────
    open Diplo.Cli.Volume

    [<Fact>]
    let ``volume inspect sans identifiant retourne 1`` () =
        let output = MockOutputPort()
        let code = run (InspectVolumeCommand(output)) (InspectVolumeSettings(Id = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``volume mount sans identifiant retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run (MountVolumeCommand(output)) (MountSettings(Id = null, Target = "C:\\mnt"))

        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``volume unmount sans identifiant retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run (UnmountVolumeCommand(output)) (UnmountSettings(Id = null, Target = "C:\\mnt"))

        code |> should equal 1
        output.Errors |> should not' (be Empty)

    // ─── Network inspect ──────────────────────────────────────────────
    open Diplo.Cli.Network

    [<Fact>]
    let ``network inspect sans identifiant retourne 1`` () =
        let output = MockOutputPort()
        let code = run (InspectNetworkCommand(output)) (InspectNetworkSettings(Id = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    // ─── Compose ──────────────────────────────────────────────────────
    open Diplo.Cli.Compose

    [<Fact>]
    let ``compose up sans fichier retourne 1`` () =
        let output = MockOutputPort()
        let code = run (ComposeUpCommand(output)) (ComposeUpSettings(File = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``compose down sans fichier retourne 1`` () =
        let output = MockOutputPort()
        let code = run (ComposeDownCommand(output)) (ComposeDownSettings(File = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``compose ps sans fichier retourne 1`` () =
        let output = MockOutputPort()
        let code = run (ComposePsCommand(output)) (ComposePsSettings(File = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``compose logs sans fichier retourne 1`` () =
        let output = MockOutputPort()
        let code = run (ComposeLogsCommand(output)) (ComposeLogsSettings(File = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``compose pull sans fichier retourne 1`` () =
        let output = MockOutputPort()
        let code = run (ComposePullCommand(output)) (ComposePullSettings(File = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    [<Fact>]
    let ``compose build sans fichier retourne 1`` () =
        let output = MockOutputPort()
        let code = run (ComposeBuildCommand(output)) (ComposeBuildSettings(File = null))
        code |> should equal 1
        output.Errors |> should not' (be Empty)
