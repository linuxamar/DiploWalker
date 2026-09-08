namespace Diplo.Core

open System
open System.IO
open System.Text.Json
open Serilog

/// Catalogue local persistant des images de conteneurs (`diplo-catalog.json`).
/// Même convention que `DiploConfig` : chemin résolu via `DIPLO_CONFIG_HOME`,
/// lecture tolérante (fichier absent/malformé ⇒ catalogue vide) et écriture
/// atomique (temp + replace). Le fichier est partagé entre CLI et GUI.
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

    /// Résout le chemin du fichier catalogue, par priorité :
    /// 1. `DIPLO_CONFIG_HOME/diplo-catalog.json` si la variable d'environnement est définie ;
    /// 2. `diplo-catalog.json` dans le répertoire courant.
    let catalogPath () =
        let home = Environment.GetEnvironmentVariable("DIPLO_CONFIG_HOME")

        if String.IsNullOrWhiteSpace(home) then
            Path.Combine(Directory.GetCurrentDirectory(), catalogFileName)
        else
            Path.Combine(home.Trim(), catalogFileName)

    /// Lit le catalogue depuis un chemin explicite : [] si le fichier est absent
    /// ou mal formé (repli : catalogue vide).
    let load (path: string) : CatalogEntry list =
        try
            if File.Exists path then
                JsonSerializer.Deserialize<CatalogEntry list>(File.ReadAllText path, options)
            else
                []
        with _ ->
            []

    /// Écrit le catalogue dans `path` (le répertoire parent est créé au besoin).
    /// Écriture atomique (temp + replace) : un crash pendant l'écriture ne doit
    /// pas laisser un catalogue tronqué que la lecture avalerait.
    let save (path: string) (entries: CatalogEntry list) : unit =
        let dir = Path.GetDirectoryName path

        if not (String.IsNullOrEmpty dir) && not (Directory.Exists dir) then
            Directory.CreateDirectory(dir) |> ignore

        // Écriture atomique (temp + replace) : voir DiploConfig.save.
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
            with ex -> Log.Warning(ex, "Échec de la suppression du fichier temporaire {Tmp}", tmp)

            reraise ()

    /// Ajoute une entrée au catalogue : true si elle a réellement été ajoutée,
    /// false si la référence est déjà présente (catalogue inchangé).
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

    /// Met à jour l'entrée `ref` (nom via `newRef` et/ou note) : true si une
    /// entrée a été modifiée, false si la référence est introuvable au catalogue.
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

    /// Retire l'entrée `ref` du catalogue : true si elle a été retirée, false
    /// si elle n'y figurait pas.
    let remove (path: string) (ref: string) : bool =
        let entries = load path

        if entries |> List.exists (fun e -> e.Ref = ref) then
            save path (entries |> List.filter (fun e -> e.Ref <> ref))
            true
        else
            false