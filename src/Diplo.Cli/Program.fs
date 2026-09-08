open System
open System.Reflection
open Diplo.Cli
open Diplo.Cli.Container
open Diplo.Cli.Volume
open Diplo.Cli.Network
open Diplo.Cli.Compose
open Diplo.Cli.Disk
open Diplo.Cli.Catalog
open Spectre.Console.Cli

let private addCmd (c: IConfigurator<CommandSettings>) (name: string) (t: Type) =
    typeof<IConfigurator<CommandSettings>>.GetTypeInfo().DeclaredMethods
    |> Seq.find (fun m -> m.Name = "AddCommand" && m.GetParameters().Length = 1 && m.IsGenericMethod)
    |> fun mi -> mi.MakeGenericMethod(t).Invoke(c, [| name |])
    |> ignore

[<EntryPoint>]
let main argv =
    let app = CommandApp(TypeRegistrar())

    app.Configure(fun (config: IConfigurator) ->

        config.AddBranch(
            "container",
            Action<IConfigurator<CommandSettings>>(fun c ->
                addCmd c "list" typeof<ListCommand>
                addCmd c "inspect" typeof<InspectContainerCommand>
                addCmd c "start" typeof<StartContainerCommand>
                addCmd c "stop" typeof<StopContainerCommand>
                addCmd c "delete" typeof<DeleteContainerCommand>
                addCmd c "pull" typeof<PullImageCommand>
                addCmd c "login" typeof<RegistryLoginCommand>
                addCmd c "logout" typeof<RegistryLogoutCommand>
                addCmd c "create" typeof<CreateContainerCommand>
                addCmd c "logs" typeof<LogsContainerCommand>
                addCmd c "exec" typeof<ExecContainerCommand>
                addCmd c "namespaces" typeof<NamespacesCommand>
                addCmd c "version" typeof<VersionCommand>
                addCmd c "rename" typeof<RenameContainerCommand>
                addCmd c "top" typeof<TopContainerCommand>
                addCmd c "stats" typeof<StatsContainerCommand>
                addCmd c "image-list" typeof<ImageListCommand>
                addCmd c "image-inspect" typeof<ImageInspectCommand>
                addCmd c "image-remove" typeof<ImageRemoveCommand>
                addCmd c "image-tag" typeof<ImageTagCommand>
                addCmd c "pause" typeof<PauseContainerCommand>
                addCmd c "unpause" typeof<UnpauseContainerCommand>
                addCmd c "wait" typeof<WaitContainerCommand>
                addCmd c "prune" typeof<PruneContainersCommand>
                addCmd c "events" typeof<ContainerEventsCommand>
                addCmd c "stats-stream" typeof<StatsStreamCommand>
                addCmd c "image-prune" typeof<ImagePruneCommand>
                addCmd c "image-commit" typeof<ImageCommitCommand>
                addCmd c "image-export" typeof<ImageExportCommand>
                addCmd c "image-import" typeof<ImageImportCommand>
                addCmd c "read-file" typeof<ReadFileCommand>
                addCmd c "write-file" typeof<WriteFileCommand>
                addCmd c "catalog-list" typeof<CatalogListCommand>
                addCmd c "catalog-add" typeof<CatalogAddCommand>
                addCmd c "catalog-update" typeof<CatalogUpdateCommand>
                addCmd c "catalog-delete" typeof<CatalogDeleteCommand>)
        )
        |> ignore

        config.AddBranch(
            "volume",
            Action<IConfigurator<CommandSettings>>(fun c ->
                addCmd c "list" typeof<ListVolumesCommand>
                addCmd c "inspect" typeof<InspectVolumeCommand>
                addCmd c "create" typeof<CreateVolumeCommand>
                addCmd c "remove" typeof<RemoveVolumeCommand>
                addCmd c "mount" typeof<MountVolumeCommand>
                addCmd c "unmount" typeof<UnmountVolumeCommand>
                addCmd c "prune" typeof<PruneVolumesCommand>)
        )
        |> ignore

        config.AddBranch(
            "network",
            Action<IConfigurator<CommandSettings>>(fun c ->
                addCmd c "list" typeof<ListNetworksCommand>
                addCmd c "inspect" typeof<InspectNetworkCommand>
                addCmd c "create" typeof<CreateNetworkCommand>
                addCmd c "remove" typeof<RemoveNetworkCommand>
                addCmd c "connect" typeof<ConnectCommand>
                addCmd c "disconnect" typeof<DisconnectCommand>
                addCmd c "run-cni-plugin" typeof<RunCniPluginCommand>
                addCmd c "prune" typeof<PruneNetworksCommand>)
        )
        |> ignore

        config.AddBranch(
            "status",
            Action<IConfigurator<CommandSettings>>(fun c -> addCmd c "check" typeof<StatusCommand>)
        )
        |> ignore

        config.AddBranch(
            "config",
            Action<IConfigurator<CommandSettings>>(fun c -> addCmd c "init" typeof<InitConfigCommand>)
        )
        |> ignore

        config.AddBranch(
            "compose",
            Action<IConfigurator<CommandSettings>>(fun c ->
                addCmd c "up" typeof<ComposeUpCommand>
                addCmd c "down" typeof<ComposeDownCommand>
                addCmd c "ps" typeof<ComposePsCommand>
                addCmd c "logs" typeof<ComposeLogsCommand>
                addCmd c "pull" typeof<ComposePullCommand>
                addCmd c "build" typeof<ComposeBuildCommand>)
        )
        |> ignore

        config.AddBranch(
            "disk",
            Action<IConfigurator<CommandSettings>>(fun c -> addCmd c "create-image" typeof<CreateImageCommand>)
        )
        |> ignore)
    |> ignore

    // Propager le code retour de Spectre : l'ignorer faisait toujours sortir
    // le processus en 0, cassant toute chaîne scriptée (&&, CI, planificateur).
    app.Run(argv)
