namespace Diplo.Disk

open System
open System.IO

/// Volume monté pour un conteneur : chemin hôte à bind-mounter dans le
/// conteneur (`ctr --mount type=bind,src=...`) et action de libération.
type MountedVolume =
    { HostPath: string
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
            { HostPath = source; Destination = destination; ReadOnly = readOnly; Dispose = fun () -> () }
        elif File.Exists source then
            let format = DiskFormat.detect source
            match format with
            | DiskFormat.Qcow1 ->
                failwith "Les images qcow v1 ne sont pas prises en charge (convertissez-les en qcow2 avec qemu-img)"
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
                { HostPath = staging; Destination = destination; ReadOnly = readOnly; Dispose = dispose }
        else
            failwithf "La source du volume n'existe pas : '%s'" source

/// Implémentation concrète d'IDiskMounter pour l'injection de dépendances.
type DiskMounter() =
    interface IDiskMounter with
        member _.Mount(source, destination, readOnly) =
            DiskMounter.mount source destination readOnly
