namespace Diplo.Cli.Container

open System
open System.Collections.Generic
open System.Runtime.ExceptionServices
open System.Threading
open System.Threading.Tasks
open Diplo.Abstractions
open Diplo.Cli
open Diplo.Core
open Diplo.Core.Clients
open Diplo.Core.Mounts
open Diplo.Core.Output
open Spectre.Console.Cli
open Spectre.Console

// ── list ──────────────────────────────────────────────────────────
type ListSettings() =
    inherit CommandSettings()

    [<CommandOption("-a|--all")>]
    member val All = false with get, set

    [<CommandOption("--namespace")>]
    member val Namespace: string = null with get, set

type ListCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<ListSettings>()
    new(output: IOutputPort) = ListCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            use client = clients.CreateContainerClient()
            let ns = if isNull settings.Namespace then "" else settings.Namespace
            let! response = client.ListAsync(all = settings.All, ct = CancellationToken.None)

            if response.Containers.Count = 0 then
                output.WriteWarning("Aucun conteneur trouvé.")
            else
                output.WriteTable(
                    response.Containers,
                    [| "ID"; "Nom"; "Image"; "État"; "Créé" |],
                    fun c -> [| c.Id; c.Name; c.Image; c.State.ToString(); c.CreatedAt |]
                )

            return 0
        }

// ── inspect ───────────────────────────────────────────────────────
type InspectContainerSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

type InspectContainerCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<InspectContainerSettings>()
    new(output: IOutputPort) = InspectContainerCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            else
                use client = clients.CreateContainerClient()
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

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

    /// Démarre en attaché à la console (sinon non interactif, en arrière-plan).
    [<CommandOption("-a|--attach")>]
    member val Attach = false with get, set

type StartContainerCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<StartSettings>()
    new(output: IOutputPort) = StartContainerCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            else
                use client = clients.CreateContainerClient()
                let! response = client.StartAsync(settings.Id, attach = settings.Attach)
                output.WriteSuccess(sprintf "Conteneur %s démarré (%s)" settings.Id (response.State.ToString()))
                return 0
        }

// ── stop ──────────────────────────────────────────────────────────
type StopSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

    [<CommandOption("-t|--timeout")>]
    member val Timeout = 10 with get, set

type StopContainerCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<StopSettings>()
    new(output: IOutputPort) = StopContainerCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            else
                use client = clients.CreateContainerClient()
                let! response = client.StopAsync(settings.Id, settings.Timeout)
                output.WriteSuccess(sprintf "Conteneur %s arrêté (%s)" settings.Id (response.State.ToString()))
                return 0
        }

// ── delete ────────────────────────────────────────────────────────
type DeleteSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

    [<CommandOption("-f|--force")>]
    member val Force = false with get, set

type DeleteContainerCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<DeleteSettings>()
    new(output: IOutputPort) = DeleteContainerCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            else
                use client = clients.CreateContainerClient()
                let! response = client.DeleteAsync(settings.Id, settings.Force)

                if response.Success then
                    output.WriteSuccess(response.Message)
                    return 0
                else
                    output.WriteError(response.Message)
                    return 1
        }

// ── pull ──────────────────────────────────────────────────────────
type PullSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<IMAGE>")>]
    member val Image: string = null with get, set

    [<CommandOption("--user")>]
    member val User: string = null with get, set

type PullImageCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<PullSettings>()
    new(output: IOutputPort) = PullImageCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Image) then
                output.WriteError("L'image est requise")
                return 1
            else
                use client = clients.CreateContainerClient()

                let! response =
                    client.PullImageAsync(
                        settings.Image,
                        ?user =
                            (if String.IsNullOrWhiteSpace(settings.User) then
                                 None
                             else
                                 Some settings.User)
                    )

                output.WriteSuccess(response.Message)
                return 0
        }

// ── login / logout (registres) ────────────────────────────────────
type RegistryLoginSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<REGISTRY>")>]
    member val Registry: string = null with get, set

    [<CommandOption("--username")>]
    member val Username: string = null with get, set

    [<CommandOption("--password")>]
    member val Password: string = null with get, set

type RegistryLoginCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<RegistryLoginSettings>()
    new(output: IOutputPort) = RegistryLoginCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Registry) then
                output.WriteError("Le registre est requis (ex. myregistry.azurecr.io)")
                return 1
            elif String.IsNullOrWhiteSpace(settings.Username) then
                output.WriteError("Le nom d'utilisateur est requis (--username)")
                return 1
            else
                let password =
                    if String.IsNullOrWhiteSpace(settings.Password) then
                        AnsiConsole.Prompt(TextPrompt<string>("Mot de passe :").Secret())
                    else
                        settings.Password

                use client = clients.CreateContainerClient()
                let! response = client.LoginRegistryAsync(settings.Registry, settings.Username, password)

                if response.Success then
                    output.WriteSuccess(response.Message)
                    return 0
                else
                    output.WriteError(response.Message)
                    return 1
        }

type RegistryLogoutSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<REGISTRY>")>]
    member val Registry: string = null with get, set

type RegistryLogoutCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<RegistryLogoutSettings>()
    new(output: IOutputPort) = RegistryLogoutCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Registry) then
                output.WriteError("Le registre est requis (ex. myregistry.azurecr.io)")
                return 1
            else
                use client = clients.CreateContainerClient()
                let! response = client.LogoutRegistryAsync(settings.Registry)

                if response.Success then
                    output.WriteSuccess(response.Message)
                    return 0
                else
                    output.WriteError(response.Message)
                    return 1
        }

// ── create ────────────────────────────────────────────────────────

type CreateContainerSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<IMAGE>")>]
    member val Image: string = null with get, set

    [<CommandArgument(1, "<NAME>")>]
    member val Name: string = null with get, set

    [<CommandOption("--env")>]
    member val Env: string[] = [||] with get, set

    [<CommandOption("--command")>]
    member val Command: string[] = [||] with get, set

    [<CommandOption("-c|--cmd")>]
    member val CommandLine: string = null with get, set

    [<CommandOption("--label")>]
    member val Labels: string[] = [||] with get, set

    [<CommandOption("--mount")>]
    member val Mounts: string[] = [||] with get, set

    [<CommandOption("--pid-limit")>]
    member val PidLimit = 0u with get, set

    [<CommandOption("--memory-limit")>]
    member val MemoryLimit = 0L with get, set

    [<CommandOption("--cpu-shares")>]
    member val CpuShares = 0L with get, set

type CreateContainerCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<CreateContainerSettings>()
    new(output: IOutputPort) = CreateContainerCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Name) then
                output.WriteError("Le nom du conteneur est requis")
                return 1
            elif String.IsNullOrWhiteSpace(settings.Image) then
                output.WriteError("L'image est requise")
                return 1
            else
                use client = clients.CreateContainerClient()

                // Signaler les entrées malformées : « --env FOO » sans '=' est
                // sinon ignoré en silence et la variable n'atteint jamais le
                // conteneur.
                let envPairs, envRejected =
                    settings.Env
                    |> Array.partition (fun e -> e.Contains('='))

                let labelsPairs, labelsRejected =
                    settings.Labels
                    |> Array.partition (fun l -> l.Contains('='))

                if not (Array.isEmpty envRejected) then
                    output.WriteWarning(
                        sprintf "Entrées --env ignorées (format attendu CLE=valeur) : %s" (String.Join(", ", envRejected))
                    )

                if not (Array.isEmpty labelsRejected) then
                    output.WriteWarning(
                        sprintf
                            "Entrées --label ignorées (format attendu cle=valeur) : %s"
                            (String.Join(", ", labelsRejected))
                    )

                let env =
                    envPairs
                    |> Array.choose (fun e ->
                        match e.Split('=', 2) with
                        | [| k; v |] -> Some(k, v)
                        | _ -> None)
                    |> dict

                let labels =
                    labelsPairs
                    |> Array.choose (fun l ->
                        match l.Split('=', 2) with
                        | [| k; v |] -> Some(k, v)
                        | _ -> None)
                    |> dict

                let command =
                    if not (String.IsNullOrWhiteSpace settings.CommandLine) then
                        CommandLine.split settings.CommandLine
                    else
                        settings.Command |> Array.toList

                let mounts = MountParser.parseArray settings.Mounts

                let! response =
                    client.CreateAsync(
                        name = settings.Name,
                        image = settings.Image,
                        ?env = (if env.Count > 0 then Some env else None),
                        ?command = (if command.IsEmpty then None else Some command),
                        ?labels = (if labels.Count > 0 then Some labels else None),
                        ?pidLimit =
                            (if settings.PidLimit > 0u then
                                 Some(int settings.PidLimit)
                             else
                                 None),
                        ?memoryLimit =
                            (if settings.MemoryLimit > 0L then
                                 Some settings.MemoryLimit
                             else
                                 None),
                        ?cpuShares =
                            (if settings.CpuShares > 0L then
                                 Some(int settings.CpuShares)
                             else
                                 None),
                        ?mounts = (if mounts.IsEmpty then None else Some mounts)
                    )

                output.WriteSuccess(sprintf "Conteneur %s créé (%s)" response.Name (response.State.ToString()))
                output.WriteLine(sprintf "  ID      : %s" response.Id)
                output.WriteLine(sprintf "  Créé    : %s" response.CreatedAt)
                return 0
        }

