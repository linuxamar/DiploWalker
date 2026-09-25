namespace DiploWalker.Cli.Container

open System
open System.Collections.Generic
open System.IO
open System.Runtime.ExceptionServices
open System.Threading
open System.Threading.Tasks
open DiploWalker.Abstractions
open DiploWalker.Cli
open DiploWalker.Core
open DiploWalker.Core.Clients
open DiploWalker.Core.Mounts
open DiploWalker.Core.Output
open DiploWalker.Grpc.Container
open Spectre.Console.Cli
open Spectre.Console

// â”€â”€ list â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type ListSettings() =
    inherit CommandSettings()

    [<CommandOption("-a|--all")>]
    member val All = false with get, set

    [<CommandOption("--namespace")>]
    member val Namespace: string = null with get, set

type ListCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<ListSettings>()
    new(output: IOutputPort) = ListCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            use client = clients.CreateContainerClient()
            let ns = if isNull settings.Namespace then "" else settings.Namespace
            let! response = client.ListAsync(namespaceName = ns, all = settings.All, ct = ct)

            if response.Containers.Count = 0 then
                output.WriteWarning("Aucun conteneur trouvÃ©.")
            else
                output.WriteTable(
                    response.Containers,
                    [| "ID"; "Nom"; "Image"; "Ã‰tat"; "CrÃ©Ã©" |],
                    fun c -> [| c.Id; c.Name; c.Image; c.State.ToString(); c.CreatedAt |]
                )

            return 0
        }

// â”€â”€ inspect â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type InspectContainerSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

type InspectContainerCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<InspectContainerSettings>()
    new(output: IOutputPort) = InspectContainerCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            else
                use client = clients.CreateContainerClient()
                let! response = client.InspectAsync(settings.Id, ct = ct)

                output.WriteSuccess(sprintf "Conteneur %s" response.Name)
                output.WriteLine(sprintf "  ID        : %s" response.Id)
                output.WriteLine(sprintf "  Image     : %s" response.Image)
                output.WriteLine(sprintf "  Ã‰tat      : %s" (response.State.ToString()))
                output.WriteLine(sprintf "  CrÃ©Ã©      : %s" response.CreatedAt)
                output.WriteLine(sprintf "  DÃ©marrÃ©   : %s" response.StartedAt)
                output.WriteLine(sprintf "  ArrÃªtÃ©    : %s" response.FinishedAt)
                output.WriteLine(sprintf "  PID       : %d" response.Pid)
                output.WriteLine(sprintf "  Exit code : %d" response.ExitCode)
                return 0
        }

// â”€â”€ start â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type StartSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

    /// DÃ©marre en attachÃ© Ã  la console (sinon non interactif, en arriÃ¨re-plan).
    [<CommandOption("-a|--attach")>]
    member val Attach = false with get, set

type StartContainerCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<StartSettings>()
    new(output: IOutputPort) = StartContainerCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            else
                use client = clients.CreateContainerClient()
                let! response = client.StartAsync(settings.Id, attach = settings.Attach, ct = ct)
                output.WriteSuccess(sprintf "Conteneur %s dÃ©marrÃ© (%s)" settings.Id (response.State.ToString()))
                return 0
        }

// â”€â”€ stop â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type StopSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

    [<CommandOption("-t|--timeout")>]
    member val Timeout = 10 with get, set

type StopContainerCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<StopSettings>()
    new(output: IOutputPort) = StopContainerCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            else
                use client = clients.CreateContainerClient()
                let! response = client.StopAsync(settings.Id, settings.Timeout, ct = ct)
                output.WriteSuccess(sprintf "Conteneur %s arrÃªtÃ© (%s)" settings.Id (response.State.ToString()))
                return 0
        }

// â”€â”€ delete â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type DeleteSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

    [<CommandOption("-f|--force")>]
    member val Force = false with get, set

type DeleteContainerCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<DeleteSettings>()
    new(output: IOutputPort) = DeleteContainerCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            else
                use client = clients.CreateContainerClient()
                let! response = client.DeleteAsync(settings.Id, settings.Force, ct = ct)

                if response.Success then
                    output.WriteSuccess(response.Message)
                    return 0
                else
                    output.WriteError(response.Message)
                    return 1
        }

