namespace DiploWalker.Network.Plugins

open System
open System.Collections.Concurrent
open System.Runtime.ExceptionServices
open Serilog
open DiploWalker.Abstractions
open DiploWalker.Abstractions.NetworkConfig

type private GrpcNetworkDriver = DiploWalker.Grpc.Network.NetworkDriver

type IHnsProvider =
    abstract member CreateNetwork: name: string * subnet: string -> unit
    abstract member RemoveNetwork: name: string -> unit
    abstract member CreateNat: natName: string * subnet: string -> unit
    abstract member RemoveNat: natName: string -> unit

type HnsPowerShellProvider() =

    interface IHnsProvider with
        member _.CreateNetwork(name, subnet) =
            runPowershellWithArgs "New-HNSNetwork" [ "-Type", "nat"; "-Name", name; "-Subnet", subnet ]
            |> ignore

        member _.RemoveNetwork(name) =
            try
                runPowershellWithArgs "Remove-HNSNetwork" [ "-Name", name ] |> ignore
            with ex ->
                Log.Warning(ex, "Erreur lors de la suppression du rÃ©seau HNS {Name}", name)

        member _.CreateNat(natName, subnet) =
            let existing =
                try
                    let result =
                        runPowershellWithArgs "Get-NetNat" [ "-Name", natName; "-ErrorAction", "SilentlyContinue" ]

                    not (String.IsNullOrWhiteSpace(result))
                with ex ->
                    Log.Debug(ex, "VÃ©rification du NAT {NatName} Ã©chouÃ©e, hypothÃ¨se : inexistant", natName)
                    false

            if not existing then
                runPowershellWithArgs "New-NetNat" [ "-Name", natName; "-InternalIPInterfaceAddressPrefix", subnet ]
                |> ignore

        member _.RemoveNat(natName) =
            try
                runPowershellWithArgs "Remove-NetNat" [ "-Name", natName; "-Confirm", "$false" ]
                |> ignore
            with ex ->
                Log.Warning(ex, "Erreur lors de la suppression du NetNat {NatName}", natName)

type PodInfo =
    { DriverInfo: NetworkDriverInfo
      MaxContainers: int }

