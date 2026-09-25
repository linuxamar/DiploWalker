namespace DiploWalker.Cli.Tests

open Xunit
open FsUnit.Xunit
open System.Reflection
open DiploWalker.TestHelpers

module ``VÃ©rification de la structure des commandes CLI`` =

    // --- Container commands ---
    open DiploWalker.Cli.Container

    [<Fact>]
    let ``ListCommand hÃ©rite de AsyncCommand<ListSettings>`` () =
        typeof<ListCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ListSettings>>)
        |> should be True

    [<Fact>]
    let ``InspectContainerCommand hÃ©rite de AsyncCommand<InspectContainerSettings>`` () =
        typeof<InspectContainerCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<InspectContainerSettings>>)
        |> should be True

    [<Fact>]
    let ``StartContainerCommand hÃ©rite de AsyncCommand<StartSettings>`` () =
        typeof<StartContainerCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<StartSettings>>)
        |> should be True

    [<Fact>]
    let ``StopContainerCommand hÃ©rite de AsyncCommand<StopSettings>`` () =
        typeof<StopContainerCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<StopSettings>>)
        |> should be True

    [<Fact>]
    let ``DeleteContainerCommand hÃ©rite de AsyncCommand<DeleteSettings>`` () =
        typeof<DeleteContainerCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<DeleteSettings>>)
        |> should be True

    [<Fact>]
    let ``PullImageCommand hÃ©rite de AsyncCommand<PullSettings>`` () =
        typeof<PullImageCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<PullSettings>>)
        |> should be True

    [<Fact>]
    let ``RegistryLoginCommand hÃ©rite de AsyncCommand<RegistryLoginSettings>`` () =
        typeof<RegistryLoginCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<RegistryLoginSettings>>)
        |> should be True

    [<Fact>]
    let ``RegistryLogoutCommand hÃ©rite de AsyncCommand<RegistryLogoutSettings>`` () =
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
    let ``VersionCommand hÃ©rite de AsyncCommand<VersionSettings>`` () =
        typeof<VersionCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<VersionSettings>>)
        |> should be True

    // --- Volume commands ---
    open DiploWalker.Cli.Volume

    [<Fact>]
    let ``ListVolumesCommand hÃ©rite de AsyncCommand<ListVolumesSettings>`` () =
        typeof<ListVolumesCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ListVolumesSettings>>)
        |> should be True

    [<Fact>]
    let ``CreateVolumeCommand hÃ©rite de AsyncCommand<CreateVolumeSettings>`` () =
        typeof<CreateVolumeCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<CreateVolumeSettings>>)
        |> should be True

    [<Fact>]
    let ``RemoveVolumeCommand hÃ©rite de AsyncCommand<RemoveVolumeSettings>`` () =
        typeof<RemoveVolumeCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<RemoveVolumeSettings>>)
        |> should be True

    [<Fact>]
    let ``MountVolumeCommand hÃ©rite de AsyncCommand<MountSettings>`` () =
        typeof<MountVolumeCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<MountSettings>>)
        |> should be True

    [<Fact>]
    let ``UnmountVolumeCommand hÃ©rite de AsyncCommand<UnmountSettings>`` () =
        typeof<UnmountVolumeCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<UnmountSettings>>)
        |> should be True

    [<Fact>]
    let ``PruneVolumesCommand hÃ©rite de AsyncCommand<PruneVolumesSettings>`` () =
        typeof<PruneVolumesCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<PruneVolumesSettings>>)
        |> should be True

    // --- Network commands ---
    open DiploWalker.Cli.Network

    [<Fact>]
    let ``ListNetworksCommand hÃ©rite de AsyncCommand<ListNetworksSettings>`` () =
        typeof<ListNetworksCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ListNetworksSettings>>)
        |> should be True

    [<Fact>]
    let ``CreateNetworkCommand hÃ©rite de AsyncCommand<CreateNetworkSettings>`` () =
        typeof<CreateNetworkCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<CreateNetworkSettings>>)
        |> should be True

    [<Fact>]
    let ``RemoveNetworkCommand hÃ©rite de AsyncCommand<RemoveNetworkSettings>`` () =
        typeof<RemoveNetworkCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<RemoveNetworkSettings>>)
        |> should be True

    [<Fact>]
    let ``ConnectCommand hÃ©rite de AsyncCommand<ConnectSettings>`` () =
        typeof<ConnectCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ConnectSettings>>)
        |> should be True

    [<Fact>]
    let ``DisconnectCommand hÃ©rite de AsyncCommand<DisconnectSettings>`` () =
        typeof<DisconnectCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<DisconnectSettings>>)
        |> should be True

    // --- New container commands ---
    [<Fact>]
    let ``CreateContainerCommand hÃ©rite de AsyncCommand<CreateContainerSettings>`` () =
        typeof<CreateContainerCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<CreateContainerSettings>>)
        |> should be True

    [<Fact>]
    let ``LogsContainerCommand hÃ©rite de AsyncCommand<LogsContainerSettings>`` () =
        typeof<LogsContainerCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<LogsContainerSettings>>)
        |> should be True

    [<Fact>]
    let ``ExecContainerCommand hÃ©rite de AsyncCommand<ExecContainerSettings>`` () =
        typeof<ExecContainerCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ExecContainerSettings>>)
        |> should be True

    [<Fact>]
    let ``NamespacesCommand hÃ©rite de AsyncCommand<NamespacesSettings>`` () =
        typeof<NamespacesCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<NamespacesSettings>>)
        |> should be True

    // --- New network commands ---
    [<Fact>]
    let ``RunCniPluginCommand hÃ©rite de AsyncCommand<RunCniPluginSettings>`` () =
        typeof<RunCniPluginCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<RunCniPluginSettings>>)
        |> should be True

    [<Fact>]
    let ``PruneNetworksCommand hÃ©rite de AsyncCommand<PruneNetworksSettings>`` () =
        typeof<PruneNetworksCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<PruneNetworksSettings>>)
        |> should be True

    // --- Status / Config commands ---
    open DiploWalker.Cli

    [<Fact>]
    let ``StatusCommand hÃ©rite de AsyncCommand<StatusSettings>`` () =
        typeof<StatusCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<StatusSettings>>)
        |> should be True

    [<Fact>]
    let ``InitConfigSettings hÃ©rite de CommandSettings`` () =
        typeof<InitConfigSettings>.IsSubclassOf(typeof<Spectre.Console.Cli.CommandSettings>)
        |> should be True

    [<Fact>]
    let ``InitConfigCommand hÃ©rite de Command<InitConfigSettings>`` () =
        typeof<InitConfigCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.Command<InitConfigSettings>>)
        |> should be True

