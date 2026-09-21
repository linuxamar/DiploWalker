namespace DiploWalker.Network.Services

open System
open System.ServiceModel
open System.Collections.Generic
open System.Text.Json
open System.Threading.Tasks
open Grpc.Core
open Serilog
open DiploWalker.Abstractions
open DiploWalker.Grpc
open DiploWalker.Grpc.Network
open DiploWalker.Network.Plugins

[<ServiceContract(Name = "INetworkService")>]
type NetworkServiceImpl(drivers: IReadOnlyDictionary<NetworkDriver, INetworkDriver>) =

    let getDriver (driverType: NetworkDriver) =
        match drivers.TryGetValue(driverType) with
        | true, driver -> Some driver
        | false, _ -> None

    let defaultDriver () =
        match drivers.TryGetValue(NetworkDriver.Bridge) with
        | true, d -> d
        | false, _ ->
            raise (
                RpcException(Status(StatusCode.Internal, "Driver Bridge non enregistrÃ© dans le registre"))
            )

    interface INetworkService with

        member _.CreateNetwork(request, _context) =
            task {
                let name =
                    if String.IsNullOrEmpty(request.Name) then
                        Guid.NewGuid().ToString("N")
                    else
                        request.Name

                SecurityValidation.validateName name "Le nom du rÃ©seau"
                SecurityValidation.validateCidr request.Subnet "Le sous-rÃ©seau"
                SecurityValidation.validateIp request.Gateway "La passerelle"

                // IpRange est une PLAGE CIDR (Â« 10.0.0.0/24 Â»), pas une IP simple :
                // validateIp rejetait toute valeur lÃ©gitime.
                SecurityValidation.validateCidr request.IpRange "La plage IP"

                // Valeur d'enum hors plage (protobuf transporte un int32) :
                // rejeter plutÃ´t que retomber silencieusement sur Bridge.
                if not (System.Enum.IsDefined(typeof<NetworkDriver>, request.Driver)) then
                    raise (
                        RpcException(
                            Status(
                                StatusCode.InvalidArgument,
                                sprintf "Valeur de driver inconnue : %d" (int request.Driver)
                            )
                        )
                    )

                let labels = request.Labels |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq

                for kv in labels do
                    SecurityValidation.validateLabel kv.Key kv.Value

                let options = request.Options |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq

                let driverType = request.Driver
                let driver = getDriver driverType |> Option.defaultValue (defaultDriver ())

                // Le chemin de plugin CNI fourni est injectÃ© comme option
                // standard consommÃ©e par CustomCniDriver.
                let options =
                    if String.IsNullOrEmpty(request.CniPluginPath) then
                        options
                    else
                        options.Add("plugin_path", request.CniPluginPath)

                let result =
                    driver.Create(name, request.Subnet, request.Gateway, request.IpRange, options, labels)

                let info =
                    match result with
                    | Ok v -> v
                    | Error msg ->
                        raise (
                            RpcException(Status(StatusCode.Internal, sprintf "Ã‰chec de la crÃ©ation du rÃ©seau: %s" msg))
                        )

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
                SecurityValidation.validateId request.Id "L'identifiant du rÃ©seau"

                let foundDriver =
                    drivers
                    |> Seq.tryPick (fun kvp ->
                        match kvp.Value.Inspect(request.Id) with
                        | Ok info when info.Driver = kvp.Key -> Some kvp.Value
                        | _ -> None)

                if foundDriver.IsNone then
                    raise (RpcException(Status(StatusCode.NotFound, sprintf "RÃ©seau '%s' introuvable" request.Id)))

                let driver = foundDriver.Value

                match driver.Remove(request.Id, request.Force) with
                | Ok() ->
                    return
                        { RemoveNetworkResponse.Success = true
                          Message = "RÃ©seau supprimÃ©" }
                | Error msg ->
                    return
                        { RemoveNetworkResponse.Success = false
                          Message = msg }
            }

        member _.InspectNetwork(request, _context) =
            task {
                SecurityValidation.validateId request.Id "L'identifiant du rÃ©seau"

                let result =
                    drivers
                    |> Seq.tryPick (fun kvp ->
                        match kvp.Value.Inspect(request.Id) with
                        | Ok info -> Some info
                        | Error _ -> None)

                if result.IsNone then
                    raise (RpcException(Status(StatusCode.NotFound, sprintf "RÃ©seau '%s' introuvable" request.Id)))

                let info = result.Value

                // DonnÃ©es honnÃªtes : CreatedAt rÃ©el, endpoints non fabriquÃ©s.
                return
                    { InspectNetworkResponse.Id = info.Id
                      Name = info.Name
                      Driver = info.Driver
                      Subnet = info.Subnet
                      Gateway = info.Gateway
                      IpRange = ""
                      Options = Dictionary<string, string>()
                      Labels = Dictionary<string, string>()
                      Endpoints = List<DiploWalker.Grpc.Network.EndpointInfo>()
                      CreatedAt = info.CreatedAt }
            }

        member _.ListNetworks(request, _context) =
            task {
                let response = { ListNetworksResponse.Networks = List<NetworkInfo>() }

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
                    | Error msg ->
                        // Un driver en Ã©chec ne doit pas passer inaperÃ§u : le
                        // client croirait voir l'inventaire complet.
                        Log.Warning(
                            "Liste des rÃ©seaux du driver {Driver} indisponible : {Error}",
                            kvp.Key,
                            msg
                        )

                // M14 : borne de liste serveur (cf. ListContainers) â€” protÃ¨ge la
                // rÃ©ponse contre un inventaire dÃ©mesurÃ©.
                if response.Networks.Count > ServiceGuards.MaxListItems then
                    Log.Warning(
                        "Liste des rÃ©seaux tronquÃ©e Ã  {Limit} Ã©lÃ©ments (reÃ§u {Count})",
                        ServiceGuards.MaxListItems,
                        response.Networks.Count
                    )

                    response.Networks.RemoveRange(
                        ServiceGuards.MaxListItems,
                        response.Networks.Count - ServiceGuards.MaxListItems
                    )

                return response
            }

        member _.ConnectContainer(request, _context) =
            task {
                SecurityValidation.validateId request.NetworkId "L'identifiant du rÃ©seau"
                SecurityValidation.validateContainerId request.ContainerId

                if String.IsNullOrEmpty(request.EndpointId) |> not then
                    SecurityValidation.validateId request.EndpointId "L'identifiant de l'endpoint"

                SecurityValidation.validateIp request.Ipv4Address "L'adresse IPv4"

                let foundDriverType =
                    drivers
                    |> Seq.tryPick (fun kvp ->
                        match kvp.Value.Inspect(request.NetworkId) with
                        | Ok info when info.Driver = kvp.Key -> Some kvp.Key
                        | _ -> None)

                let driver =
                    match foundDriverType with
                    | Some dt -> getDriver dt |> Option.defaultValue (defaultDriver ())
                    | None ->
                        // NotFound explicite : masquer l'absence derriÃ¨re une
                        // rÃ©ponse Â« succÃ¨s Â» au message textuel casse le contrat.
                        raise (
                            RpcException(
                                Status(StatusCode.NotFound, sprintf "RÃ©seau '%s' introuvable" request.NetworkId)
                            )
                        )

                let connectOptions = request.Options |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq

                let ipv4Opt =
                    if String.IsNullOrEmpty(request.Ipv4Address) then
                        None
                    else
                        Some request.Ipv4Address

                match
                    driver.Connect(request.NetworkId, request.ContainerId, request.EndpointId, ipv4Opt, connectOptions)
                with
                | Ok epInfo ->
                    return
                        { ConnectContainerResponse.EndpointId = epInfo.EndpointId
                          Ipv4Address = epInfo.Ipv4Address
                          MacAddress = epInfo.MacAddress
                          Message = epInfo.Message }
                | Error msg ->
                    return
                        { ConnectContainerResponse.EndpointId = ""
                          Ipv4Address = ""
                          MacAddress = ""
                          Message = msg }
            }

        member _.DisconnectContainer(request, _context) =
            task {
                SecurityValidation.validateId request.NetworkId "L'identifiant du rÃ©seau"
                SecurityValidation.validateContainerId request.ContainerId

                if String.IsNullOrEmpty(request.EndpointId) |> not then
                    SecurityValidation.validateId request.EndpointId "L'identifiant de l'endpoint"

                let foundDriver =
                    drivers
                    |> Seq.tryPick (fun kvp ->
                        match kvp.Value.Inspect(request.NetworkId) with
                        | Ok info when info.Driver = kvp.Key -> Some kvp.Value
                        | _ -> None)

                let driver =
                    match foundDriver with
                    | Some d -> d
                    | None ->
                        raise (
                            RpcException(
                                Status(StatusCode.NotFound, sprintf "RÃ©seau '%s' introuvable" request.NetworkId)
                            )
                        )

                match driver.Disconnect(request.NetworkId, request.ContainerId, request.EndpointId, request.Force) with
                | Ok() ->
                    return
                        { DisconnectContainerResponse.Success = true
                          Message = "DÃ©connectÃ©" }
                | Error msg ->
                    return
                        { DisconnectContainerResponse.Success = false
                          Message = msg }
            }

        member _.RunCniPlugin(request, _context) =
            task {
                SecurityValidation.validateContainerId request.ContainerId
                let pluginPath = request.PluginPath

                if String.IsNullOrEmpty(pluginPath) then
                    return
                        { RunCniPluginResponse.Success = false
                          Ifname = ""
                          Ipv4Address = ""
                          Gateway = ""
                          Message = "Chemin du plugin CNI non spÃ©cifiÃ©" }
                else
                    let resolvedPluginPath = SecurityValidation.validateCniPluginPath pluginPath
                    SecurityValidation.validateNetnsPath request.NetnsPath "Le chemin netns"

                    let command =
                        if String.IsNullOrEmpty(request.Command) then
                            "ADD"
                        else
                            request.Command

                    SecurityValidation.validateCniCommand command
                    // Propager la forme NORMALISÃ‰E : la spec CNI est sensible Ã 
                    // la casse, Â« add Â» ou Â« ADD Â» avec espaces serait rejetÃ©.
                    let normalizedCommand = command.Trim().ToUpperInvariant()

                    try
                        let configJson =
                            if not (obj.ReferenceEquals(request.Config, null)) then
                                SecurityValidation.validateCidr request.Config.Subnet "Le sous-rÃ©seau CNI"
                                SecurityValidation.validateIp request.Config.Gateway "La passerelle CNI"

                                let config =
                                    {| cniVersion = "1.0.0"
                                       name = request.Config.Name
                                       ``type`` = request.Config.``Type``
                                       ipam =
                                        {| ``type`` = "host-local"
                                           subnet = request.Config.Subnet
                                           gateway = request.Config.Gateway |} |}

                                JsonSerializer.Serialize(config)
                            else
                                "{}"

                        let args =
                            [ normalizedCommand
                              "--container-id"
                              request.ContainerId
                              "--netns"
                              request.NetnsPath ]

                        let code, stdout, stderr =
                            ProcessExec.runWithResult resolvedPluginPath args (Some 60_000) (Some configJson) None

                        let mutable ifname = ""
                        let mutable ipv4Addr = ""
                        let mutable gw = ""
                        let msg = if code = 0 then stdout else stderr

                        if code = 0 then
                            let (parsedIfname, parsedIpv4, parsedGw) =
                                DiploWalker.Network.Plugins.CniParsing.parseCniResult stdout

                            ifname <- parsedIfname
                            ipv4Addr <- parsedIpv4
                            gw <- parsedGw

                        return
                            { RunCniPluginResponse.Success = (code = 0)
                              Ifname = ifname
                              Ipv4Address = ipv4Addr
                              Gateway = gw
                              Message = msg }
                    with
                    | :? RpcException as rpcEx -> return raise rpcEx
                    | ex ->
                        Log.Error(ex, "Erreur lors de l'exÃ©cution du plugin CNI {Plugin}", System.IO.Path.GetFileName(pluginPath))

                        return
                            { RunCniPluginResponse.Success = false
                              Ifname = ""
                              Ipv4Address = ""
                              Gateway = ""
                              Message = "Erreur lors de l'exÃ©cution du plugin CNI" }
            }

        member _.PruneNetworks(request, _context) =
            task {
                let deleted = ResizeArray<string>()

                for kvp in drivers do
                    match kvp.Value.Prune() with
                    | Ok ids -> deleted.AddRange(ids)
                    | Error msg ->
                        Log.Warning("Erreur lors du nettoyage des rÃ©seaux du pilote {Driver}: {Error}", kvp.Key, msg)

                return
                    { PruneNetworksResponse.NetworksDeleted = List<string>(deleted)
                      Count = deleted.Count
                      Message = sprintf "%d rÃ©seau(x) supprimÃ©(s)" deleted.Count }
            }


