namespace Diplo.Abstractions.Tests

open System
open System.IO
open Xunit
open FsUnit.Xunit
open Diplo.Abstractions

module private NetCfgFixtures =

    let tempRoot =
        let d = Path.Combine(Path.GetTempPath(), sprintf "diplo-netcfg-%s" (Guid.NewGuid().ToString("N")))
        Directory.CreateDirectory(d) |> ignore
        d

    /// petit chemin de fichier JSON unique (le dossier racine persiste pour la durée des tests).
    let freshJsonPath () =
        Path.Combine(tempRoot, Guid.NewGuid().ToString("N") + ".json")

    let sampleConfig : NetworkConfig.CniNatConfig =
        { CniVersion = "1.0.0"
          NatName = "nat"
          Subnet = "172.18.0.0/16"
          Gateway = "172.18.0.1"
          MasterInterface = "Ethernet"
          AutoDetect = false
          SubnetCandidates = [ "10.100.0.0/16" ]
          PortMappings = true
          Dns = false }

    /// compare deux Result en utilisant l'égalité structurelle F# (fiable via xUnit).
    let shouldEqualResult (expected: Result<string, string>) (actual: Result<string, string>) =
        Assert.Equal<Result<string, string>>(expected, actual)

    /// compare deux enregistrements CniNatConfig (équité structurelle F# fiable).
    let shouldEqualConfig (expected: NetworkConfig.CniNatConfig) (actual: NetworkConfig.CniNatConfig) =
        Assert.Equal<NetworkConfig.CniNatConfig>(expected, actual)

    /// compare deux listes de chaînes (équité structurelle F# fiable).
    let shouldEqualStringList (expected: string list) (actual: string list) =
        Assert.Equal<List<string>>(expected, actual)

open NetCfgFixtures

