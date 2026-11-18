namespace DiploWalker.Gui.ViewModels

open System
open System.Collections.ObjectModel
open System.Threading.Tasks
open ReactiveUI
open DiploWalker.Core.Clients
open DiploWalker.Grpc.Container

/// ViewModel pour la gestion des conteneurs.
type ContainerViewModel() =
    inherit ViewModelBase()

    let containers = ObservableCollection<ContainerInfo>()
    let mutable selectedContainer : ContainerInfo option = None
    let mutable statusMessage = String.Empty
    let mutable client : IContainerClient option = None

    /// Collection observable de conteneurs (binding DataGrid)
    member this.Containers = containers

    /// Conteneur sélectionné dans le DataGrid
    member this.SelectedContainer
        with get() = selectedContainer
        and set(v) =
            selectedContainer <- v
            this.RaisePropertyChanged("SelectedContainer") |> ignore

    /// Message de statut (dernière action, erreur, etc.)
    member this.StatusMessage
        with get() = statusMessage
        and set(v) =
            statusMessage <- v
            this.RaisePropertyChanged("StatusMessage") |> ignore

    /// Rafraîchir la liste des conteneurs
    member this.Refresh() : Task =
        task {
            this.IsLoading <- true
            this.StatusMessage <- "Chargement des conteneurs..."
            try
                match client with
                | None ->
                    client <- Some (new ContainerClient() :> IContainerClient)
                | Some _ -> ()

                match client with
                | Some c ->
                    let! response = c.ListAsync(all = true)
                    containers.Clear()
                    response.Containers |> Seq.iter (containers.Add)
                    this.StatusMessage <- sprintf "%d conteneur(s) chargé(s)" containers.Count
                | None ->
                    this.StatusMessage <- "Client non initialisé"
            with ex ->
                this.StatusMessage <- sprintf "Erreur : %s" ex.Message
                this.LogDebug(sprintf "Refresh error: %s" (ex.ToString()))
            this.IsLoading <- false
        }

    /// Démarrer le conteneur sélectionné
    member this.StartSelected() : Task =
        task {
            match selectedContainer with
            | Some container ->
                this.IsLoading <- true
                this.StatusMessage <- sprintf "Démarrage de %s..." container.Name
                try
                    match client with
                    | None ->
                        client <- Some (new ContainerClient() :> IContainerClient)
                    | Some _ -> ()

                    match client with
                    | Some cli ->
                        let! _ = cli.StartAsync(container.Id)
                        do! this.Refresh()
                        this.StatusMessage <- sprintf "Conteneur %s démarré" container.Name
                    | None ->
                        this.StatusMessage <- "Client non initialisé"
                with ex ->
                    this.StatusMessage <- sprintf "Erreur : %s" ex.Message
                this.IsLoading <- false
            | None ->
                this.StatusMessage <- "Aucun conteneur sélectionné"
        }

    /// Arrêter le conteneur sélectionné
    member this.StopSelected() : Task =
        task {
            match selectedContainer with
            | Some container ->
                this.IsLoading <- true
                this.StatusMessage <- sprintf "Arrêt de %s..." container.Name
                try
                    match client with
                    | None ->
                        client <- Some (new ContainerClient() :> IContainerClient)
                    | Some _ -> ()

                    match client with
                    | Some cli ->
                        let! _ = cli.StopAsync(container.Id)
                        do! this.Refresh()
                        this.StatusMessage <- sprintf "Conteneur %s arrêté" container.Name
                    | None ->
                        this.StatusMessage <- "Client non initialisé"
                with ex ->
                    this.StatusMessage <- sprintf "Erreur : %s" ex.Message
                this.IsLoading <- false
            | None ->
                this.StatusMessage <- "Aucun conteneur sélectionné"
        }

    /// Supprimer le conteneur sélectionné
    member this.DeleteSelected() : Task =
        task {
            match selectedContainer with
            | Some container ->
                this.IsLoading <- true
                this.StatusMessage <- sprintf "Suppression de %s..." container.Name
                try
                    match client with
                    | None ->
                        client <- Some (new ContainerClient() :> IContainerClient)
                    | Some _ -> ()

                    match client with
                    | Some cli ->
                        let! _ = cli.DeleteAsync(container.Id, force = true)
                        do! this.Refresh()
                        this.StatusMessage <- sprintf "Conteneur %s supprimé" container.Name
                    | None ->
                        this.StatusMessage <- "Client non initialisé"
                with ex ->
                    this.StatusMessage <- sprintf "Erreur : %s" ex.Message
                this.IsLoading <- false
            | None ->
                this.StatusMessage <- "Aucun conteneur sélectionné"
        }

    member this.StartCommand =
        ReactiveCommand.CreateFromTask(this.StartSelected)

    member this.StopCommand =
        ReactiveCommand.CreateFromTask(this.StopSelected)

    member this.DeleteCommand =
        ReactiveCommand.CreateFromTask(this.DeleteSelected)

    member this.RefreshCommand =
        ReactiveCommand.CreateFromTask(this.Refresh)
