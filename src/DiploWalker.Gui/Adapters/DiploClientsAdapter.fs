namespace DiploWalker.Gui.Adapters

open System
open System.Threading.Tasks
open ReactiveUI
open DiploWalker.Core.Clients
open DiploWalker.Core.Connection

/// Adaptateur pour gérer les clients DiploWalker et leur état de connexion.
/// Fournit une interface unifiée pour créer les clients gRPC et gérer les erreurs.
type DiploClientsAdapter() as this =
    inherit ReactiveObject()

    let mutable isConnected = false
    let mutable lastError: string option = None
    let mutable containerClient: IContainerClient option = None
    let mutable networkClient: INetworkClient option = None
    let mutable volumeClient: IVolumeClient option = None

    /// Indique si la connexion aux services est établie.
    member this.IsConnected
        with get() = isConnected
        and private set(v) =
            isConnected <- v
            this.RaisePropertyChanged("IsConnected")

    /// Dernière erreur de connexion.
    member this.LastError
        with get() = lastError
        and private set(v) =
            lastError <- v
            this.RaisePropertyChanged("LastError")

    /// Crée et teste la connexion aux services.
    member this.Connect() : Task<bool> =
        task {
            try
                this.LastError <- None

                // Créer les clients
                let c = new ContainerClient() :> IContainerClient
                let n = new NetworkClient() :> INetworkClient
                let v = new VolumeClient() :> IVolumeClient

                // Tester la connexion en listant les conteneurs
                let! _ = c.ListAsync(all = false)

                containerClient <- Some c
                networkClient <- Some n
                volumeClient <- Some v
                this.IsConnected <- true

                return true
            with ex ->
                this.IsConnected <- false
                this.LastError <- Some ex.Message
                this.DisposeClients()
                return false
        }

    /// Déconnecte et libère les ressources.
    member this.Disconnect() : Task =
        task {
            this.DisposeClients()
            this.IsConnected <- false
        }

    /// Libère les clients.
    member private this.DisposeClients() =
        match containerClient with
        | Some c -> (c :> IDisposable).Dispose()
        | None -> ()
        match networkClient with
        | Some n -> (n :> IDisposable).Dispose()
        | None -> ()
        match volumeClient with
        | Some v -> (v :> IDisposable).Dispose()
        | None -> ()

        containerClient <- None
        networkClient <- None
        volumeClient <- None

    /// Obtient le client conteneur (le crée si nécessaire).
    member this.GetContainerClient() : IContainerClient =
        match containerClient with
        | Some c -> c
        | None ->
            let c = new ContainerClient() :> IContainerClient
            containerClient <- Some c
            c

    /// Obtient le client réseau (le crée si nécessaire).
    member this.GetNetworkClient() : INetworkClient =
        match networkClient with
        | Some n -> n
        | None ->
            let n = new NetworkClient() :> INetworkClient
            networkClient <- Some n
            n

    /// Obtient le client volume (le crée si nécessaire).
    member this.GetVolumeClient() : IVolumeClient =
        match volumeClient with
        | Some v -> v
        | None ->
            let v = new VolumeClient() :> IVolumeClient
            volumeClient <- Some v
            v

    /// Teste la connectivité sans créer de clients persistants.
    member this.TestConnection() : Task<bool> =
        task {
            try
                let c = new ContainerClient() :> IContainerClient
                let! _ = c.ListAsync(all = false)
                (c :> IDisposable).Dispose()
                return true
            with _ ->
                return false
        }

    interface IDisposable with
        member _.Dispose() =
            this.DisposeClients()