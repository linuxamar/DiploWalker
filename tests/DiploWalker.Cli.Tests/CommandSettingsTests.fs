namespace DiploWalker.Cli.Tests

open Xunit
open FsUnit.Xunit
open System.Reflection
open DiploWalker.TestHelpers

module ``Vérification de la structure des commandes CLI`` =

    // --- Container commands ---
    open DiploWalker.Cli.Container

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
    let ``RegistryLoginCommand hérite de AsyncCommand<RegistryLoginSettings>`` () =
        typeof<RegistryLoginCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<RegistryLoginSettings>>)
        |> should be True

    [<Fact>]
    let ``RegistryLogoutCommand hérite de AsyncCommand<RegistryLogoutSettings>`` () =
        typeof<RegistryLogoutCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<RegistryLogoutSettings>>)
        |> should be True

    [<Fact>]
    let ``PullSettings porte l'option --user`` () =
        let attr =
            typeof<PullSettings>
                .GetProperty("User")
                .GetCustomAttributes(typeof<Spectre.Console.Cli.CommandOptionAttribute>, false)

        let longNames =
            attr
            |> Array.tryPick (function
                | :? Spectre.Console.Cli.CommandOptionAttribute as a -> Some(a.LongNames |> Seq.toArray)
                | _ -> None)

        longNames |> should equal (Some [| "user" |])

    [<Fact>]
    let ``VersionCommand hérite de AsyncCommand<VersionSettings>`` () =
        typeof<VersionCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<VersionSettings>>)
        |> should be True

    // --- Volume commands ---
    open DiploWalker.Cli.Volume

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
    let ``PruneVolumesCommand hérite de AsyncCommand<PruneVolumesSettings>`` () =
        typeof<PruneVolumesCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<PruneVolumesSettings>>)
        |> should be True

    // --- Network commands ---
    open DiploWalker.Cli.Network

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
    let ``NamespacesCommand hérite de AsyncCommand<NamespacesSettings>`` () =
        typeof<NamespacesCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<NamespacesSettings>>)
        |> should be True

    // --- New network commands ---
    [<Fact>]
    let ``RunCniPluginCommand hérite de AsyncCommand<RunCniPluginSettings>`` () =
        typeof<RunCniPluginCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<RunCniPluginSettings>>)
        |> should be True

    [<Fact>]
    let ``PruneNetworksCommand hérite de AsyncCommand<PruneNetworksSettings>`` () =
        typeof<PruneNetworksCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<PruneNetworksSettings>>)
        |> should be True

    // --- Status / Config commands ---
    open DiploWalker.Cli

    [<Fact>]
    let ``StatusCommand hérite de AsyncCommand<StatusSettings>`` () =
        typeof<StatusCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<StatusSettings>>)
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

    open DiploWalker.Cli.Container

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

    open DiploWalker.Cli.Volume

    [<Fact>]
    let ``CreateVolumeSettings.Driver est 'local' par défaut`` () =
        let settings = CreateVolumeSettings()
        settings.Driver |> should equal "local"

    [<Fact>]
    let ``RemoveVolumeSettings.Force est false par défaut`` () =
        let settings = RemoveVolumeSettings()
        settings.Force |> should equal false

    open DiploWalker.Cli.Network

    [<Fact>]
    let ``CreateNetworkSettings.Driver est 'bridge' par défaut`` () =
        let settings = CreateNetworkSettings()
        settings.Driver |> should equal "bridge"

    [<Fact>]
    let ``DisconnectSettings.Force est false par défaut`` () =
        let settings = DisconnectSettings()
        settings.Force |> should equal false

    open DiploWalker.Cli

    [<Fact>]
    let ``InitConfigSettings.Path est vide par défaut`` () =
        let settings = InitConfigSettings()
        settings.Path |> should equal ""

    [<Fact>]
    let ``InitConfigSettings.Transport est tcp par défaut`` () =
        let settings = InitConfigSettings()
        settings.Transport |> should equal "tcp"

    [<Fact>]
    let ``InitConfigSettings.Transport accepte pipe`` () =
        let settings = InitConfigSettings(Transport = "pipe")
        settings.Transport |> should equal "pipe"

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

    [<Fact>]
    let ``ImageSearchSettings.Query est null par défaut`` () =
        let settings = ImageSearchSettings()
        settings.Query |> should be Null

    [<Fact>]
    let ``ImageSearchSettings.Registry est null par défaut`` () =
        let settings = ImageSearchSettings()
        settings.Registry |> should be Null

    [<Fact>]
    let ``ImageSearchSettings.Limit est 25 par défaut`` () =
        let settings = ImageSearchSettings()
        settings.Limit |> should equal 25

    // --- Disk settings ---
    open DiploWalker.Cli.Disk

    [<Fact>]
    let ``CreateImageSettings.Source est null par défaut`` () =
        let settings = CreateImageSettings()
        settings.Source |> should be Null

    [<Fact>]
    let ``CreateImageSettings.Dest est null par défaut`` () =
        let settings = CreateImageSettings()
        settings.Dest |> should be Null

    [<Fact>]
    let ``CreateImageSettings.Format est 'raw' par défaut`` () =
        let settings = CreateImageSettings()
        settings.Format |> should equal "raw"

    [<Fact>]
    let ``CreateImageSettings porte l'option --format`` () =
        let attr =
            typeof<CreateImageSettings>
                .GetProperty("Format")
                .GetCustomAttributes(typeof<Spectre.Console.Cli.CommandOptionAttribute>, false)

        let longNames =
            attr
            |> Array.tryPick (function
                | :? Spectre.Console.Cli.CommandOptionAttribute as a -> Some(a.LongNames |> Seq.toArray)
                | _ -> None)

        longNames |> should equal (Some [| "format" |])

    open DiploWalker.Cli.Container

    [<Fact>]
    let ``RegistryLoginSettings.Registry est null par défaut`` () =
        let settings = RegistryLoginSettings()
        settings.Registry |> should be Null

    [<Fact>]
    let ``RegistryLoginSettings.Username est null par défaut`` () =
        let settings = RegistryLoginSettings()
        settings.Username |> should be Null

    [<Fact>]
    let ``RegistryLoginSettings.Password est null par défaut`` () =
        let settings = RegistryLoginSettings()
        settings.Password |> should be Null

    [<Fact>]
    let ``RegistryLoginSettings porte l'option --username`` () =
        let attr =
            typeof<RegistryLoginSettings>
                .GetProperty("Username")
                .GetCustomAttributes(typeof<Spectre.Console.Cli.CommandOptionAttribute>, false)

        let longNames =
            attr
            |> Array.tryPick (function
                | :? Spectre.Console.Cli.CommandOptionAttribute as a -> Some(a.LongNames |> Seq.toArray)
                | _ -> None)

        longNames |> should equal (Some [| "username" |])

    [<Fact>]
    let ``RegistryLoginSettings porte l'option --password`` () =
        let attr =
            typeof<RegistryLoginSettings>
                .GetProperty("Password")
                .GetCustomAttributes(typeof<Spectre.Console.Cli.CommandOptionAttribute>, false)

        let longNames =
            attr
            |> Array.tryPick (function
                | :? Spectre.Console.Cli.CommandOptionAttribute as a -> Some(a.LongNames |> Seq.toArray)
                | _ -> None)

        longNames |> should equal (Some [| "password" |])

    [<Fact>]
    let ``RegistryLogoutSettings.Registry est null par défaut`` () =
        let settings = RegistryLogoutSettings()
        settings.Registry |> should be Null

    // --- New container commands ---
    open DiploWalker.Cli.Container

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

    [<Fact>]
    let ``CreateContainerSettings.Mounts est vide par défaut`` () =
        let settings = CreateContainerSettings()
        settings.Mounts.Length |> should equal 0

    [<Fact>]
    let ``CreateContainerSettings.Mounts porte l'option --mount`` () =
        let attr =
            typeof<CreateContainerSettings>
                .GetProperty("Mounts")
                .GetCustomAttributes(typeof<Spectre.Console.Cli.CommandOptionAttribute>, false)

        let longNames =
            attr
            |> Array.tryPick (function
                | :? Spectre.Console.Cli.CommandOptionAttribute as a -> Some(a.LongNames |> Seq.toArray)
                | _ -> None)

        longNames |> should equal (Some [| "mount" |])

    // --- Volume inspect/mount/unmount ---
    [<Fact>]
    let ``InspectVolumeCommand hérite de AsyncCommand<InspectVolumeSettings>`` () =
        typeof<InspectVolumeCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<InspectVolumeSettings>>)
        |> should be True

    [<Fact>]
    let ``MountVolumeCommand hérite de AsyncCommand<MountSettings>`` () =
        typeof<MountVolumeCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<MountSettings>>)
        |> should be True

    [<Fact>]
    let ``UnmountVolumeCommand hérite de AsyncCommand<UnmountSettings>`` () =
        typeof<UnmountVolumeCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<UnmountSettings>>)
        |> should be True

    // --- Network inspect ---
    [<Fact>]
    let ``InspectNetworkCommand hérite de AsyncCommand<InspectNetworkSettings>`` () =
        typeof<InspectNetworkCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<InspectNetworkSettings>>)
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

    [<Fact>]
    let ``ImageSearchCommand hérite de AsyncCommand<ImageSearchSettings>`` () =
        typeof<ImageSearchCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ImageSearchSettings>>)
        |> should be True

    // --- Disk commands ---
    open DiploWalker.Cli.Disk

    [<Fact>]
    let ``CreateImageCommand hérite de AsyncCommand<CreateImageSettings>`` () =
        typeof<CreateImageCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<CreateImageSettings>>)
        |> should be True

    // --- Compose commands ---
    open DiploWalker.Cli.Compose

    [<Fact>]
    let ``ComposeUpCommand hérite de AsyncCommand<ComposeUpSettings>`` () =
        typeof<ComposeUpCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ComposeUpSettings>>)
        |> should be True

    [<Fact>]
    let ``ComposeDownCommand hérite de AsyncCommand<ComposeDownSettings>`` () =
        typeof<ComposeDownCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ComposeDownSettings>>)
        |> should be True

    [<Fact>]
    let ``ComposePsCommand hérite de AsyncCommand<ComposePsSettings>`` () =
        typeof<ComposePsCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ComposePsSettings>>)
        |> should be True

    [<Fact>]
    let ``ComposeLogsCommand hérite de AsyncCommand<ComposeLogsSettings>`` () =
        typeof<ComposeLogsCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ComposeLogsSettings>>)
        |> should be True

    [<Fact>]
    let ``ComposePullCommand hérite de AsyncCommand<ComposePullSettings>`` () =
        typeof<ComposePullCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ComposePullSettings>>)
        |> should be True

    [<Fact>]
    let ``ComposeBuildCommand hérite de AsyncCommand<ComposeBuildSettings>`` () =
        typeof<ComposeBuildCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ComposeBuildSettings>>)
        |> should be True

    // --- Compose settings ---
    [<Fact>]
    let ``ComposeLogsSettings.Service est null par défaut`` () =
        let settings = ComposeLogsSettings()
        settings.Service |> should be Null

module ``Vérification du MockOutputPort`` =

    open DiploWalker.Core.Output

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

