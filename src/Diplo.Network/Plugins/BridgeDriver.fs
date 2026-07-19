namespace Diplo.Network.Plugins

open System
open System.Diagnostics
open System.Net.NetworkInformation
open System.Collections.Concurrent
open Serilog
open Diplo.Grpc.Network

type BridgeNetworkDriver() =

    let networks = ConcurrentDictionary<string, NetworkDriverInfo>()

    let runNetsh args =
        let psi = ProcessStartInfo()
        psi.FileName <- "netsh"
        psi.Arguments <- args
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- true
        use proc = Process.Start(psi)
        if proc |> isNull then failwithf "Impossible de démarrer netsh"
        let stdout = proc.StandardOutput.ReadToEnd()
        let stderr = proc.StandardError.ReadToEnd()
        proc.WaitForExit()
        if proc.ExitCode <> 0 then
            failwithf "netsh a échoué (code %d): %s" proc.ExitCode stderr
        stdout

    let runPowershell args =
        let psi = ProcessStartInfo()
        psi.FileName <- "powershell"
        psi.Arguments <- "-NoProfile -NonInteractive -Command " + args
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- true
        use proc = Process.Start(psi)
        if proc |> isNull then failwithf "Impossible de démarrer PowerShell"
        let stdout = proc.StandardOutput.ReadToEnd()
        let stderr = proc.StandardError.ReadToEnd()
        proc.WaitForExit()
        if proc.ExitCode <> 0 then
            failwithf "PowerShell a échoué (code %d): %s" proc.ExitCode stderr
        stdout

    let getAvailableSubnet () =
        let interfaces = NetworkInterface.GetAllNetworkInterfaces()
        let usedSubnets =
            interfaces
            |> Array.choose (fun iface ->
                try
                    let props = iface.GetIPProperties()
                    props.UnicastAddresses
                    |> Seq.tryHead
                    |> Option.map (fun addr -> addr.Address.ToString())
                with ex ->
                    Log.Debug(ex, "Impossible de récupérer l'adresse IP de l'interface {Interface}", iface.Name)
                    None)
            |> Set.ofArray
        let candidates = [
            "172.18.0.0/16"; "172.19.0.0/16"; "172.20.0.0/16"; "172.21.0.0/16"
            "10.100.0.0/16"; "10.101.0.0/16"; "10.102.0.0/16"
        ]
        candidates |> List.tryFind (fun c ->
            let baseIp = c.Split('/')[0]
            let prefix = baseIp.Split('.') |> Array.take 2 |> String.concat "."
            not (usedSubnets |> Set.exists (fun ip -> ip.StartsWith(prefix))))
        |> Option.defaultValue "172.18.0.0/16"

    let getDefaultGateway (subnet: string) =
        let parts = subnet.Split('/')
        let ipParts = parts.[0].Split('.')
        sprintf "%s.%s.%s.1" ipParts.[0] ipParts.[1] ipParts.[2]

    interface INetworkDriver with
        member _.DriverType = NetworkDriver.Bridge

        member _.Create(name, subnet, gateway, _ipRange, _options, labels) =
            try
                let actualSubnet = if String.IsNullOrEmpty(subnet) then getAvailableSubnet () else subnet
                let actualGateway = if String.IsNullOrEmpty(gateway) then getDefaultGateway actualSubnet else gateway
                let createArgs = sprintf "New-VMSwitch -Name '%s' -SwitchType Internal -AllowManagementOS $true" name
                runPowershell createArgs |> ignore
                if not (String.IsNullOrEmpty(actualSubnet)) then
                    let natArgs = sprintf "New-NetNat -Name '%sNat' -InternalIPInterfaceAddressPrefix '%s'" name actualSubnet
                    runPowershell natArgs |> ignore
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
                Error (sprintf "Erreur lors de la création du bridge: %s" ex.Message)

        member _.Remove(id, _force) =
            match networks.TryGetValue(id) with
            | true, netInfo ->
                try
                    let args = sprintf "Remove-VMSwitch -Name '%s' -Force" netInfo.Name
                    runPowershell args |> ignore
                    let natArgs = sprintf "Remove-NetNat -Name '%sNat' -Confirm:$false" netInfo.Name
                    try runPowershell natArgs |> ignore
                    with ex -> Log.Warning(ex, "Erreur lors de la suppression du NAT {NatName}", netInfo.Name + "Nat")
                    networks.TryRemove(id) |> ignore
                    Ok ()
                with ex ->
                    Error (sprintf "Erreur lors de la suppression du bridge: %s" ex.Message)
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
                    let actualEndpointId = if String.IsNullOrEmpty(endpointId) then
                                               Guid.NewGuid().ToString("N")
                                           else endpointId
                    let adapterName = sprintf "vEthernet (%s-%s)" netInfo.Name (containerId.Substring(0, min 8 containerId.Length))
                    let args = sprintf "Add-VMNetworkAdapter -SwitchName '%s' -Name '%s' -ManagementOS" netInfo.Name adapterName
                    runPowershell args |> ignore
                    let mutable assignedIp = ""
                    match ipv4Address with
                    | Some ip when not (String.IsNullOrEmpty(ip)) ->
                        let assignArgs = sprintf "New-NetIPAddress -InterfaceAlias '%s' -IPAddress '%s'" adapterName ip
                        runPowershell assignArgs |> ignore
                        assignedIp <- ip
                    | _ -> assignedIp <- "DHCP"
                    let macArgs = sprintf "Get-VMNetworkAdapter -Name '%s' | Select-Object -ExpandProperty MacAddress" adapterName
                    let mac = runPowershell(macArgs).Trim()
                    Ok {
                        EndpointId = actualEndpointId
                        Ipv4Address = assignedIp
                        MacAddress = mac
                        Message = sprintf "Connecté au bridge '%s'" netInfo.Name
                    }
                with ex ->
                    Error (sprintf "Erreur de connexion au bridge: %s" ex.Message)

        member _.Disconnect(networkId, containerId, endpointId, _force) =
            match networks.TryGetValue(networkId) with
            | false, _ -> Error (sprintf "Bridge '%s' introuvable" networkId)
            | true, netInfo ->
                try
                    let adapterName = if String.IsNullOrEmpty(endpointId) then
                                          sprintf "vEthernet (%s-%s)" netInfo.Name (containerId.Substring(0, min 8 containerId.Length))
                                      else endpointId
                    let args = sprintf "Remove-VMNetworkAdapter -Name '%s' -ManagementOS" adapterName
                    runPowershell args |> ignore
                    Ok ()
                with ex ->
                    Error (sprintf "Erreur de déconnexion du bridge: %s" ex.Message)
