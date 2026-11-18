namespace DiploWalker.Gui.ViewModels

open System
open System.Collections.ObjectModel
open System.Threading.Tasks
open ReactiveUI
open DiploWalker.Core.Clients
open DiploWalker.Grpc.Container

/// ViewModel pour la gestion des catalogues d'images.
type CatalogViewModel() =
    inherit ViewModelBase()

    let mutable statusMessage = String.Empty
    let mutable query = String.Empty
    let mutable registry = String.Empty
    let mutable results = ObservableCollection<RegistrySearchResult>()
    let mutable selectedImage: RegistrySearchResult option = None
    let mutable client: IContainerClient option = None

    member this.StatusMessage
        with get() = statusMessage
        and set(v) =
            statusMessage <- v
            this.RaisePropertyChanged("StatusMessage")

    member this.Query
        with get() = query
        and set(v) =
            query <- v
            this.RaisePropertyChanged("Query")

    member this.Registry
        with get() = registry
        and set(v) =
            registry <- v
            this.RaisePropertyChanged("Registry")

    member this.Results = results

    member this.SelectedImage
        with get() = selectedImage
        and set(v) =
            selectedImage <- v
            this.RaisePropertyChanged("SelectedImage")

    /// Registres disponibles
    member this.Registries =
        [ "docker.io"; "quay.io"; "ghcr.io"; "mcr.microsoft.com" ]

    /// Rechercher des images dans les catalogues.
    member this.Search() : Task =
        task {
            this.IsLoading <- true
            this.StatusMessage <- sprintf "Recherche de '%s'..." query
            try
                match client with
                | None ->
                    client <- Some (new ContainerClient() :> IContainerClient)
                | Some _ -> ()

                match client with
                | Some c ->
                    let! response = c.SearchImagesAsync(query, registry = registry)
                    results.Clear()
                    for img in response.Results do
                        results.Add(img)
                    this.StatusMessage <- sprintf "%d résultat(s)" results.Count
                | None ->
                    this.StatusMessage <- "Client non initialisé"
            with ex ->
                this.StatusMessage <- sprintf "Erreur : %s" ex.Message
            this.IsLoading <- false
        }

    member this.SearchCommand =
        ReactiveCommand.CreateFromTask(this.Search)
