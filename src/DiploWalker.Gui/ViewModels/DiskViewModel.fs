namespace DiploWalker.Gui.ViewModels

open System
open System.Collections.ObjectModel
open System.IO
open System.Threading.Tasks
open ReactiveUI
open DiploWalker.Disk

/// ViewModel pour la gestion des images disque.
type DiskViewModel() =
    inherit ViewModelBase()

    let mutable statusMessage = String.Empty
    let mutable sourceDir = String.Empty
    let mutable destPath = String.Empty
    let mutable selectedFormat = "raw"

    member this.StatusMessage
        with get() = statusMessage
        and set(v) =
            statusMessage <- v
            this.RaisePropertyChanged("StatusMessage")

    member this.SourceDir
        with get() = sourceDir
        and set(v) =
            sourceDir <- v
            this.RaisePropertyChanged("SourceDir")

    member this.DestPath
        with get() = destPath
        and set(v) =
            destPath <- v
            this.RaisePropertyChanged("DestPath")

    member this.SelectedFormat
        with get() = selectedFormat
        and set(v) =
            selectedFormat <- v
            this.RaisePropertyChanged("SelectedFormat")

    /// Formats disponibles
    member this.Formats =
        [ "raw"; "vhd"; "vhdx"; "vmdk"; "vdi" ]

    /// Rafraîchir (no-op pour le disque, mais requis par l'interface)
    member this.Refresh() : Task =
        task {
            this.StatusMessage <- "Module disque prêt"
        }

    /// Créer une image disque
    member private this.CreateImage() : Task =
        task {
            this.IsLoading <- true
            this.StatusMessage <- "Création de l'image..."
            try
                let format =
                    match selectedFormat.ToLowerInvariant() with
                    | "vhd" -> DiskFormat.Format.Vhd
                    | "vhdx" -> DiskFormat.Format.Vhdx
                    | "vmdk" -> DiskFormat.Format.Vmdk
                    | "vdi" -> DiskFormat.Format.Vdi
                    | _ -> DiskFormat.Format.Raw

                let result = FsImage.create sourceDir destPath format

                match result with
                | Ok path ->
                    let size = FileInfo(path).Length
                    this.StatusMessage <- sprintf "Image créée : %s (%d Mo)" path (size / 1024L / 1024L)
                | Error err ->
                    this.StatusMessage <- sprintf "Erreur : %s" err
            with ex ->
                this.StatusMessage <- sprintf "Erreur : %s" ex.Message
            this.IsLoading <- false
        }

    /// Extraire une image disque
    member private this.ExtractImage() : Task =
        task {
            this.IsLoading <- true
            this.StatusMessage <- "Extraction de l'image..."
            try
                let result = FsImage.extract destPath sourceDir false

                match result with
                | Ok count ->
                    this.StatusMessage <- sprintf "%d fichier(s) extrait(s)" count
                | Error err ->
                    this.StatusMessage <- sprintf "Erreur : %s" err
            with ex ->
                this.StatusMessage <- sprintf "Erreur : %s" ex.Message
            this.IsLoading <- false
        }

    member this.CreateCommand =
        ReactiveCommand.CreateFromTask(this.CreateImage)

    member this.ExtractCommand =
        ReactiveCommand.CreateFromTask(this.ExtractImage)

    member this.RefreshCommand =
        ReactiveCommand.CreateFromTask(this.Refresh)
