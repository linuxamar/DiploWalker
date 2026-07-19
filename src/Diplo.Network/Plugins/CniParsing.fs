namespace Diplo.Network.Plugins

open System.Text.Json
open Serilog

[<AutoOpen>]
module CniParsing =

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
        with ex ->
            Log.Warning(ex, "Erreur lors du parsing du résultat CNI")
            ("", "", "")
