namespace Diplo.Network.Services

open System
open System.Collections.Generic
open System.Diagnostics
open System.Text.Json
open System.Threading.Tasks
open Grpc.Core
open Serilog
open Diplo.Abstractions
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
            if String.IsNullOrEmpty(request.Name) |> not && request.Name.Length > 63 then
                raise (RpcException(Status(StatusCode.InvalidArgument, "Le nom du réseau ne doit pas dépasser 63 caractères")))
            let name = if String.IsNullOrEmpty(request.Name) then Guid.NewGuid().ToString("N") else request.Name
            let driverType = request.Driver
            let driver = getDriver driverType |> Option.defaultValue (defaultDriver ())
            let result = driver.Create(name, request.Subnet, request.Gateway, request.IpRange, Map.empty, Map.empty)
            let info =
                match result with
                | Ok v -> v
                | Error msg -> raise (RpcException(Status(StatusCode.Internal, sprintf "Échec de la création du réseau: %s" msg)))
            let response = CreateNetworkResponse()
            response.Id <- info.Id
            response.Name <- info.Name
            response.Driver <- info.Driver
            response.Subnet <- info.Subnet
            response.Gateway <- info.Gateway
            response.CreatedAt <- info.CreatedAt
            return response
        }

    override _.RemoveNetwork(request, context) =
        task {
            if String.IsNullOrEmpty(request.Id) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du réseau est requis")))
            let mutable foundDriver = None
            for kvp in drivers do
                match kvp.Value.Inspect(request.Id) with
                | Ok info when info.Driver = kvp.Key ->
                    foundDriver <- Some kvp.Value
                | _ -> ()
            if foundDriver.IsNone then
                raise (RpcException(Status(StatusCode.NotFound, sprintf "Réseau '%s' introuvable" request.Id)))
            let driver = foundDriver.Value
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
        }

    override _.InspectNetwork(request, context) =
        task {
            if String.IsNullOrEmpty(request.Id) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du réseau est requis")))
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
            if result.IsNone then
                raise (RpcException(Status(StatusCode.NotFound, sprintf "Réseau '%s' introuvable" request.Id)))
            return result.Value
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
            if String.IsNullOrEmpty(request.NetworkId) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du réseau est requis")))
            if String.IsNullOrEmpty(request.ContainerId) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du conteneur est requis")))
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
            if String.IsNullOrEmpty(request.NetworkId) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du réseau est requis")))
            if String.IsNullOrEmpty(request.ContainerId) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du conteneur est requis")))
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
                    let resolvedPluginPath = SecurityValidation.validateCniPluginPath pluginPath
                    let command =
                        if String.IsNullOrEmpty(request.Command) then "ADD"
                        else request.Command
                    SecurityValidation.validateCniCommand command
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
                    psi.FileName <- resolvedPluginPath
                    psi.ArgumentList.Add(command) |> ignore
                    psi.ArgumentList.Add("--config") |> ignore
                    psi.RedirectStandardInput <- true
                    psi.RedirectStandardOutput <- true
                    psi.RedirectStandardError <- true
                    psi.UseShellExecute <- false
                    psi.CreateNoWindow <- true
                    use proc = System.Diagnostics.Process.Start(psi)
                    if proc |> isNull then failwithf "Impossible de démarrer le plugin CNI: %s" pluginPath
                    proc.StandardInput.Write(configJson)
                    proc.StandardInput.Close()
                    let stdout = proc.StandardOutput.ReadToEnd()
                    let stderr = proc.StandardError.ReadToEnd()
                    if not (proc.WaitForExit(60_000)) then
                        try proc.Kill(true) with _ -> ()
                        failwith "Délai d'attente dépassé pour le plugin CNI (60s)"
                    let mutable ifname = ""
                    let mutable ipv4Addr = ""
                    let mutable gw = ""
                    let msg = if proc.ExitCode = 0 then stdout else stderr
                    if proc.ExitCode = 0 then
                        let (parsedIfname, parsedIpv4, parsedGw) = CniParsing.parseCniResult stdout
                        ifname <- parsedIfname
                        ipv4Addr <- parsedIpv4
                        gw <- parsedGw
                    let response = RunCniPluginResponse()
                    response.Success <- (proc.ExitCode = 0)
                    response.Ifname <- ifname
                    response.Ipv4Address <- ipv4Addr
                    response.Gateway <- gw
                    response.Message <- msg
                    return response
                with ex ->
                    Log.Error(ex, "Erreur lors de l'exécution du plugin CNI {PluginPath}", pluginPath)
                    let r = RunCniPluginResponse()
                    r.Success <- false
                    r.Message <- "Erreur lors de l'exécution du plugin CNI"
                    return r
        }
