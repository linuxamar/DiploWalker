namespace Diplo.Disk

open System.IO
open System.Text.Json

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
                let entries = JsonSerializer.Deserialize<ContainerState list>(File.ReadAllText path)
                entries
                |> Seq.map (fun e -> e.Id, e.Mounts)
                |> Map.ofSeq
        with _ -> Map.empty

    /// Enregistre l'état des volumes montés.
    let save (path: string) (mounted: seq<string * MountEntry list>) =
        let entries =
            mounted
            |> Seq.map (fun (id, mounts) -> { Id = id; Mounts = mounts })
            |> Seq.toList
        let json = JsonSerializer.Serialize(entries, JsonSerializerOptions(WriteIndented = true))
        File.WriteAllText(path, json)
