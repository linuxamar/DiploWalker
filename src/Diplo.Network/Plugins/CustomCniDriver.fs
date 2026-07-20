namespace Diplo.Network.Plugins

open System
open System.Diagnostics
open System.Text.Json
open Serilog
open Diplo.Abstractions
open Diplo.Abstractions.NetworkConfig
open Diplo.Grpc.Network

type CustomCniDriver() =

    let networks = System.Collections.Concurrent.ConcurrentDictionary<string, NetworkDriverInfo>()

    let runProcess (fileName: string) (args: string list) =
        let psi = ProcessStartInfo()
        psi.FileName <- fileName
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- true
        for arg in args do
            psi.ArgumentList.Add(arg) |> ignore
        use proc = Process.Start(psi)
        if proc |> isNull then failwithf "Impossible de démarrer %s" fileName
        let stdout = proc.StandardOutput.ReadToEnd()
        let stderr = proc.StandardError.ReadToEnd()
        proc.WaitForExit()
        (proc.ExitCode, stdout, stderr)

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
                let actualSubnet = if String.IsNullOrEmpty(subnet) then
                                       let config = loadConfig None
                                       let existing = networks.Values |> Seq.map (fun n -> n.Subnet) |> Set.ofSeq
                                       findAvailableSubnet config.SubnetCandidates existing
                                   else subnet
                let actualGateway = if String.IsNullOrEmpty(gateway) then
                                        deriveGateway actualSubnet
                                    else gateway
                let id = Guid.NewGuid().ToString("N")
                let info = {
                    Id = id
                    Name = name
                    Driver = NetworkDriver.CustomCni
                    Subnet = actualSubnet
                    Gateway = actualGateway
                    Options = options
                    Labels = labels
                    CreatedAt = DateTime.UtcNow.ToString("o")
                }
                networks.TryAdd(id, info) |> ignore
                Ok info
            with ex ->
                Error (sprintf "Erreur lors de la création du réseau CNI: %s" ex.Message)

        member _.Remove(id, _force) =
            match networks.TryRemove(id) with
            | true, _ -> Ok ()
            | false, _ -> Error (sprintf "Réseau CNI '%s' introuvable" id)

        member _.Inspect(id) =
            match networks.TryGetValue(id) with
            | true, info -> Ok info
            | false, _ -> Error (sprintf "Réseau CNI '%s' introuvable" id)

        member _.List() =
            networks.Values |> Seq.toList |> Ok

        member _.Connect(networkId, containerId, endpointId, ipv4Address, options) =
            match networks.TryGetValue(networkId) with
            | false, _ -> Error (sprintf "Réseau CNI '%s' introuvable" networkId)
            | true, netInfo ->
                try
                    SecurityValidation.validateContainerId containerId
                    let actualEndpointId = if String.IsNullOrEmpty(endpointId) then
                                               Guid.NewGuid().ToString("N")
                                           else endpointId
                    let mutable assignedIp = ipv4Address |> Option.defaultValue ""
                    let mutable mac = ""
                    match options |> Map.tryFind "plugin_path" with
                    | Some pluginPath when not (String.IsNullOrEmpty(pluginPath)) ->
                        SecurityValidation.validateCniPluginPath pluginPath
                        let config = {|
                            cniVersion = "1.0.0"
                            name = netInfo.Name
                            ``type`` = options |> Map.tryFind "plugin_type" |> Option.defaultValue "bridge"
                            ipam = {|
                                ``type`` = "host-local"
                                subnet = netInfo.Subnet
                                gateway = netInfo.Gateway
                            |}
                        |}
                        let configJson = JsonSerializer.Serialize(config)
                        let (_exitCode, stdout, _stderr) =
                            runProcess pluginPath [ "ADD"; "--container-id"; containerId; "--netns"; sprintf "/proc/%s/ns/net" containerId ]
                        if _exitCode = 0 then
                            let (ifname, ipv4, gw) = parseCniResult stdout
                            if not (String.IsNullOrEmpty(ifname)) then assignedIp <- ipv4
                    | _ -> ()
                    Ok {
                        EndpointId = actualEndpointId
                        Ipv4Address = assignedIp
                        MacAddress = mac
                        Message = sprintf "Connecté au réseau CNI '%s'" netInfo.Name
                    }
                with ex ->
                    Error (sprintf "Erreur de connexion CNI: %s" ex.Message)

        member _.Disconnect(networkId, _containerId, endpointId, _force) =
            match networks.TryGetValue(networkId) with
            | false, _ -> Error (sprintf "Réseau CNI '%s' introuvable" networkId)
            | true, netInfo ->
                    match netInfo.Options |> Map.tryFind "plugin_path" with
                    | Some pluginPath when not (String.IsNullOrEmpty(pluginPath)) ->
                        try
                            SecurityValidation.validateCniPluginPath pluginPath
                            SecurityValidation.validateContainerId endpointId
                            let (_exitCode, _stdout, _stderr) =
                                runProcess pluginPath [ "DEL"; "--container-id"; endpointId; "--netns"; sprintf "/proc/%s/ns/net" endpointId ]
                            if _exitCode = 0 then Ok ()
                            else Error (sprintf "Échec de la déconnexion CNI (code %d)" _exitCode)
                        with ex -> Error (sprintf "Erreur de déconnexion CNI: %s" ex.Message)
                    | _ -> Ok ()