// â”€â”€ pull â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type PullSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<IMAGE>")>]
    member val Image: string = null with get, set

    [<CommandOption("--user")>]
    member val User: string = null with get, set

type PullImageCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<PullSettings>()
    new(output: IOutputPort) = PullImageCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Image) then
                output.WriteError("L'image est requise")
                return 1
            else
                use client = clients.CreateContainerClient()

                try
                    let! response =
                        client.PullImageAsync(
                            settings.Image,
                            ?user =
                                (if String.IsNullOrWhiteSpace(settings.User) then
                                     None
                                 else
                                     Some settings.User),
                            ct = ct
                        )

                    output.WriteSuccess(response.Message)
                    return 0
                with :? Grpc.Core.RpcException as rex ->
                    output.WriteError(rex.Status.Detail)
                    return 1
        }

// â”€â”€ login / logout (registres) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
    new(output: IOutputPort) = RegistryLoginCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Registry) then
                output.WriteError("Le registre est requis (ex. ghcr.io, docker.io, quay.io, mcr.microsoft.com)")
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

                try
                    let! response = client.LoginRegistryAsync(settings.Registry, settings.Username, password, ct = ct)

                    if response.Success then
                        output.WriteSuccess(response.Message)
                        return 0
                    else
                        output.WriteError(response.Message)
                        return 1
                with :? Grpc.Core.RpcException as rex ->
                    output.WriteError(rex.Status.Detail)
                    return 1
        }

type RegistryLogoutSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<REGISTRY>")>]
    member val Registry: string = null with get, set

type RegistryLogoutCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<RegistryLogoutSettings>()
    new(output: IOutputPort) = RegistryLogoutCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Registry) then
                output.WriteError("Le registre est requis (ex. ghcr.io, docker.io, quay.io, mcr.microsoft.com)")
                return 1
            else
                use client = clients.CreateContainerClient()

                try
                    let! response = client.LogoutRegistryAsync(settings.Registry, ct = ct)

                    if response.Success then
                        output.WriteSuccess(response.Message)
                        return 0
                    else
                        output.WriteError(response.Message)
                        return 1
                with :? Grpc.Core.RpcException as rex ->
                    output.WriteError(rex.Status.Detail)
                    return 1
        }

// â”€â”€ create â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
    new(output: IOutputPort) = CreateContainerCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Name) then
                output.WriteError("Le nom du conteneur est requis")
                return 1
            elif String.IsNullOrWhiteSpace(settings.Image) then
                output.WriteError("L'image est requise")
                return 1
            else
                use client = clients.CreateContainerClient()

                // Signaler les entrÃ©es malformÃ©es : Â« --env FOO Â» sans '=' est
                // sinon ignorÃ© en silence et la variable n'atteint jamais le
                // conteneur.
                let envPairs, envRejected =
                    settings.Env
                    |> Array.partition (fun e -> e.Contains('='))

                let labelsPairs, labelsRejected =
                    settings.Labels
                    |> Array.partition (fun l -> l.Contains('='))

                if not (Array.isEmpty envRejected) then
                    output.WriteWarning(
                        sprintf "EntrÃ©es --env ignorÃ©es (format attendu CLE=valeur) : %s" (String.Join(", ", envRejected))
                    )

                if not (Array.isEmpty labelsRejected) then
                    output.WriteWarning(
                        sprintf
                            "EntrÃ©es --label ignorÃ©es (format attendu cle=valeur) : %s"
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
                        ?mounts = (if mounts.IsEmpty then None else Some mounts),
                        ct = ct
                    )

                output.WriteSuccess(sprintf "Conteneur %s crÃ©Ã© (%s)" response.Name (response.State.ToString()))
                output.WriteLine(sprintf "  ID      : %s" response.Id)
                output.WriteLine(sprintf "  CrÃ©Ã©    : %s" response.CreatedAt)
                return 0
        }

