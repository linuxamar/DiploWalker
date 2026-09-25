namespace DiploWalker.Disk

open System
open System.IO
open Serilog
open DiploWalker.Abstractions

/// Volume montÃ© pour un conteneur : chemin hÃ´te Ã  bind-mounter dans le
/// conteneur (`ctr --mount type=bind,src=...`) et action de libÃ©ration.
/// `Source` est la source d'origine (rÃ©pertoire hÃ´te ou image disque) et
/// permet de restaurer le write-back aprÃ¨s un redÃ©marrage du service.
type MountedVolume =
    { Source: string
      HostPath: string
      Destination: string
      ReadOnly: bool
      Dispose: unit -> unit }

/// Service de montage d'images disque pour les conteneurs.
type IDiskMounter =
    /// Monte la source (rÃ©pertoire ou image disque) pour la destination
    /// donnÃ©e et retourne le volume Ã  bind-mounter.
    abstract member Mount: source: string * destination: string * readOnly: bool -> MountedVolume

/// Orchestration du montage des images disque au dÃ©marrage des conteneurs.
///
/// Une source peut Ãªtre :
///  - un rÃ©pertoire hÃ´te : bind mount direct (aucune copie) ;
///  - une image disque (qcow2, raw, vhd, vhdx, vmdk) : son contenu est
///    exposÃ© dans un dossier de staging sous %ProgramData%\Diplo\volumes.
///    Ã€ la libÃ©ration, les modifications sont rÃ©Ã©crites dans l'image :
///    pour qcow2, l'Ã©criture se fait directement dans les clusters du
///    fichier via le pilote maison (aucune conversion, aucun VHD).
module DiskMounter =

    let stagingRoot () = AppPaths.dataDir "volumes"

    /// Supprime les dossiers de staging orphelins (GUID) plus anciens que
    /// `maxAge`. Un staging est orphelin quand le service a crashÃ© entre
    /// l'extraction et l'appel au write-back. Les stagings rÃ©fÃ©rencÃ©s par
    /// l'Ã©tat persistÃ© (mounted-state.json, en attente de rehydration aprÃ¨s un
    /// arrÃªt long) sont prÃ©servÃ©s : les supprimer dÃ©truirait des modifications
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
                    "Impossible de lire l'Ã©tat persistÃ© avant le prune : aucun staging ne sera supprimÃ©"
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
                        Log.Warning("Staging orphelin supprimÃ© : {Dir} (Ã¢ge {Age})", dir, age)
                with ex ->
                    Log.Warning(ex, "Ã‰chec de la suppression du staging orphelin : {Dir}", dir)

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
                            // Suppression du staging uniquement aprÃ¨s writeBack rÃ©ussi
                            Directory.Delete(staging, true)
                        with ex ->
                            Log.Error(
                                ex,
                                "Ã‰chec du write-back pour {Source} â€” le staging est conservÃ© dans {Staging}",
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

    /// Reconstruit un volume montÃ© Ã  partir de l'Ã©tat persistÃ© (aprÃ¨s un
    /// redÃ©marrage du service) : rÃ©utilise le dossier de staging existant et
    /// restaure le write-back sans rÃ©-extraire l'image. Pour un bind mount de
    /// rÃ©pertoire (Source est un dossier), la libÃ©ration reste sans effet.
    let rehydrate (source: string) (hostPath: string) (destination: string) (readOnly: bool) : MountedVolume =
        let dispose =
            if Directory.Exists source then
                fun () -> ()
            elif not (Directory.Exists hostPath) then
                // Le staging a disparu (prune, nettoyage manuel...) : le
                // write-back est impossible, le signaler bruyamment plutÃ´t que
                // de transformer la libÃ©ration en no-op silencieux.
                fun () ->
                    Log.Error(
                        "Write-back impossible : le staging {HostPath} de la source {Source} n'existe plus â€” modifications perdues",
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
                                "Ã‰chec du write-back pour {Source} â€” le staging est conservÃ© dans {Staging}",
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

/// ImplÃ©mentation concrÃ¨te d'IDiskMounter pour l'injection de dÃ©pendances.
type DiskMounter() =
    do DiskMounter.pruneStaleStaging (TimeSpan.FromHours 24.0)
    interface IDiskMounter with
        member _.Mount(source, destination, readOnly) =
            DiskMounter.mount source destination readOnly

