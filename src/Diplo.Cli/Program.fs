open Diplo.Cli
open Diplo.Cli.Container
open Diplo.Cli.Volume
open Diplo.Cli.Network
open Spectre.Console.Cli

[<AutoOpen>]
module private SpectreCliHelpers =
    open System.Reflection

    let private addCommandMethod =
        typeof<IConfigurator<CommandSettings>>
            .GetMethods(BindingFlags.Public ||| BindingFlags.Instance)
            |> Array.find (fun m ->
                m.Name = "AddCommand" && m.IsGenericMethodDefinition)

    let addCmd<'TCommand when 'TCommand : not struct>
        (config: IConfigurator<CommandSettings>) (name: string) =
        addCommandMethod
            .MakeGenericMethod(typeof<'TCommand>)
            .Invoke(config, [| box name |])
        |> ignore

[<EntryPoint>]
let main argv =
    let app = CommandApp(TypeRegistrar())
    app.Configure(fun (config: IConfigurator) ->
        config.AddBranch("container", fun (c: IConfigurator<CommandSettings>) ->
            addCmd<ListCommand> c "list"
            addCmd<InspectContainerCommand> c "inspect"
            addCmd<StartContainerCommand> c "start"
            addCmd<StopContainerCommand> c "stop"
            addCmd<DeleteContainerCommand> c "delete"
            addCmd<PullImageCommand> c "pull"
            addCmd<CreateContainerCommand> c "create"
            addCmd<LogsContainerCommand> c "logs"
            addCmd<ExecContainerCommand> c "exec"
            addCmd<NamespacesCommand> c "namespaces"
            addCmd<VersionCommand> c "version"
            addCmd<RenameContainerCommand> c "rename"
            addCmd<TopContainerCommand> c "top"
            addCmd<StatsContainerCommand> c "stats"
            addCmd<ImageListCommand> c "image-list"
            addCmd<ImageInspectCommand> c "image-inspect"
            addCmd<ImageRemoveCommand> c "image-remove"
            addCmd<ImageTagCommand> c "image-tag"
        ) |> ignore

        config.AddBranch("volume", fun (c: IConfigurator<CommandSettings>) ->
            addCmd<ListVolumesCommand> c "list"
            addCmd<InspectVolumeCommand> c "inspect"
            addCmd<CreateVolumeCommand> c "create"
            addCmd<RemoveVolumeCommand> c "remove"
            addCmd<MountVolumeCommand> c "mount"
            addCmd<UnmountVolumeCommand> c "unmount"
            addCmd<PruneVolumesCommand> c "prune"
        ) |> ignore

        config.AddBranch("network", fun (c: IConfigurator<CommandSettings>) ->
            addCmd<ListNetworksCommand> c "list"
            addCmd<InspectNetworkCommand> c "inspect"
            addCmd<CreateNetworkCommand> c "create"
            addCmd<RemoveNetworkCommand> c "remove"
            addCmd<ConnectCommand> c "connect"
            addCmd<DisconnectCommand> c "disconnect"
            addCmd<RunCniPluginCommand> c "run-cni-plugin"
            addCmd<PruneNetworksCommand> c "prune"
        ) |> ignore

        config.AddBranch("status", fun (c: IConfigurator<CommandSettings>) ->
            addCmd<StatusCommand> c "check"
        ) |> ignore

        config.AddBranch("config", fun (c: IConfigurator<CommandSettings>) ->
            addCmd<InitConfigCommand> c "init"
        ) |> ignore
    ) |> ignore
    app.Run(argv)
