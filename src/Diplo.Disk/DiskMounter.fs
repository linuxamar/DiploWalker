namespace Diplo.Disk

open System
open System.IO

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

    let stagingRoot () =
        let baseDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Diplo", "volumes")
        Directory.CreateDirectory baseDir |> ignore
        baseDir

    let private extractOrRaise (source: string) (staging: string) (readOnly: bool) =
        try
            FsImage.extract source staging readOnly |> ignore
        with ex ->
            try Directory.Delete(staging, true) with _ -> ()
            reraise ()

    let mount (source: string) (destination: string) (readOnly: bool) : MountedVolume =
        if Directory.Exists source then
            { Source = source; HostPath = source; Destination = destination; ReadOnly = readOnly; Dispose = fun () -> () }
        elif File.Exists source then
            let format = DiskFormat.detect source
            match format with
            | DiskFormat.Unknown ->
                failwithf "Format d'image disque non reconnu : '%s'" source
            | _ ->
                let staging = Path.Combine(stagingRoot (), Guid.NewGuid().ToString("N"))
                Directory.CreateDirectory staging |> ignore
                extractOrRaise source staging readOnly
                let dispose () =
                    try
                        if not readOnly then FsImage.writeBack source staging
                    finally
                        try Directory.Delete(staging, true) with _ -> ()
                { Source = source; HostPath = staging; Destination = destination; ReadOnly = readOnly; Dispose = dispose }
        else
            failwithf "La source du volume n'existe pas : '%s'" source

    /// Reconstruit un volume monté à partir de l'état persisté (après un
    /// redémarrage du service) : réutilise le dossier de staging existant et
    /// restaure le write-back sans ré-extraire l'image. Pour un bind mount de
    /// répertoire (Source est un dossier), la libération reste sans effet.
    let rehydrate (source: string) (hostPath: string) (destination: string) (readOnly: bool) : MountedVolume =
        let dispose =
            if Directory.Exists source then fun () -> ()
            elif not (Directory.Exists hostPath) then fun () -> ()
            else
                fun () ->
                    try
                        if not readOnly then FsImage.writeBack source hostPath
                    finally
                        try Directory.Delete(hostPath, true) with _ -> ()
        { Source = source; HostPath = hostPath; Destination = destination; ReadOnly = readOnly; Dispose = dispose }

/// Implémentation concrète d'IDiskMounter pour l'injection de dépendances.
type DiskMounter() =
    interface IDiskMounter with
        member _.Mount(source, destination, readOnly) =
            DiskMounter.mount source destination readOnly