type NetworkConfigTests() =

    // ── defaults ──────────────────────────────────────────────────────────

    [<Fact>]
    let ``defaultConfig applique les défauts attendus`` () =
        NetworkConfig.defaultConfig.CniVersion |> should equal "1.0.0"
        NetworkConfig.defaultConfig.NatName |> should equal "nat"
        NetworkConfig.defaultConfig.MasterInterface |> should equal "Ethernet"
        NetworkConfig.defaultConfig.AutoDetect |> should equal true
        NetworkConfig.defaultConfig.SubnetCandidates
        |> shouldEqualStringList NetworkConfig.defaultBridgeCandidates

    [<Fact>]
    let ``les candidats par défaut sont des CIDR valides`` () =
        NetworkConfig.defaultBridgeCandidates |> List.forall NetworkConfig.isValidCidr
        |> should equal true
        NetworkConfig.defaultCniCandidates |> List.forall NetworkConfig.isValidCidr
        |> should equal true

    // ── isValidCidr ───────────────────────────────────────────────────────

    [<Fact>]
    let ``isValidCidr accepte un CIDR canonique`` () =
        NetworkConfig.isValidCidr "172.18.0.0/16" |> should equal true

    [<Fact>]
    let ``isValidCidr accepte un masque /32 et /0`` () =
        NetworkConfig.isValidCidr "192.168.1.1/32" |> should equal true
        NetworkConfig.isValidCidr "0.0.0.0/0" |> should equal true

    [<Theory>]
    [<InlineData(null)>]
    [<InlineData("")>]
    [<InlineData("   ")>]
    let ``isValidCidr rejette vide ou null`` (cidr: string) =
        NetworkConfig.isValidCidr cidr |> should equal false

    [<Theory>]
    [<InlineData("172.18.0.0")>]
    [<InlineData("172.18.0.0/33")>]
    [<InlineData("172.18.0.0/-1")>]
    [<InlineData("172.18.0.0/aa")>]
    [<InlineData("172.18.0/16")>]
    [<InlineData("172.18.0.0.1/16")>]
    [<InlineData("999.18.0.0/16")>]
    [<InlineData("172.18.0.256/16")>]
    let ``isValidCidr rejette les formats invalides`` (cidr: string) =
        NetworkConfig.isValidCidr cidr |> should equal false

    // ── Serialisation ─────────────────────────────────────────────────────

    [<Fact>]
    let ``serializeConfig produit du JSON en snake_case`` () =
        let json = NetworkConfig.serializeConfig sampleConfig
        json |> should haveSubstring "\"nat_name\": \"nat\""
        json |> should haveSubstring "\"cni_version\""
        json |> should haveSubstring "\"subnet_candidates\""

    [<Fact>]
    let ``deserializeConfig relit un JSON`` () =
        let json =
            """{"cni_version":"1.0.0","nat_name":"nat","subnet":"172.18.0.0/16","gateway":"172.18.0.1","master_interface":"Ethernet","auto_detect":false,"subnet_candidates":["10.100.0.0/16"],"port_mappings":true,"dns":false}"""

        let cfg = NetworkConfig.deserializeConfig json
        cfg.NatName |> should equal "nat"
        cfg.Subnet |> should equal "172.18.0.0/16"
        cfg.SubnetCandidates |> should equal [ "10.100.0.0/16" ]
        cfg.Dns |> should equal false

    [<Fact>]
    let ``round-trip serialisation puis deserialisation conserve la config`` () =
        let back = NetworkConfig.serializeConfig sampleConfig |> NetworkConfig.deserializeConfig
        back |> should equal sampleConfig

    // ── findAvailableSubnet / getUsedPrefixes ─────────────────────────────

    [<Fact>]
    let ``findAvailableSubnet retourne le premier candidat libre`` () =
        NetworkConfig.findAvailableSubnet [ "172.18.0.0/16"; "10.100.0.0/16" ] (Set.empty)
        |> shouldEqualResult (Ok "172.18.0.0/16")

    [<Fact>]
    let ``findAvailableSubnet retombe sur le premier valide si tous les candidats sont utilisés`` () =
        let used = Set.ofList [ "172.18.1.5"; "10.100.0.2" ]
        NetworkConfig.findAvailableSubnet [ "172.18.0.0/16"; "10.100.0.0/16" ] used
        |> shouldEqualResult (Ok "172.18.0.0/16")

    [<Fact>]
    let ``findAvailableSubnet ne confond pas 172.20 et 172.200`` () =
        let used = Set.ofList [ "172.200.1.5" ]
        NetworkConfig.findAvailableSubnet [ "172.20.0.0/16" ] used
        |> shouldEqualResult (Ok "172.20.0.0/16")

    [<Fact>]
    let ``findAvailableSubnet ignore les candidats mal formés`` () =
        NetworkConfig.findAvailableSubnet [ "pas-un-cidr"; "10.5.0.0/16" ] (Set.empty)
        |> shouldEqualResult (Ok "10.5.0.0/16")

    [<Fact>]
    let ``findAvailableSubnet erreur si aucun candidat valide`` () =
        NetworkConfig.findAvailableSubnet [ "invalide" ] (Set.empty)
        |> shouldEqualResult (Error "Aucun sous-réseau valide disponible")

    [<Fact>]
    let ``getUsedPrefixes retourne un ensemble sans lever`` () =
        let prefixes = NetworkConfig.getUsedPrefixes ()
        prefixes |> should be instanceOfType<Set<string>>

    // ── deriveGateway ─────────────────────────────────────────────────────

    [<Fact>]
    let ``deriveGateway met .1 sur le troisième octet`` () =
        NetworkConfig.deriveGateway "172.18.0.0/16" |> shouldEqualResult (Ok "172.18.0.1")

    [<Fact>]
    let ``deriveGateway erreur si subnet trop court`` () =
        NetworkConfig.deriveGateway "172.18/16"
        |> shouldEqualResult (Error "Sous-réseau invalide pour dériver la passerelle: '172.18/16'")

    // ── resolveSubnet / resolveGateway ────────────────────────────────────

    [<Fact>]
    let ``resolveSubnet utilise le subnet configuré`` () =
        NetworkConfig.resolveSubnet sampleConfig Set.empty
        |> shouldEqualResult (Ok "172.18.0.0/16")

    [<Fact>]
    let ``resolveSubnet auto-détecte si subnet vide et AutoDetect`` () =
        let cfg = { sampleConfig with Subnet = ""; AutoDetect = true; SubnetCandidates = [ "10.7.0.0/16" ] }
        NetworkConfig.resolveSubnet cfg Set.empty |> shouldEqualResult (Ok "10.7.0.0/16")

    [<Fact>]
    let ``resolveSubnet prend le premier candidat si subnet vide sans autodetect`` () =
        let cfg = { sampleConfig with Subnet = ""; AutoDetect = false; SubnetCandidates = [ "10.8.0.0/16" ] }
        NetworkConfig.resolveSubnet cfg Set.empty |> shouldEqualResult (Ok "10.8.0.0/16")

    [<Fact>]
    let ``resolveGateway utilise le subnet dérivé si gateway vide`` () =
        NetworkConfig.resolveGateway sampleConfig "172.18.0.0/16"
        |> shouldEqualResult (Ok "172.18.0.1")

    [<Fact>]
    let ``resolveGateway garde la passerelle configurée`` () =
        let cfg = { sampleConfig with Gateway = "172.18.0.99" }
        NetworkConfig.resolveGateway cfg "172.18.0.0/16" |> shouldEqualResult (Ok "172.18.0.99")

    // ── loadConfig / saveConfig ───────────────────────────────────────────

    [<Fact>]
    let ``loadConfig retourne les défauts si fichier absent`` () =
        let missing = freshJsonPath ()
        NetworkConfig.loadConfig (Some missing) |> shouldEqualConfig NetworkConfig.defaultConfig

    [<Fact>]
    let ``saveConfig puis loadConfig relit la config`` () =
        // NB : loadConfig vide une gateway qui n'est pas un CIDR valide (avec masque).
        let roundTrip = { sampleConfig with Gateway = "172.18.0.1/32" }
        let path = freshJsonPath ()
        NetworkConfig.saveConfig (Some path) roundTrip
        let loaded = NetworkConfig.loadConfig (Some path)
        loaded |> shouldEqualConfig roundTrip

    [<Fact>]
    let ``loadConfig corrige un subnet invalide`` () =
        let path = freshJsonPath ()
        let bad =
            """{"cni_version":"1.0.0","nat_name":"nat","subnet":"not-a-cidr","gateway":"","master_interface":"Ethernet","auto_detect":true,"subnet_candidates":[],"port_mappings":true,"dns":true}"""

        File.WriteAllText(path, bad)
        let loaded = NetworkConfig.loadConfig (Some path)
        loaded.Subnet |> should equal ""
        loaded.SubnetCandidates |> should equal NetworkConfig.defaultBridgeCandidates

    [<Fact>]
    let ``loadConfig vide une gateway non CIDR`` () =
        let path = freshJsonPath ()
        let withBadGateway =
            """{"cni_version":"1.0.0","nat_name":"nat","subnet":"172.18.0.0/16","gateway":"172.18.0.1","master_interface":"Ethernet","auto_detect":false,"subnet_candidates":["10.100.0.0/16"],"port_mappings":true,"dns":false}"""

        File.WriteAllText(path, withBadGateway)
        let loaded = NetworkConfig.loadConfig (Some path)
        loaded.Gateway |> should equal ""

    [<Fact>]
    let ``loadConfig conserve une gateway CIDR valide`` () =
        let path = freshJsonPath ()
        let withGoodGateway =
            """{"cni_version":"1.0.0","nat_name":"nat","subnet":"172.18.0.0/16","gateway":"172.18.0.1/32","master_interface":"Ethernet","auto_detect":false,"subnet_candidates":["10.100.0.0/16"],"port_mappings":true,"dns":false}"""

        File.WriteAllText(path, withGoodGateway)
        let loaded = NetworkConfig.loadConfig (Some path)
        loaded.Gateway |> should equal "172.18.0.1/32"

    [<Fact>]
    let ``loadConfig retourne les défauts sur JSON corrompu`` () =
        let path = freshJsonPath ()
        File.WriteAllText(path, "{ pas du json !!!")
        NetworkConfig.loadConfig (Some path) |> shouldEqualConfig NetworkConfig.defaultConfig

    // ── generateCniConflistJson ───────────────────────────────────────────

    [<Fact>]
    let ``generateCniConflistJson produit la structure attendue`` () =
        let json = NetworkConfig.generateCniConflistJson sampleConfig "172.18.0.0/16" "172.18.0.1"
        json |> should haveSubstring "\"cniVersion\": \"1.0.0\""
        json |> should haveSubstring "\"subnet\": \"172.18.0.0/16\""
        json |> should haveSubstring "\"gateway\": \"172.18.0.1\""
        json |> should haveSubstring "\"portMappings\": true"

    [<Fact>]
    let ``generateCniConflistJson désactive portMappings et dns`` () =
        let cfg = { sampleConfig with PortMappings = false; Dns = false }
        let json = NetworkConfig.generateCniConflistJson cfg "172.18.0.0/16" "172.18.0.1"
        json |> should haveSubstring "\"portMappings\": false"
        json |> should haveSubstring "\"dns\": false"
