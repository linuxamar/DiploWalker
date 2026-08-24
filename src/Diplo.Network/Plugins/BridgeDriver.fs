namespace Diplo.Network.Plugins

open System
open System.Collections.Concurrent
open Serilog
open Diplo.Abstractions
open Diplo.Abstractions.NetworkConfig
open Diplo.Grpc.Network

type BridgeNetworkDriver() =

    let networks = ConcurrentDictionary<string, NetworkDriverInfo>()

    let getAvailableSubnet () =
        let config = loadConfig None
        let existing = networks.Values |> Seq.map (fun n -> n.Subnet) |> Set.ofSeq
        findAvailableSubnet config.SubnetCandidates existing

    let getDefaultGateway (subnet: string) =
        deriveGateway subnet

    interface INetworkDriver with
        member _.DriverType = NetworkDriver.Bridge

        member _.Create(name, subnet, gateway, _ipRange, _options, labels) =
            try
                SecurityValidation.validateName name "Le nom du réseau"
                if not (String.IsNullOrEmpty(subnet)) then
                    SecurityValidation.validateCidr subnet "Le sous-réseau"
                if not (String.IsNullOrEmpty(gateway)) then
                    SecurityValidation.validateIp gateway "La passerelle"
                let actualSubnet = if String.IsNullOrEmpty(subnet) then getAvailableSubnet () else subnet
                let actualGateway = if String.IsNullOrEmpty(gateway) then getDefaultGateway actualSubnet else gateway
                // Idempotence : vérifier si le switch existe déjà avant de le créer
                let existingSwitch =
                    try
                        let result =
                            runPowershellWithArgs "Get-VMSwitch"
                                [ "-Name", name; "-ErrorAction", "SilentlyContinue" ]
                        not (String.IsNullOrWhiteSpace(result))
                    with ex ->
                        Log.Debug(ex, "Vérification du switch VM {Name} échouée, hypothèse : inexistant", name)
                        false
                if not existingSwitch then
                    runPowershellWithArgs "New-VMSwitch"
                        [ "-Name", name; "-SwitchType", "Internal"; "-AllowManagementOS", "$true" ]
                    |> ignore
                if not (String.IsNullOrEmpty(actualSubnet)) then
                    // Idempotence : vérifier si le NAT existe déjà
                    let natName = sprintf "%sNat" name
                    let existingNat =
                        try
                            let result =
                                runPowershellWithArgs "Get-NetNat"
                                    [ "-Name", natName; "-ErrorAction", "SilentlyContinue" ]
                            not (String.IsNullOrWhiteSpace(result))
                        with ex ->
                            Log.Debug(ex, "Vérification du NAT {NatName} échouée, hypothèse : inexistant", natName)
                            false
                    if not existingNat then
                        runPowershellWithArgs "New-NetNat"
                            [ "-Name", natName; "-InternalIPInterfaceAddressPrefix", actualSubnet ]
                        |> ignore
                let id = Guid.NewGuid().ToString("N")
                let info = {
                    Id = id
                    Name = name
                    Driver = NetworkDriver.Bridge
                    Subnet = actualSubnet
                    Gateway = actualGateway
                    Options = Map.empty
                    Labels = labels
                    CreatedAt = DateTime.UtcNow.ToString("o")
                }
                networks.TryAdd(id, info) |> ignore
                Ok info
            with ex ->
                Log.Error(ex, "Erreur lors de la création du bridge {Name}", name)
                Error "Erreur lors de la création du bridge"

        member _.Remove(id, _force) =
            match networks.TryGetValue(id) with
            | true, netInfo ->
                try
                    runPowershellWithArgs "Remove-VMSwitch"
                        [ "-Name", netInfo.Name; "-Force", "$true" ] |> ignore
                    try runPowershellWithArgs "Remove-NetNat"
                            [ "-Name", sprintf "%sNat" netInfo.Name; "-Confirm", "$false" ] |> ignore
                    with ex -> Log.Warning(ex, "Erreur lors de la suppression du NAT {NatName}", netInfo.Name + "Nat")
                    networks.TryRemove(id) |> ignore
                    Ok ()
                with ex ->
                    Log.Error(ex, "Erreur lors de la suppression du bridge {Id}", id)
                    Error "Erreur lors de la suppression du bridge"
            | false, _ -> Error (sprintf "Bridge '%s' introuvable" id)

        member _.Inspect(id) =
            match networks.TryGetValue(id) with
            | true, info -> Ok info
            | false, _ -> Error (sprintf "Bridge '%s' introuvable" id)

        member _.List() =
            networks.Values |> Seq.toList |> Ok

        member _.Connect(networkId, containerId, endpointId, ipv4Address, _options) =
            match networks.TryGetValue(networkId) with
            | false, _ -> Error (sprintf "Bridge '%s' introuvable" networkId)
            | true, netInfo ->
                try
                    SecurityValidation.validateContainerId containerId
                    SecurityValidation.validateName netInfo.Name "Le nom du bridge"
                    let actualEndpointId = if String.IsNullOrEmpty(endpointId) then
                                               Guid.NewGuid().ToString("N")
                                           else endpointId
                    let shortId = containerId.Substring(0, min 8 containerId.Length)
                    let adapterName = sprintf "vEthernet (%s-%s)" netInfo.Name shortId
                    runPowershellWithArgs "Add-VMNetworkAdapter"
                        [ "-SwitchName", netInfo.Name; "-Name", adapterName; "-ManagementOS", "$true" ] |> ignore
                    let mutable assignedIp = ""
                    match ipv4Address with
                    | Some ip when not (String.IsNullOrEmpty(ip)) ->
                        SecurityValidation.validateIp ip "L'adresse IP"
                        runPowershellWithArgs "New-NetIPAddress"
                            [ "-InterfaceAlias", adapterName; "-IPAddress", ip ] |> ignore
                        assignedIp <- ip
                    | _ -> assignedIp <- "DHCP"
                    let mac =
                        runPowershellScript
                            "Get-VMNetworkAdapter -Name $p0 | Select-Object -ExpandProperty MacAddress"
                            [ "Name", adapterName ]
                        |> fun s -> s.Trim()
                    Ok {
                        EndpointId = actualEndpointId
                        ContainerId = containerId
                        Ipv4Address = assignedIp
                        MacAddress = mac
                        Message = sprintf "Connecté au bridge '%s'" netInfo.Name
                    }
                with ex ->
                    Log.Error(ex, "Erreur de connexion au bridge {NetworkId} pour le conteneur {ContainerId}", networkId, containerId)
                    Error "Erreur de connexion au bridge"

        member _.Disconnect(networkId, containerId, _endpointId, _force) =
            match networks.TryGetValue(networkId) with
            | false, _ -> Error (sprintf "Bridge '%s' introuvable" networkId)
            | true, netInfo ->
                try
                    SecurityValidation.validateContainerId containerId
                    let shortId = containerId.Substring(0, min 8 containerId.Length)
                    let adapterName = sprintf "vEthernet (%s-%s)" netInfo.Name shortId
                    runPowershellWithArgs "Remove-VMNetworkAdapter"
                        [ "-Name", adapterName; "-ManagementOS", "$true" ] |> ignore
                    Ok ()
                with ex ->
                    Log.Error(ex, "Erreur de déconnexion du bridge {NetworkId}", networkId)
                    Error "Erreur de déconnexion du bridge"

        member _.Prune() =
            let ids = networks.Keys |> Seq.toList
            Ok ids
