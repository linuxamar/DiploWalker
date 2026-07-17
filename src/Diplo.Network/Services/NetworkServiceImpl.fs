namespace Diplo.Network.Services

open System
open System.Collections.Generic
open System.Diagnostics
open System.Text.Json
open System.Threading.Tasks
open Grpc.Core
open Serilog
open Diplo.Grpc.Network
open Diplo.Network.Plugins

type NetworkServiceImpl(drivers: IReadOnlyDictionary<NetworkDriver, INetworkDriver>) =
    inherit NetworkService.NetworkServiceBase()

    let getDriver (driverType: NetworkDriver) =
        match drivers.TryGetValue(driverType) with
        | true, driver -> Some driver
        | false, _ -> None

    let defaultDriver () =
        drivers.[NetworkDriver.Bridge]

    override _.CreateNetwork(request, context) =
        task {
            let name = if String.IsNullOrEmpty(request.Name) then Guid.NewGuid().ToString("N") else request.Name
            let driverType = request.Driver
            let driver = getDriver driverType |> Option.defaultValue (defaultDriver ())
            match driver.Create(name, request.Subnet, request.Gateway, request.IpRange, Map.empty, Map.empty) with
            | Ok info ->
                let response = CreateNetworkResponse()
                response.Id <- info.Id
                response.Name <- info.Name
                response.Driver <- info.Driver
                response.Subnet <- info.Subnet
                response.Gateway <- info.Gateway
                response.CreatedAt <- info.CreatedAt
                return response
            | Error msg ->
                raise (Exception(sprintf "Échec de la création du réseau: %s" msg))
                return CreateNetworkResponse()
        }

    override _.RemoveNetwork(request, context) =
        task {
            let mutable foundDriver = None
            for kvp in drivers do
                match kvp.Value.Inspect(request.Id) with
                | Ok info when info.Driver = kvp.Key ->
                    foundDriver <- Some kvp.Value
                | _ -> ()
            match foundDriver with
            | Some driver ->
                match driver.Remove(request.Id, request.Force) with
                | Ok () ->
                    let r = RemoveNetworkResponse()
                    r.Success <- true
                    r.Message <- "Réseau supprimé"
                    return r
                | Error msg ->
                    let r = RemoveNetworkResponse()
                    r.Success <- false
                    r.Message <- msg
                    return r
            | None ->
                let r = RemoveNetworkResponse()
                r.Success <- false
                r.Message <- sprintf "Réseau '%s' introuvable" request.Id
                return r
        }

    override _.InspectNetwork(request, context) =
        task {
            let mutable result = None
            for kvp in drivers do
                if result.IsNone then
                    match kvp.Value.Inspect(request.Id) with
                    | Ok info ->
                        let response = InspectNetworkResponse()
                        response.Id <- info.Id
                        response.Name <- info.Name
                        response.Driver <- info.Driver
                        response.Subnet <- info.Subnet
                        response.Gateway <- info.Gateway
                        result <- Some response
                    | Error _ -> ()
            match result with
            | Some r -> return r
            | None -> return InspectNetworkResponse()
        }

    override _.ListNetworks(request, context) =
        task {
            let response = ListNetworksResponse()
            for kvp in drivers do
                match kvp.Value.List() with
                | Ok networkList ->
                    for netInfo in networkList do
                        let ni = NetworkInfo()
                        ni.Id <- netInfo.Id
                        ni.Name <- netInfo.Name
                        ni.Driver <- netInfo.Driver
                        ni.Subnet <- netInfo.Subnet
                        ni.Gateway <- netInfo.Gateway
                        ni.CreatedAt <- netInfo.CreatedAt
                        response.Networks.Add(ni)
                | Error _ -> ()
            return response
        }

    override _.ConnectContainer(request, context) =
        task {
            let driverType =
                let mutable found = None
                for kvp in drivers do
                    match kvp.Value.Inspect(request.NetworkId) with
                    | Ok info when info.Driver = kvp.Key -> found <- Some kvp.Key
                    | _ -> ()
                found |> Option.defaultValue NetworkDriver.Bridge
            let driver = getDriver driverType |> Option.defaultValue (defaultDriver ())
            let ipv4Opt = if String.IsNullOrEmpty(request.Ipv4Address) then None else Some request.Ipv4Address
            match driver.Connect(request.NetworkId, request.ContainerId, request.EndpointId, ipv4Opt, Map.empty) with
            | Ok epInfo ->
                let r = ConnectContainerResponse()
                r.EndpointId <- epInfo.EndpointId
                r.Ipv4Address <- epInfo.Ipv4Address
                r.MacAddress <- epInfo.MacAddress
                r.Message <- epInfo.Message
                return r
            | Error msg ->
                let r = ConnectContainerResponse()
                r.Message <- msg
                return r
        }

    override _.DisconnectContainer(request, context) =
        task {
            let mutable foundDriver = None
            for kvp in drivers do
                match kvp.Value.Inspect(request.NetworkId) with
                | Ok info when info.Driver = kvp.Key -> foundDriver <- Some kvp.Value
                | _ -> ()
            let driver = foundDriver |> Option.defaultValue (defaultDriver ())
            match driver.Disconnect(request.NetworkId, request.ContainerId, request.EndpointId, request.Force) with
            | Ok () ->
                let r = DisconnectContainerResponse()
                r.Success <- true
                r.Message <- "Déconnecté"
                return r
            | Error msg ->
                let r = DisconnectContainerResponse()
                r.Success <- false
                r.Message <- msg
                return r
        }

    override _.RunCniPlugin(request, context) =
        task {
            let pluginPath = request.PluginPath
            if String.IsNullOrEmpty(pluginPath) then
                let r = RunCniPluginResponse()
                r.Success <- false
                r.Message <- "Chemin du plugin CNI non spécifié"
                return r
            else
                try
                    let configJson =
                        if request.Config |> isNull |> not then
                            let config = {|
                                cniVersion = "1.0.0"
                                name = request.Config.Name
                                ``type`` = request.Config.``Type``
                                ipam = {|
                                    ``type`` = "host-local"
                                    subnet = request.Config.Subnet
                                    gateway = request.Config.Gateway
                                |}
                            |}
                            JsonSerializer.Serialize(config)
                        else "{}"
                    let psi = ProcessStartInfo()
                    psi.FileName <- pluginPath
                    psi.Arguments <- sprintf "%s --config <&0" request.Command
                    psi.RedirectStandardInput <- true
                    psi.RedirectStandardOutput <- true
                    psi.RedirectStandardError <- true
                    psi.UseShellExecute <- false
                    psi.CreateNoWindow <- true
                    let proc = System.Diagnostics.Process.Start(psi)
                    proc.StandardInput.Write(configJson)
                    proc.StandardInput.Close()
                    let stdout = proc.StandardOutput.ReadToEnd()
                    let stderr = proc.StandardError.ReadToEnd()
                    proc.WaitForExit()
                    let mutable ifname = ""
                    let mutable ipv4Addr = ""
                    let mutable gw = ""
                    let msg = if proc.ExitCode = 0 then stdout else stderr
                    if proc.ExitCode = 0 then
                        try
                            let doc = JsonDocument.Parse(stdout)
                            let root = doc.RootElement
                            let mutable tv = Unchecked.defaultof<JsonElement>
                            if root.TryGetProperty("interfaces", &tv) then
                                let interfaces = root.GetProperty("interfaces")
                                if interfaces.GetArrayLength() > 0 then
                                    let iface = interfaces.[0]
                                    let mutable iv = Unchecked.defaultof<JsonElement>
                                    if iface.TryGetProperty("name", &iv) then
                                        ifname <- iv.GetString()
                                    if iface.TryGetProperty("ips", &iv) && iface.GetProperty("ips").GetArrayLength() > 0 then
                                        let ipInfo = iface.GetProperty("ips").[0]
                                        let mutable av = Unchecked.defaultof<JsonElement>
                                        if ipInfo.TryGetProperty("address", &av) then
                                            ipv4Addr <- ipInfo.GetProperty("address").GetString()
                            let mutable dv = Unchecked.defaultof<JsonElement>
                            if root.TryGetProperty("dns", &dv) then
                                let dns = root.GetProperty("dns")
                                let mutable nsv = Unchecked.defaultof<JsonElement>
                                if dns.TryGetProperty("nameservers", &nsv) then
                                    let ns = dns.GetProperty("nameservers")
                                    if ns.GetArrayLength() > 0 then
                                        gw <- ns.[0].GetString()
                        with ex ->
                            Log.Warning(ex, "Erreur lors du parsing du résultat CNI")
                    let response = RunCniPluginResponse()
                    response.Success <- (proc.ExitCode = 0)
                    response.Ifname <- ifname
                    response.Ipv4Address <- ipv4Addr
                    response.Gateway <- gw
                    response.Message <- msg
                    return response
                with ex ->
                    let r = RunCniPluginResponse()
                    r.Success <- false
                    r.Message <- sprintf "Erreur: %s" ex.Message
                    return r
        }