// â”€â”€ logs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
    new(output: IOutputPort) = LogsContainerCommand(output, DiploWalkerClients())

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

// â”€â”€ exec â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type ExecContainerSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

    [<CommandArgument(1, "<COMMAND>")>]
    member val Command: string[] = [||] with get, set

type ExecContainerCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<ExecContainerSettings>()
    new(output: IOutputPort) = ExecContainerCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            elif settings.Command.Length = 0 then
                output.WriteError("Au moins une commande est requise")
                return 1
            else
                use client = clients.CreateContainerClient()
                let! entries = client.Exec(settings.Id, settings.Command :> seq<string>, ct = ct)

                for entry in entries do
                    output.WriteLine(System.Text.Encoding.UTF8.GetString(entry.Data))

                return 0
        }

// â”€â”€ namespaces â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type NamespacesSettings() =
    inherit CommandSettings()

type NamespacesCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<NamespacesSettings>()
    new(output: IOutputPort) = NamespacesCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, _settings, ct) : Task<int> =
        task {
            use client = clients.CreateContainerClient()
            let! response = client.ListNamespacesAsync(ct = ct)

            if response.Namespaces.Count = 0 then
                output.WriteWarning("Aucun namespace trouvÃ©.")
            else
                output.WriteSuccess("Namespaces disponibles :")

                for ns in response.Namespaces do
                    output.WriteLine(sprintf "  - %s" ns)

            return 0
        }

// â”€â”€ version â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type VersionSettings() =
    inherit CommandSettings()

type VersionCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<VersionSettings>()
    new(output: IOutputPort) = VersionCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, _settings, ct) : Task<int> =
        task {
            use client = clients.CreateContainerClient()
            let! v = client.GetVersionAsync(ct = ct)
            output.WriteSuccess("Diplo")
            output.WriteLine(sprintf "  Version   : %s" v.Version)
            output.WriteLine(sprintf "  RÃ©vision  : %s" v.Revision)
            output.WriteLine(sprintf "  Go        : %s" v.GoVersion)
            output.WriteLine(sprintf "  OS        : %s" v.Os)
            output.WriteLine(sprintf "  Arch      : %s" v.Arch)
            output.WriteLine("")
            output.WriteLine("  Licences des dÃ©pendances :")

            output.WriteLine(
                "    MIT (22)              : Avalonia, FSharp.Core, DiscUtils, Microsoft, Spectre, YamlDotNet, ZstdSharp"
            )

            output.WriteLine("    Apache-2.0 (13)       : gRPC, protobuf-net, Serilog, xunit")
            output.WriteLine("    BSD-3-Clause (1)      : Google.Protobuf")
            output.WriteLine("    LGPL-3.0+ (1)         : Hawkynt.FileFormats.FileSystems")
            output.WriteLine("  Voir THIRD-PARTY-NOTICES.txt pour le texte intÃ©gral.")
            return 0
        }

// â”€â”€ rename â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type RenameContainerSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

    [<CommandArgument(1, "<NEW_NAME>")>]
    member val NewName: string = null with get, set

type RenameContainerCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<RenameContainerSettings>()
    new(output: IOutputPort) = RenameContainerCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            elif String.IsNullOrWhiteSpace(settings.NewName) then
                output.WriteError("Le nouveau nom est requis")
                return 1
            else
                use client = clients.CreateContainerClient()
                let! response = client.RenameContainerAsync(settings.Id, settings.NewName, ct = ct)

                if response.Success then
                    output.WriteSuccess(response.Message)
                    return 0
                else
                    output.WriteError(response.Message)
                    return 1
        }

// â”€â”€ top â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type TopContainerSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

type TopContainerCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<TopContainerSettings>()
    new(output: IOutputPort) = TopContainerCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            else
                use client = clients.CreateContainerClient()
                let! response = client.TopContainerAsync(settings.Id, ct = ct)

                if response.Processes.Count = 0 then
                    output.WriteWarning("Aucun processus trouvÃ© dans le conteneur.")
                else
                    output.WriteTable(
                        response.Processes,
                        [| "PID"; "Utilisateur"; "Commande" |],
                        fun p -> [| string p.Pid; p.User; p.Command |]
                    )

                return 0
        }

