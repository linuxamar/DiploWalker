namespace Diplo.Core

open System
open System.IO
open System.Text.Json

/// Lecture du fichier de configuration client `diplo.json` (adresses des services).
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
        if a.Contains("://") then a
        else "http://" + a

    let private getAddress (root: JsonElement) (section: string) : string option =
        let mutable sectionEl = Unchecked.defaultof<JsonElement>
        if root.TryGetProperty(section, &sectionEl) then
            let mutable addr = Unchecked.defaultof<JsonElement>
            if sectionEl.TryGetProperty("address", &addr) && addr.ValueKind = JsonValueKind.String then
                let s = addr.GetString()
                if String.IsNullOrWhiteSpace(s) then None
                else Some(normalizeAddress s)
            else None
        else None

    let private parseConfig (json: string) : (string option * string option * string option) =
        try
            use doc = JsonDocument.Parse(json, JsonDocumentOptions(MaxDepth = 8))
            let root = doc.RootElement
            (getAddress root "container", getAddress root "volume", getAddress root "network")
        with _ ->
            (None, None, None)

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
        if File.Exists path then parseConfig (File.ReadAllText path)
        else (None, None, None)

    /// Config lue une seule fois par processus (fichier volontairement ignoré
    /// s'il est absent ou mal formé : repli sur les ports par défaut).
    let private cached = lazy (load (configPath ()))

    let containerAddress () : string option =
        let (c, _, _) = cached.Value
        c

    let volumeAddress () : string option =
        let (_, v, _) = cached.Value
        v

    let networkAddress () : string option =
        let (_, _, n) = cached.Value
        n

    /// Parse un texte JSON (exposé pour les tests).
    let parse (json: string) : (string option * string option * string option) =
        parseConfig json