namespace Diplo.Network.Plugins

open System
open System.Collections.Concurrent
open Serilog
open Diplo.Abstractions
open Diplo.Abstractions.NetworkConfig
open Diplo.Grpc.Network

type BridgeNetworkDriver() =

    let networks = ConcurrentDictionary<string, NetworkDriverInfo>()

    // Endpoints suivis par réseau (endpointId -> containerId) : nécessaire au
    // prune réel (un réseau avec endpoints actifs ne doit pas être supprimé)
    // et au nettoyage lors des déconnexions.
    let endpoints =
        ConcurrentDictionary<string, ConcurrentDictionary<string, string>>()

    // Sérialise Create/Remove/Connect/Disconnect : le ConcurrentDictionary ne
    // protège que le registre, pas les ressources Hyper-V externes (TOCTOU
    // switch/NAT, double allocation de sous-réseau).
    let stateLock = obj ()

    let getAvailableSubnet () =
        let config = loadConfig None
        let existing = networks.Values |> Seq.map (fun n -> n.Subnet) |> Set.ofSeq
        findAvailableSubnet config.SubnetCandidates existing |> Result.defaultWith failwith

    let getDefaultGateway (subnet: string) = deriveGateway subnet

    interface INetworkDriver with
        member _.DriverType = NetworkDriver.Bridge

        member this.Create(name, subnet, gateway, _ipRange, _options, labels) =
            lock stateLock (fun () ->
                try
                    SecurityValidation.validateName name "Le nom du réseau"

                    for kv in labels do
                        SecurityValidation.validateLabel kv.Key kv.Value

                    if not (String.IsNullOrEmpty(subnet)) then
                        SecurityValidation.validateCidr subnet "Le sous-réseau"

                    if not (String.IsNullOrEmpty(gateway)) then
                        SecurityValidation.validateIp gateway "La passerelle"

                    // Unicité du nom : sinon deux réseaux partagent le même
                    // VMSwitch/NAT et Remove de l'un casse l'autre.
                    if
                        networks.Values
                        |> Seq.exists (fun n -> n.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    then
                        Error(sprintf "Un réseau nommé '%s' existe déjà" name)
                    else
                        let actualSubnet =
                            if String.IsNullOrEmpty(subnet) then
                                getAvailableSubnet ()
                            else
                                subnet

                        let actualGateway =
                            if String.IsNullOrEmpty(gateway) then
                                getDefaultGateway actualSubnet
                            else
                                gateway
                        // Idempotence : vérifier si le switch existe déjà avant de le créer
                        let existingSwitch =
                            try
                                let result =
                                    runPowershellWithArgs
                                        "Get-VMSwitch"
                                        [ "-Name", name; "-ErrorAction", "SilentlyContinue" ]

                                not (String.IsNullOrWhiteSpace(result))
                            with ex ->
                                Log.Debug(
                                    ex,
                                    "Vérification du switch VM {Name} échouée, hypothèse : inexistant",
                                    name
                                )

                                false

                        let mutable createdSwitch = false

                        if not existingSwitch then
                            // -ErrorAction Stop : sans lui, un échec non terminant
                            // (Hyper-V absent...) sort en code 0 et passe pour un succès.
                            runPowershellWithArgs
                                "New-VMSwitch"
                                [ "-Name", name
                                  "-SwitchType", "Internal"
                                  "-AllowManagementOS", "$true"
                                  "-ErrorAction", "Stop" ]
                            |> ignore

                            createdSwitch <- true

                        try
                            if not (String.IsNullOrEmpty(actualSubnet)) then
                                // Idempotence : vérifier si le NAT existe déjà
                                let natName = sprintf "%sNat" name

                                let existingNat =
                                    try
                                        let result =
                                            runPowershellWithArgs
                                                "Get-NetNat"
                                                [ "-Name", natName; "-ErrorAction", "SilentlyContinue" ]

                                        not (String.IsNullOrWhiteSpace(result))
                                    with ex ->
                                        Log.Debug(
                                            ex,
                                            "Vérification du NAT {NatName} échouée, hypothèse : inexistant",
                                            natName
                                        )

                                        false

                                if not existingNat then
                                    runPowershellWithArgs
                                        "New-NetNat"
                                        [ "-Name", natName
                                          "-InternalIPInterfaceAddressPrefix", actualSubnet
                                          "-ErrorAction", "Stop" ]
                                    |> ignore

                            let id = Guid.NewGuid().ToString("N")

                            let info =
                                { Id = id
                                  Name = name
                                  Driver = NetworkDriver.Bridge
                                  Subnet = actualSubnet
                                  Gateway = actualGateway
                                  Options = Map.empty
                                  Labels = labels
                                  CreatedAt = DateTime.UtcNow.ToString("o") }

                            networks.TryAdd(id, info) |> ignore
                            endpoints.TryAdd(id, ConcurrentDictionary<string, string>()) |> ignore

                            Ok info
                        with ex ->
                            // Rollback : un échec du NAT après création du switch
                            // laisserait sinon un VMSwitch orphelin sur l'hôte.
                            if createdSwitch then
                                try
                                    runPowershellWithArgs
                                        "Remove-VMSwitch"
                                        [ "-Name", name; "-Force", "$true"; "-ErrorAction", "SilentlyContinue" ]
                                    |> ignore
                                with rbEx ->
                                    Log.Warning(rbEx, "Rollback du VMSwitch {Name} impossible", name)

                            raise ex
                with ex ->
                    Log.Error(ex, "Erreur lors de la création du bridge {Name}", name)
                    Error "Erreur lors de la création du bridge")

        member _.Remove(id, _force) =
            lock stateLock (fun () ->
                match networks.TryGetValue(id) with
                | true, netInfo ->
                    try
                        runPowershellWithArgs "Remove-VMSwitch" [ "-Name", netInfo.Name; "-Force", "$true" ]
                        |> ignore

                        try
                            runPowershellWithArgs
                                "Remove-NetNat"
                                [ "-Name", sprintf "%sNat" netInfo.Name; "-Confirm", "$false" ]
                            |> ignore
                        with ex ->
                            Log.Warning(ex, "Erreur lors de la suppression du NAT {NatName}", netInfo.Name + "Nat")

                        networks.TryRemove(id) |> ignore
                        endpoints.TryRemove(id) |> ignore
                        Ok()
                    with ex ->
                        Log.Error(ex, "Erreur lors de la suppression du bridge {Id}", id)
                        Error "Erreur lors de la suppression du bridge"
                | false, _ -> Error(sprintf "Bridge '%s' introuvable" id))

        member _.Inspect(id) =
            match networks.TryGetValue(id) with
            | true, info -> Ok info
            | false, _ -> Error(sprintf "Bridge '%s' introuvable" id)

        member _.List() = networks.Values |> Seq.toList |> Ok

        member this.Connect(networkId, containerId, endpointId, ipv4Address, _options) =
            lock stateLock (fun () ->
                match networks.TryGetValue(networkId) with
                | false, _ -> Error(sprintf "Bridge '%s' introuvable" networkId)
                | true, netInfo ->
                    let mutable createdAdapter = false
                    let adapterName = ref ""

                    try
                        SecurityValidation.validateContainerId containerId
                        SecurityValidation.validateName netInfo.Name "Le nom du bridge"

                        let actualEndpointId =
                            if String.IsNullOrEmpty(endpointId) then
                                Guid.NewGuid().ToString("N")
                            else
                                endpointId

                        let shortId = containerId.Substring(0, min 8 containerId.Length)

                        adapterName.Value <- sprintf "vEthernet (%s-%s)" netInfo.Name shortId

                        runPowershellWithArgs
                            "Add-VMNetworkAdapter"
                            [ "-SwitchName", netInfo.Name
                              "-Name", adapterName.Value
                              "-ManagementOS", "$true"
                              "-ErrorAction", "Stop" ]
                        |> ignore

                        createdAdapter <- true

                        let mutable assignedIp = ""

                        match ipv4Address with
                        | Some ip when not (String.IsNullOrEmpty(ip)) ->
                            SecurityValidation.validateIp ip "L'adresse IP"

                            runPowershellWithArgs
                                "New-NetIPAddress"
                                [ "-InterfaceAlias", adapterName.Value; "-IPAddress", ip; "-ErrorAction", "Stop" ]
                            |> ignore

                            assignedIp <- ip
                        | _ -> assignedIp <- "DHCP"

                        let mac =
                            runPowershellScript
                                "Get-VMNetworkAdapter -Name $p0 | Select-Object -ExpandProperty MacAddress"
                                [ "Name", adapterName.Value ]
                            |> fun s -> s.Trim()

                        if String.IsNullOrEmpty mac then
                            // Une MAC vide signifie que l'adaptateur n'est pas
                            // opérationnel : échec, pas un endpoint amputé.
                            raise (
                                InvalidOperationException(
                                    sprintf "L'adaptateur %s n'a pas de adresse MAC (propagation Hyper-V ?)" adapterName.Value
                                )
                            )

                        endpoints.GetOrAdd(networkId, fun _ -> ConcurrentDictionary())
                            .TryAdd(actualEndpointId, containerId)
                        |> ignore

                        Ok
                            { EndpointId = actualEndpointId
                              ContainerId = containerId
                              Ipv4Address = assignedIp
                              MacAddress = mac
                              Message = sprintf "Connecté au bridge '%s'" netInfo.Name }
                    with ex ->
                        // Rollback : retirer l'adaptateur créé, sinon il reste
                        // orphelin et bloque les reconnexions (nom déjà pris).
                        if createdAdapter then
                            try
                                runPowershellWithArgs
                                    "Remove-VMNetworkAdapter"
                                    [ "-Name", adapterName.Value
                                      "-ManagementOS", "$true"
                                      "-ErrorAction", "SilentlyContinue" ]
                                |> ignore
                            with rbEx ->
                                Log.Warning(
                                    rbEx,
                                    "Rollback de l'adaptateur {Adapter} impossible",
                                    adapterName.Value
                                )

                        Log.Error(
                            ex,
                            "Erreur de connexion au bridge {NetworkId} pour le conteneur {ContainerId}",
                            networkId,
                            containerId
                        )

                        Error "Erreur de connexion au bridge")

        member _.Disconnect(networkId, containerId, endpointId, _force) =
            lock stateLock (fun () ->
                match networks.TryGetValue(networkId) with
                | false, _ -> Error(sprintf "Bridge '%s' introuvable" networkId)
                | true, netInfo ->
                    try
                        SecurityValidation.validateContainerId containerId
                        let shortId = containerId.Substring(0, min 8 containerId.Length)
                        let adapterName = sprintf "vEthernet (%s-%s)" netInfo.Name shortId

                        runPowershellWithArgs "Remove-VMNetworkAdapter" [ "-Name", adapterName; "-ManagementOS", "$true" ]
                        |> ignore

                        // Retirer les endpoints suivis de ce conteneur.
                        match endpoints.TryGetValue(networkId) with
                        | true, eps ->
                            for kv in eps do
                                if kv.Value = containerId then
                                    eps.TryRemove(kv.Key) |> ignore
                        | false, _ -> ()

                        Ok()
                    with ex ->
                        Log.Error(ex, "Erreur de déconnexion du bridge {NetworkId}", networkId)
                        Error "Erreur de déconnexion du bridge")

        /// Nettoyage RÉEL : supprime les réseaux sans endpoint actif (switch +
        /// NAT) et retourne uniquement les identifiants réellement supprimés.
        member this.Prune() =
            lock stateLock (fun () ->
                let removed = ResizeArray<string>()

                for kvp in networks do
                    let hasEndpoints =
                        match endpoints.TryGetValue(kvp.Key) with
                        | true, eps -> eps.Count > 0
                        | false, _ -> false

                    if not hasEndpoints then
                        match (this :> INetworkDriver).Remove(kvp.Key, false) with
                        | Ok() -> removed.Add(kvp.Key)
                        | Error msg -> Log.Warning("Prune du réseau {Id} impossible : {Error}", kvp.Key, msg)
                    else
                        Log.Debug("Réseau {Id} conservé : {Count} endpoint(s) actif(s)", kvp.Key)

                Ok(removed |> Seq.toList))
