namespace DiploWalker.Cli.Network

open System
open System.Threading
open System.Threading.Tasks
open DiploWalker.Abstractions
open DiploWalker.Core.Clients
open DiploWalker.Grpc
open DiploWalker.Core.Output
open DiploWalker.Grpc.Network
open Spectre.Console.Cli

// ── list ──────────────────────────────────────────────────────────
type ListNetworksSettings() =
    inherit CommandSettings()

type ListNetworksCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<ListNetworksSettings>()
    new(output: IOutputPort) = ListNetworksCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, _settings, ct) : Task<int> =
        task {
            use client = clients.CreateNetworkClient()
            let! response = client.ListAsync(ct = ct)

            if response.Networks.Count = 0 then
                output.WriteWarning("Aucun réseau trouvé.")
            else
                output.WriteTable(
                    response.Networks,
                    [| "ID"; "Nom"; "Driver"; "Sous-réseau"; "Passerelle"; "Endpoints" |],
                    fun n ->
                        [| n.Id
                           n.Name
                           n.Driver.ToString()
                           n.Subnet
                           n.Gateway
                           string n.EndpointCount |]
                )

            return 0
        }

// ── inspect ───────────────────────────────────────────────────────
type InspectNetworkSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

type InspectNetworkCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<InspectNetworkSettings>()
    new(output: IOutputPort) = InspectNetworkCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError("L'identifiant du réseau est requis")
                return 1
            else
                use client = clients.CreateNetworkClient()
                let! response = client.InspectAsync(settings.Id, ct = ct)

                output.WriteSuccess(sprintf "Réseau %s" response.Name)
                output.WriteLine(sprintf "  ID          : %s" response.Id)
                output.WriteLine(sprintf "  Driver      : %s" (response.Driver.ToString()))
                output.WriteLine(sprintf "  Sous-réseau : %s" response.Subnet)
                output.WriteLine(sprintf "  Passerelle  : %s" response.Gateway)
                output.WriteLine(sprintf "  Plage IP    : %s" response.IpRange)
                output.WriteLine(sprintf "  Créé        : %s" response.CreatedAt)

                if response.Endpoints.Count > 0 then
                    output.WriteLine("  Endpoints:")

                    for ep in response.Endpoints do
                        output.WriteLine(
                            sprintf "    - %s (%s) → %s" ep.ContainerId ep.Ipv4Address (ep.State.ToString())
                        )

                return 0
        }

// ── create ────────────────────────────────────────────────────────
type CreateNetworkSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<NAME>")>]
    member val Name: string = null with get, set

    [<CommandOption("--driver")>]
    member val Driver = "bridge" with get, set

    [<CommandOption("--subnet")>]
    member val Subnet: string = null with get, set

    [<CommandOption("--gateway")>]
    member val Gateway: string = null with get, set

type CreateNetworkCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<CreateNetworkSettings>()
    new(output: IOutputPort) = CreateNetworkCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Name) then
                output.WriteError("Le nom du réseau est requis")
                return 1
            elif not (DriverMappings.isValidNetworkDriver settings.Driver) then
                output.WriteError(sprintf "Driver inconnu: %s. Valeurs: bridge, none, custom_cni, pod" settings.Driver)
                return 1
            else
                let driver = DriverMappings.parseNetworkDriver settings.Driver

                use client = clients.CreateNetworkClient()

                let! response =
                    client.CreateAsync(
                        name = settings.Name,
                        driver = driver,
                        subnet = (if isNull settings.Subnet then "" else settings.Subnet),
                        gateway = (if isNull settings.Gateway then "" else settings.Gateway),
                        ct = ct
                    )

                output.WriteSuccess(sprintf "Réseau %s créé (ID: %s)" response.Name response.Id)
                return 0
        }

// ── remove ────────────────────────────────────────────────────────
type RemoveNetworkSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<ID>")>]
    member val Id: string = null with get, set

    [<CommandOption("-f|--force")>]
    member val Force = false with get, set

type RemoveNetworkCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<RemoveNetworkSettings>()
    new(output: IOutputPort) = RemoveNetworkCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError("L'identifiant du réseau est requis")
                return 1
            else
                use client = clients.CreateNetworkClient()
                let! response = client.RemoveAsync(settings.Id, settings.Force, ct = ct)

                if response.Success then
                    output.WriteSuccess(response.Message)
                    return 0
                else
                    output.WriteError(response.Message)
                    return 1
        }

// ── connect ───────────────────────────────────────────────────────
type ConnectSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<NETWORK_ID>")>]
    member val NetworkId: string = null with get, set

    [<CommandArgument(1, "<CONTAINER_ID>")>]
    member val ContainerId: string = null with get, set

    [<CommandOption("--endpoint-id")>]
    member val EndpointId: string = null with get, set

    [<CommandOption("--ipv4")>]
    member val Ipv4Address: string = null with get, set

type ConnectCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<ConnectSettings>()
    new(output: IOutputPort) = ConnectCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.NetworkId) then
                output.WriteError("L'identifiant du réseau est requis")
                return 1
            elif String.IsNullOrWhiteSpace(settings.ContainerId) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            else
                use client = clients.CreateNetworkClient()

                let! response =
                    client.ConnectAsync(
                        networkId = settings.NetworkId,
                        containerId = settings.ContainerId,
                        endpointId =
                            (if isNull settings.EndpointId then
                                 ""
                             else
                                 settings.EndpointId),
                        ipv4Address =
                            (if isNull settings.Ipv4Address then
                                 ""
                             else
                                 settings.Ipv4Address),
                        ct = ct
                    )

                output.WriteSuccess(response.Message)
                output.WriteLine(sprintf "  Endpoint  : %s" response.EndpointId)
                output.WriteLine(sprintf "  IPv4      : %s" response.Ipv4Address)
                output.WriteLine(sprintf "  MAC       : %s" response.MacAddress)
                return 0
        }