// â”€â”€ stats â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type StatsContainerSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

type StatsContainerCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<StatsContainerSettings>()
    new(output: IOutputPort) = StatsContainerCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            else
                use client = clients.CreateContainerClient()
                let! response = client.GetContainerStatsAsync(settings.Id, ct = ct)
                output.WriteSuccess(sprintf "MÃ©triques du conteneur %s" settings.Id)
                output.WriteLine(sprintf "  CPU       : %.2f" response.CpuUsage)
                output.WriteLine(sprintf "  MÃ©moire   : %d / %d octets" response.MemoryUsage response.MemoryLimit)
                output.WriteLine(sprintf "  RÃ©seau rx : %d octets" response.NetworkRx)
                output.WriteLine(sprintf "  RÃ©seau tx : %d octets" response.NetworkTx)
                output.WriteLine(sprintf "  Disque r  : %d octets" response.DiskRead)
                output.WriteLine(sprintf "  Disque w  : %d octets" response.DiskWrite)
                output.WriteLine(sprintf "  PIDs      : %d" response.Pids)
                return 0
        }

// â”€â”€ image list â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type ImageListSettings() =
    inherit CommandSettings()

    [<CommandOption("--namespace")>]
    member val Namespace: string = null with get, set

type ImageListCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<ImageListSettings>()
    new(output: IOutputPort) = ImageListCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            use client = clients.CreateContainerClient()
            let ns = if isNull settings.Namespace then "" else settings.Namespace
            let! response = client.ListImagesAsync(ns, ct = ct)

            if response.Images.Count = 0 then
                output.WriteWarning("Aucune image trouvÃ©e.")
            else
                output.WriteTable(
                    response.Images,
                    [| "ID"; "RÃ©fÃ©rentiel"; "Tag"; "Taille"; "CrÃ©Ã©" |],
                    fun img -> [| img.Id; img.Repository; img.Tag; string img.Size; img.CreatedAt |]
                )

            return 0
        }

// â”€â”€ image search â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type ImageSearchSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<QUERY>")>]
    member val Query: string = null with get, set

    [<CommandOption("--registry")>]
    member val Registry: string = null with get, set

    [<CommandOption("--limit")>]
    member val Limit: int = 25 with get, set

type ImageSearchCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<ImageSearchSettings>()
    new(output: IOutputPort) = ImageSearchCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Query) then
                output.WriteError("La requÃªte de recherche est requise")
                return 1
            else
                use client = clients.CreateContainerClient()
                let registry = if isNull settings.Registry then "" else settings.Registry
                let! response = client.SearchImagesAsync(settings.Query, registry, settings.Limit, ct = ct)

                if not (String.IsNullOrEmpty(response.Message)) then
                    output.WriteWarning(response.Message)

                if response.Results.Count = 0 then
                    output.WriteWarning("Aucune image trouvÃ©e dans les catalogues en ligne.")
                else
                    output.WriteTable(
                        response.Results,
                        [| "Registre"; "RÃ©fÃ©rence"; "Ã‰toiles"; "Description" |],
                        fun r ->
                            [| r.Registry
                               r.Ref
                               string r.Stars
                               r.Description |]
                    )

                return 0
        }

// â”€â”€ image inspect â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type ImageInspectSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<REF>")>]
    member val Ref: string = null with get, set

    [<CommandOption("--namespace")>]
    member val Namespace: string = null with get, set

type ImageInspectCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<ImageInspectSettings>()
    new(output: IOutputPort) = ImageInspectCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Ref) then
                output.WriteError("La rÃ©fÃ©rence de l'image est requise")
                return 1
            else
                use client = clients.CreateContainerClient()
                let ns = if isNull settings.Namespace then "" else settings.Namespace
                let! response = client.InspectImageAsync(settings.Ref, ns, ct = ct)
                output.WriteSuccess(sprintf "Image %s" response.Ref)
                output.WriteLine(sprintf "  ID           : %s" response.Id)
                output.WriteLine(sprintf "  RÃ©fÃ©rentiel : %s" response.Repository)
                output.WriteLine(sprintf "  Tag          : %s" response.Tag)
                output.WriteLine(sprintf "  Taille       : %d octets" response.Size)
                output.WriteLine(sprintf "  CrÃ©Ã©         : %s" response.CreatedAt)

                if response.Labels.Count > 0 then
                    for kv in response.Labels do
                        output.WriteLine(sprintf "  Label        : %s=%s" kv.Key kv.Value)

                return 0
        }

// â”€â”€ image remove â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type ImageRemoveSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<REF>")>]
    member val Ref: string = null with get, set

    [<CommandOption("--namespace")>]
    member val Namespace: string = null with get, set

type ImageRemoveCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<ImageRemoveSettings>()
    new(output: IOutputPort) = ImageRemoveCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Ref) then
                output.WriteError("La rÃ©fÃ©rence de l'image est requise")
                return 1
            else
                use client = clients.CreateContainerClient()
                let ns = if isNull settings.Namespace then "" else settings.Namespace
                let! response = client.RemoveImageAsync(settings.Ref, ns, ct = ct)

                if response.Success then
                    output.WriteSuccess(response.Message)
                    return 0
                else
                    output.WriteError(response.Message)
                    return 1
        }

// â”€â”€ image tag â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
    new(output: IOutputPort) = ImageTagCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Source) then
                output.WriteError("La rÃ©fÃ©rence source est requise")
                return 1
            elif String.IsNullOrWhiteSpace(settings.Target) then
                output.WriteError("La rÃ©fÃ©rence cible est requise")
                return 1
            else
                use client = clients.CreateContainerClient()
                let ns = if isNull settings.Namespace then "" else settings.Namespace
                let! response = client.TagImageAsync(settings.Source, settings.Target, ns, ct = ct)
                output.WriteSuccess(response.Message)
                return 0
        }

// â”€â”€ pause â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type PauseContainerSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

type PauseContainerCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<PauseContainerSettings>()
    new(output: IOutputPort) = PauseContainerCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            else
                use client = clients.CreateContainerClient()
                let! response = client.PauseAsync(settings.Id, ct = ct)
                output.WriteSuccess(sprintf "Conteneur %s en pause (%s)" settings.Id (response.State.ToString()))
                return 0
        }

// â”€â”€ unpause â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type UnpauseContainerSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

type UnpauseContainerCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<UnpauseContainerSettings>()
    new(output: IOutputPort) = UnpauseContainerCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            else
                use client = clients.CreateContainerClient()
                let! response = client.UnpauseAsync(settings.Id, ct = ct)
                output.WriteSuccess(sprintf "Conteneur %s repris (%s)" settings.Id (response.State.ToString()))
                return 0
        }

// â”€â”€ wait â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type WaitContainerSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

    [<CommandOption("-t|--timeout")>]
    member val Timeout = 0 with get, set

type WaitContainerCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<WaitContainerSettings>()
    new(output: IOutputPort) = WaitContainerCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            else
                use client = clients.CreateContainerClient()
                let timeout = if settings.Timeout < 0 then 0 else settings.Timeout
                let! response = client.WaitAsync(settings.Id, timeout, ct = ct)

                // code -1 = abandon par dÃ©lai d'attente (voir WaitContainer).
                if response.ExitCode = -1 then
                    output.WriteWarning(
                        if String.IsNullOrWhiteSpace(response.Message) then
                            sprintf "Conteneur %s toujours en cours aprÃ¨s %d s" settings.Id timeout
                        else
                            response.Message
                    )

                    return 0
                else
                    output.WriteSuccess(sprintf "Conteneur %s terminÃ© (code de sortie %d)" settings.Id response.ExitCode)
                    return 0
        }

// â”€â”€ prune (conteneurs) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type PruneContainersSettings() =
    inherit CommandSettings()

type PruneContainersCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<PruneContainersSettings>()
    new(output: IOutputPort) = PruneContainersCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, _settings, ct) : Task<int> =
        task {
            use client = clients.CreateContainerClient()
            let! response = client.PruneContainersAsync(ct = ct)

            if response.Deleted.Count = 0 then
                output.WriteWarning("Aucun conteneur arrÃªtÃ© Ã  supprimer.")
            else
                output.WriteSuccess(sprintf "%d conteneur(s) supprimÃ©(s) :" response.Deleted.Count)

                for id in response.Deleted do
                    output.WriteLine(sprintf "  - %s" id)

            return 0
        }

// â”€â”€ events â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type ContainerEventsSettings() =
    inherit CommandSettings()

type ContainerEventsCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<ContainerEventsSettings>()
    new(output: IOutputPort) = ContainerEventsCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, _settings, ct) : Task<int> =
        task {
            use client = clients.CreateContainerClient()

            try
                use ctrlC = new CtrlCHandler()
                use linked = CancellationTokenSource.CreateLinkedTokenSource(ct, ctrlC.Token)
                let watchCt = linked.Token
                let stream = client.WatchEventsStream(ct = watchCt)
                let enumerator = stream.GetAsyncEnumerator(watchCt)
                let mutable failure: System.Exception option = None

                try
                    let mutable moving = true

                    while moving do
                        let! hasNext = enumerator.MoveNextAsync().AsTask()

                        if hasNext then
                            let evt = enumerator.Current

                            let exit =
                                if evt.ExitCode <> 0 then
                                    sprintf " (exit %d)" evt.ExitCode
                                else
                                    ""

                            output.WriteLine(
                                sprintf "[%s] %-8s %s %s%s" evt.Timestamp evt.EventType evt.Id evt.Status exit
                            )
                        else
                            moving <- false
                with ex ->
                    failure <- Some ex

                do! enumerator.DisposeAsync().AsTask()

                match failure with
                | Some ex -> ExceptionDispatchInfo.Capture(ex).Throw()
                | None -> ()

                return 0
            with
            | :? Grpc.Core.RpcException as rex when rex.StatusCode = Grpc.Core.StatusCode.Cancelled -> return 0
            | :? OperationCanceledException -> return 0
            | ex ->
                output.WriteError(ex.Message)
                return 1
        }

// â”€â”€ stats-stream â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type StatsStreamSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

    [<CommandOption("-i|--interval")>]
    member val Interval = 0 with get, set

type StatsStreamCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<StatsStreamSettings>()
    new(output: IOutputPort) = StatsStreamCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            else
                use client = clients.CreateContainerClient()

                try
                    use ctrlC = new CtrlCHandler()
                    use linked = CancellationTokenSource.CreateLinkedTokenSource(ct, ctrlC.Token)
                    let watchCt = linked.Token
                    let interval = if settings.Interval < 0 then 0 else settings.Interval
                    let stream = client.GetContainerStatsStream(settings.Id, interval, ct = watchCt)
                    let enumerator = stream.GetAsyncEnumerator(watchCt)
                    let mutable failure: System.Exception option = None

                    try
                        let mutable moving = true

                        while moving do
                            let! hasNext = enumerator.MoveNextAsync().AsTask()

                            if hasNext then
                                let s = enumerator.Current

                                output.WriteLine(
                                    sprintf "CPU %.2f | MÃ©moire %d/%d | rx %d tx %d | disque r %d w %d | %d PID"
                                        s.CpuUsage
                                        s.MemoryUsage
                                        s.MemoryLimit
                                        s.NetworkRx
                                        s.NetworkTx
                                        s.DiskRead
                                        s.DiskWrite
                                        s.Pids
                                )
                            else
                                moving <- false
                    with ex ->
                        failure <- Some ex

                    do! enumerator.DisposeAsync().AsTask()

                    match failure with
                    | Some ex -> ExceptionDispatchInfo.Capture(ex).Throw()
                    | None -> ()

                    return 0
                with
                | :? Grpc.Core.RpcException as rex when rex.StatusCode = Grpc.Core.StatusCode.Cancelled -> return 0
                | :? OperationCanceledException -> return 0
                | ex ->
                    output.WriteError(ex.Message)
                    return 1
        }

