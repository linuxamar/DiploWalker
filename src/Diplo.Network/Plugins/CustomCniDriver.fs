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

    member _.GetAvailableSubnet() =
        let config = loadConfig None
        let existing = networks.Values |> Seq.map (fun n -> n.Subnet) |> Set.ofSeq
        findAvailableSubnet config.SubnetCandidates existing

    interface INetworkDriver with
        member _.DriverType = NetworkDriver.CustomCni

        member _.Create(name, subnet, gateway, ipRange, options, labels) =
            try
                SecurityValidation.validateName name "Le nom du réseau CNI"

                if not (String.IsNullOrEmpty(subnet)) then
                    SecurityValidation.validateCidr subnet "Le sous-réseau"

                if not (String.IsNullOrEmpty(gateway)) then
                    SecurityValidation.validateIp gateway "La passerelle"

                let actualSubnet =
                    if String.IsNullOrEmpty(subnet) then
                        let config = loadConfig None
                        let existing = networks.Values |> Seq.map (fun n -> n.Subnet) |> Set.ofSeq
                        findAvailableSubnet config.SubnetCandidates existing
                    else
                        subnet

                let actualGateway =
                    if String.IsNullOrEmpty(gateway) then
                        deriveGateway actualSubnet
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
            | true, _ -> Ok()
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

                        let (_exitCode, stdout, _stderr) =
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

                        if _exitCode = 0 then
                            let (_ifname, ipv4, _gw) = parseCniResult stdout
                            // L'adresse IP allouée est celle qui compte (le ifname n'est pas un critère).
                            if not (String.IsNullOrEmpty(ipv4)) then
                                assignedIp <- ipv4
                    | _ -> ()

                    Ok
                        { EndpointId = actualEndpointId
                          ContainerId = containerId
                          Ipv4Address = assignedIp
                          MacAddress = mac
                          Message = sprintf "Connecté au réseau CNI '%s'" netInfo.Name }
                with ex ->
                    Log.Error(ex, "Erreur de connexion CNI {NetworkId}", networkId)
                    Error "Erreur de connexion CNI"

        member _.Disconnect(networkId, _containerId, endpointId, _force) =
            match networks.TryGetValue(networkId) with
            | false, _ -> Error(sprintf "Réseau CNI '%s' introuvable" networkId)
            | true, netInfo ->
                match netInfo.Options |> Map.tryFind "plugin_path" with
                | Some pluginPath when not (String.IsNullOrEmpty(pluginPath)) ->
                    try
                        let resolvedPluginPath = SecurityValidation.validateCniPluginPath pluginPath
                        SecurityValidation.validateContainerId endpointId

                        let (_exitCode, _stdout, _stderr) =
                            // REMARQUE : le chemin /proc/<pid>/ns/net est spécifique à Linux.
                            // Sur Windows, le driver CNI doit utiliser un mécanisme différent (ex. HNSEndpoint).
                            ProcessExec.runWithResult
                                resolvedPluginPath
                                [ "DEL"
                                  "--container-id"
                                  endpointId
                                  "--netns"
                                  sprintf "/proc/%s/ns/net" endpointId ]
                                None
                                None

                        if _exitCode = 0 then
                            Ok()
                        else
                            Error(sprintf "Échec de la déconnexion CNI (code %d)" _exitCode)
                    with ex ->
                        Log.Error(ex, "Erreur de déconnexion CNI {NetworkId}", networkId)
                        Error "Erreur de déconnexion CNI"
                | _ -> Ok()

        member _.Prune() =
            let ids = networks.Keys |> Seq.toList
            Ok ids
