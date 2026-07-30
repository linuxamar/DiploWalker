namespace Diplo.Cli.Container

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
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

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
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

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.Id) then
                output.WriteError("L'identifiant du conteneur est requis")
                return 1
            else
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

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.Id) then
                output.WriteError("L'identifiant du conteneur est requis")
                return 1
            else
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

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.Id) then
                output.WriteError("L'identifiant du conteneur est requis")
                return 1
            else
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

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.Id) then
                output.WriteError("L'identifiant du conteneur est requis")
                return 1
            else
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

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.Image) then
                output.WriteError("L'image est requise")
                return 1
            else
                use client = new ContainerClient()
                let! response = client.PullImageAsync(settings.Image)
                output.WriteSuccess(response.Message)
                return 0
        }

// ── create ────────────────────────────────────────────────────────
type CreateContainerSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<IMAGE>")>] member val Image: string = null with get, set
    [<CommandArgument(1, "<NAME>")>] member val Name: string = null with get, set
    [<CommandOption("--env")>] member val Env: string[] = [||] with get, set
    [<CommandOption("--command")>] member val Command: string[] = [||] with get, set
    [<CommandOption("--label")>] member val Labels: string[] = [||] with get, set
    [<CommandOption("--pid-limit")>] member val PidLimit = 0u with get, set
    [<CommandOption("--memory-limit")>] member val MemoryLimit = 0L with get, set
    [<CommandOption("--cpu-shares")>] member val CpuShares = 0L with get, set

type CreateContainerCommand(output: IOutputPort) =
    inherit AsyncCommand<CreateContainerSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.Name) then
                output.WriteError("Le nom du conteneur est requis")
                return 1
            elif String.IsNullOrEmpty(settings.Image) then
                output.WriteError("L'image est requise")
                return 1
            else
                use client = new ContainerClient()
                let env =
                    settings.Env
                    |> Array.choose (fun e ->
                        match e.Split('=', 2) with
                        | [| k; v |] -> Some (k, v)
                        | _ -> None)
                    |> dict
                let labels =
                    settings.Labels
                    |> Array.choose (fun l ->
                        match l.Split('=', 2) with
                        | [| k; v |] -> Some (k, v)
                        | _ -> None)
                    |> dict
                let command = settings.Command |> Array.toList
                let! response =
                    client.CreateAsync(
                        name = settings.Name,
                        image = settings.Image,
                        ?env = (if env.Count > 0 then Some env else None),
                        ?command = (if command.IsEmpty then None else Some command),
                        ?labels = (if labels.Count > 0 then Some labels else None),
                        ?pidLimit = (if settings.PidLimit > 0u then Some(int settings.PidLimit) else None),
                        ?memoryLimit = (if settings.MemoryLimit > 0L then Some settings.MemoryLimit else None),
                        ?cpuShares = (if settings.CpuShares > 0L then Some(int settings.CpuShares) else None))
                output.WriteSuccess(sprintf "Conteneur %s créé (%s)" response.Name (response.State.ToString()))
                output.WriteLine(sprintf "  ID      : %s" response.Id)
                output.WriteLine(sprintf "  Créé    : %s" response.CreatedAt)
                return 0
        }

// ── logs ──────────────────────────────────────────────────────────
type LogsContainerSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<ID>")>] member val Id: string = null with get, set
    [<CommandOption("-f|--follow")>] member val Follow = false with get, set
    [<CommandOption("-n|--tail")>] member val Tail = 100 with get, set
    [<CommandOption("--since")>] member val Since: string = null with get, set

type LogsContainerCommand(output: IOutputPort) =
    inherit AsyncCommand<LogsContainerSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.Id) then
                output.WriteError("L'identifiant du conteneur est requis")
                return 1
            else
                use client = new ContainerClient()
                let since = if isNull settings.Since then "" else settings.Since
                let! entries = client.GetLogs(settings.Id, follow = settings.Follow, tail = settings.Tail, since = since)
                for entry in entries do
                    output.WriteLine(sprintf "[%s] %s" entry.Timestamp entry.Log)
                return 0
        }

