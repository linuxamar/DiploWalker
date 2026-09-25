namespace DiploWalker.Core

open System
open System.IO
open System.Text.Json
open Serilog

/// Catalogue local persistant des images de conteneurs (`diplo-catalog.json`).
/// MÃªme convention que `DiploWalkerConfig` : chemin rÃ©solu via `DIPLO_CONFIG_HOME`,
/// lecture tolÃ©rante (fichier absent/malformÃ© â‡’ catalogue vide) et Ã©criture
/// atomique (temp + replace). Le fichier est partagÃ© entre CLI et GUI.
[<RequireQualifiedAccess>]
module ImageCatalog =

    type CatalogEntry =
        { Ref: string
          Note: string option
          AddedAt: string
          UpdatedAt: string }

    let private catalogFileName = "diplo-catalog.json"

    let private options = JsonSerializerOptions(WriteIndented = true)

    let private now () = DateTime.UtcNow.ToString("o")

    let private clean (s: string) : string option =
        let t = s.Trim()
        if String.IsNullOrEmpty t then None else Some t

    /// RÃ©sout le chemin du fichier catalogue, par prioritÃ© :
    /// 1. `DIPLO_CONFIG_HOME/diplo-catalog.json` si la variable d'environnement est dÃ©finie ;
    /// 2. `diplo-catalog.json` dans le rÃ©pertoire courant.
    let catalogPath () =
        let home = Environment.GetEnvironmentVariable("DIPLO_CONFIG_HOME")

        if String.IsNullOrWhiteSpace(home) then
            Path.Combine(Directory.GetCurrentDirectory(), catalogFileName)
        else
            Path.Combine(home.Trim(), catalogFileName)

    /// Lit le catalogue depuis un chemin explicite : [] si le fichier est absent
    /// ou mal formÃ© (repli : catalogue vide).
    let load (path: string) : CatalogEntry list =
        try
            if File.Exists path then
                JsonSerializer.Deserialize<CatalogEntry list>(File.ReadAllText path, options)
            else
                []
        with ex ->
            // M15 : un catalogue illisible ne passe plus en silence â€” avertir,
            // puis repli sur catalogue vide.
            Log.Warning(ex, "Catalogue d'images illisible {Path} (repli sur un catalogue vide)", path)
            []

    /// Ã‰crit le catalogue dans `path` (le rÃ©pertoire parent est crÃ©Ã© au besoin).
    /// Ã‰criture atomique (temp + replace) : un crash pendant l'Ã©criture ne doit
    /// pas laisser un catalogue tronquÃ© que la lecture avalerait.
    let save (path: string) (entries: CatalogEntry list) : unit =
        let dir = Path.GetDirectoryName path

        if not (String.IsNullOrEmpty dir) && not (Directory.Exists dir) then
            Directory.CreateDirectory(dir) |> ignore

        // Ã‰criture atomique (temp + replace) : voir DiploWalkerConfig.save.
        let tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp"

        try
            File.WriteAllText(tmp, JsonSerializer.Serialize(entries, options))

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

    /// Ajoute une entrÃ©e au catalogue : true si elle a rÃ©ellement Ã©tÃ© ajoutÃ©e,
    /// false si la rÃ©fÃ©rence est dÃ©jÃ  prÃ©sente (catalogue inchangÃ©).
    let add (path: string) (ref: string) (note: string option) : bool =
        let entries = load path

        if entries |> List.exists (fun e -> e.Ref = ref) then
            false
        else
            let entry =
                { Ref = ref
                  Note = note |> Option.bind clean
                  AddedAt = now ()
                  UpdatedAt = now () }

            save path (entries @ [ entry ])
            true

    /// Met Ã  jour l'entrÃ©e `ref` (nom via `newRef` et/ou note) : true si une
    /// entrÃ©e a Ã©tÃ© modifiÃ©e, false si la rÃ©fÃ©rence est introuvable au catalogue.
    let update (path: string) (ref: string) (newRef: string option) (note: string option) : bool =
        let entries = load path

        match entries |> List.tryFind (fun e -> e.Ref = ref) with
        | None -> false
        | Some existing ->
            let newRef' = newRef |> Option.bind clean

            let updated =
                { existing with
                    Ref = defaultArg newRef' existing.Ref
                    Note = note |> Option.bind clean |> Option.orElse existing.Note
                    UpdatedAt = now () }

            save path (entries |> List.map (fun e -> if e.Ref = ref then updated else e))
            true

    /// Retire l'entrÃ©e `ref` du catalogue : true si elle a Ã©tÃ© retirÃ©e, false
    /// si elle n'y figurait pas.
    let remove (path: string) (ref: string) : bool =
        let entries = load path

        if entries |> List.exists (fun e -> e.Ref = ref) then
            save path (entries |> List.filter (fun e -> e.Ref <> ref))
            true
        else
            false

