namespace DiploWalker.Disk

open System
open System.IO
open Serilog
open DiploWalker.Abstractions

/// Volume monté pour un conteneur : chemin hôte à bind-mounter dans le
/// conteneur (`ctr --mount type=bind,src=...`) et action de libération.
/// `Source` est la source d'origine (répertoire hôte ou image disque) et
/// permet de restaurer le write-back après un redémarrage du service.
type MountedVolume =
    { Source: string
      HostPath: string
      Destination: string
      ReadOnly: bool
      Dispose: unit -> unit }

/// Service de montage d'images disque pour les conteneurs.
type IDiskMounter =
    /// Monte la source (répertoire ou image disque) pour la destination
    /// donnée et retourne le volume à bind-mounter.
    abstract member Mount: source: string * destination: string * readOnly: bool -> MountedVolume

/// Orchestration du montage des images disque au démarrage des conteneurs.
///
/// Une source peut être :
///  - un répertoire hôte : bind mount direct (aucune copie) ;
///  - une image disque (qcow2, raw, vhd, vhdx, vmdk) : son contenu est
///    exposé dans un dossier de staging sous %ProgramData%\Diplo\volumes.
///    À la libération, les modifications sont réécrites dans l'image :
///    pour qcow2, l'écriture se fait directement dans les clusters du
///    fichier via le pilote maison (aucune conversion, aucun VHD).
module DiskMounter =

    let stagingRoot () = AppPaths.dataDir "volumes"

    /// Supprime les dossiers de staging orphelins (GUID) plus anciens que
    /// `maxAge`. Un staging est orphelin quand le service a crashé entre
    /// l'extraction et l'appel au write-back. Les stagings référencés par
    /// l'état persisté (mounted-state.json, en attente de rehydration après un
    /// arrêt long) sont préservés : les supprimer détruirait des modifications
    /// de conteneur sans aucun write-back.
    let pruneStaleStaging (maxAge: TimeSpan) =
        let root = stagingRoot ()

        let referencedHostPaths =
            try
                let statePath = MountState.stateFile ()

                if File.Exists statePath then
                    MountState.load statePath
                    |> Map.toSeq
                    |> Seq.collect snd
                    |> Seq.map (fun m -> m.HostPath)
                    |> Set.ofSeq
                else
                    Set.empty
            with ex ->
                Log.Warning(
                    ex,
                    "Impossible de lire l'état persisté avant le prune : aucun staging ne sera supprimé"
                )

                // Fail-safe : en cas d'erreur de lecture, ne rien supprimer.
                Directory.GetDirectories root |> Set.ofArray

        for dir in Directory.GetDirectories root do
            let dirName = Path.GetFileName dir

            let isGuid (s: string) =
                s.Length = 32
                && s
                    |> Seq.forall (fun c -> c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F')

            if isGuid dirName && not (referencedHostPaths.Contains dir) then
                try
                    let age = DateTime.UtcNow - Directory.GetCreationTimeUtc(dir)

                    if age > maxAge then
                        Directory.Delete(dir, true)
                        Log.Warning("Staging orphelin supprimé : {Dir} (âge {Age})", dir, age)
                with ex ->
                    Log.Warning(ex, "Échec de la suppression du staging orphelin : {Dir}", dir)

    let private extractOrRaise (source: string) (staging: string) (readOnly: bool) =
        try
            FsImage.extract source staging readOnly |> Result.defaultWith failwith |> ignore
        with ex ->
            try
                Directory.Delete(staging, true)
            with _ ->
                ()

            reraise ()

    let mount (source: string) (destination: string) (readOnly: bool) : MountedVolume =
        if Directory.Exists source then
            { Source = source
              HostPath = source
              Destination = destination
              ReadOnly = readOnly
              Dispose = fun () -> () }
        elif File.Exists source then
            let format = DiskFormat.detect source

            match format with
            | DiskFormat.Unknown -> invalidArg "source" (sprintf "Format d'image disque non reconnu : '%s'" source)
            | _ ->
                let staging = Path.Combine(stagingRoot (), Guid.NewGuid().ToString("N"))
                Directory.CreateDirectory staging |> ignore
                extractOrRaise source staging readOnly

                let dispose () =
                    if not readOnly then
                        try
                            FsImage.writeBack source staging |> Result.defaultWith failwith
                            // Suppression du staging uniquement après writeBack réussi
                            Directory.Delete(staging, true)
                        with ex ->
                            Log.Error(
                                ex,
                                "Échec du write-back pour {Source} — le staging est conservé dans {Staging}",
                                source,
                                staging
                            )
                    else
                        try
                            Directory.Delete(staging, true)
                        with _ ->
                            ()

                { Source = source
                  HostPath = staging
                  Destination = destination
                  ReadOnly = readOnly
                  Dispose = dispose }
        else
            invalidArg "source" (sprintf "La source du volume n'existe pas : '%s'" source)

    /// Reconstruit un volume monté à partir de l'état persisté (après un
    /// redémarrage du service) : réutilise le dossier de staging existant et
    /// restaure le write-back sans ré-extraire l'image. Pour un bind mount de
    /// répertoire (Source est un dossier), la libération reste sans effet.
    let rehydrate (source: string) (hostPath: string) (destination: string) (readOnly: bool) : MountedVolume =
        let dispose =
            if Directory.Exists source then
                fun () -> ()
            elif not (Directory.Exists hostPath) then
                // Le staging a disparu (prune, nettoyage manuel...) : le
                // write-back est impossible, le signaler bruyamment plutôt que
                // de transformer la libération en no-op silencieux.
                fun () ->
                    Log.Error(
                        "Write-back impossible : le staging {HostPath} de la source {Source} n'existe plus — modifications perdues",
                        hostPath,
                        source
                    )
            else
                fun () ->
                    if not readOnly then
                        try
                            FsImage.writeBack source hostPath |> Result.defaultWith failwith
                            Directory.Delete(hostPath, true)
                        with ex ->
                            Log.Error(
                                ex,
                                "Échec du write-back pour {Source} — le staging est conservé dans {Staging}",
                                source,
                                hostPath
                            )
                    else
                        try
                            Directory.Delete(hostPath, true)
                        with _ ->
                            ()

        { Source = source
          HostPath = hostPath
          Destination = destination
          ReadOnly = readOnly
          Dispose = dispose }

/// Implémentation concrète d'IDiskMounter pour l'injection de dépendances.
type DiskMounter() =
    do DiskMounter.pruneStaleStaging (TimeSpan.FromHours 24.0)
    interface IDiskMounter with
        member _.Mount(source, destination, readOnly) =
            DiskMounter.mount source destination readOnly