// ── exec ──────────────────────────────────────────────────────────
type ExecContainerSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<ID>")>] member val Id: string = null with get, set
    [<CommandArgument(1, "<COMMAND>")>] member val Command: string[] = [||] with get, set

type ExecContainerCommand(output: IOutputPort) =
    inherit AsyncCommand<ExecContainerSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.Id) then
                output.WriteError("L'identifiant du conteneur est requis")
                return 1
            elif settings.Command.Length = 0 then
                output.WriteError("Au moins une commande est requise")
                return 1
            else
                use client = new ContainerClient()
                let! entries = client.Exec(settings.Id, settings.Command :> seq<string>)
                for entry in entries do
                    output.WriteLine(System.Text.Encoding.UTF8.GetString(entry.Data))
                return 0
        }

// ── namespaces ────────────────────────────────────────────────────
type NamespacesCommand(output: IOutputPort) =
    inherit AsyncCommand<CommandSettings>()

    override _.ExecuteAsync(_ctx, _settings, _ct) : Task<int> =
        task {
            use client = new ContainerClient()
            let! response = client.ListNamespacesAsync()
            if response.Namespaces.Count = 0 then
                output.WriteWarning("Aucun namespace trouvé.")
            else
                output.WriteSuccess("Namespaces disponibles :")
                for ns in response.Namespaces do
                    output.WriteLine(sprintf "  - %s" ns)
            return 0
        }

// ── version ───────────────────────────────────────────────────────
type VersionCommand(output: IOutputPort) =
    inherit AsyncCommand<CommandSettings>()

    override _.ExecuteAsync(_ctx, _settings, _ct) : Task<int> =
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

// ── rename ────────────────────────────────────────────────────────
type RenameContainerSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<ID>")>] member val Id: string = null with get, set
    [<CommandArgument(1, "<NEW_NAME>")>] member val NewName: string = null with get, set

type RenameContainerCommand(output: IOutputPort) =
    inherit AsyncCommand<RenameContainerSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.Id) then
                output.WriteError("L'identifiant du conteneur est requis")
                return 1
            elif String.IsNullOrEmpty(settings.NewName) then
                output.WriteError("Le nouveau nom est requis")
                return 1
            else
                use client = new ContainerClient()
                let! response = client.RenameContainerAsync(settings.Id, settings.NewName)
                if response.Success then
                    output.WriteSuccess(response.Message)
                else
                    output.WriteError(response.Message)
                return 0
        }

// ── top ───────────────────────────────────────────────────────────
type TopContainerSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<ID>")>] member val Id: string = null with get, set

type TopContainerCommand(output: IOutputPort) =
    inherit AsyncCommand<TopContainerSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.Id) then
                output.WriteError("L'identifiant du conteneur est requis")
                return 1
            else
                use client = new ContainerClient()
                let! response = client.TopContainerAsync(settings.Id)
                if response.Processes.Count = 0 then
                    output.WriteWarning("Aucun processus trouvé dans le conteneur.")
                else
                    output.WriteTable(
                        response.Processes,
                        [| "PID"; "Utilisateur"; "Commande" |],
                        fun p ->
                            [| string p.Pid
                               p.User
                               p.Command |])
                return 0
        }

// ── stats ─────────────────────────────────────────────────────────
type StatsContainerSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<ID>")>] member val Id: string = null with get, set

type StatsContainerCommand(output: IOutputPort) =
    inherit AsyncCommand<StatsContainerSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.Id) then
                output.WriteError("L'identifiant du conteneur est requis")
                return 1
            else
                use client = new ContainerClient()
                let! response = client.GetContainerStatsAsync(settings.Id)
                output.WriteSuccess(sprintf "Métriques du conteneur %s" settings.Id)
                output.WriteLine(sprintf "  CPU       : %.2f" response.CpuUsage)
                output.WriteLine(sprintf "  Mémoire   : %d / %d octets" response.MemoryUsage response.MemoryLimit)
                output.WriteLine(sprintf "  Réseau rx : %d octets" response.NetworkRx)
                output.WriteLine(sprintf "  Réseau tx : %d octets" response.NetworkTx)
                output.WriteLine(sprintf "  Disque r  : %d octets" response.DiskRead)
                output.WriteLine(sprintf "  Disque w  : %d octets" response.DiskWrite)
                output.WriteLine(sprintf "  PIDs      : %d" response.Pids)
                return 0
        }

