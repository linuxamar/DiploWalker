namespace DiploWalker.Gui.ViewModels

open System
open System.Collections.ObjectModel
open System.Threading.Tasks
open ReactiveUI
open DiploWalker.Core.Clients
open DiploWalker.Grpc.Volume

/// ViewModel pour la gestion des volumes.
type VolumeViewModel() =
    inherit ViewModelBase()

    let volumes = ObservableCollection<VolumeInfo>()
    let mutable selectedVolume : VolumeInfo option = None
    let mutable statusMessage = String.Empty
    let mutable client : IVolumeClient option = None

    member this.Volumes = volumes

    member this.SelectedVolume
        with get() = selectedVolume
        and set(v) =
            selectedVolume <- v
            this.RaisePropertyChanged("SelectedVolume") |> ignore

    member this.StatusMessage
        with get() = statusMessage
        and set(v) =
            statusMessage <- v
            this.RaisePropertyChanged("StatusMessage") |> ignore

    member this.Refresh() : Task =
        task {
            this.IsLoading <- true
            this.StatusMessage <- "Chargement des volumes..."
            try
                match client with
                | None ->
                    client <- Some (new VolumeClient() :> IVolumeClient)
                | Some _ -> ()

                match client with
                | Some c ->
                    let! response = c.ListAsync()
                    volumes.Clear()
                    response.Volumes |> Seq.iter (volumes.Add)
                    this.StatusMessage <- sprintf "%d volume(s) chargé(s)" volumes.Count
                | None ->
                    this.StatusMessage <- "Client non initialisé"
            with ex ->
                this.StatusMessage <- sprintf "Erreur : %s" ex.Message
            this.IsLoading <- false
        }

    member this.RemoveSelected() : Task =
        task {
            match selectedVolume with
            | Some v ->
                this.IsLoading <- true
                this.StatusMessage <- sprintf "Suppression de %s..." v.Name
                try
                    match client with
                    | None ->
                        client <- Some (new VolumeClient() :> IVolumeClient)
                    | Some _ -> ()

                    match client with
                    | Some cli ->
                        let! _ = cli.RemoveAsync(v.Id, force = true)
                        do! this.Refresh()
                        this.StatusMessage <- sprintf "Volume %s supprimé" v.Name
                    | None ->
                        this.StatusMessage <- "Client non initialisé"
                with ex ->
                    this.StatusMessage <- sprintf "Erreur : %s" ex.Message
                this.IsLoading <- false
            | None ->
                this.StatusMessage <- "Aucun volume sélectionné"
        }

    member this.RemoveCommand =
        ReactiveCommand.CreateFromTask(this.RemoveSelected)

    member this.RefreshCommand =
        ReactiveCommand.CreateFromTask(this.Refresh)
