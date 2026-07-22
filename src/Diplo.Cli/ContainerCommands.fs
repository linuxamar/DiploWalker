namespace Diplo.Cli.Container

open System.Threading
open Diplo.Core.Clients
open Diplo.Core.Output
open Spectre.Console.Cli

// ── list ──────────────────────────────────────────────────────────
type ListSettings() =
    inherit CommandSettings()
    [<CommandOption("-a|--all")>] member val All = false with get, set
    [<CommandOption("--namespace")>] member val Namespace: string = null with get, set

type ListCommand(output: IOutputPort) =
    inherit AsyncCommand<ListSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) =
        task {
            use client = new ContainerClient()
            let ns = if isNull settings.Namespace then "" else settings.Namespace
            let! response = client.ListAsync(all = settings.All, ct = CancellationToken.None)

            if response.Containers.Count = 0 then
                output.WriteWarning("Aucun conteneur trouvé.")
            else
                output.WriteTable(
                    response.Containers,
                    [| "ID"; "Nom"; "Image"; "État"; "Créé" |],
                    fun c ->
                        [| c.Id
                           c.Name
                           c.Image
                           c.State.ToString()
                           c.CreatedAt |])
            return 0
        }

// ── inspect ───────────────────────────────────────────────────────
type InspectContainerSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<ID>")>] member val Id: string = null with get, set

type InspectContainerCommand(output: IOutputPort) =
    inherit AsyncCommand<InspectContainerSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) =
        task {
            use client = new ContainerClient()
            let! response = client.InspectAsync(settings.Id)

            output.WriteSuccess(sprintf "Conteneur %s" response.Name)
            output.WriteLine(sprintf "  ID        : %s" response.Id)
            output.WriteLine(sprintf "  Image     : %s" response.Image)
            output.WriteLine(sprintf "  État      : %s" (response.State.ToString()))
            output.WriteLine(sprintf "  Créé      : %s" response.CreatedAt)
            output.WriteLine(sprintf "  Démarré   : %s" response.StartedAt)
            output.WriteLine(sprintf "  Arrêté    : %s" response.FinishedAt)
            output.WriteLine(sprintf "  PID       : %d" response.Pid)
            output.WriteLine(sprintf "  Exit code : %d" response.ExitCode)
            return 0
        }

// ── start ─────────────────────────────────────────────────────────
type StartSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<ID>")>] member val Id: string = null with get, set

type StartContainerCommand(output: IOutputPort) =
    inherit AsyncCommand<StartSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) =
        task {
            use client = new ContainerClient()
            let! response = client.StartAsync(settings.Id)
            output.WriteSuccess(sprintf "Conteneur %s démarré (%s)" settings.Id (response.State.ToString()))
            return 0
        }

// ── stop ──────────────────────────────────────────────────────────
type StopSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<ID>")>] member val Id: string = null with get, set
    [<CommandOption("-t|--timeout")>] member val Timeout = 10 with get, set

type StopContainerCommand(output: IOutputPort) =
    inherit AsyncCommand<StopSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) =
        task {
            use client = new ContainerClient()
            let! response = client.StopAsync(settings.Id, settings.Timeout)
            output.WriteSuccess(sprintf "Conteneur %s arrêté (%s)" settings.Id (response.State.ToString()))
            return 0
        }

// ── delete ────────────────────────────────────────────────────────
type DeleteSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<ID>")>] member val Id: string = null with get, set
    [<CommandOption("-f|--force")>] member val Force = false with get, set

type DeleteContainerCommand(output: IOutputPort) =
    inherit AsyncCommand<DeleteSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) =
        task {
            use client = new ContainerClient()
            let! response = client.DeleteAsync(settings.Id, settings.Force)
            if response.Success then
                output.WriteSuccess(response.Message)
            else
                output.WriteError(response.Message)
            return 0
        }

// ── pull ──────────────────────────────────────────────────────────
type PullSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<IMAGE>")>] member val Image: string = null with get, set

type PullImageCommand(output: IOutputPort) =
    inherit AsyncCommand<PullSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) =
        task {
            use client = new ContainerClient()
            let! response = client.PullImageAsync(settings.Image)
            output.WriteSuccess(response.Message)
            return 0
        }

// ── version ───────────────────────────────────────────────────────
type VersionCommand(output: IOutputPort) =
    inherit AsyncCommand<CommandSettings>()

    override _.ExecuteAsync(_ctx, _settings, _ct) =
        task {
            use client = new ContainerClient()
            let! v = client.GetVersionAsync()
            output.WriteSuccess("Diplo")
            output.WriteLine(sprintf "  Version   : %s" v.Version)
            output.WriteLine(sprintf "  Révision  : %s" v.Revision)
            output.WriteLine(sprintf "  Go        : %s" v.GoVersion)
            output.WriteLine(sprintf "  OS        : %s" v.Os)
            output.WriteLine(sprintf "  Arch      : %s" v.Arch)
            return 0
        }