// ── logs ──────────────────────────────────────────────────────────
type LogsContainerSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

    [<CommandOption("-f|--follow")>]
    member val Follow = false with get, set

    [<CommandOption("-n|--tail")>]
    member val Tail = 100 with get, set

    [<CommandOption("--since")>]
    member val Since: string = null with get, set

type LogsContainerCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<LogsContainerSettings>()
    new(output: IOutputPort) = LogsContainerCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            else
                use client = clients.CreateContainerClient()
                let since = if isNull settings.Since then "" else settings.Since

                try
                    if settings.Follow then
                        use ctrlC = new CtrlCHandler()
                        use linked = CancellationTokenSource.CreateLinkedTokenSource(ct, ctrlC.Token)
                        let followCt = linked.Token

                        let stream =
                            client.GetLogsStream(
                                settings.Id,
                                follow = true,
                                tail = settings.Tail,
                                since = since,
                                ct = followCt
                            )

                        let enumerator = stream.GetAsyncEnumerator(followCt)
                        let mutable failure: System.Exception option = None

                        try
                            let mutable moving = true

                            while moving do
                                let! hasNext = enumerator.MoveNextAsync().AsTask()

                                if hasNext then
                                    output.WriteLine(
                                        sprintf "[%s] %s" enumerator.Current.Timestamp enumerator.Current.Log
                                    )
                                else
                                    moving <- false
                        with ex ->
                            failure <- Some ex

                        // Disposition attendue : l'abandon fire-and-forget laissait
                        // le canal gRPC se fermer pendant le dispose du flux.
                        do! enumerator.DisposeAsync().AsTask()

                        match failure with
                        | Some ex -> ExceptionDispatchInfo.Capture(ex).Throw()
                        | None -> ()
                    else
                        let! entries = client.GetLogs(settings.Id, tail = settings.Tail, since = since, ct = ct)

                        for entry in entries do
                            output.WriteLine(sprintf "[%s] %s" entry.Timestamp entry.Log)

                    return 0
                with
                | :? Grpc.Core.RpcException as rex when rex.StatusCode = Grpc.Core.StatusCode.Cancelled -> return 0
                | :? OperationCanceledException -> return 0
                | ex ->
                    output.WriteError(ex.Message)
                    return 1
        }

// ── exec ──────────────────────────────────────────────────────────
type ExecContainerSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

    [<CommandArgument(1, "<COMMAND>")>]
    member val Command: string[] = [||] with get, set

type ExecContainerCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<ExecContainerSettings>()
    new(output: IOutputPort) = ExecContainerCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            elif settings.Command.Length = 0 then
                output.WriteError("Au moins une commande est requise")
                return 1
            else
                use client = clients.CreateContainerClient()
                let! entries = client.Exec(settings.Id, settings.Command :> seq<string>)

                for entry in entries do
                    output.WriteLine(System.Text.Encoding.UTF8.GetString(entry.Data))

                return 0
        }

// ── namespaces ────────────────────────────────────────────────────
type NamespacesSettings() =
    inherit CommandSettings()

type NamespacesCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<NamespacesSettings>()
    new(output: IOutputPort) = NamespacesCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, _settings, _ct) : Task<int> =
        task {
            use client = clients.CreateContainerClient()
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
type VersionSettings() =
    inherit CommandSettings()

type VersionCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<VersionSettings>()
    new(output: IOutputPort) = VersionCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, _settings, _ct) : Task<int> =
        task {
            use client = clients.CreateContainerClient()
            let! v = client.GetVersionAsync()
            output.WriteSuccess("Diplo")
            output.WriteLine(sprintf "  Version   : %s" v.Version)
            output.WriteLine(sprintf "  Révision  : %s" v.Revision)
            output.WriteLine(sprintf "  Go        : %s" v.GoVersion)
            output.WriteLine(sprintf "  OS        : %s" v.Os)
            output.WriteLine(sprintf "  Arch      : %s" v.Arch)
            output.WriteLine("")
            output.WriteLine("  Licences des dépendances :")

            output.WriteLine(
                "    MIT (22)              : Avalonia, FSharp.Core, DiscUtils, Microsoft, Spectre, YamlDotNet, ZstdSharp"
            )

            output.WriteLine("    Apache-2.0 (13)       : gRPC, protobuf-net, Serilog, xunit")
            output.WriteLine("    BSD-3-Clause (1)      : Google.Protobuf")
            output.WriteLine("    LGPL-3.0+ (1)         : Hawkynt.FileFormats.FileSystems")
            output.WriteLine("  Voir THIRD-PARTY-NOTICES.txt pour le texte intégral.")
            return 0
        }