type PodDriver(hns: IHnsProvider) =

    let networks = ConcurrentDictionary<string, PodInfo>()

    let endpoints =
        ConcurrentDictionary<string, ConcurrentDictionary<string, EndpointInfo>>()

    let defaultMaxContainers = 32
    let maxContainersLimit = 256

    let parseMaxContainers (options: Map<string, string>) =
        match options.TryGetValue("max_containers") with
        | true, value ->
            match Int32.TryParse(value) with
            | true, n when n > 0 && n <= maxContainersLimit -> n
            | _ -> defaultMaxContainers
        | _ -> defaultMaxContainers

    let getAvailableSubnet () =
        let usedPrefixes = getUsedPrefixes ()
        let config = loadConfig None
        findAvailableSubnet config.SubnetCandidates usedPrefixes |> Result.defaultWith failwith

    let getDefaultGateway (subnet: string) = deriveGateway subnet

    let getEndpointCount (networkId: string) =
        match endpoints.TryGetValue(networkId) with
        | true, eps -> eps.Count
        | false, _ -> 0

    new() = PodDriver(HnsPowerShellProvider() :> IHnsProvider)

    interface INetworkDriver with
        member _.DriverType = GrpcNetworkDriver.``Pod``

        member _.Create(name, subnet, gateway, _ipRange, options, labels) =
            try
                SecurityValidation.validateName name "Le nom du Pod"

                for kvp in labels do
                    SecurityValidation.validateLabel kvp.Key kvp.Value

                if not (String.IsNullOrEmpty(subnet)) then
                    SecurityValidation.validateCidr subnet "Le sous-rÃ©seau du Pod"

                if not (String.IsNullOrEmpty(gateway)) then
                    SecurityValidation.validateIp gateway "La passerelle du Pod"

                let maxContainers = parseMaxContainers options

                let actualSubnet =
                    if String.IsNullOrEmpty(subnet) then
                        getAvailableSubnet ()
                    else
                        subnet

                let actualGateway =
                    if String.IsNullOrEmpty(gateway) then
                        getDefaultGateway actualSubnet |> Result.defaultWith failwith
                    else
                        gateway

                // Rollback : un Ã©chec du NAT aprÃ¨s crÃ©ation du rÃ©seau HNS
                // laisserait sinon un HNSNetwork orphelin sur l'hÃ´te.
                try
                    hns.CreateNetwork(name, actualSubnet)
                with ex ->
                    Log.Error(ex, "CrÃ©ation du rÃ©seau HNS {Name} Ã©chouÃ©e", name)
                    ExceptionDispatchInfo.Capture(ex).Throw()

                try
                    hns.CreateNat(sprintf "%sNat" name, actualSubnet)
                with natEx ->
                    Log.Error(natEx, "CrÃ©ation du NAT {Name} Ã©chouÃ©e : rollback du rÃ©seau HNS", sprintf "%sNat" name)
                    hns.RemoveNetwork(name)
                    ExceptionDispatchInfo.Capture(natEx).Throw()

                let id = Guid.NewGuid().ToString("N")

                let info =
                    { Id = id
                      Name = name
                      Driver = GrpcNetworkDriver.``Pod``
                      Subnet = actualSubnet
                      Gateway = actualGateway
                      Options = options
                      Labels = labels
                      CreatedAt = DateTime.UtcNow.ToString("o") }

                let podInfo =
                    { DriverInfo = info
                      MaxContainers = maxContainers }

                networks.TryAdd(id, podInfo) |> ignore
                endpoints.TryAdd(id, ConcurrentDictionary<string, EndpointInfo>()) |> ignore

                Log.Information(
                    "Pod {Name} crÃ©Ã© avec sous-rÃ©seau {Subnet} (max {Max} conteneurs)",
                    name,
                    actualSubnet,
                    maxContainers
                )

                Ok info
            with ex ->
                Log.Error(ex, "Erreur lors de la crÃ©ation du Pod {Name}", name)
                Error "Erreur lors de la crÃ©ation du Pod"

        member _.Remove(id, force) =
            match networks.TryGetValue(id) with
            | true, podInfo ->
                let epCount = getEndpointCount id

                // SÃ©mantique docker : refuser sans -f lorsqu'il reste des
                // endpoints actifs (le compte ne servait qu'Ã  un log).
                if epCount > 0 && not force then
                    Error(
                        sprintf
                            "Le Pod '%s' contient %d endpoint(s) actif(s) â€” utilisez force pour forcer la suppression"
                            podInfo.DriverInfo.Name
                            epCount
                    )
                else
                    try
                        hns.RemoveNetwork(podInfo.DriverInfo.Name)
                        hns.RemoveNat(sprintf "%sNat" podInfo.DriverInfo.Name)
                        networks.TryRemove(id) |> ignore
                        endpoints.TryRemove(id) |> ignore
                        Ok()
                    with ex ->
                        Log.Error(ex, "Erreur lors de la suppression du Pod {Id}", id)
                        Error "Erreur lors de la suppression du Pod"
            | false, _ -> Error(sprintf "Pod '%s' introuvable" id)

        member _.Inspect(id) =
            match networks.TryGetValue(id) with
            | true, podInfo -> Ok podInfo.DriverInfo
            | false, _ -> Error(sprintf "Pod '%s' introuvable" id)

        member _.List() =
            networks.Values |> Seq.map (fun p -> p.DriverInfo) |> Seq.toList |> Ok

        member _.Connect(networkId, containerId, endpointId, ipv4Address, _options) =
            match networks.TryGetValue(networkId) with
            | false, _ -> Error(sprintf "Pod '%s' introuvable" networkId)
            | true, podInfo ->
                try
                    SecurityValidation.validateContainerId containerId
                    let epCount = getEndpointCount networkId

                    if epCount >= podInfo.MaxContainers then
                        Error(
                            sprintf
                                "Le Pod '%s' a atteint sa limite de %d conteneurs"
                                podInfo.DriverInfo.Name
                                podInfo.MaxContainers
                        )
                    else
                        let actualEndpointId =
                            if String.IsNullOrEmpty(endpointId) then
                                Guid.NewGuid().ToString("N")
                            else
                                endpointId

                        let shortId = containerId.Substring(0, min 8 containerId.Length)
                        let epName = sprintf "pod-%s-%s" podInfo.DriverInfo.Name shortId
                        let mutable assignedIp = ""

                        match ipv4Address with
                        | Some ip when not (String.IsNullOrEmpty(ip)) ->
                            SecurityValidation.validateIp ip "L'adresse IP"
                            assignedIp <- ip
                        | _ -> assignedIp <- "DHCP"

                        let epInfo =
                            { EndpointId = actualEndpointId
                              ContainerId = containerId
                              Ipv4Address = assignedIp
                              MacAddress = ""
                              Message =
                                sprintf
                                    "ConnectÃ© au Pod '%s' (%d/%d)"
                                    podInfo.DriverInfo.Name
                                    (epCount + 1)
                                    podInfo.MaxContainers }

                        let podEndpoints = endpoints.GetOrAdd(networkId, fun _ -> ConcurrentDictionary())

                        // Course check-then-add : vÃ©rifier le compteur APRÃˆS
                        // l'ajout et dÃ©faire si la limite est dÃ©passÃ©e â€” N
                        // connexions simultanÃ©es voyaient toutes epCount < max.
                        if not (podEndpoints.TryAdd(actualEndpointId, epInfo)) then
                            Error(sprintf "L'endpoint '%s' existe dÃ©jÃ " actualEndpointId)
                        else
                            let newCount = getEndpointCount networkId

                            if newCount > podInfo.MaxContainers then
                                podEndpoints.TryRemove(actualEndpointId) |> ignore

                                Error(
                                    sprintf
                                        "Le Pod '%s' a atteint sa limite de %d conteneurs"
                                        podInfo.DriverInfo.Name
                                        podInfo.MaxContainers
                                )
                            else
                                Ok epInfo
                with ex ->
                    Log.Error(
                        ex,
                        "Erreur de connexion au Pod {NetworkId} pour le conteneur {ContainerId}",
                        networkId,
                        containerId
                    )

                    Error "Erreur de connexion au Pod"

        member _.Disconnect(networkId, containerId, endpointId, _force) =
            match networks.TryGetValue(networkId) with
            | false, _ -> Error(sprintf "Pod '%s' introuvable" networkId)
            | true, _ ->
                try
                    match endpoints.TryGetValue(networkId) with
                    | true, podEndpoints ->
                        if String.IsNullOrEmpty(endpointId) then
                            let removed =
                                podEndpoints.Values
                                |> Seq.filter (fun ep -> ep.ContainerId = containerId)
                                |> Seq.map (fun ep -> podEndpoints.TryRemove(ep.EndpointId))
                                |> Seq.fold (fun acc (removed, _) -> acc || removed) false

                            if not removed then
                                Log.Warning(
                                    "Aucun endpoint trouvÃ© pour le conteneur {ContainerId} dans le Pod {NetworkId}",
                                    containerId,
                                    networkId
                                )
                        else
                            podEndpoints.TryRemove(endpointId) |> ignore

                        Ok()
                    | false, _ -> Ok()
                with ex ->
                    Log.Error(ex, "Erreur de dÃ©connexion du Pod {NetworkId}", networkId)
                    Error "Erreur de dÃ©connexion du Pod"

        /// Nettoyage RÃ‰EL : supprime les Pods sans endpoint actif et retourne
        /// uniquement les identifiants rÃ©ellement supprimÃ©s.
        member this.Prune() =
            let removed = ResizeArray<string>()

            for kvp in networks do
                if getEndpointCount kvp.Key = 0 then
                    match (this :> INetworkDriver).Remove(kvp.Key, false) with
                    | Ok() -> removed.Add(kvp.Key)
                    | Error msg -> Log.Warning("Prune du Pod {Id} impossible : {Error}", kvp.Key, msg)

            Ok(removed |> Seq.toList)


