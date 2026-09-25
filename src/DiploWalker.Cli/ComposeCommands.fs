namespace DiploWalker.Cli.Compose

open System
open System.Threading.Tasks
open DiploWalker.Core.Compose
open DiploWalker.Core.Output
open Spectre.Console.Cli

// â”€â”€ up â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type ComposeUpSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<FILE>")>]
    member val File: string = null with get, set

type ComposeUpCommand(output: IOutputPort) =
    inherit AsyncCommand<ComposeUpSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.File) then
                output.WriteError("Le chemin du fichier compose est requis")
                return 1
            else
                use orchestrator = new ComposeOrchestrator(output)
                do! orchestrator.Up(settings.File)
                return 0
        }

// â”€â”€ down â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type ComposeDownSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<FILE>")>]
    member val File: string = null with get, set

type ComposeDownCommand(output: IOutputPort) =
    inherit AsyncCommand<ComposeDownSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.File) then
                output.WriteError("Le chemin du fichier compose est requis")
                return 1
            else
                use orchestrator = new ComposeOrchestrator(output)
                do! orchestrator.Down(settings.File)
                return 0
        }

// â”€â”€ ps â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type ComposePsSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<FILE>")>]
    member val File: string = null with get, set

type ComposePsCommand(output: IOutputPort) =
    inherit AsyncCommand<ComposePsSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.File) then
                output.WriteError("Le chemin du fichier compose est requis")
                return 1
            else
                use orchestrator = new ComposeOrchestrator(output)
                do! orchestrator.Ps(settings.File)
                return 0
        }

// â”€â”€ logs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type ComposeLogsSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<FILE>")>]
    member val File: string = null with get, set

    [<CommandOption("-s|--service")>]
    member val Service: string = null with get, set

type ComposeLogsCommand(output: IOutputPort) =
    inherit AsyncCommand<ComposeLogsSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.File) then
                output.WriteError("Le chemin du fichier compose est requis")
                return 1
            else
                use orchestrator = new ComposeOrchestrator(output)

                let service =
                    if isNull settings.Service then
                        None
                    else
                        Some settings.Service

                do! orchestrator.Logs(settings.File, service)
                return 0
        }

// â”€â”€ pull â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type ComposePullSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<FILE>")>]
    member val File: string = null with get, set

type ComposePullCommand(output: IOutputPort) =
    inherit AsyncCommand<ComposePullSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.File) then
                output.WriteError("Le chemin du fichier compose est requis")
                return 1
            else
                use orchestrator = new ComposeOrchestrator(output)
                do! orchestrator.Pull(settings.File)
                return 0
        }

// â”€â”€ build â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type ComposeBuildSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<FILE>")>]
    member val File: string = null with get, set

type ComposeBuildCommand(output: IOutputPort) =
    inherit AsyncCommand<ComposeBuildSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.File) then
                output.WriteError("Le chemin du fichier compose est requis")
                return 1
            else
                use orchestrator = new ComposeOrchestrator(output)
                do! orchestrator.Build(settings.File)
                return 0
        }

