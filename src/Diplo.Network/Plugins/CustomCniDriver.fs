namespace Diplo.Network.Plugins

open System
open System.Text.Json
open Serilog
open Diplo.Abstractions
open Diplo.Abstractions.NetworkConfig
open Diplo.Grpc.Network

type CustomCniDriver() =

    let networks =
        System.Collections.Concurrent.ConcurrentDictionary<string, NetworkDriverInfo>()

    // Association endpointId -> containerId : le plugin CNI DEL exige le
    // CONTAINER-ID ; passer l'endpointId à sa place faisait échouer le DEL et
    // fuyait le bail IPAM du conteneur.
    let endpoints =
        System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<string, string>>()

    member _.GetAvailableSubnet() =
        let config = loadConfig None
        let existing = networks.Values |> Seq.map (fun n -> n.Subnet) |> Set.ofSeq
        findAvailableSubnet config.SubnetCandidates existing |> Result.defaultWith failwith

    interface INetworkDriver with
        member _.DriverType = NetworkDriver.CustomCni

        member _.Create(name, subnet, gateway, ipRange, options, labels) =
            try
                SecurityValidation.validateName name "Le nom du réseau CNI"

                for kvp in labels do
                    SecurityValidation.validateLabel kvp.Key kvp.Value

                if not (String.IsNullOrEmpty(subnet)) then
                    SecurityValidation.validateCidr subnet "Le sous-réseau"

                if not (String.IsNullOrEmpty(gateway)) then
                    SecurityValidation.validateIp gateway "La passerelle"

                let actualSubnet =
                    if String.IsNullOrEmpty(subnet) then
                        let config = loadConfig None
                        let existing = networks.Values |> Seq.map (fun n -> n.Subnet) |> Set.ofSeq
                        findAvailableSubnet config.SubnetCandidates existing |> Result.defaultWith failwith
                    else
                        subnet

                let actualGateway =
                    if String.IsNullOrEmpty(gateway) then
                        deriveGateway actualSubnet |> Result.defaultWith failwith
                    else
                        gateway

                let id = Guid.NewGuid().ToString("N")

                let info =
                    { Id = id
                      Name = name
                      Driver = NetworkDriver.CustomCni
                      Subnet = actualSubnet
                      Gateway = actualGateway
                      Options = options
                      Labels = labels
                      CreatedAt = DateTime.UtcNow.ToString("o") }

                networks.TryAdd(id, info) |> ignore
                Ok info
            with ex ->
                Log.Error(ex, "Erreur lors de la création du réseau CNI {Name}", name)
                Error "Erreur lors de la création du réseau CNI"

        member _.Remove(id, _force) =
            match networks.TryRemove(id) with
            | true, _ ->
                endpoints.TryRemove(id) |> ignore
                Ok()
            | false, _ -> Error(sprintf "Réseau CNI '%s' introuvable" id)

        member _.Inspect(id) =
            match networks.TryGetValue(id) with
            | true, info -> Ok info
            | false, _ -> Error(sprintf "Réseau CNI '%s' introuvable" id)

        member _.List() = networks.Values |> Seq.toList |> Ok

        member _.Connect(networkId, containerId, endpointId, ipv4Address, options) =
            match networks.TryGetValue(networkId) with
            | false, _ -> Error(sprintf "Réseau CNI '%s' introuvable" networkId)
            | true, netInfo ->
                try
                    SecurityValidation.validateContainerId containerId

                    let actualEndpointId =
                        if String.IsNullOrEmpty(endpointId) then
                            Guid.NewGuid().ToString("N")
                        else
                            endpointId

                    let mutable assignedIp = ipv4Address |> Option.defaultValue ""
                    let mutable mac = ""

                    match options |> Map.tryFind "plugin_path" with
                    | Some pluginPath when not (String.IsNullOrEmpty(pluginPath)) ->
                        let resolvedPluginPath = SecurityValidation.validateCniPluginPath pluginPath

                        let config =
                            {| cniVersion = "1.0.0"
                               name = netInfo.Name
                               ``type`` = options |> Map.tryFind "plugin_type" |> Option.defaultValue "bridge"
                               ipam =
                                {| ``type`` = "host-local"
                                   subnet = netInfo.Subnet
                                   gateway = netInfo.Gateway |} |}

                        let configJson = JsonSerializer.Serialize(config)

                        let (exitCode, stdout, stderr) =
                            // REMARQUE : le chemin /proc/<pid>/ns/net est spécifique à Linux.
                            // Sur Windows, le driver CNI doit utiliser un mécanisme différent (ex. HNSEndpoint).
                            ProcessExec.runWithResult
                                resolvedPluginPath
                                [ "ADD"
                                  "--container-id"
                                  containerId
                                  "--netns"
                                  sprintf "/proc/%s/ns/net" containerId ]
                                None
                                (Some configJson)

                        // Symétrique au DEL : ignorer le code retour annoncerait un
                        // succès sans allocation IP réelle.
                        if exitCode <> 0 then
                            Error(
                                sprintf
                                    "Échec du plugin CNI ADD (code %d) : %s"
                                    exitCode
                                    (stderr.Trim())
                            )
                        else
                            let (_ifname, ipv4, _gw) = parseCniResult stdout
                            // L'adresse IP allouée est celle qui compte (le ifname n'est pas un critère).
                            if not (String.IsNullOrEmpty(ipv4)) then
                                assignedIp <- ipv4

                            endpoints.GetOrAdd(networkId, fun _ -> System.Collections.Concurrent.ConcurrentDictionary())
                                .TryAdd(actualEndpointId, containerId)
                            |> ignore

                            Ok
                                { EndpointId = actualEndpointId
                                  ContainerId = containerId
                                  Ipv4Address = assignedIp
                                  MacAddress = mac
                                  Message = sprintf "Connecté au réseau CNI '%s'" netInfo.Name }
                    | _ ->
                        endpoints.GetOrAdd(networkId, fun _ -> System.Collections.Concurrent.ConcurrentDictionary())
                            .TryAdd(actualEndpointId, containerId)
                        |> ignore

                        Ok
                            { EndpointId = actualEndpointId
                              ContainerId = containerId
                              Ipv4Address = assignedIp
                              MacAddress = mac
                              Message = sprintf "Connecté au réseau CNI '%s'" netInfo.Name }
                with ex ->
                    Log.Error(ex, "Erreur de connexion CNI {NetworkId}", networkId)
                    Error "Erreur de connexion CNI"

        member _.Disconnect(networkId, containerId, endpointId, _force) =
            match networks.TryGetValue(networkId) with
            | false, _ -> Error(sprintf "Réseau CNI '%s' introuvable" networkId)
            | true, netInfo ->
                match netInfo.Options |> Map.tryFind "plugin_path" with
                | Some pluginPath when not (String.IsNullOrEmpty(pluginPath)) ->
                    try
                        let resolvedPluginPath = SecurityValidation.validateCniPluginPath pluginPath

                        // Résoudre le vrai CONTAINER-ID : la requête peut ne
                        // fournir que l'endpointId (souvent le cas côté client),
                        // et le plugin CNI DEL exige un container-id valide.
                        let resolvedContainerId =
                            match endpoints.TryGetValue(networkId) with
                            | true, eps when not (String.IsNullOrEmpty(endpointId)) ->
                                match eps.TryGetValue(endpointId) with
                                | true, cid -> cid
                                | false, _ -> containerId
                            | _ -> containerId

                        let targetId =
                            if String.IsNullOrEmpty(resolvedContainerId) then
                                endpointId
                            else
                                resolvedContainerId

                        if String.IsNullOrEmpty(targetId) then
                            Error "Ni containerId ni endpointId fournis pour la déconnexion"
                        else
                            SecurityValidation.validateContainerId targetId

                            let (exitCode, _stdout, stderr) =
                                // REMARQUE : le chemin /proc/<pid>/ns/net est spécifique à Linux.
                                // Sur Windows, le driver CNI doit utiliser un mécanisme différent (ex. HNSEndpoint).
                                ProcessExec.runWithResult
                                    resolvedPluginPath
                                    [ "DEL"
                                      "--container-id"
                                      targetId
                                      "--netns"
                                      sprintf "/proc/%s/ns/net" targetId ]
                                    None
                                    None

                            // Retirer l'association endpoint enregistrée.
                            match endpoints.TryGetValue(networkId) with
                            | true, eps ->
                                for kv in eps do
                                    if kv.Value = targetId then
                                        eps.TryRemove(kv.Key) |> ignore
                            | false, _ -> ()

                            if exitCode = 0 then
                                Ok()
                            else
                                Error(
                                    sprintf
                                        "Échec de la déconnexion CNI (code %d) : %s"
                                        exitCode
                                        (stderr.Trim())
                                )
                    with ex ->
                        Log.Error(ex, "Erreur de déconnexion CNI {NetworkId}", networkId)
                        Error "Erreur de déconnexion CNI"
                | _ -> Ok()

        /// Nettoyage réel : supprime les réseaux CNI (registre local, sans
        /// ressource externe) et retourne les identifiants réellement retirés.
        member _.Prune() =
            let removed = ResizeArray<string>()

            for kvp in networks do
                match networks.TryRemove(kvp.Key) with
                | true, _ ->
                    endpoints.TryRemove(kvp.Key) |> ignore
                    removed.Add(kvp.Key)
                | false, _ -> ()

            Ok(removed |> Seq.toList)
