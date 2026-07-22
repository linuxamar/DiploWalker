namespace Diplo.Cli.Network

open System.Threading
open Diplo.Core.Clients
open Diplo.Core.Output
open Diplo.Grpc.Network
open Spectre.Console.Cli

// ── list ──────────────────────────────────────────────────────────
type ListNetworksSettings() =
    inherit CommandSettings()

type ListNetworksCommand(output: IOutputPort) =
    inherit AsyncCommand<ListNetworksSettings>()

    override _.ExecuteAsync(_ctx, _settings, _ct) =
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

    override _.ExecuteAsync(_ctx, settings, _ct) =
        task {
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

    override _.ExecuteAsync(_ctx, settings, _ct) =
        task {
            let driver =
                match settings.Driver.ToLowerInvariant() with
                | "bridge" -> NetworkDriver.Bridge
                | "none" -> NetworkDriver.None
                | "custom_cni" -> NetworkDriver.CustomCni
                | "pod" -> NetworkDriver.Pod
                | other -> failwithf "Driver inconnu: %s. Valeurs: bridge, none, custom_cni, pod" other

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

    override _.ExecuteAsync(_ctx, settings, _ct) =
        task {
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

    override _.ExecuteAsync(_ctx, settings, _ct) =
        task {
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

    override _.ExecuteAsync(_ctx, settings, _ct) =
        task {
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
