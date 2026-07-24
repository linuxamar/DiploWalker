namespace Diplo.Cli.Tests

open Xunit
open FsUnit.Xunit
open System.Reflection

module ``Vérification de la structure des commandes CLI`` =

    // --- Container commands ---
    open Diplo.Cli.Container

    [<Fact>]
    let ``ListCommand hérite de AsyncCommand<ListSettings>`` () =
        typeof<ListCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ListSettings>>)
        |> should be True

    [<Fact>]
    let ``InspectContainerCommand hérite de AsyncCommand<InspectContainerSettings>`` () =
        typeof<InspectContainerCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<InspectContainerSettings>>)
        |> should be True

    [<Fact>]
    let ``StartContainerCommand hérite de AsyncCommand<StartSettings>`` () =
        typeof<StartContainerCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<StartSettings>>)
        |> should be True

    [<Fact>]
    let ``StopContainerCommand hérite de AsyncCommand<StopSettings>`` () =
        typeof<StopContainerCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<StopSettings>>)
        |> should be True

    [<Fact>]
    let ``DeleteContainerCommand hérite de AsyncCommand<DeleteSettings>`` () =
        typeof<DeleteContainerCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<DeleteSettings>>)
        |> should be True

    [<Fact>]
    let ``PullImageCommand hérite de AsyncCommand<PullSettings>`` () =
        typeof<PullImageCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<PullSettings>>)
        |> should be True

    [<Fact>]
    let ``VersionCommand hérite de AsyncCommand<CommandSettings>`` () =
        typeof<VersionCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<Spectre.Console.Cli.CommandSettings>>)
        |> should be True

    // --- Volume commands ---
    open Diplo.Cli.Volume

    [<Fact>]
    let ``ListVolumesCommand hérite de AsyncCommand<ListVolumesSettings>`` () =
        typeof<ListVolumesCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ListVolumesSettings>>)
        |> should be True

    [<Fact>]
    let ``CreateVolumeCommand hérite de AsyncCommand<CreateVolumeSettings>`` () =
        typeof<CreateVolumeCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<CreateVolumeSettings>>)
        |> should be True

    [<Fact>]
    let ``RemoveVolumeCommand hérite de AsyncCommand<RemoveVolumeSettings>`` () =
        typeof<RemoveVolumeCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<RemoveVolumeSettings>>)
        |> should be True

    [<Fact>]
    let ``MountVolumeCommand hérite de AsyncCommand<MountSettings>`` () =
        typeof<MountVolumeCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<MountSettings>>)
        |> should be True

    [<Fact>]
    let ``UnmountVolumeCommand hérite de AsyncCommand<UnmountSettings>`` () =
        typeof<UnmountVolumeCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<UnmountSettings>>)
        |> should be True

    [<Fact>]
    let ``PruneVolumesCommand hérite de AsyncCommand<CommandSettings>`` () =
        typeof<PruneVolumesCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<Spectre.Console.Cli.CommandSettings>>)
        |> should be True

    // --- Network commands ---
    open Diplo.Cli.Network

    [<Fact>]
    let ``ListNetworksCommand hérite de AsyncCommand<ListNetworksSettings>`` () =
        typeof<ListNetworksCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ListNetworksSettings>>)
        |> should be True

    [<Fact>]
    let ``CreateNetworkCommand hérite de AsyncCommand<CreateNetworkSettings>`` () =
        typeof<CreateNetworkCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<CreateNetworkSettings>>)
        |> should be True

    [<Fact>]
    let ``RemoveNetworkCommand hérite de AsyncCommand<RemoveNetworkSettings>`` () =
        typeof<RemoveNetworkCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<RemoveNetworkSettings>>)
        |> should be True

    [<Fact>]
    let ``ConnectCommand hérite de AsyncCommand<ConnectSettings>`` () =
        typeof<ConnectCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ConnectSettings>>)
        |> should be True

    [<Fact>]
    let ``DisconnectCommand hérite de AsyncCommand<DisconnectSettings>`` () =
        typeof<DisconnectCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<DisconnectSettings>>)
        |> should be True

    // --- New container commands ---
    [<Fact>]
    let ``CreateContainerCommand hérite de AsyncCommand<CreateContainerSettings>`` () =
        typeof<CreateContainerCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<CreateContainerSettings>>)
        |> should be True

    [<Fact>]
    let ``LogsContainerCommand hérite de AsyncCommand<LogsContainerSettings>`` () =
        typeof<LogsContainerCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<LogsContainerSettings>>)
        |> should be True

    [<Fact>]
    let ``ExecContainerCommand hérite de AsyncCommand<ExecContainerSettings>`` () =
        typeof<ExecContainerCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ExecContainerSettings>>)
        |> should be True

    [<Fact>]
    let ``NamespacesCommand hérite de AsyncCommand<CommandSettings>`` () =
        typeof<NamespacesCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<Spectre.Console.Cli.CommandSettings>>)
        |> should be True

    // --- New network commands ---
    [<Fact>]
    let ``RunCniPluginCommand hérite de AsyncCommand<RunCniPluginSettings>`` () =
        typeof<RunCniPluginCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<RunCniPluginSettings>>)
        |> should be True

    [<Fact>]
    let ``PruneNetworksCommand hérite de AsyncCommand<CommandSettings>`` () =
        typeof<PruneNetworksCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<Spectre.Console.Cli.CommandSettings>>)
        |> should be True

    // --- Status / Config commands ---
    open Diplo.Cli

    [<Fact>]
    let ``StatusCommand hérite de AsyncCommand<CommandSettings>`` () =
        typeof<StatusCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<Spectre.Console.Cli.CommandSettings>>)
        |> should be True

    [<Fact>]
    let ``InitConfigSettings hérite de CommandSettings`` () =
        typeof<InitConfigSettings>.IsSubclassOf(typeof<Spectre.Console.Cli.CommandSettings>)
        |> should be True

    [<Fact>]
    let ``InitConfigCommand hérite de Command<InitConfigSettings>`` () =
        typeof<InitConfigCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.Command<InitConfigSettings>>)
        |> should be True

