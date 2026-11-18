namespace DiploWalker.Gui.ViewModels

open System
open System.Collections.ObjectModel
open System.Threading.Tasks
open ReactiveUI
open DiploWalker.Core.Compose
open DiploWalker.Core.Output
open DiploWalker.Core.Clients
open DiploWalker.Cli

/// ViewModel pour l'orchestration Compose.
type ComposeViewModel() =
    inherit ViewModelBase()

    let mutable statusMessage = String.Empty
    let mutable composeFile = String.Empty
    let mutable services = ObservableCollection<string>()
    let mutable orchestrator: ComposeOrchestrator option = None

    member this.StatusMessage
        with get() = statusMessage
        and set(v) =
            statusMessage <- v
            this.RaisePropertyChanged("StatusMessage") |> ignore

    member this.ComposeFile
        with get() = composeFile
        and set(v) =
            composeFile <- v
            this.RaisePropertyChanged("ComposeFile") |> ignore

    member this.Services = services

    /// Rafraîchir la liste des services.
    member this.Refresh() : Task =
        task {
            this.IsLoading <- true
            this.StatusMessage <- "Liste des services..."
            try
                match orchestrator with
                | None ->
                    let output = new SpectreOutputPort() :> IOutputPort
                    let client = new ContainerClient() :> IContainerClient
                    orchestrator <- Some (new ComposeOrchestrator(output, client))
                | Some _ -> ()

                match orchestrator with
                | Some o ->
                    let! _ = o.Ps(composeFile)
                    this.StatusMessage <- "Services listés"
                | None ->
                    this.StatusMessage <- "Orchestrateur non initialisé"
            with ex ->
                this.StatusMessage <- sprintf "Erreur : %s" ex.Message
            this.IsLoading <- false
        }

    /// Exécuter `docker-compose up -d`.
    member this.Up() : Task =
        task {
            this.IsLoading <- true
            this.StatusMessage <- "Démarrage des services..."
            try
                match orchestrator with
                | None ->
                    let output = new SpectreOutputPort() :> IOutputPort
                    let client = new ContainerClient() :> IContainerClient
                    orchestrator <- Some (new ComposeOrchestrator(output, client))
                | Some _ -> ()

                match orchestrator with
                | Some o ->
                    let! _ = o.Up(composeFile)
                    this.StatusMessage <- "Services démarrés"
                    do! this.Refresh()
                | None ->
                    this.StatusMessage <- "Orchestrateur non initialisé"
            with ex ->
                this.StatusMessage <- sprintf "Erreur : %s" ex.Message
            this.IsLoading <- false
        }

    /// Exécuter `docker-compose down`.
    member this.Down() : Task =
        task {
            this.IsLoading <- true
            this.StatusMessage <- "Arrêt des services..."
            try
                match orchestrator with
                | None ->
                    let output = new SpectreOutputPort() :> IOutputPort
                    let client = new ContainerClient() :> IContainerClient
                    orchestrator <- Some (new ComposeOrchestrator(output, client))
                | Some _ -> ()

                match orchestrator with
                | Some o ->
                    let! _ = o.Down(composeFile)
                    this.StatusMessage <- "Services arrêtés"
                    do! this.Refresh()
                | None ->
                    this.StatusMessage <- "Orchestrateur non initialisé"
            with ex ->
                this.StatusMessage <- sprintf "Erreur : %s" ex.Message
            this.IsLoading <- false
        }

    member this.RefreshCommand =
        ReactiveCommand.CreateFromTask(this.Refresh)

    member this.UpCommand =
        ReactiveCommand.CreateFromTask(this.Up)

    member this.DownCommand =
        ReactiveCommand.CreateFromTask(this.Down)