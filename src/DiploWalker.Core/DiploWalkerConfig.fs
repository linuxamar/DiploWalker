namespace DiploWalker.Core

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Serilog

/// Lecture/Ã©criture du fichier de configuration client `DiploWalker.json` (adresses des services).
/// Format gÃ©nÃ©rÃ© par `container config init` :
/// { "container": { "address": "localhost:5001", "namespace": "default" },
///   "volume": { "address": "localhost:5002" },
///   "network": { "address": "localhost:5003" },
///   "logLevel": "Information" }
[<RequireQualifiedAccess>]
module DiploWalkerConfig =

    let private configFileName = "DiploWalker.json"

    /// RÃ©sout l'adresse complÃ¨te (URL) d'un service Ã  partir de l'adresse configurÃ©e :
    /// - "localhost:5001"            â†’ "http://localhost:5001"
    /// - "http://pipe:/diplo-container" â†’ inchangÃ© (URL complÃ¨te, canal par named pipe)
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
        with ex ->
            // M15 : un JSON illisible n'est pas ignorÃ© en silence â€” avertissement
            // explicite, puis repli sur les valeurs par dÃ©faut.
            Log.Warning(ex, "Fichier de configuration client mal formÃ© (repli sur les valeurs par dÃ©faut)")
            (None, None, None)

    /// Extrait les mÃ©tadonnÃ©es (namespace, logLevel) d'un fichier existant afin de les
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
        with ex ->
            Log.Warning(ex, "Impossible de lire les mÃ©tadonnÃ©es du fichier de configuration {Path}", path)
            (None, None)

    /// RÃ©sout le chemin du fichier de configuration, par prioritÃ© :
    /// 1. `DIPLO_CONFIG_HOME/DiploWalker.json` si la variable d'environnement est dÃ©finie ;
    /// 2. `DiploWalker.json` dans le rÃ©pertoire courant.
    let configPath () =
        let home = Environment.GetEnvironmentVariable("DIPLO_CONFIG_HOME")

        if String.IsNullOrWhiteSpace(home) then
            Path.Combine(Directory.GetCurrentDirectory(), configFileName)
        else
            Path.Combine(home.Trim(), configFileName)

    /// Lit la configuration depuis un chemin explicite : None pour chaque section
    /// si le fichier est absent ou mal formÃ© (repli sur les ports par dÃ©faut).
    let load (path: string) : (string option * string option * string option) =
        if File.Exists path then
            parseConfig (File.ReadAllText path)
        else
            (None, None, None)

    /// Cache de la configuration lue une seule fois par processus, vidÃ© par
    /// `invalidate` (fichier volontairement ignorÃ© s'il est absent ou mal formÃ© :
    /// repli sur les ports par dÃ©faut).
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

    /// Vide le cache : la prochaine lecture relira le fichier. UtilisÃ© par la GUI
    /// aprÃ¨s un enregistrement des paramÃ¨tres pour appliquer la configuration
    /// sans redÃ©marrage.
    let invalidate () =
        lock cacheLock (fun () -> cacheValue.Value <- None)

    /// Parse un texte JSON (exposÃ© pour les tests).
    let parse (json: string) : (string option * string option * string option) = parseConfig json

    /// Ã‰crit la configuration client dans `path` (le rÃ©pertoire parent est crÃ©Ã© au
    /// besoin). Les champs `namespace` et `logLevel` dÃ©jÃ  prÃ©sents sont conservÃ©s.
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

        // Ã‰criture atomique (temp + replace) : un crash pendant l'Ã©criture ne
        // doit pas laisser un DiploWalker.json tronquÃ© que la lecture avalerait.
        let tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp"

        try
            File.WriteAllText(tmp, root.ToJsonString(JsonSerializerOptions(WriteIndented = true)))

            try
                File.Replace(tmp, path, null)
            with :? FileNotFoundException ->
                File.Move(tmp, path)
        with
        | _ ->
            try
                File.Delete(tmp)
            with ex -> Log.Warning(ex, "Ã‰chec de la suppression du fichier temporaire {Tmp}", tmp)
            reraise ()


