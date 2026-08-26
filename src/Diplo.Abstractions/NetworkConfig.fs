module Diplo.Abstractions.NetworkConfig

open System
open System.IO
open System.Net.NetworkInformation
open System.Text.Json
open System.Text.Json.Serialization
open Serilog

// ─── Types ───────────────────────────────────────────────────────────────

type CniNatConfig =
    { [<JsonPropertyName("cni_version")>]
      CniVersion: string

      [<JsonPropertyName("nat_name")>]
      NatName: string

      [<JsonPropertyName("subnet")>]
      Subnet: string

      [<JsonPropertyName("gateway")>]
      Gateway: string

      [<JsonPropertyName("master_interface")>]
      MasterInterface: string

      [<JsonPropertyName("auto_detect")>]
      AutoDetect: bool

      [<JsonPropertyName("subnet_candidates")>]
      SubnetCandidates: string list

      [<JsonPropertyName("port_mappings")>]
      PortMappings: bool

      [<JsonPropertyName("dns")>]
      Dns: bool }

// ─── Valeurs par défaut ──────────────────────────────────────────────────

let defaultBridgeCandidates =
    [ "172.18.0.0/16"
      "172.19.0.0/16"
      "172.20.0.0/16"
      "172.21.0.0/16"
      "10.100.0.0/16"
      "10.101.0.0/16"
      "10.102.0.0/16" ]

let defaultCniCandidates =
    [ "10.244.0.0/16"
      "10.245.0.0/16"
      "10.246.0.0/16"
      "172.30.0.0/16"
      "172.31.0.0/16" ]

let defaultConfig =
    { CniVersion = "1.0.0"
      NatName = "nat"
      Subnet = ""
      Gateway = ""
      MasterInterface = "Ethernet"
      AutoDetect = true
      SubnetCandidates = defaultBridgeCandidates
      PortMappings = true
      Dns = true }

// ─── Serialisation JSON ──────────────────────────────────────────────────

let private jsonOptions = JsonSerializerOptions()
do jsonOptions.PropertyNamingPolicy <- JsonNamingPolicy.SnakeCaseLower
do jsonOptions.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
do jsonOptions.WriteIndented <- true
do jsonOptions.MaxDepth <- 64

let serializeConfig (config: CniNatConfig) : string =
    JsonSerializer.Serialize(config, jsonOptions)

let deserializeConfig (json: string) : CniNatConfig =
    JsonSerializer.Deserialize<CniNatConfig>(json, jsonOptions)

// ─── Validation de format ────────────────────────────────────────────────

/// Vérifie le format canonique A.B.C.D/masque d'un sous-réseau.
let isValidCidr (cidr: string) =
    if String.IsNullOrWhiteSpace cidr then
        false
    else
        match cidr.Split('/') with
        | [| ip; mask |] ->
            let octets = ip.Split('.')

            let maskOk =
                match Int32.TryParse(mask) with
                | true, m -> m >= 0 && m <= 32
                | _ -> false

            octets.Length = 4
            && maskOk
            && (octets
                |> Array.forall (fun o ->
                    match Int32.TryParse(o) with
                    | true, v when v >= 0 && v <= 255 -> true
                    | _ -> false))
        | _ -> false

// ─── Chargement depuis fichier ───────────────────────────────────────────

let private getConfigPath () =
    let installDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Diplo")

    Path.Combine(installDir, "config", "network.json")

let loadConfig (configPath: string option) : CniNatConfig =
    let path = configPath |> Option.defaultValue (getConfigPath ())

    if File.Exists(path) then
        try
            let json = File.ReadAllText(path)
            let loaded = deserializeConfig json

            let keepOrDefault value fallback =
                if String.IsNullOrWhiteSpace value then fallback else value

            // Fusionner TOUS les champs manquants avec les défauts, et valider
            // le format des valeurs réseau : une config corrompue ne doit pas
            // faire planter le service au démarrage.
            { CniVersion = keepOrDefault loaded.CniVersion defaultConfig.CniVersion
              NatName = keepOrDefault loaded.NatName defaultConfig.NatName
              Subnet =
                  if isValidCidr loaded.Subnet then
                      loaded.Subnet
                  elif not (String.IsNullOrWhiteSpace loaded.Subnet) then
                      Log.Warning(
                          "Sous-réseau invalide dans {Path} : '{Subnet}', détection automatique activée",
                          path,
                          loaded.Subnet
                      )

                      ""
                  else
                      ""
              Gateway =
                  if String.IsNullOrWhiteSpace loaded.Gateway || isValidCidr loaded.Gateway then
                      loaded.Gateway
                  else
                      Log.Warning("Passerelle invalide dans {Path} : '{Gateway}', elle sera dérivée", path, loaded.Gateway)

                      ""
              MasterInterface = keepOrDefault loaded.MasterInterface defaultConfig.MasterInterface
              AutoDetect = loaded.AutoDetect
              SubnetCandidates =
                  if loaded.SubnetCandidates |> List.isEmpty then
                      defaultBridgeCandidates
                  else
                      loaded.SubnetCandidates
              PortMappings = loaded.PortMappings
              Dns = loaded.Dns }
        with ex ->
            Log.Warning(ex, "Erreur lors du chargement de {Path}: {Message}", path, ex.Message)
            defaultConfig
    else
        defaultConfig