// â”€â”€ image prune â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type ImagePruneSettings() =
    inherit CommandSettings()

type ImagePruneCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<ImagePruneSettings>()
    new(output: IOutputPort) = ImagePruneCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, _settings, ct) : Task<int> =
        task {
            use client = clients.CreateContainerClient()
            let! response = client.PruneImagesAsync(ct = ct)

            if response.Deleted.Count = 0 then
                output.WriteWarning("Aucune image inutilisÃ©e Ã  supprimer.")
            else
                output.WriteSuccess(sprintf "%d image(s) supprimÃ©e(s) :" response.Deleted.Count)

                for id in response.Deleted do
                    output.WriteLine(sprintf "  - %s" id)

            return 0
        }

// â”€â”€ image commit â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type ImageCommitSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<CONTAINER_ID>")>]
    member val ContainerId: string = null with get, set

    [<CommandArgument(1, "<IMAGE_REF>")>]
    member val ImageRef: string = null with get, set

    [<CommandOption("--message")>]
    member val Message: string = null with get, set

    [<CommandOption("--author")>]
    member val Author: string = null with get, set

type ImageCommitCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<ImageCommitSettings>()
    new(output: IOutputPort) = ImageCommitCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.ContainerId) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            elif String.IsNullOrWhiteSpace(settings.ImageRef) then
                output.WriteError("La rÃ©fÃ©rence de l'image est requise")
                return 1
            else
                use client = clients.CreateContainerClient()

                let! response =
                    client.CommitImageAsync(
                        settings.ContainerId,
                        settings.ImageRef,
                        ?message =
                            (if String.IsNullOrWhiteSpace(settings.Message) then
                                 None
                             else
                                 Some settings.Message),
                        ?author =
                            (if String.IsNullOrWhiteSpace(settings.Author) then
                                 None
                             else
                                 Some settings.Author),
                        ct = ct
                    )

                if response.Success then
                    output.WriteSuccess(response.Message)
                    return 0
                else
                    output.WriteError(response.Message)
                    return 1
        }

// â”€â”€ image export â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type ImageExportSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<REF>")>]
    member val Ref: string = null with get, set

    [<CommandOption("--namespace")>]
    member val Namespace: string = null with get, set

    [<CommandOption("-o|--output")>]
    member val Output: string = null with get, set

type ImageExportCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<ImageExportSettings>()
    new(output: IOutputPort) = ImageExportCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Ref) then
                output.WriteError("La rÃ©fÃ©rence de l'image est requise")
                return 1
            elif String.IsNullOrWhiteSpace(settings.Output) then
                output.WriteError("Le fichier de sortie est requis (--output)")
                return 1
            else
                use client = clients.CreateContainerClient()
                let ns = if isNull settings.Namespace then "" else settings.Namespace
                let stream = client.ExportImageStream(settings.Ref, ns)
                let enumerator = stream.GetAsyncEnumerator(CancellationToken.None)
                let mutable total = 0L
                let mutable failure: System.Exception option = None

                try
                    use fs = new FileStream(settings.Output, FileMode.Create, FileAccess.Write, FileShare.Read)

                    try
                        let mutable moving = true

                        while moving do
                            let! hasNext = enumerator.MoveNextAsync().AsTask()

                            if hasNext then
                                let chunk = enumerator.Current

                                if not (isNull chunk.Data) && chunk.Data.Length > 0 then
                                    fs.Write(chunk.Data, 0, chunk.Data.Length)
                                    total <- total + int64 chunk.Data.Length
                            else
                                moving <- false
                    with ex ->
                        failure <- Some ex
                with ex ->
                    failure <- Some ex

                do! enumerator.DisposeAsync().AsTask()

                match failure with
                | Some ex ->
                    output.WriteError(ex.Message)
                    return 1
                | None ->
                    output.WriteSuccess(sprintf "Image '%s' exportÃ©e vers %s (%d octets)" settings.Ref settings.Output total)
                    return 0
        }

