namespace Diplo.Disk

open System
open System.IO
open System.Text.Json
open Serilog

/// Persistance de l'état des volumes montés par conteneur : permet de
/// restaurer le write-back après un redémarrage du service sans ré-extraire
/// les images disque (voir DiskMounter.rehydrate).
module MountState =

    /// Un volume monté tel que persisté dans l'état.
    type MountEntry =
        { Source: string
          HostPath: string
          Destination: string
          ReadOnly: bool }

    type ContainerState =
        { Id: string
          Mounts: MountEntry list }

    let stateFile () =
        Path.Combine(DiskMounter.stagingRoot (), "mounted-state.json")

    /// Charge l'état persisté (Map identifiant conteneur -> volumes).
    /// Retourne un état vide si le fichier est absent ou illisible.
    let load (path: string) : Map<string, MountEntry list> =
        try
            if not (File.Exists path) then Map.empty
            else
                let json = File.ReadAllText path
                if String.IsNullOrWhiteSpace(json) || json = "null" then
                    Log.Warning("Fichier d'état de montage vide ou null : {Path}", path)
                    Map.empty
                else
                    let entries = JsonSerializer.Deserialize<ContainerState list>(json)
                    if isNull (box entries) then
                        Log.Warning("Fichier d'état de montage contient null : {Path}", path)
                        Map.empty
                    else
                        entries
                        |> Seq.map (fun e -> e.Id, e.Mounts)
                        |> Map.ofSeq
        with
        | :? JsonException as ex ->
            Log.Warning(ex, "Fichier d'état de montage corrompu : {Path}", path)
            Map.empty
        | ex ->
            Log.Warning(ex, "Erreur lecture état de montage : {Path}", path)
            Map.empty

    /// Enregistre l'état des volumes montés (écriture atomique : fichier
    /// temporaire du même répertoire puis remplacement).
    let save (path: string) (mounted: seq<string * MountEntry list>) =
        let entries =
            mounted
            |> Seq.map (fun (id, mounts) -> { Id = id; Mounts = mounts })
            |> Seq.toList
        let json = JsonSerializer.Serialize(entries, JsonSerializerOptions(WriteIndented = true))
        let dir = Path.GetDirectoryName(path)
        if not (String.IsNullOrEmpty dir) then Directory.CreateDirectory dir |> ignore
        let name = Path.GetFileName(path)
        let tmp = Path.Combine(dir, name + "." + Guid.NewGuid().ToString("N") + ".tmp")
        File.WriteAllText(tmp, json)
        try
            File.Replace(tmp, path, null)
        with :? FileNotFoundException ->
            File.Move(tmp, path)