module ``VÃ©rification des paramÃ¨tres des commandes`` =

    open DiploWalker.Cli.Container

    [<Fact>]
    let ``ListSettings.All est false par dÃ©faut`` () =
        let settings = ListSettings()
        settings.All |> should equal false

    [<Fact>]
    let ``ListSettings.Namespace est null par dÃ©faut`` () =
        let settings = ListSettings()
        settings.Namespace |> should be Null

    [<Fact>]
    let ``InspectContainerSettings a un argument Id`` () =
        let prop = typeof<InspectContainerSettings>.GetProperty("Id")
        prop |> should not' (be Null)

    [<Fact>]
    let ``StopSettings.Timeout est 10 par dÃ©faut`` () =
        let settings = StopSettings()
        settings.Timeout |> should equal 10

    [<Fact>]
    let ``DeleteSettings.Force est false par dÃ©faut`` () =
        let settings = DeleteSettings()
        settings.Force |> should equal false

    open DiploWalker.Cli.Volume

    [<Fact>]
    let ``CreateVolumeSettings.Driver est 'local' par dÃ©faut`` () =
        let settings = CreateVolumeSettings()
        settings.Driver |> should equal "local"

    [<Fact>]
    let ``RemoveVolumeSettings.Force est false par dÃ©faut`` () =
        let settings = RemoveVolumeSettings()
        settings.Force |> should equal false

    open DiploWalker.Cli.Network

    [<Fact>]
    let ``CreateNetworkSettings.Driver est 'bridge' par dÃ©faut`` () =
        let settings = CreateNetworkSettings()
        settings.Driver |> should equal "bridge"

    [<Fact>]
    let ``DisconnectSettings.Force est false par dÃ©faut`` () =
        let settings = DisconnectSettings()
        settings.Force |> should equal false

    open DiploWalker.Cli

    [<Fact>]
    let ``InitConfigSettings.Path est vide par dÃ©faut`` () =
        let settings = InitConfigSettings()
        settings.Path |> should equal ""

    [<Fact>]
    let ``InitConfigSettings.Transport est tcp par dÃ©faut`` () =
        let settings = InitConfigSettings()
        settings.Transport |> should equal "tcp"

    [<Fact>]
    let ``InitConfigSettings.Transport accepte pipe`` () =
        let settings = InitConfigSettings(Transport = "pipe")
        settings.Transport |> should equal "pipe"

    [<Fact>]
    let ``LogsContainerSettings.Follow est false par dÃ©faut`` () =
        let settings = LogsContainerSettings()
        settings.Follow |> should equal false

    [<Fact>]
    let ``LogsContainerSettings.Tail est 100 par dÃ©faut`` () =
        let settings = LogsContainerSettings()
        settings.Tail |> should equal 100

    [<Fact>]
    let ``ExecContainerSettings.Command est vide par dÃ©faut`` () =
        let settings = ExecContainerSettings()
        settings.Command.Length |> should equal 0

    [<Fact>]
    let ``RunCniPluginSettings.ConfigType est null par dÃ©faut`` () =
        let settings = RunCniPluginSettings()
        settings.ConfigType |> should be Null

    // --- Image settings ---
    [<Fact>]
    let ``ImageListSettings.Namespace est null par dÃ©faut`` () =
        let settings = ImageListSettings()
        settings.Namespace |> should be Null

    [<Fact>]
    let ``ImageInspectSettings.Ref est null par dÃ©faut`` () =
        let settings = ImageInspectSettings()
        settings.Ref |> should be Null

    [<Fact>]
    let ``ImageRemoveSettings.Ref est null par dÃ©faut`` () =
        let settings = ImageRemoveSettings()
        settings.Ref |> should be Null

    [<Fact>]
    let ``ImageTagSettings.Source est null par dÃ©faut`` () =
        let settings = ImageTagSettings()
        settings.Source |> should be Null

    [<Fact>]
    let ``ImageTagSettings.Target est null par dÃ©faut`` () =
        let settings = ImageTagSettings()
        settings.Target |> should be Null

    [<Fact>]
    let ``ImageSearchSettings.Query est null par dÃ©faut`` () =
        let settings = ImageSearchSettings()
        settings.Query |> should be Null

    [<Fact>]
    let ``ImageSearchSettings.Registry est null par dÃ©faut`` () =
        let settings = ImageSearchSettings()
        settings.Registry |> should be Null

    [<Fact>]
    let ``ImageSearchSettings.Limit est 25 par dÃ©faut`` () =
        let settings = ImageSearchSettings()
        settings.Limit |> should equal 25

    // --- Disk settings ---
    open DiploWalker.Cli.Disk

    [<Fact>]
    let ``CreateImageSettings.Source est null par dÃ©faut`` () =
        let settings = CreateImageSettings()
        settings.Source |> should be Null

    [<Fact>]
    let ``CreateImageSettings.Dest est null par dÃ©faut`` () =
        let settings = CreateImageSettings()
        settings.Dest |> should be Null

    [<Fact>]
    let ``CreateImageSettings.Format est 'raw' par dÃ©faut`` () =
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
    let ``RegistryLoginSettings.Registry est null par dÃ©faut`` () =
        let settings = RegistryLoginSettings()
        settings.Registry |> should be Null

    [<Fact>]
    let ``RegistryLoginSettings.Username est null par dÃ©faut`` () =
        let settings = RegistryLoginSettings()
        settings.Username |> should be Null

    [<Fact>]
    let ``RegistryLoginSettings.Password est null par dÃ©faut`` () =
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
    let ``RegistryLogoutSettings.Registry est null par dÃ©faut`` () =
        let settings = RegistryLogoutSettings()
        settings.Registry |> should be Null

    // --- New container commands ---
    open DiploWalker.Cli.Container

    [<Fact>]
    let ``RenameContainerSettings hÃ©rite de CommandSettings`` () =
        typeof<RenameContainerSettings>.IsSubclassOf(typeof<Spectre.Console.Cli.CommandSettings>)
        |> should be True

    [<Fact>]
    let ``TopContainerSettings hÃ©rite de CommandSettings`` () =
        typeof<TopContainerSettings>.IsSubclassOf(typeof<Spectre.Console.Cli.CommandSettings>)
        |> should be True

    [<Fact>]
    let ``StatsContainerSettings hÃ©rite de CommandSettings`` () =
        typeof<StatsContainerSettings>.IsSubclassOf(typeof<Spectre.Console.Cli.CommandSettings>)
        |> should be True

    [<Fact>]
    let ``CreateContainerSettings.Mounts est vide par dÃ©faut`` () =
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
    let ``InspectVolumeCommand hÃ©rite de AsyncCommand<InspectVolumeSettings>`` () =
        typeof<InspectVolumeCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<InspectVolumeSettings>>)
        |> should be True

    [<Fact>]
    let ``MountVolumeCommand hÃ©rite de AsyncCommand<MountSettings>`` () =
        typeof<MountVolumeCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<MountSettings>>)
        |> should be True

    [<Fact>]
    let ``UnmountVolumeCommand hÃ©rite de AsyncCommand<UnmountSettings>`` () =
        typeof<UnmountVolumeCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<UnmountSettings>>)
        |> should be True

    // --- Network inspect ---
    [<Fact>]
    let ``InspectNetworkCommand hÃ©rite de AsyncCommand<InspectNetworkSettings>`` () =
        typeof<InspectNetworkCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<InspectNetworkSettings>>)
        |> should be True

    // --- Image commands ---
    [<Fact>]
    let ``ImageListCommand hÃ©rite de AsyncCommand<ImageListSettings>`` () =
        typeof<ImageListCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ImageListSettings>>)
        |> should be True

    [<Fact>]
    let ``ImageInspectCommand hÃ©rite de AsyncCommand<ImageInspectSettings>`` () =
        typeof<ImageInspectCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ImageInspectSettings>>)
        |> should be True

    [<Fact>]
    let ``ImageRemoveCommand hÃ©rite de AsyncCommand<ImageRemoveSettings>`` () =
        typeof<ImageRemoveCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ImageRemoveSettings>>)
        |> should be True

    [<Fact>]
    let ``ImageTagCommand hÃ©rite de AsyncCommand<ImageTagSettings>`` () =
        typeof<ImageTagCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ImageTagSettings>>)
        |> should be True

    [<Fact>]
    let ``ImageSearchCommand hÃ©rite de AsyncCommand<ImageSearchSettings>`` () =
        typeof<ImageSearchCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ImageSearchSettings>>)
        |> should be True

    // --- Disk commands ---
    open DiploWalker.Cli.Disk

    [<Fact>]
    let ``CreateImageCommand hÃ©rite de AsyncCommand<CreateImageSettings>`` () =
        typeof<CreateImageCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<CreateImageSettings>>)
        |> should be True

    // --- Compose commands ---
    open DiploWalker.Cli.Compose

    [<Fact>]
    let ``ComposeUpCommand hÃ©rite de AsyncCommand<ComposeUpSettings>`` () =
        typeof<ComposeUpCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ComposeUpSettings>>)
        |> should be True

    [<Fact>]
    let ``ComposeDownCommand hÃ©rite de AsyncCommand<ComposeDownSettings>`` () =
        typeof<ComposeDownCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ComposeDownSettings>>)
        |> should be True

    [<Fact>]
    let ``ComposePsCommand hÃ©rite de AsyncCommand<ComposePsSettings>`` () =
        typeof<ComposePsCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ComposePsSettings>>)
        |> should be True

    [<Fact>]
    let ``ComposeLogsCommand hÃ©rite de AsyncCommand<ComposeLogsSettings>`` () =
        typeof<ComposeLogsCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ComposeLogsSettings>>)
        |> should be True

    [<Fact>]
    let ``ComposePullCommand hÃ©rite de AsyncCommand<ComposePullSettings>`` () =
        typeof<ComposePullCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ComposePullSettings>>)
        |> should be True

    [<Fact>]
    let ``ComposeBuildCommand hÃ©rite de AsyncCommand<ComposeBuildSettings>`` () =
        typeof<ComposeBuildCommand>.IsSubclassOf(typeof<Spectre.Console.Cli.AsyncCommand<ComposeBuildSettings>>)
        |> should be True

    // --- Compose settings ---
    [<Fact>]
    let ``ComposeLogsSettings.Service est null par dÃ©faut`` () =
        let settings = ComposeLogsSettings()
        settings.Service |> should be Null

module ``VÃ©rification du MockOutputPort`` =

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
    let ``WriteSuccess enregistre le succÃ¨s`` () =
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

