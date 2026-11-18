namespace DiploWalker.Gui.ViewModels

open System
open System.Collections.ObjectModel
open System.Threading.Tasks
open ReactiveUI
open DiploWalker.Core.Clients
open DiploWalker.Grpc.Network

/// ViewModel pour la gestion des réseaux.
type NetworkViewModel() =
    inherit ViewModelBase()

    let networks = ObservableCollection<NetworkInfo>()
    let mutable selectedNetwork : NetworkInfo option = None
    let mutable statusMessage = String.Empty
    let mutable client : INetworkClient option = None

    member this.Networks = networks

    member this.SelectedNetwork
        with get() = selectedNetwork
        and set(v) =
            selectedNetwork <- v
            this.RaisePropertyChanged("SelectedNetwork")

    member this.StatusMessage
        with get() = statusMessage
        and set(v) =
            statusMessage <- v
            this.RaisePropertyChanged("StatusMessage")

    member this.Refresh() : Task =
        task {
            this.IsLoading <- true
            this.StatusMessage <- "Chargement des réseaux..."
            try
                match client with
                | None ->
                    client <- Some (new NetworkClient() :> INetworkClient)
                | Some _ -> ()

                match client with
                | Some c ->
                    let! response = c.ListAsync()
                    networks.Clear()
                    response.Networks |> Seq.iter (networks.Add)
                    this.StatusMessage <- sprintf "%d réseau(x) chargé(s)" networks.Count
                | None ->
                    this.StatusMessage <- "Client non initialisé"
            with ex ->
                this.StatusMessage <- sprintf "Erreur : %s" ex.Message
            this.IsLoading <- false
        }

    member private this.RemoveSelected() : Task =
        task {
            match selectedNetwork with
            | Some n ->
                this.IsLoading <- true
                this.StatusMessage <- sprintf "Suppression de %s..." n.Name
                try
                    match client with
                    | None ->
                        client <- Some (new NetworkClient() :> INetworkClient)
                    | Some _ -> ()

                    match client with
                    | Some c ->
                        let! _ = c.RemoveAsync(n.Id, force = true)
                        do! this.Refresh()
                        this.StatusMessage <- sprintf "Réseau %s supprimé" n.Name
                    | None ->
                        this.StatusMessage <- "Client non initialisé"
                with ex ->
                    this.StatusMessage <- sprintf "Erreur : %s" ex.Message
                this.IsLoading <- false
            | None ->
                this.StatusMessage <- "Aucun réseau sélectionné"
        }

    member this.RemoveCommand =
        ReactiveCommand.CreateFromTask(this.RemoveSelected)

    member this.RefreshCommand =
        ReactiveCommand.CreateFromTask(this.Refresh)

    interface IDisposable with
        member _.Dispose() =
            match client with
            | Some c -> (c :> IDisposable).Dispose()
            | None -> ()