module ``Vérification des paramètres des commandes`` =

    open Diplo.Cli.Container

    [<Fact>]
    let ``ListSettings.All est false par défaut`` () =
        let settings = ListSettings()
        settings.All |> should equal false

    [<Fact>]
    let ``ListSettings.Namespace est null par défaut`` () =
        let settings = ListSettings()
        settings.Namespace |> should be Null

    [<Fact>]
    let ``InspectContainerSettings a un argument Id`` () =
        let prop = typeof<InspectContainerSettings>.GetProperty("Id")
        prop |> should not' (be Null)

    [<Fact>]
    let ``StopSettings.Timeout est 10 par défaut`` () =
        let settings = StopSettings()
        settings.Timeout |> should equal 10

    [<Fact>]
    let ``DeleteSettings.Force est false par défaut`` () =
        let settings = DeleteSettings()
        settings.Force |> should equal false

    open Diplo.Cli.Volume

    [<Fact>]
    let ``CreateVolumeSettings.Driver est 'local' par défaut`` () =
        let settings = CreateVolumeSettings()
        settings.Driver |> should equal "local"

    [<Fact>]
    let ``RemoveVolumeSettings.Force est false par défaut`` () =
        let settings = RemoveVolumeSettings()
        settings.Force |> should equal false

    open Diplo.Cli.Network

    [<Fact>]
    let ``CreateNetworkSettings.Driver est 'bridge' par défaut`` () =
        let settings = CreateNetworkSettings()
        settings.Driver |> should equal "bridge"

    [<Fact>]
    let ``DisconnectSettings.Force est false par défaut`` () =
        let settings = DisconnectSettings()
        settings.Force |> should equal false

    open Diplo.Cli

    [<Fact>]
    let ``InitConfigSettings.Path est vide par défaut`` () =
        let settings = InitConfigSettings()
        settings.Path |> should equal ""

    [<Fact>]
    let ``LogsContainerSettings.Follow est false par défaut`` () =
        let settings = LogsContainerSettings()
        settings.Follow |> should equal false

    [<Fact>]
    let ``LogsContainerSettings.Tail est 100 par défaut`` () =
        let settings = LogsContainerSettings()
        settings.Tail |> should equal 100

    [<Fact>]
    let ``ExecContainerSettings.Command est vide par défaut`` () =
        let settings = ExecContainerSettings()
        settings.Command.Length |> should equal 0

    [<Fact>]
    let ``RunCniPluginSettings.ConfigType est null par défaut`` () =
        let settings = RunCniPluginSettings()
        settings.ConfigType |> should be Null

    // --- Image settings ---
    [<Fact>]
    let ``ImageListSettings.Namespace est null par défaut`` () =
        let settings = ImageListSettings()
        settings.Namespace |> should be Null

    [<Fact>]
    let ``ImageInspectSettings.Ref est null par défaut`` () =
        let settings = ImageInspectSettings()
        settings.Ref |> should be Null

    [<Fact>]
    let ``ImageRemoveSettings.Ref est null par défaut`` () =
        let settings = ImageRemoveSettings()
        settings.Ref |> should be Null

    [<Fact>]
    let ``ImageTagSettings.Source est null par défaut`` () =
        let settings = ImageTagSettings()
        settings.Source |> should be Null

    [<Fact>]
    let ``ImageTagSettings.Target est null par défaut`` () =
        let settings = ImageTagSettings()
        settings.Target |> should be Null

    // --- New container commands ---
    open Diplo.Cli.Container

    [<Fact>]
    let ``RenameContainerSettings hérite de CommandSettings`` () =
        typeof<RenameContainerSettings>.IsSubclassOf(typeof<Spectre.Console.Cli.CommandSettings>)
        |> should be True

    [<Fact>]
    let ``TopContainerSettings hérite de CommandSettings`` () =
        typeof<TopContainerSettings>.IsSubclassOf(typeof<Spectre.Console.Cli.CommandSettings>)
        |> should be True

    [<Fact>]
    let ``StatsContainerSettings hérite de CommandSettings`` () =
        typeof<StatsContainerSettings>.IsSubclassOf(typeof<Spectre.Console.Cli.CommandSettings>)
        |> should be True

    // --- Image commands ---
    [<Fact>]
    let ``ImageListCommand hérite de AsyncCommand<ImageListSettings>`` () =
        typeof<ImageListCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ImageListSettings>>)
        |> should be True

    [<Fact>]
    let ``ImageInspectCommand hérite de AsyncCommand<ImageInspectSettings>`` () =
        typeof<ImageInspectCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ImageInspectSettings>>)
        |> should be True

    [<Fact>]
    let ``ImageRemoveCommand hérite de AsyncCommand<ImageRemoveSettings>`` () =
        typeof<ImageRemoveCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ImageRemoveSettings>>)
        |> should be True

    [<Fact>]
    let ``ImageTagCommand hérite de AsyncCommand<ImageTagSettings>`` () =
        typeof<ImageTagCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ImageTagSettings>>)
        |> should be True

