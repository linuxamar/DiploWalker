namespace Diplo.Network.Plugins

open System
open System.Diagnostics
open System.Net.NetworkInformation
open System.Collections.Concurrent
open Serilog
open Diplo.Abstractions
open Diplo.Abstractions.NetworkConfig
open Diplo.Grpc.Network

type BridgeNetworkDriver() =

    let networks = ConcurrentDictionary<string, NetworkDriverInfo>()

    let runPowershellWithArgs (cmdlet: string) (parameters: (string * string) list) =
        let psi = ProcessStartInfo()
        psi.FileName <- "powershell"
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- true
        psi.ArgumentList.Add("-NoProfile") |> ignore
        psi.ArgumentList.Add("-NonInteractive") |> ignore
        psi.ArgumentList.Add("-Command") |> ignore
        // Utiliser un script paramétré pour éviter l'injection
        let paramNames = parameters |> List.mapi (fun i _ -> sprintf "$p%d" i)
        let paramValues = parameters |> List.map snd
        let paramDecl = paramNames |> String.concat ", "
        let body = sprintf "%s -%s" cmdlet (parameters |> List.mapi (fun i (name, _) -> sprintf "%s %s" name paramNames.[i]) |> String.concat " -")
        let script = sprintf "{ param(%s) %s }" paramDecl body
        psi.ArgumentList.Add(script) |> ignore
        for value in paramValues do
            psi.ArgumentList.Add(value) |> ignore
        use proc = Process.Start(psi)
        if proc |> isNull then failwithf "Impossible de démarrer PowerShell"
        let stdout = proc.StandardOutput.ReadToEnd()
        let stderr = proc.StandardError.ReadToEnd()
        if not (proc.WaitForExit(60_000)) then
            try proc.Kill(true) with _ -> ()
            failwith "Délai d'attente dépassé pour PowerShell (60s)"
        if proc.ExitCode <> 0 then
            failwithf "PowerShell a échoué (code %d)" proc.ExitCode
        stdout

    let runPowershellScript (scriptBody: string) (parameters: (string * string) list) =
        let psi = ProcessStartInfo()
        psi.FileName <- "powershell"
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- true
        psi.ArgumentList.Add("-NoProfile") |> ignore
        psi.ArgumentList.Add("-NonInteractive") |> ignore
        psi.ArgumentList.Add("-Command") |> ignore
        let paramNames = parameters |> List.mapi (fun i _ -> sprintf "$p%d" i)
        let paramValues = parameters |> List.map snd
        let paramDecl = paramNames |> String.concat ", "
        let script = sprintf "{ param(%s) %s }" paramDecl scriptBody
        psi.ArgumentList.Add(script) |> ignore
        for value in paramValues do
            psi.ArgumentList.Add(value) |> ignore
        use proc = Process.Start(psi)
        if proc |> isNull then failwithf "Impossible de démarrer PowerShell"
        let stdout = proc.StandardOutput.ReadToEnd()
        let stderr = proc.StandardError.ReadToEnd()
        if not (proc.WaitForExit(60_000)) then
            try proc.Kill(true) with _ -> ()
            failwith "Délai d'attente dépassé pour PowerShell (60s)"
        if proc.ExitCode <> 0 then
            failwithf "PowerShell a échoué (code %d)" proc.ExitCode
        stdout

    let getAvailableSubnet () =
        let usedPrefixes = getUsedPrefixes ()
        let config = loadConfig None
        findAvailableSubnet config.SubnetCandidates usedPrefixes

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
                runPowershellWithArgs "New-VMSwitch"
                    [ "-Name", name; "-SwitchType", "Internal"; "-AllowManagementOS", "$true" ] |> ignore
                if not (String.IsNullOrEmpty(actualSubnet)) then
                    runPowershellWithArgs "New-NetNat"
                        [ "-Name", sprintf "%sNat" name; "-InternalIPInterfaceAddressPrefix", actualSubnet ] |> ignore
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
                        Ipv4Address = assignedIp
                        MacAddress = mac
                        Message = sprintf "Connecté au bridge '%s'" netInfo.Name
                    }
                with ex ->
                    Log.Error(ex, "Erreur de connexion au bridge {NetworkId} pour le conteneur {ContainerId}", networkId, containerId)
                    Error "Erreur de connexion au bridge"

        member _.Disconnect(networkId, containerId, endpointId, _force) =
            match networks.TryGetValue(networkId) with
            | false, _ -> Error (sprintf "Bridge '%s' introuvable" networkId)
            | true, netInfo ->
                try
                    SecurityValidation.validateContainerId containerId
                    let adapterName = if String.IsNullOrEmpty(endpointId) then
                                          let shortId = containerId.Substring(0, min 8 containerId.Length)
                                          sprintf "vEthernet (%s-%s)" netInfo.Name shortId
                                      else endpointId
                    runPowershellWithArgs "Remove-VMNetworkAdapter"
                        [ "-Name", adapterName; "-ManagementOS", "$true" ] |> ignore
                    Ok ()
                with ex ->
                    Log.Error(ex, "Erreur de déconnexion du bridge {NetworkId}", networkId)
                    Error "Erreur de déconnexion du bridge"
