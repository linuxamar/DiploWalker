namespace Diplo.Cli.Network

open System
open System.Threading
open System.Threading.Tasks
open Diplo.Core.Clients
open Diplo.Core.Output
open Diplo.Grpc.Network
open Spectre.Console.Cli

// ── list ──────────────────────────────────────────────────────────
type ListNetworksSettings() =
    inherit CommandSettings()

type ListNetworksCommand(output: IOutputPort) =
    inherit AsyncCommand<ListNetworksSettings>()

    override _.ExecuteAsync(_ctx, _settings, _ct) : Task<int> =
        task {
            use client = new NetworkClient()
            let! response = client.ListAsync(ct = CancellationToken.None)

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
                           string n.EndpointCount |])
            return 0
        }

// ── inspect ───────────────────────────────────────────────────────
type InspectNetworkSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<ID>")>] member val Id: string = null with get, set

type InspectNetworkCommand(output: IOutputPort) =
    inherit AsyncCommand<InspectNetworkSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.Id) then
                output.WriteError("L'identifiant du réseau est requis")
                return 1
            else
                use client = new NetworkClient()
                let! response = client.InspectAsync(settings.Id)

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
                        output.WriteLine(sprintf "    - %s (%s) → %s" ep.ContainerId ep.Ipv4Address (ep.State.ToString()))
                return 0
        }

// ── create ────────────────────────────────────────────────────────
type CreateNetworkSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<NAME>")>] member val Name: string = null with get, set
    [<CommandOption("--driver")>] member val Driver = "bridge" with get, set
    [<CommandOption("--subnet")>] member val Subnet: string = null with get, set
    [<CommandOption("--gateway")>] member val Gateway: string = null with get, set

type CreateNetworkCommand(output: IOutputPort) =
    inherit AsyncCommand<CreateNetworkSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.Name) then
                output.WriteError("Le nom du réseau est requis")
                return 1
            elif not (List.contains (settings.Driver.ToLowerInvariant()) ["bridge"; "none"; "custom_cni"; "pod"]) then
                output.WriteError(sprintf "Driver inconnu: %s. Valeurs: bridge, none, custom_cni, pod" settings.Driver)
                return 1
            else
                let driver =
                    match settings.Driver.ToLowerInvariant() with
                    | "bridge" -> NetworkDriver.Bridge
                    | "none" -> NetworkDriver.None
                    | "custom_cni" -> NetworkDriver.CustomCni
                    | "pod" -> NetworkDriver.Pod
                    | _ -> failwithf "Driver %s non géré (normalement déjà validé)" settings.Driver

                use client = new NetworkClient()
                let! response =
                    client.CreateAsync(
                        name = settings.Name,
                        driver = driver,
                        subnet = (if isNull settings.Subnet then "" else settings.Subnet),
                        gateway = (if isNull settings.Gateway then "" else settings.Gateway))

                output.WriteSuccess(sprintf "Réseau %s créé (ID: %s)" response.Name response.Id)
                return 0
        }

// ── remove ────────────────────────────────────────────────────────
type RemoveNetworkSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<ID>")>] member val Id: string = null with get, set
    [<CommandOption("-f|--force")>] member val Force = false with get, set

type RemoveNetworkCommand(output: IOutputPort) =
    inherit AsyncCommand<RemoveNetworkSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.Id) then
                output.WriteError("L'identifiant du réseau est requis")
                return 1
            else
                use client = new NetworkClient()
                let! response = client.RemoveAsync(settings.Id, settings.Force)
                if response.Success then
                    output.WriteSuccess(response.Message)
                else
                    output.WriteError(response.Message)
                return 0
        }

// ── connect ───────────────────────────────────────────────────────
type ConnectSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<NETWORK_ID>")>] member val NetworkId: string = null with get, set
    [<CommandArgument(1, "<CONTAINER_ID>")>] member val ContainerId: string = null with get, set
    [<CommandOption("--endpoint-id")>] member val EndpointId: string = null with get, set
    [<CommandOption("--ipv4")>] member val Ipv4Address: string = null with get, set

