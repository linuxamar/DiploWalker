namespace Diplo.Network.Tests

module NetworkConfigTests =

    open System
    open System.IO
    open Xunit
    open FsUnit.Xunit
    open Diplo.Abstractions.NetworkConfig

    // ─── Valeurs par défaut ─────────────────────────────────────────────

    [<Fact>]
    let ``defaultConfig a les bonnes valeurs par defaut`` () =
        defaultConfig.CniVersion |> should equal "1.0.0"
        defaultConfig.NatName |> should equal "nat"
        defaultConfig.MasterInterface |> should equal "Ethernet"
        defaultConfig.AutoDetect |> should equal true
        defaultConfig.PortMappings |> should equal true
        defaultConfig.Dns |> should equal true

    [<Fact>]
    let ``defaultBridgeCandidates contient 7 sous-reseaux`` () =
        defaultBridgeCandidates.Length |> should equal 7

    [<Fact>]
    let ``defaultCniCandidates contient 5 sous-reseaux`` () =
        defaultCniCandidates.Length |> should equal 5

    // ─── Serialisation JSON ──────────────────────────────────────────────

    [<Fact>]
    let ``serializeConfig produit du JSON valide`` () =
        let json = serializeConfig defaultConfig
        json.Contains("\"cni_version\"") |> should equal true
        json.Contains("\"nat_name\"") |> should equal true
        json.Contains("\"auto_detect\": true") |> should equal true

    [<Fact>]
    let ``deserializeConfig restaure la config depuis JSON`` () =
        let json = serializeConfig defaultConfig
        let restored = deserializeConfig json
        restored.CniVersion |> should equal defaultConfig.CniVersion
        restored.NatName |> should equal defaultConfig.NatName
        restored.MasterInterface |> should equal defaultConfig.MasterInterface
        restored.AutoDetect |> should equal defaultConfig.AutoDetect

    [<Fact>]
    let ``round-trip serialisation preserves tous les champs`` () =
        let config =
            { defaultConfig with
                CniVersion = "0.4.0"
                NatName = "custom-nat"
                Subnet = "10.0.0.0/16"
                Gateway = "10.0.0.1"
                MasterInterface = "Ethernet 2"
                AutoDetect = false
                SubnetCandidates = [ "192.168.0.0/16" ]
                PortMappings = false
                Dns = false }

        let json = serializeConfig config
        let restored = deserializeConfig json
        restored.CniVersion |> should equal "0.4.0"
        restored.NatName |> should equal "custom-nat"
        restored.Subnet |> should equal "10.0.0.0/16"
        restored.Gateway |> should equal "10.0.0.1"
        restored.MasterInterface |> should equal "Ethernet 2"
        restored.AutoDetect |> should equal false
        restored.SubnetCandidates |> should equal [ "192.168.0.0/16" ]
        restored.PortMappings |> should equal false
        restored.Dns |> should equal false

    // ─── Chargement/sauvegarde fichier ──────────────────────────────────

    [<Fact>]
    let ``loadConfig retourne defaultConfig si fichier absent`` () =
        let tempPath =
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "network.json")

        let config = loadConfig (Some tempPath)
        config.CniVersion |> should equal defaultConfig.CniVersion
        config.NatName |> should equal defaultConfig.NatName

    [<Fact>]
    let ``loadConfig charge la config depuis un fichier existant`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
        Directory.CreateDirectory(tempDir) |> ignore
        let tempPath = Path.Combine(tempDir, "network.json")

        let config =
            { defaultConfig with
                CniVersion = "0.4.0"
                NatName = "test-nat"
                Subnet = "10.10.0.0/16" }

        saveConfig (Some tempPath) config
        let loaded = loadConfig (Some tempPath)
        loaded.CniVersion |> should equal "0.4.0"
        loaded.NatName |> should equal "test-nat"
        loaded.Subnet |> should equal "10.10.0.0/16"
        File.Delete(tempPath)
        Directory.Delete(tempDir)

    [<Fact>]
    let ``loadConfig retourne defaultConfig si JSON invalide`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
        Directory.CreateDirectory(tempDir) |> ignore
        let tempPath = Path.Combine(tempDir, "network.json")
        File.WriteAllText(tempPath, "{ invalid json !!!")
        let config = loadConfig (Some tempPath)
        config.CniVersion |> should equal defaultConfig.CniVersion
        File.Delete(tempPath)
        Directory.Delete(tempDir)

    [<Fact>]
    let ``saveConfig cree le repertoire si inexistant`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "sub")
        let tempPath = Path.Combine(tempDir, "network.json")
        saveConfig (Some tempPath) defaultConfig
        File.Exists(tempPath) |> should equal true
        File.Delete(tempPath)
        Directory.Delete(tempDir, true)

    // ─── Detection de sous-reseau ────────────────────────────────────────

    [<Fact>]
    let ``findAvailableSubnet retourne le premier candidat non utilise`` () =
        let candidates = [ "172.18.0.0/16"; "172.19.0.0/16"; "172.20.0.0/16" ]
        let used = Set.empty
        findAvailableSubnet candidates used |> Result.defaultWith failwith |> should equal "172.18.0.0/16"

    [<Fact>]
    let ``findAvailableSubnet saute les prefixes utilises`` () =
        let candidates = [ "172.18.0.0/16"; "172.19.0.0/16"; "172.20.0.0/16" ]
        let used = Set.ofList [ "172.18.1.5" ]
        findAvailableSubnet candidates used |> Result.defaultWith failwith |> should equal "172.19.0.0/16"

    [<Fact>]
    let ``findAvailableSubnet saute tous les prefixes utilises et prend le premier`` () =
        let candidates = [ "172.18.0.0/16"; "172.19.0.0/16" ]
        let used = Set.ofList [ "172.18.1.5"; "172.19.2.1" ]
        findAvailableSubnet candidates used |> Result.defaultWith failwith |> should equal "172.18.0.0/16"

    [<Fact>]
    let ``findAvailableSubnet avec une seule candidate retourne cette candidate`` () =
        let candidates = [ "10.244.0.0/16" ]
        findAvailableSubnet candidates Set.empty |> Result.defaultWith failwith |> should equal "10.244.0.0/16"

    // ─── deriveGateway ───────────────────────────────────────────────────

    [<Fact>]
    let ``deriveGateway retourne .1 comme passerelle`` () =
        deriveGateway "172.20.0.0/16" |> should equal "172.20.0.1"

    [<Fact>]
    let ``deriveGateway fonctionne avec un sous-reseau /24`` () =
        deriveGateway "10.0.0.0/24" |> should equal "10.0.0.1"

    [<Fact>]
    let ``deriveGateway fonctionne avec 10.x`` () =
        deriveGateway "10.100.0.0/16" |> should equal "10.100.0.1"

    // ─── resolveSubnet ──────────────────────────────────────────────────

    [<Fact>]
    let ``resolveSubnet retourne le subnet specifie si non vide`` () =
        let config =
            { defaultConfig with
                Subnet = "192.168.1.0/24"
                AutoDetect = true }

        resolveSubnet config Set.empty |> Result.defaultWith failwith |> should equal "192.168.1.0/24"

    [<Fact>]
    let ``resolveSubnet auto-detecte si subnet vide et AutoDetect true`` () =
        let config =
            { defaultConfig with
                Subnet = ""
                AutoDetect = true }

        resolveSubnet config Set.empty |> Result.defaultWith failwith |> should not' (equal "")

    [<Fact>]
    let ``resolveSubnet prend le premier candidat si subnet vide et AutoDetect false`` () =
        let config =
            { defaultConfig with
                Subnet = ""
                AutoDetect = false }

        resolveSubnet config Set.empty |> Result.defaultWith failwith |> should equal (defaultBridgeCandidates |> List.head)

    // ─── resolveGateway ─────────────────────────────────────────────────

    [<Fact>]
    let ``resolveGateway retourne le gateway specifie si non vide`` () =
        let config =
            { defaultConfig with
                Gateway = "10.0.0.254" }

        let result = resolveGateway config "10.0.0.0/16"
        result |> should equal "10.0.0.254"

    [<Fact>]
    let ``resolveGateway derive la passerelle si vide`` () =
        let config = { defaultConfig with Gateway = "" }
        let result = resolveGateway config "172.20.0.0/16"
        result |> should equal "172.20.0.1"

    // ─── generateCniConflistJson ────────────────────────────────────────

    [<Fact>]
    let ``generateCniConflistJson contient le bon cniVersion`` () =
        let config =
            { defaultConfig with
                CniVersion = "1.0.0"
                NatName = "nat" }

        let json = generateCniConflistJson config "172.20.0.0/16" "172.20.0.1"
        json.Contains("\"cniVersion\": \"1.0.0\"") |> should equal true

    [<Fact>]
    let ``generateCniConflistJson contient le bon subnet`` () =
        let config =
            { defaultConfig with
                CniVersion = "1.0.0" }

        let json = generateCniConflistJson config "10.100.0.0/16" "10.100.0.1"
        json.Contains("\"subnet\": \"10.100.0.0/16\"") |> should equal true

    [<Fact>]
    let ``generateCniConflistJson contient le bon gateway`` () =
        let config =
            { defaultConfig with
                CniVersion = "1.0.0" }

        let json = generateCniConflistJson config "172.20.0.0/16" "172.20.0.1"
        json.Contains("\"gateway\": \"172.20.0.1\"") |> should equal true

    [<Fact>]
    let ``generateCniConflistJson contient portMappings et dns`` () =
        let config =
            { defaultConfig with
                CniVersion = "1.0.0"
                PortMappings = true
                Dns = true }

        let json = generateCniConflistJson config "172.20.0.0/16" "172.20.0.1"
        json.Contains("\"portMappings\": true") |> should equal true
        json.Contains("\"dns\": true") |> should equal true

    [<Fact>]
    let ``generateCniConflistJson contient le type nat et master`` () =
        let config =
            { defaultConfig with
                CniVersion = "1.0.0"
                NatName = "nat"
                MasterInterface = "Ethernet" }

        let json = generateCniConflistJson config "172.20.0.0/16" "172.20.0.1"
        json.Contains("\"type\": \"nat\"") |> should equal true
        json.Contains("\"master\": \"Ethernet\"") |> should equal true

    // ─── Integration: config personnalisee ──────────────────────────────

    [<Fact>]
    let ``config personnalisee se charge et genere un conflist valide`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
        Directory.CreateDirectory(tempDir) |> ignore
        let tempPath = Path.Combine(tempDir, "network.json")

        let customConfig =
            { defaultConfig with
                CniVersion = "0.4.0"
                NatName = "custom-nat"
                MasterInterface = "Ethernet 2"
                AutoDetect = false
                SubnetCandidates = [ "192.168.100.0/24"; "192.168.101.0/24" ]
                Subnet = "192.168.100.0/24"
                Gateway = "192.168.100.1" }

        saveConfig (Some tempPath) customConfig
        let (config, subnet, gateway) = resolveAll (Some tempPath) |> Result.defaultWith failwith
        config.CniVersion |> should equal "0.4.0"
        config.NatName |> should equal "custom-nat"
        subnet |> should equal "192.168.100.0/24"
        gateway |> should equal "192.168.100.1"
        let json = generateCniConflistJson config subnet gateway
        json.Contains("\"cniVersion\": \"0.4.0\"") |> should equal true
        json.Contains("\"name\": \"custom-nat\"") |> should equal true
        json.Contains("\"subnet\": \"192.168.100.0/24\"") |> should equal true
        File.Delete(tempPath)
        Directory.Delete(tempDir, true)

    [<Fact>]
    let ``resolveAll avec defaultConfig fonctionne sans fichier`` () =
        let tempPath =
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "nonexistent.json")

        let (config, subnet, gateway) = resolveAll (Some tempPath) |> Result.defaultWith failwith
        config.CniVersion |> should equal "1.0.0"
        subnet |> should not' (equal "")
        gateway |> should not' (equal "")