module ``Vérification du MockOutputPort`` =

    open Diplo.Core.Output

    [<Fact>]
    let ``WriteLine enregistre le texte`` () =
        let output = MockOutputPort()
        (output :> IOutputPort).WriteLine("test")
        output.Lines |> should haveLength 1
        output.Lines.[0] |> should equal "test"

    [<Fact>]
    let ``WriteError enregistre l'erreur`` () =
        let output = MockOutputPort()
        (output :> IOutputPort).WriteError("erreur")
        output.Errors |> should haveLength 1
        output.Errors.[0] |> should equal "erreur"

    [<Fact>]
    let ``WriteSuccess enregistre le succès`` () =
        let output = MockOutputPort()
        (output :> IOutputPort).WriteSuccess("ok")
        output.Successes |> should haveLength 1
        output.Successes.[0] |> should equal "ok"

    [<Fact>]
    let ``WriteWarning enregistre l'avertissement`` () =
        let output = MockOutputPort()
        (output :> IOutputPort).WriteWarning("attention")
        output.Warnings |> should haveLength 1
        output.Warnings.[0] |> should equal "attention"

    [<Fact>]
    let ``Reset efface tout`` () =
        let output = MockOutputPort()
        (output :> IOutputPort).WriteLine("a")
        (output :> IOutputPort).WriteError("b")
        (output :> IOutputPort).WriteSuccess("c")
        (output :> IOutputPort).WriteWarning("d")
        output.Reset()
        output.HasOutput |> should equal false