let saveConfig (configPath: string option) (config: CniNatConfig) =
    let path = configPath |> Option.defaultValue (getConfigPath ())
    let dir = Path.GetDirectoryName(path)

    if not (Directory.Exists(dir)) then
        Directory.CreateDirectory(dir) |> ignore
    // Écriture atomique : fichier temp + renommage pour éviter la corruption
    let tempPath = path + ".tmp." + Guid.NewGuid().ToString("N")

    try
        File.WriteAllText(tempPath, serializeConfig config)
        File.Move(tempPath, path, true)
    with ex ->
        try
            if File.Exists(tempPath) then
                File.Delete(tempPath)
        with _ ->
            ()

        reraise ()

// ─── Détection automatique de sous-réseau ────────────────────────────────

let getUsedPrefixes () =
    let interfaces = NetworkInterface.GetAllNetworkInterfaces()

    interfaces
    |> Array.choose (fun iface ->
        try
            let props = iface.GetIPProperties()

            props.UnicastAddresses
            |> Seq.tryHead
            |> Option.map (fun addr -> addr.Address.ToString())
        with _ ->
            None)
    |> Set.ofArray

/// Extrait les deux premiers octets d'une IP ou d'un CIDR sous forme de paire
/// d'entiers. Comparer par segments (et non via StartsWith) évite les faux
/// positifs du type « 172.200.x.x » considéré comme dans « 172.20.0.0/16 ».
let private firstTwoOctets (addressOrCidr: string) =
    if String.IsNullOrWhiteSpace addressOrCidr then
        None
    else
        let ip = addressOrCidr.Split('/').[0]

        match ip.Split('.') with
        | [| a; b; _; _ |] ->
            match Int32.TryParse(a), Int32.TryParse(b) with
            | (true, x), (true, y) -> Some(struct (x, y))
            | _ -> None
        | _ -> None

let findAvailableSubnet (candidates: string list) (usedPrefixes: Set<string>) : Result<string, string> =
    let usedPairs =
        usedPrefixes |> Seq.choose firstTwoOctets |> Set.ofSeq

    candidates
    |> List.tryFind (fun c ->
        // Ignorer silencieusement tout candidat mal formé (config externe)
        match firstTwoOctets c with
        | Some pair when isValidCidr c -> not (usedPairs.Contains pair)
        | _ -> false)
    |> function
        | Some subnet -> Ok subnet
        | None ->
            match candidates |> List.tryFind isValidCidr with
            | Some h -> Ok h
            | None -> Error "Aucun sous-réseau valide disponible"

let deriveGateway (subnet: string) =
    let ipPart = subnet.Split('/') |> Array.head
    let parts = ipPart.Split('.')

    if parts.Length < 3 then
        failwithf "Sous-réseau invalide pour dériver la passerelle: '%s'" subnet

    sprintf "%s.%s.%s.1" parts.[0] parts.[1] parts.[2]

// ─── Résolution de la configuration finale ───────────────────────────────

let resolveSubnet (config: CniNatConfig) (usedPrefixes: Set<string>) : Result<string, string> =
    if String.IsNullOrEmpty(config.Subnet) && config.AutoDetect then
        findAvailableSubnet config.SubnetCandidates usedPrefixes
    elif String.IsNullOrEmpty(config.Subnet) then
        match config.SubnetCandidates |> List.tryHead with
        | Some h -> Ok h
        | None -> Error "Aucun sous-réseau disponible"
    else
        Ok config.Subnet

let resolveGateway (config: CniNatConfig) (resolvedSubnet: string) =
    if String.IsNullOrEmpty(config.Gateway) then
        deriveGateway resolvedSubnet
    else
        config.Gateway

let resolveAll (configPath: string option) : Result<CniNatConfig * string * string, string> =
    let config = loadConfig configPath
    let usedPrefixes = getUsedPrefixes ()
    resolveSubnet config usedPrefixes
    |> Result.map (fun subnet ->
        let gateway = resolveGateway config subnet
        (config, subnet, gateway))

// ─── Génération du conflist CNI ─────────────────────────────────────────

let generateCniConflistJson (config: CniNatConfig) (subnet: string) (gateway: string) =
    let conflist =
        dict
            [ "cniVersion", config.CniVersion :> obj
              "name", config.NatName :> obj
              "type", config.NatName :> obj
              "master", config.MasterInterface :> obj
              "ipam",
              dict
                  [ "subnet", subnet :> obj
                    "routes", [| dict [ "gateway", gateway :> obj ] |] :> obj ]
              :> obj
              "capabilities", dict [ "portMappings", config.PortMappings :> obj; "dns", config.Dns :> obj ] :> obj ]

    JsonSerializer.Serialize(conflist, JsonSerializerOptions(WriteIndented = true))
