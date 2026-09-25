namespace DiploWalker.Network.Plugins

open System.Text.Json
open Serilog

[<AutoOpen>]
module CniParsing =

    let parseCniResult (json: string) =
        try
            use doc = JsonDocument.Parse(json, JsonDocumentOptions(MaxDepth = 32))
            let root = doc.RootElement

            // Racine non-objet (tableau/scalar) : TryGetProperty lÃ¨verait une
            // InvalidOperationException et annulerait tout le parsing.
            if root.ValueKind <> JsonValueKind.Object then
                ("", "", "")
            else
                let mutable ifname = ""
                let mutable ipv4 = ""
                let mutable gw = ""
                let mutable tv = Unchecked.defaultof<JsonElement>

                if root.TryGetProperty("interfaces", &tv) then
                    let interfaces = root.GetProperty("interfaces")

                    if interfaces.ValueKind = JsonValueKind.Array && interfaces.GetArrayLength() > 0 then
                        let iface = interfaces.[0]
                        let mutable iv = Unchecked.defaultof<JsonElement>

                        if iface.TryGetProperty("name", &iv) then
                            ifname <- iv.GetString()

                        if iface.TryGetProperty("ips", &iv)
                           && iv.ValueKind = JsonValueKind.Array
                           && iv.GetArrayLength() > 0 then
                            let ipInfo = iv.[0]
                            let mutable av = Unchecked.defaultof<JsonElement>

                            if ipInfo.TryGetProperty("address", &av) then
                                ipv4 <- av.GetString()

                            // SPEC CNI : la passerelle est dans ips[].gateway â€”
                            // dns.nameservers est un serveur DNS, pas une
                            // passerelle ; l'utiliser pose des routes fausses.
                            if ipInfo.TryGetProperty("gateway", &av) then
                                gw <- av.GetString()

                (ifname, ipv4, gw)
        with ex ->
            Log.Warning(ex, "Erreur lors du parsing du rÃ©sultat CNI")
            ("", "", "")

