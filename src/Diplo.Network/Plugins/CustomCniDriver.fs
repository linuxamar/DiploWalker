namespace Diplo.Network.Plugins

open System
open System.Diagnostics
open System.Text.Json
open Diplo.Grpc.Network

type CustomCniDriver() =

    let networks = System.Collections.Concurrent.ConcurrentDictionary<string, NetworkDriverInfo>()

    let runProcess (fileName: string) (args: string) =
        let psi = ProcessStartInfo()
        psi.FileName <- fileName
        psi.Arguments <- args
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- true
        let proc = Process.Start(psi)
        let stdout = proc.StandardOutput.ReadToEnd()
        let stderr = proc.StandardError.ReadToEnd()
        proc.WaitForExit()
        (proc.ExitCode, stdout, stderr)

    let parseCniResult (json: string) =
        try
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement
            let mutable ifname = ""
            let mutable ipv4 = ""
            let mutable gw = ""
            let mutable tv = Unchecked.defaultof<JsonElement>
            if root.TryGetProperty("interfaces", &tv) then
                let interfaces = root.GetProperty("interfaces")
                if interfaces.GetArrayLength() > 0 then
                    let iface = interfaces.[0]
                    let mutable iv = Unchecked.defaultof<JsonElement>
                    if iface.TryGetProperty("name", &iv) then
                        ifname <- iv.GetString()
                    if iface.TryGetProperty("ips", &iv) && iface.GetProperty("ips").GetArrayLength() > 0 then
                        let ipInfo = iface.GetProperty("ips").[0]
                        let mutable av = Unchecked.defaultof<JsonElement>
                        if ipInfo.TryGetProperty("address", &av) then
                            ipv4 <- ipInfo.GetProperty("address").GetString()
            let mutable dv = Unchecked.defaultof<JsonElement>
            if root.TryGetProperty("dns", &dv) then
                let dns = root.GetProperty("dns")
                let mutable nsv = Unchecked.defaultof<JsonElement>
                if dns.TryGetProperty("nameservers", &nsv) then
                    let ns = dns.GetProperty("nameservers")
                    if ns.GetArrayLength() > 0 then
                        gw <- ns.[0].GetString()
            (ifname, ipv4, gw)
        with _ -> ("", "", "")

    member _.GetAvailableSubnet() =
        let candidates = [
            "10.244.0.0/16"; "10.245.0.0/16"; "10.246.0.0/16"
            "172.30.0.0/16"; "172.31.0.0/16"
        ]
        let existing = networks.Values |> Seq.map (fun n -> n.Subnet) |> Set.ofSeq
        candidates |> List.tryFind (fun c -> not (existing.Contains c)) |> Option.defaultValue "10.244.0.0/16"

    interface INetworkDriver with
        member _.DriverType = NetworkDriver.CustomCni

        member _.Create(name, subnet, gateway, ipRange, options, labels) =
            try
                let actualSubnet = if String.IsNullOrEmpty(subnet) then
                                       let driver = CustomCniDriver()
                                       driver.GetAvailableSubnet()
                                   else subnet
                let actualGateway = if String.IsNullOrEmpty(gateway) then
                                        let parts = actualSubnet.Split('/')
                                        let ipParts = parts.[0].Split('.')
                                        sprintf "%s.%s.%s.1" ipParts.[0] ipParts.[1] ipParts.[2]
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
                    let actualEndpointId = if String.IsNullOrEmpty(endpointId) then
                                               Guid.NewGuid().ToString("N")
                                           else endpointId
                    let mutable assignedIp = ipv4Address |> Option.defaultValue ""
                    let mutable mac = ""
                    match options |> Map.tryFind "plugin_path" with
                    | Some pluginPath when not (String.IsNullOrEmpty(pluginPath)) ->
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
                        let psi = ProcessStartInfo()
                        psi.FileName <- pluginPath
                        psi.Arguments <- sprintf "ADD --container-id %s --netns /proc/%s/ns/net" containerId containerId
                        psi.RedirectStandardInput <- true
                        psi.RedirectStandardOutput <- true
                        psi.RedirectStandardError <- true
                        psi.UseShellExecute <- false
                        psi.CreateNoWindow <- true
                        let proc = Process.Start(psi)
                        proc.StandardInput.Write(configJson)
                        proc.StandardInput.Close()
                        let stdout = proc.StandardOutput.ReadToEnd()
                        proc.WaitForExit()
                        if proc.ExitCode = 0 then
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
            | true, _ ->
                match networks.TryGetValue(networkId) with
                | true, netInfo ->
                    match netInfo.Options |> Map.tryFind "plugin_path" with
                    | Some pluginPath when not (String.IsNullOrEmpty(pluginPath)) ->
                        try
                            let psi = ProcessStartInfo()
                            psi.FileName <- pluginPath
                            psi.Arguments <- sprintf "DEL --container-id %s --netns /proc/%s/ns/net" endpointId endpointId
                            psi.RedirectStandardOutput <- true
                            psi.RedirectStandardError <- true
                            psi.UseShellExecute <- false
                            psi.CreateNoWindow <- true
                            let proc = Process.Start(psi)
                            proc.WaitForExit()
                            if proc.ExitCode = 0 then Ok ()
                            else Error (sprintf "Échec de la déconnexion CNI (code %d)" proc.ExitCode)
                        with ex -> Error (sprintf "Erreur de déconnexion CNI: %s" ex.Message)
                    | _ -> Ok ()
                | false, _ -> Error (sprintf "Réseau CNI '%s' introuvable" networkId)
