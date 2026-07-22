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
            addCmd<VersionCommand> c "version"
        ) |> ignore

        config.AddBranch("volume", fun (c: IConfigurator<CommandSettings>) ->
            addCmd<ListVolumesCommand> c "list"
            addCmd<InspectVolumeCommand> c "inspect"
            addCmd<CreateVolumeCommand> c "create"
            addCmd<RemoveVolumeCommand> c "remove"
            addCmd<MountVolumeCommand> c "mount"
            addCmd<UnmountVolumeCommand> c "unmount"
        ) |> ignore

        config.AddBranch("network", fun (c: IConfigurator<CommandSettings>) ->
            addCmd<ListNetworksCommand> c "list"
            addCmd<InspectNetworkCommand> c "inspect"
            addCmd<CreateNetworkCommand> c "create"
            addCmd<RemoveNetworkCommand> c "remove"
            addCmd<ConnectCommand> c "connect"
            addCmd<DisconnectCommand> c "disconnect"
        ) |> ignore
    ) |> ignore
    app.Run(argv)