type ConnectCommand(output: IOutputPort) =
    inherit AsyncCommand<ConnectSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.NetworkId) then
                output.WriteError("L'identifiant du réseau est requis")
                return 1
            elif String.IsNullOrEmpty(settings.ContainerId) then
                output.WriteError("L'identifiant du conteneur est requis")
                return 1
            else
                use client = new NetworkClient()
                let! response =
                    client.ConnectAsync(
                        networkId = settings.NetworkId,
                        containerId = settings.ContainerId,
                        endpointId = (if isNull settings.EndpointId then "" else settings.EndpointId),
                        ipv4Address = (if isNull settings.Ipv4Address then "" else settings.Ipv4Address))

                output.WriteSuccess(response.Message)
                output.WriteLine(sprintf "  Endpoint  : %s" response.EndpointId)
                output.WriteLine(sprintf "  IPv4      : %s" response.Ipv4Address)
                output.WriteLine(sprintf "  MAC       : %s" response.MacAddress)
                return 0
        }

// ── disconnect ────────────────────────────────────────────────────
type DisconnectSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<NETWORK_ID>")>] member val NetworkId: string = null with get, set
    [<CommandArgument(1, "<CONTAINER_ID>")>] member val ContainerId: string = null with get, set
    [<CommandOption("--endpoint-id")>] member val EndpointId: string = null with get, set
    [<CommandOption("-f|--force")>] member val Force = false with get, set

type DisconnectCommand(output: IOutputPort) =
    inherit AsyncCommand<DisconnectSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.NetworkId) then
                output.WriteError("L'identifiant du réseau est requis")
                return 1
            elif String.IsNullOrEmpty(settings.ContainerId) then
                output.WriteError("L'identifiant du conteneur est requis")
                return 1
            else
                use client = new NetworkClient()
                let! response =
                    client.DisconnectAsync(
                        networkId = settings.NetworkId,
                        containerId = settings.ContainerId,
                        endpointId = (if isNull settings.EndpointId then "" else settings.EndpointId),
                        force = settings.Force)

                if response.Success then
                    output.WriteSuccess(response.Message)
                else
                    output.WriteError(response.Message)
                return 0
        }

// ── run-cni-plugin ────────────────────────────────────────────────
type RunCniPluginSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<PLUGIN_PATH>")>] member val PluginPath: string = null with get, set
    [<CommandArgument(1, "<COMMAND>")>] member val CniCommand: string = null with get, set
    [<CommandArgument(2, "<CONTAINER_ID>")>] member val ContainerId: string = null with get, set
    [<CommandArgument(3, "<NETNS_PATH>")>] member val NetnsPath: string = null with get, set
    [<CommandOption("--config-name")>] member val ConfigName: string = null with get, set
    [<CommandOption("--config-type")>] member val ConfigType: string = null with get, set
    [<CommandOption("--config-subnet")>] member val ConfigSubnet: string = null with get, set
    [<CommandOption("--config-gateway")>] member val ConfigGateway: string = null with get, set

type RunCniPluginCommand(output: IOutputPort) =
    inherit AsyncCommand<RunCniPluginSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            use client = new NetworkClient()
            let config =
                if isNull settings.ConfigType then
                    None
                else
                    Some { CniConfiguration.Name = (if isNull settings.ConfigName then "" else settings.ConfigName)
                           Type = settings.ConfigType
                           Subnet = (if isNull settings.ConfigSubnet then "" else settings.ConfigSubnet)
                           Gateway = (if isNull settings.ConfigGateway then "" else settings.ConfigGateway)
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
                    ?config = config)

            if response.Success then
                output.WriteSuccess("Plugin CNI exécuté avec succès")
                output.WriteLine(sprintf "  Interface : %s" response.Ifname)
                output.WriteLine(sprintf "  IPv4      : %s" response.Ipv4Address)
                output.WriteLine(sprintf "  Passerelle: %s" response.Gateway)
                if not (System.String.IsNullOrEmpty(response.Message)) then
                    output.WriteLine(sprintf "  Message   : %s" response.Message)
            else
                output.WriteError(sprintf "Échec du plugin CNI: %s" response.Message)
            return 0
        }

// ── prune ─────────────────────────────────────────────────────────
type PruneNetworksSettings() =
    inherit CommandSettings()

type PruneNetworksCommand(output: IOutputPort) =
    inherit AsyncCommand<PruneNetworksSettings>()

    override _.ExecuteAsync(_ctx, _settings, _ct) : Task<int> =
        task {
            use client = new NetworkClient()
            let! response = client.PruneNetworksAsync()
            if response.Count > 0 then
                output.WriteSuccess(response.Message)
                for id in response.NetworksDeleted do
                    output.WriteLine(sprintf "  - %s" id)
            else
                output.WriteWarning("Aucun réseau à supprimer.")
            return 0
        }
