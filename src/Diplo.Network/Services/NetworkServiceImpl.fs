namespace Diplo.Network.Services

open System
open System.Collections.Generic
open System.Diagnostics
open System.Text.Json
open System.Threading.Tasks
open Grpc.Core
open Serilog
open Diplo.Abstractions
open Diplo.Grpc
open Diplo.Grpc.Network
open Diplo.Network.Plugins

type NetworkServiceImpl(drivers: IReadOnlyDictionary<NetworkDriver, INetworkDriver>) =

    let getDriver (driverType: NetworkDriver) =
        match drivers.TryGetValue(driverType) with
        | true, driver -> Some driver
        | false, _ -> None

    let defaultDriver () =
        drivers.[NetworkDriver.Bridge]

    interface INetworkService with

        member _.CreateNetwork(request, _context) =
            task {
                let name = if String.IsNullOrEmpty(request.Name) then Guid.NewGuid().ToString("N") else request.Name
                SecurityValidation.validateName name "Le nom du réseau"
                SecurityValidation.validateCidr request.Subnet "Le sous-réseau"
                SecurityValidation.validateIp request.Gateway "La passerelle"
                SecurityValidation.validateIp request.IpRange "La plage IP"
                let labels = request.Labels |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
                for kv in labels do
                    SecurityValidation.validateLabel kv.Key kv.Value
                let driverType = request.Driver
                let driver = getDriver driverType |> Option.defaultValue (defaultDriver ())
                let result = driver.Create(name, request.Subnet, request.Gateway, request.IpRange, Map.empty, labels)
                let info =
                    match result with
                    | Ok v -> v
                    | Error msg -> raise (RpcException(Status(StatusCode.Internal, sprintf "Échec de la création du réseau: %s" msg)))
                return
                    { CreateNetworkResponse.Id = info.Id
                      Name = info.Name
                      Driver = info.Driver
                      Subnet = info.Subnet
                      Gateway = info.Gateway
                      CreatedAt = info.CreatedAt }
            }

        member _.RemoveNetwork(request, _context) =
            task {
                SecurityValidation.validateId request.Id "L'identifiant du réseau"
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
                    return { RemoveNetworkResponse.Success = true; Message = "Réseau supprimé" }
                | Error msg ->
                    return { RemoveNetworkResponse.Success = false; Message = msg }
            }

        member _.InspectNetwork(request, _context) =
            task {
                SecurityValidation.validateId request.Id "L'identifiant du réseau"
                let mutable result = None
                for kvp in drivers do
                    if result.IsNone then
                        match kvp.Value.Inspect(request.Id) with
                        | Ok info ->
                            let ep =
                                { Diplo.Grpc.Network.EndpointInfo.EndpointId = info.Id
                                  ContainerId = ""
                                  Ipv4Address = info.Gateway
                                  MacAddress = ""
                                  State = EndpointState.Active }
                            let response =
                                { InspectNetworkResponse.Id = info.Id
                                  Name = info.Name
                                  Driver = info.Driver
                                  Subnet = info.Subnet
                                  Gateway = info.Gateway
                                  IpRange = ""
                                  Options = Dictionary<string, string>()
                                  Labels = Dictionary<string, string>()
                                  Endpoints = List<Diplo.Grpc.Network.EndpointInfo>()
                                  CreatedAt = "" }
                            response.Endpoints.Add(ep)
                            result <- Some response
                        | Error _ -> ()
                if result.IsNone then
                    raise (RpcException(Status(StatusCode.NotFound, sprintf "Réseau '%s' introuvable" request.Id)))
                return result.Value
            }

        member _.ListNetworks(request, _context) =
            task {
                let response =
                    { ListNetworksResponse.Networks = List<NetworkInfo>() }
                for kvp in drivers do
                    match kvp.Value.List() with
                    | Ok networkList ->
                        for netInfo in networkList do
                            let ni =
                                { NetworkInfo.Id = netInfo.Id
                                  Name = netInfo.Name
                                  Driver = netInfo.Driver
                                  Subnet = netInfo.Subnet
                                  Gateway = netInfo.Gateway
                                  EndpointCount = 0
                                  CreatedAt = netInfo.CreatedAt }
                            response.Networks.Add(ni)
                    | Error _ -> ()
                return response
            }

        member _.ConnectContainer(request, _context) =
            task {
                SecurityValidation.validateId request.NetworkId "L'identifiant du réseau"
                SecurityValidation.validateContainerId request.ContainerId
                if String.IsNullOrEmpty(request.EndpointId) |> not then
                    SecurityValidation.validateId request.EndpointId "L'identifiant de l'endpoint"
                SecurityValidation.validateIp request.Ipv4Address "L'adresse IPv4"
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
                    return
                        { ConnectContainerResponse.EndpointId = epInfo.EndpointId
                          Ipv4Address = epInfo.Ipv4Address
                          MacAddress = epInfo.MacAddress
                          Message = epInfo.Message }
                | Error msg ->
                    return { ConnectContainerResponse.EndpointId = ""; Ipv4Address = ""; MacAddress = ""; Message = msg }
            }

        member _.DisconnectContainer(request, _context) =
            task {
                SecurityValidation.validateId request.NetworkId "L'identifiant du réseau"
                SecurityValidation.validateContainerId request.ContainerId
                if String.IsNullOrEmpty(request.EndpointId) |> not then
                    SecurityValidation.validateId request.EndpointId "L'identifiant de l'endpoint"
                let mutable foundDriver = None
                for kvp in drivers do
                    match kvp.Value.Inspect(request.NetworkId) with
                    | Ok info when info.Driver = kvp.Key -> foundDriver <- Some kvp.Value
                    | _ -> ()
                let driver = foundDriver |> Option.defaultValue (defaultDriver ())
                match driver.Disconnect(request.NetworkId, request.ContainerId, request.EndpointId, request.Force) with
                | Ok () ->
                    return { DisconnectContainerResponse.Success = true; Message = "Déconnecté" }
                | Error msg ->
                    return { DisconnectContainerResponse.Success = false; Message = msg }
            }

        member _.RunCniPlugin(request, _context) =
            task {
                SecurityValidation.validateContainerId request.ContainerId
                let pluginPath = request.PluginPath
                if String.IsNullOrEmpty(pluginPath) then
                    return { RunCniPluginResponse.Success = false; Ifname = ""; Ipv4Address = ""; Gateway = ""; Message = "Chemin du plugin CNI non spécifié" }
                else
                    try
                        let resolvedPluginPath = SecurityValidation.validateCniPluginPath pluginPath
                        let command =
                            if String.IsNullOrEmpty(request.Command) then "ADD"
                            else request.Command
                        SecurityValidation.validateCniCommand command
                        let configJson =
                            if not (obj.ReferenceEquals(request.Config, null)) then
                                SecurityValidation.validateCidr request.Config.Subnet "Le sous-réseau CNI"
                                SecurityValidation.validateIp request.Config.Gateway "La passerelle CNI"
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
                            let (parsedIfname, parsedIpv4, parsedGw) = Diplo.Network.Plugins.CniParsing.parseCniResult stdout
                            ifname <- parsedIfname
                            ipv4Addr <- parsedIpv4
                            gw <- parsedGw
                        return
                            { RunCniPluginResponse.Success = (proc.ExitCode = 0)
                              Ifname = ifname
                              Ipv4Address = ipv4Addr
                              Gateway = gw
                              Message = msg }
                    with ex ->
                        Log.Error(ex, "Erreur lors de l'exécution du plugin CNI {PluginPath}", pluginPath)
                        return { RunCniPluginResponse.Success = false; Ifname = ""; Ipv4Address = ""; Gateway = ""; Message = "Erreur lors de l'exécution du plugin CNI" }
            }

        member _.PruneNetworks(request, _context) =
            task {
                let deleted = ResizeArray<string>()
                for kvp in drivers do
                    match kvp.Value.Prune() with
                    | Ok ids -> deleted.AddRange(ids)
                    | Error msg -> Log.Warning("Erreur lors du nettoyage des réseaux du pilote {Driver}: {Error}", kvp.Key, msg)
                return
                    { PruneNetworksResponse.NetworksDeleted = List<string>(deleted)
                      Count = deleted.Count
                      Message = sprintf "%d réseau(x) supprimé(s)" deleted.Count }
            }
