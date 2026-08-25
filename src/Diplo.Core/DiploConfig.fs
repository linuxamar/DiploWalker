namespace Diplo.Core

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes

/// Lecture/écriture du fichier de configuration client `diplo.json` (adresses des services).
/// Format généré par `container config init` :
/// { "container": { "address": "localhost:5001", "namespace": "default" },
///   "volume": { "address": "localhost:5002" },
///   "network": { "address": "localhost:5003" },
///   "logLevel": "Information" }
[<RequireQualifiedAccess>]
module DiploConfig =

    let private configFileName = "diplo.json"

    /// Résout l'adresse complète (URL) d'un service à partir de l'adresse configurée :
    /// - "localhost:5001"            → "http://localhost:5001"
    /// - "http://pipe:/diplo-container" → inchangé (URL complète, canal par named pipe)
    let normalizeAddress (address: string) : string =
        let a = address.Trim()
        if a.Contains("://") then a else "http://" + a

    let private getRaw (root: JsonElement) (section: string) (key: string) : string option =
        let mutable sectionEl = Unchecked.defaultof<JsonElement>

        if root.TryGetProperty(section, &sectionEl) then
            let mutable value = Unchecked.defaultof<JsonElement>

            if sectionEl.TryGetProperty(key, &value) && value.ValueKind = JsonValueKind.String then
                let s = value.GetString()
                if String.IsNullOrWhiteSpace(s) then None else Some s
            else
                None
        else
            None

    let private getAddress (root: JsonElement) (section: string) : string option =
        getRaw root section "address" |> Option.map normalizeAddress

    let private getTopLevel (root: JsonElement) (key: string) : string option =
        let mutable value = Unchecked.defaultof<JsonElement>

        if root.TryGetProperty(key, &value) && value.ValueKind = JsonValueKind.String then
            let s = value.GetString()
            if String.IsNullOrWhiteSpace(s) then None else Some s
        else
            None

    let private parseConfig (json: string) : (string option * string option * string option) =
        try
            use doc = JsonDocument.Parse(json, JsonDocumentOptions(MaxDepth = 8))
            let root = doc.RootElement
            (getAddress root "container", getAddress root "volume", getAddress root "network")
        with _ ->
            (None, None, None)

    /// Extrait les métadonnées (namespace, logLevel) d'un fichier existant afin de les
    /// conserver lors d'une sauvegarde.
    let private readMeta (path: string) : (string option * string option) =
        try
            if File.Exists path then
                use doc =
                    JsonDocument.Parse(File.ReadAllText path, JsonDocumentOptions(MaxDepth = 8))

                let root = doc.RootElement
                (getRaw root "container" "namespace", getTopLevel root "logLevel")
            else
                (None, None)
        with _ ->
            (None, None)

    /// Résout le chemin du fichier de configuration, par priorité :
    /// 1. `DIPLO_CONFIG_HOME/diplo.json` si la variable d'environnement est définie ;
    /// 2. `diplo.json` dans le répertoire courant.
    let configPath () =
        let home = Environment.GetEnvironmentVariable("DIPLO_CONFIG_HOME")

        if String.IsNullOrWhiteSpace(home) then
            Path.Combine(Directory.GetCurrentDirectory(), configFileName)
        else
            Path.Combine(home.Trim(), configFileName)

    /// Lit la configuration depuis un chemin explicite : None pour chaque section
    /// si le fichier est absent ou mal formé (repli sur les ports par défaut).
    let load (path: string) : (string option * string option * string option) =
        if File.Exists path then
            parseConfig (File.ReadAllText path)
        else
            (None, None, None)

    /// Cache de la configuration lue une seule fois par processus, vidé par
    /// `invalidate` (fichier volontairement ignoré s'il est absent ou mal formé :
    /// repli sur les ports par défaut).
    let private cacheLock = obj ()
    let private cacheValue = ref None

    let private readCached () =
        lock cacheLock (fun () ->
            match cacheValue.Value with
            | Some value -> value
            | None ->
                let value = load (configPath ())
                cacheValue.Value <- Some value
                value)

    let containerAddress () : string option =
        let (c, _, _) = readCached ()
        c

    let volumeAddress () : string option =
        let (_, v, _) = readCached ()
        v

    let networkAddress () : string option =
        let (_, _, n) = readCached ()
        n

    /// Vide le cache : la prochaine lecture relira le fichier. Utilisé par la GUI
    /// après un enregistrement des paramètres pour appliquer la configuration
    /// sans redémarrage.
    let invalidate () =
        lock cacheLock (fun () -> cacheValue.Value <- None)

    /// Parse un texte JSON (exposé pour les tests).
    let parse (json: string) : (string option * string option * string option) = parseConfig json

    /// Écrit la configuration client dans `path` (le répertoire parent est créé au
    /// besoin). Les champs `namespace` et `logLevel` déjà présents sont conservés.
    let save (path: string) (container: string) (volume: string) (network: string) : unit =
        let ns, logLevel = readMeta path
        let containerSection = JsonObject()
        containerSection["address"] <- JsonValue.Create(container)

        ns
        |> Option.iter (fun n -> containerSection["namespace"] <- JsonValue.Create(n))

        let volumeSection = JsonObject()
        volumeSection["address"] <- JsonValue.Create(volume)
        let networkSection = JsonObject()
        networkSection["address"] <- JsonValue.Create(network)
        let root = JsonObject()
        root["container"] <- containerSection
        root["volume"] <- volumeSection
        root["network"] <- networkSection
        logLevel |> Option.iter (fun l -> root["logLevel"] <- JsonValue.Create(l))
        let dir = Path.GetDirectoryName(path)

        if not (String.IsNullOrEmpty dir) && not (Directory.Exists dir) then
            Directory.CreateDirectory(dir) |> ignore

        File.WriteAllText(path, root.ToJsonString(JsonSerializerOptions(WriteIndented = true)))
