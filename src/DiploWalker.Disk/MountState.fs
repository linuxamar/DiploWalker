namespace DiploWalker.Disk

open System
open System.IO
open System.Text.Json
open Serilog

/// Persistance de l'Ã©tat des volumes montÃ©s par conteneur : permet de
/// restaurer le write-back aprÃ¨s un redÃ©marrage du service sans rÃ©-extraire
/// les images disque (voir DiskMounter.rehydrate).
module MountState =

    /// Un volume montÃ© tel que persistÃ© dans l'Ã©tat.
    type MountEntry =
        { Source: string
          HostPath: string
          Destination: string
          ReadOnly: bool }

    type ContainerState = { Id: string; Mounts: MountEntry list }

    // MÃªme racine que DiskMounter.stagingRoot() : calculÃ©e localement pour
    // Ã©viter une dÃ©pendance croisÃ©e entre les deux modules.
    let stateFile () =
        let baseDir =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Diplo",
                "volumes"
            )

        Path.Combine(baseDir, "mounted-state.json")

    /// Verrou global : les handlers gRPC appellent persistMounts en parallÃ¨le ;
    /// sans sÃ©rialisation, deux sauvegardes concurrentes s'Ã©crasent et des
    /// entrÃ©es de montage sont perdues (write-backs jamais exÃ©cutÃ©s aprÃ¨s crash).
    let private stateLock = obj ()

    /// Charge l'Ã©tat persistÃ© (Map identifiant conteneur -> volumes).
    /// Retourne un Ã©tat vide si le fichier est absent ou illisible.
    let load (path: string) : Map<string, MountEntry list> =
        lock stateLock (fun () ->
            try
                if not (File.Exists path) then
                    Map.empty
                else
                    let json = File.ReadAllText path

                    if String.IsNullOrWhiteSpace(json) || json = "null" then
                        Log.Warning("Fichier d'Ã©tat de montage vide ou null : {Path}", path)
                        Map.empty
                    else
                        let entries = JsonSerializer.Deserialize<ContainerState list>(json)

                        if isNull (box entries) then
                            Log.Warning("Fichier d'Ã©tat de montage contient null : {Path}", path)
                            Map.empty
                        else
                            entries |> Seq.map (fun e -> e.Id, e.Mounts) |> Map.ofSeq
            with
            | :? JsonException as ex ->
                Log.Warning(ex, "Fichier d'Ã©tat de montage corrompu : {Path}", path)
                Map.empty
            | ex ->
                Log.Warning(ex, "Erreur lecture Ã©tat de montage : {Path}", path)
                Map.empty)

    /// Enregistre l'Ã©tat des volumes montÃ©s (Ã©criture atomique : fichier
    /// temporaire du mÃªme rÃ©pertoire puis remplacement ; le temporaire est
    /// supprimÃ© mÃªme en cas d'Ã©chec).
    let save (path: string) (mounted: seq<string * MountEntry list>) =
        lock stateLock (fun () ->
            let entries =
                mounted
                |> Seq.map (fun (id, mounts) -> { Id = id; Mounts = mounts })
                |> Seq.toList

            let json = JsonSerializer.Serialize(entries, JsonSerializerOptions(WriteIndented = true))
            let dir = Path.GetDirectoryName(path)

            if not (String.IsNullOrEmpty dir) then
                Directory.CreateDirectory dir |> ignore

            let name = Path.GetFileName(path)
            let tmp = Path.Combine(dir, name + "." + Guid.NewGuid().ToString("N") + ".tmp")

            try
                try
                    File.WriteAllText(tmp, json)
                    File.Replace(tmp, path, null)
                with :? FileNotFoundException ->
                    File.Move(tmp, path)
            with
            | _ ->
                try
                    File.Delete(tmp)
                with ex -> Log.Warning(ex, "Ã‰chec de la suppression du fichier temporaire {Tmp}", tmp)
                reraise ())

