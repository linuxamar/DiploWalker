module DiploWalker.Abstractions.NetworkConfig

open System
open System.IO
open System.Net.NetworkInformation
open System.Text.Json
open System.Text.Json.Serialization
open Serilog

// â”€â”€â”€ Types â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

// â”€â”€â”€ Valeurs par dÃ©faut â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

// â”€â”€â”€ Serialisation JSON â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

let private jsonOptions = JsonSerializerOptions()
do jsonOptions.PropertyNamingPolicy <- JsonNamingPolicy.SnakeCaseLower
do jsonOptions.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
do jsonOptions.WriteIndented <- true
do jsonOptions.MaxDepth <- 64

let serializeConfig (config: CniNatConfig) : string =
    JsonSerializer.Serialize(config, jsonOptions)

let deserializeConfig (json: string) : CniNatConfig =
    JsonSerializer.Deserialize<CniNatConfig>(json, jsonOptions)

// â”€â”€â”€ Validation de format â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

/// VÃ©rifie le format canonique A.B.C.D/masque d'un sous-rÃ©seau.
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

// â”€â”€â”€ Chargement depuis fichier â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

            // Fusionner TOUS les champs manquants avec les dÃ©fauts, et valider
            // le format des valeurs rÃ©seau : une config corrompue ne doit pas
            // faire planter le service au dÃ©marrage.
            { CniVersion = keepOrDefault loaded.CniVersion defaultConfig.CniVersion
              NatName = keepOrDefault loaded.NatName defaultConfig.NatName
              Subnet =
                  if isValidCidr loaded.Subnet then
                      loaded.Subnet
                  elif not (String.IsNullOrWhiteSpace loaded.Subnet) then
                      Log.Warning(
                          "Sous-rÃ©seau invalide dans {Path} : '{Subnet}', dÃ©tection automatique activÃ©e",
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
                      Log.Warning("Passerelle invalide dans {Path} : '{Gateway}', elle sera dÃ©rivÃ©e", path, loaded.Gateway)

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
    // Ã‰criture atomique : fichier temp + renommage pour Ã©viter la corruption
    let tempPath = path + ".tmp." + Guid.NewGuid().ToString("N")

    try
        File.WriteAllText(tempPath, serializeConfig config)
        File.Move(tempPath, path, true)
    with ex ->
        try
            if File.Exists(tempPath) then
                File.Delete(tempPath)
        with cleanupEx ->
            Log.Warning(cleanupEx, "Ã‰chec de la suppression du fichier temporaire {Tmp}", tempPath)

        reraise ()

// â”€â”€â”€ DÃ©tection automatique de sous-rÃ©seau â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
/// d'entiers. Comparer par segments (et non via StartsWith) Ã©vite les faux
/// positifs du type Â« 172.200.x.x Â» considÃ©rÃ© comme dans Â« 172.20.0.0/16 Â».
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
        // Ignorer silencieusement tout candidat mal formÃ© (config externe)
        match firstTwoOctets c with
        | Some pair when isValidCidr c -> not (usedPairs.Contains pair)
        | _ -> false)
    |> function
        | Some subnet -> Ok subnet
        | None ->
            match candidates |> List.tryFind isValidCidr with
            | Some h -> Ok h
            | None -> Error "Aucun sous-rÃ©seau valide disponible"

let deriveGateway (subnet: string) : Result<string, string> =
    let ipPart = subnet.Split('/') |> Array.head
    let parts = ipPart.Split('.')

    if parts.Length < 3 then
        Error(sprintf "Sous-rÃ©seau invalide pour dÃ©river la passerelle: '%s'" subnet)
    else
        Ok(sprintf "%s.%s.%s.1" parts.[0] parts.[1] parts.[2])

// â”€â”€â”€ RÃ©solution de la configuration finale â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

let resolveSubnet (config: CniNatConfig) (usedPrefixes: Set<string>) : Result<string, string> =
    if String.IsNullOrEmpty(config.Subnet) && config.AutoDetect then
        findAvailableSubnet config.SubnetCandidates usedPrefixes
    elif String.IsNullOrEmpty(config.Subnet) then
        match config.SubnetCandidates |> List.tryHead with
        | Some h -> Ok h
        | None -> Error "Aucun sous-rÃ©seau disponible"
    else
        Ok config.Subnet

let resolveGateway (config: CniNatConfig) (resolvedSubnet: string) : Result<string, string> =
    if String.IsNullOrEmpty(config.Gateway) then
        deriveGateway resolvedSubnet
    else
        Ok config.Gateway

let resolveAll (configPath: string option) : Result<CniNatConfig * string * string, string> =
    let config = loadConfig configPath
    let usedPrefixes = getUsedPrefixes ()
    resolveSubnet config usedPrefixes
    |> Result.bind (fun subnet ->
        resolveGateway config subnet
        |> Result.map (fun gateway -> (config, subnet, gateway)))

// â”€â”€â”€ GÃ©nÃ©ration du conflist CNI â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