// â”€â”€ image import â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

module private ImageTransfer =

    /// DÃ©coupe un fichier local en morceaux de 64 Ko pour l'import gRPC.
    let imageChunksOfFile (path: string) : IAsyncEnumerable<ImageChunk> =
        { new IAsyncEnumerable<ImageChunk> with
            member _.GetAsyncEnumerator(_ct) =
                let fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
                let buffer = Array.zeroCreate<byte> (64 * 1024)
                let mutable current: ImageChunk = Unchecked.defaultof<ImageChunk>

                { new IAsyncEnumerator<ImageChunk> with
                    member _.Current = current

                    member _.MoveNextAsync() =
                        let n = fs.Read(buffer, 0, buffer.Length)

                        if n > 0 then
                            current <-
                                { ImageChunk.Data = (if n = buffer.Length then buffer else buffer[0 .. n - 1]) }

                            ValueTask<bool>(true)
                        else
                            ValueTask<bool>(false)

                    member _.DisposeAsync() =
                        fs.Dispose()
                        ValueTask() } }

type ImageImportSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<FILE>")>]
    member val File: string = null with get, set

    [<CommandOption("--namespace")>]
    member val Namespace: string = null with get, set

type ImageImportCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<ImageImportSettings>()
    new(output: IOutputPort) = ImageImportCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.File) then
                output.WriteError("Le fichier Ã  importer est requis")
                return 1
            elif not (File.Exists settings.File) then
                output.WriteError(sprintf "Fichier introuvable : %s" settings.File)
                return 1
            else
                use client = clients.CreateContainerClient()
                let chunks = ImageTransfer.imageChunksOfFile settings.File
                let! response = client.ImportImage(chunks, ct = ct)
                output.WriteSuccess(response.Message)

                for r in response.ImageRefs do
                    output.WriteLine(sprintf "  - %s" r)

                return 0
        }

// â”€â”€ read-file â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type ReadFileSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

    [<CommandArgument(1, "<PATH>")>]
    member val Path: string = null with get, set

    [<CommandOption("-o|--output")>]
    member val Output: string = null with get, set

type ReadFileCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<ReadFileSettings>()
    new(output: IOutputPort) = ReadFileCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            elif String.IsNullOrWhiteSpace(settings.Path) then
                output.WriteError("Le chemin du fichier est requis")
                return 1
            elif String.IsNullOrWhiteSpace(settings.Output) then
                output.WriteError("Le fichier de sortie est requis (--output)")
                return 1
            else
                use client = clients.CreateContainerClient()
                let! response = client.ReadFileAsync(settings.Id, settings.Path, ct = ct)

                if response.Success then
                    File.WriteAllBytes(settings.Output, response.Data)
                    output.WriteSuccess(sprintf "%d octets copiÃ©s vers %s" response.Data.Length settings.Output)
                    return 0
                else
                    output.WriteError(response.Message)
                    return 1
        }

// â”€â”€ write-file â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type WriteFileSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

    [<CommandArgument(1, "<PATH>")>]
    member val Path: string = null with get, set

    [<CommandOption("-i|--input")>]
    member val Input: string = null with get, set

type WriteFileCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<WriteFileSettings>()
    new(output: IOutputPort) = WriteFileCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            elif String.IsNullOrWhiteSpace(settings.Path) then
                output.WriteError("Le chemin du fichier est requis")
                return 1
            elif String.IsNullOrWhiteSpace(settings.Input) then
                output.WriteError("Le fichier source est requis (--input)")
                return 1
            elif not (File.Exists settings.Input) then
                output.WriteError(sprintf "Fichier introuvable : %s" settings.Input)
                return 1
            else
                use client = clients.CreateContainerClient()
                let data = File.ReadAllBytes settings.Input
                let! response = client.WriteFileAsync(settings.Id, settings.Path, data, ct = ct)

                if response.Success then
                    output.WriteSuccess(response.Message)
                    return 0
                else
                    output.WriteError(response.Message)
                    return 1
        }