// ── rename ────────────────────────────────────────────────────────
type RenameContainerSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

    [<CommandArgument(1, "<NEW_NAME>")>]
    member val NewName: string = null with get, set

type RenameContainerCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<RenameContainerSettings>()
    new(output: IOutputPort) = RenameContainerCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            elif String.IsNullOrWhiteSpace(settings.NewName) then
                output.WriteError("Le nouveau nom est requis")
                return 1
            else
                use client = clients.CreateContainerClient()
                let! response = client.RenameContainerAsync(settings.Id, settings.NewName)

                if response.Success then
                    output.WriteSuccess(response.Message)
                    return 0
                else
                    output.WriteError(response.Message)
                    return 1
        }

// ── top ───────────────────────────────────────────────────────────
type TopContainerSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

type TopContainerCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<TopContainerSettings>()
    new(output: IOutputPort) = TopContainerCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            else
                use client = clients.CreateContainerClient()
                let! response = client.TopContainerAsync(settings.Id)

                if response.Processes.Count = 0 then
                    output.WriteWarning("Aucun processus trouvé dans le conteneur.")
                else
                    output.WriteTable(
                        response.Processes,
                        [| "PID"; "Utilisateur"; "Commande" |],
                        fun p -> [| string p.Pid; p.User; p.Command |]
                    )

                return 0
        }

// ── stats ─────────────────────────────────────────────────────────
type StatsContainerSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

type StatsContainerCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<StatsContainerSettings>()
    new(output: IOutputPort) = StatsContainerCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            else
                use client = clients.CreateContainerClient()
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

    [<CommandOption("--namespace")>]
    member val Namespace: string = null with get, set

type ImageListCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<ImageListSettings>()
    new(output: IOutputPort) = ImageListCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            use client = clients.CreateContainerClient()
            let ns = if isNull settings.Namespace then "" else settings.Namespace
            let! response = client.ListImagesAsync(ns)

            if response.Images.Count = 0 then
                output.WriteWarning("Aucune image trouvée.")
            else
                output.WriteTable(
                    response.Images,
                    [| "ID"; "Référentiel"; "Tag"; "Taille"; "Créé" |],
                    fun img -> [| img.Id; img.Repository; img.Tag; string img.Size; img.CreatedAt |]
                )

            return 0
        }

// ── image inspect ─────────────────────────────────────────────────
type ImageInspectSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<REF>")>]
    member val Ref: string = null with get, set

    [<CommandOption("--namespace")>]
    member val Namespace: string = null with get, set

type ImageInspectCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<ImageInspectSettings>()
    new(output: IOutputPort) = ImageInspectCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Ref) then
                output.WriteError("La référence de l'image est requise")
                return 1
            else
                use client = clients.CreateContainerClient()
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

    [<CommandArgument(0, "<REF>")>]
    member val Ref: string = null with get, set

    [<CommandOption("--namespace")>]
    member val Namespace: string = null with get, set

type ImageRemoveCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<ImageRemoveSettings>()
    new(output: IOutputPort) = ImageRemoveCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Ref) then
                output.WriteError("La référence de l'image est requise")
                return 1
            else
                use client = clients.CreateContainerClient()
                let ns = if isNull settings.Namespace then "" else settings.Namespace
                let! response = client.RemoveImageAsync(settings.Ref, ns)

                if response.Success then
                    output.WriteSuccess(response.Message)
                    return 0
                else
                    output.WriteError(response.Message)
                    return 1
        }

// ── image tag ─────────────────────────────────────────────────────
type ImageTagSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<SOURCE>")>]
    member val Source: string = null with get, set

    [<CommandArgument(1, "<TARGET>")>]
    member val Target: string = null with get, set

    [<CommandOption("--namespace")>]
    member val Namespace: string = null with get, set

type ImageTagCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<ImageTagSettings>()
    new(output: IOutputPort) = ImageTagCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Source) then
                output.WriteError("La référence source est requise")
                return 1
            elif String.IsNullOrWhiteSpace(settings.Target) then
                output.WriteError("La référence cible est requise")
                return 1
            else
                use client = clients.CreateContainerClient()
                let ns = if isNull settings.Namespace then "" else settings.Namespace
                let! response = client.TagImageAsync(settings.Source, settings.Target, ns)
                output.WriteSuccess(response.Message)
                return 0
        }