// ── disconnect ────────────────────────────────────────────────────
type DisconnectSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<NETWORK_ID>")>]
    member val NetworkId: string = null with get, set

    [<CommandArgument(1, "<CONTAINER_ID>")>]
    member val ContainerId: string = null with get, set

    [<CommandOption("--endpoint-id")>]
    member val EndpointId: string = null with get, set

    [<CommandOption("-f|--force")>]
    member val Force = false with get, set

type DisconnectCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<DisconnectSettings>()
    new(output: IOutputPort) = DisconnectCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.NetworkId) then
                output.WriteError("L'identifiant du réseau est requis")
                return 1
            elif String.IsNullOrWhiteSpace(settings.ContainerId) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            else
                use client = clients.CreateNetworkClient()

                let! response =
                    client.DisconnectAsync(
                        networkId = settings.NetworkId,
                        containerId = settings.ContainerId,
                        endpointId =
                            (if isNull settings.EndpointId then
                                 ""
                             else
                                 settings.EndpointId),
                        force = settings.Force,
                        ct = ct
                    )

                if response.Success then

                    output.WriteSuccess(response.Message)

                    return 0

                else

                    output.WriteError(response.Message)

                    return 1
        }

// ── run-cni-plugin ────────────────────────────────────────────────
type RunCniPluginSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<PLUGIN_PATH>")>]
    member val PluginPath: string = null with get, set

    [<CommandArgument(1, "<COMMAND>")>]
    member val CniCommand: string = null with get, set

    [<CommandArgument(2, "<CONTAINER_ID>")>]
    member val ContainerId: string = null with get, set

    [<CommandArgument(3, "<NETNS_PATH>")>]
    member val NetnsPath: string = null with get, set

    [<CommandOption("--config-name")>]
    member val ConfigName: string = null with get, set

    [<CommandOption("--config-type")>]
    member val ConfigType: string = null with get, set

    [<CommandOption("--config-subnet")>]
    member val ConfigSubnet: string = null with get, set

    [<CommandOption("--config-gateway")>]
    member val ConfigGateway: string = null with get, set

type RunCniPluginCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<RunCniPluginSettings>()
    new(output: IOutputPort) = RunCniPluginCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.PluginPath) then
                output.WriteError("Le chemin du plugin CNI est requis")
                return 1
            elif not (System.IO.Path.IsPathRooted(settings.PluginPath)) then
                output.WriteError(sprintf "Le chemin du plugin CNI doit être absolu : %s" settings.PluginPath)
                return 1
            elif String.IsNullOrWhiteSpace(settings.CniCommand) then
                output.WriteError("La commande CNI est requise (ADD ou DEL)")
                return 1
            elif String.IsNullOrWhiteSpace(settings.ContainerId) then
                output.WriteError(ServiceGuards.ContainerIdRequired)
                return 1
            elif String.IsNullOrWhiteSpace(settings.NetnsPath) then
                output.WriteError("Le chemin du namespace réseau est requis")
                return 1
            else
                use client = clients.CreateNetworkClient()

                let config =
                    if isNull settings.ConfigType then
                        None
                    else
                        Some
                            { CniConfiguration.Name =
                                (if isNull settings.ConfigName then
                                     ""
                                 else
                                     settings.ConfigName)
                              Type = settings.ConfigType
                              Subnet =
                                (if isNull settings.ConfigSubnet then
                                     ""
                                 else
                                     settings.ConfigSubnet)
                              Gateway =
                                (if isNull settings.ConfigGateway then
                                     ""
                                 else
                                     settings.ConfigGateway)
                              IpRange = ""
                              HairpinMode = false
                              IsDefaultGateway = false
                              Dns = System.Collections.Generic.Dictionary<string, string>() }

                let! response =
                    client.RunCniPluginAsync(
                        pluginPath = settings.PluginPath,
                        command = settings.CniCommand,
                        containerId = settings.ContainerId,
                        netnsPath = settings.NetnsPath,
                        ?config = config,
                        ct = ct
                    )

                if response.Success then
                    output.WriteSuccess("Plugin CNI exécuté avec succès")
                    output.WriteLine(sprintf "  Interface : %s" response.Ifname)
                    output.WriteLine(sprintf "  IPv4      : %s" response.Ipv4Address)
                    output.WriteLine(sprintf "  Passerelle: %s" response.Gateway)

                    if not (System.String.IsNullOrWhiteSpace(response.Message)) then
                        output.WriteLine(sprintf "  Message   : %s" response.Message)

                    return 0
                else
                    output.WriteError(sprintf "Échec du plugin CNI: %s" response.Message)
                    return 1
        }

// ── prune ─────────────────────────────────────────────────────────
type PruneNetworksSettings() =
    inherit CommandSettings()

type PruneNetworksCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<PruneNetworksSettings>()
    new(output: IOutputPort) = PruneNetworksCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, _settings, ct) : Task<int> =
        task {
            use client = clients.CreateNetworkClient()
            let! response = client.PruneNetworksAsync(ct = ct)

            if response.Count > 0 then
                output.WriteSuccess(response.Message)

                for id in response.NetworksDeleted do
                    output.WriteLine(sprintf "  - %s" id)
            else
                output.WriteWarning("Aucun réseau à supprimer.")

            return 0
        }