// ── image list ────────────────────────────────────────────────────
type ImageListSettings() =
    inherit CommandSettings()
    [<CommandOption("--namespace")>] member val Namespace: string = null with get, set

type ImageListCommand(output: IOutputPort) =
    inherit AsyncCommand<ImageListSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            use client = new ContainerClient()
            let ns = if isNull settings.Namespace then "" else settings.Namespace
            let! response = client.ListImagesAsync(ns)
            if response.Images.Count = 0 then
                output.WriteWarning("Aucune image trouvée.")
            else
                output.WriteTable(
                    response.Images,
                    [| "ID"; "Référentiel"; "Tag"; "Taille"; "Créé" |],
                    fun img ->
                        [| img.Id
                           img.Repository
                           img.Tag
                           string img.Size
                           img.CreatedAt |])
            return 0
        }

// ── image inspect ─────────────────────────────────────────────────
type ImageInspectSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<REF>")>] member val Ref: string = null with get, set
    [<CommandOption("--namespace")>] member val Namespace: string = null with get, set

type ImageInspectCommand(output: IOutputPort) =
    inherit AsyncCommand<ImageInspectSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.Ref) then
                output.WriteError("La référence de l'image est requise")
                return 1
            else
                use client = new ContainerClient()
                let ns = if isNull settings.Namespace then "" else settings.Namespace
                let! response = client.InspectImageAsync(settings.Ref, ns)
                output.WriteSuccess(sprintf "Image %s" response.Ref)
                output.WriteLine(sprintf "  ID           : %s" response.Id)
                output.WriteLine(sprintf "  Référentiel : %s" response.Repository)
                output.WriteLine(sprintf "  Tag          : %s" response.Tag)
                output.WriteLine(sprintf "  Taille       : %d octets" response.Size)
                output.WriteLine(sprintf "  Créé         : %s" response.CreatedAt)
                if response.Labels.Count > 0 then
                    for kv in response.Labels do
                        output.WriteLine(sprintf "  Label        : %s=%s" kv.Key kv.Value)
                return 0
        }

// ── image remove ──────────────────────────────────────────────────
type ImageRemoveSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<REF>")>] member val Ref: string = null with get, set
    [<CommandOption("--namespace")>] member val Namespace: string = null with get, set

type ImageRemoveCommand(output: IOutputPort) =
    inherit AsyncCommand<ImageRemoveSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.Ref) then
                output.WriteError("La référence de l'image est requise")
                return 1
            else
                use client = new ContainerClient()
                let ns = if isNull settings.Namespace then "" else settings.Namespace
                let! response = client.RemoveImageAsync(settings.Ref, ns)
                if response.Success then
                    output.WriteSuccess(response.Message)
                else
                    output.WriteError(response.Message)
                return 0
        }

// ── image tag ─────────────────────────────────────────────────────
type ImageTagSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<SOURCE>")>] member val Source: string = null with get, set
    [<CommandArgument(1, "<TARGET>")>] member val Target: string = null with get, set
    [<CommandOption("--namespace")>] member val Namespace: string = null with get, set

type ImageTagCommand(output: IOutputPort) =
    inherit AsyncCommand<ImageTagSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.Source) then
                output.WriteError("La référence source est requise")
                return 1
            elif String.IsNullOrEmpty(settings.Target) then
                output.WriteError("La référence cible est requise")
                return 1
            else
                use client = new ContainerClient()
                let ns = if isNull settings.Namespace then "" else settings.Namespace
                let! response = client.TagImageAsync(settings.Source, settings.Target, ns)
                output.WriteSuccess(response.Message)
                return 0
        }
