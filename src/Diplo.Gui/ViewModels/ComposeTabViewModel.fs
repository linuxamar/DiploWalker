namespace Diplo.Gui.ViewModels

open System
open System.Collections.ObjectModel
open Avalonia.Threading
open Diplo.Core.Clients
open Diplo.Core.Compose
open Diplo.Core.Output

type ComposeServiceInfo = {
    Service: string
    Conteneur: string
    Image: string
    État: string
    Projet: string
}

type ComposeTabViewModel(outputPort: IOutputPort) as this =
    inherit ViewModelBase()

    let composeServices = ObservableCollection<ComposeServiceInfo>()

    let mutable composeFilePath = ""
    let mutable composeServiceName = ""

    member _.ComposeServices = composeServices

    member _.ComposeFilePath with get () = composeFilePath and set v = composeFilePath <- v; this.OnPropertyChanged()
    member _.ComposeServiceName with get () = composeServiceName and set v = composeServiceName <- v; this.OnPropertyChanged()

    member _.ComposeUpCommand = RelayCommand(Action(fun () -> this.ComposeUp() |> Async.Start))
    member _.ComposeDownCommand = RelayCommand(Action(fun () -> this.ComposeDown() |> Async.Start))
    member _.ComposePsCommand = RelayCommand(Action(fun () -> this.ComposePs() |> Async.Start))
    member _.ComposeLogsCommand = RelayCommand(Action(fun () -> this.ComposeLogs() |> Async.Start))
    member _.ComposePullCommand = RelayCommand(Action(fun () -> this.ComposePull() |> Async.Start))
    member _.ComposeBuildCommand = RelayCommand(Action(fun () -> this.ComposeBuild() |> Async.Start))

    member private this.ComposeUp() =
        async {
            try
                let orchestrator = ComposeOrchestrator(outputPort)
                do! orchestrator.Up(this.ComposeFilePath) |> Async.AwaitTask
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.ComposeDown() =
        async {
            try
                let orchestrator = ComposeOrchestrator(outputPort)
                do! orchestrator.Down(this.ComposeFilePath) |> Async.AwaitTask
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.ComposePs() =
        async {
            try
                let orchestrator = ComposeOrchestrator(outputPort)
                let compose = orchestrator.ParseFile(this.ComposeFilePath)
                use client = new ContainerClient()
                let! response = client.ListAsync(all = true) |> Async.AwaitTask

                Dispatcher.UIThread.Post(fun () ->
                    composeServices.Clear()
                    for c in response.Containers do
                        let hasProject =
                            c.Labels
                            |> Seq.exists (fun kv -> kv.Key = composeProjectLabel && kv.Value = compose.ProjectName)
                        if hasProject then
                            let service =
                                c.Labels
                                |> Seq.tryFind (fun kv -> kv.Key = composeServiceLabel)
                                |> Option.map (fun kv -> kv.Value)
                                |> Option.defaultValue "-"
                            composeServices.Add({
                                Service = service
                                Conteneur = c.Name
                                Image = c.Image
                                État = c.State.ToString()
                                Projet = compose.ProjectName
                            })
                )
                outputPort.WriteSuccess(sprintf "%d conteneur(s) compose trouvé(s)" composeServices.Count)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.ComposeLogs() =
        async {
            try
                let orchestrator = ComposeOrchestrator(outputPort)
                let service = if String.IsNullOrEmpty(this.ComposeServiceName) then None else Some this.ComposeServiceName
                do! orchestrator.Logs(this.ComposeFilePath, service) |> Async.AwaitTask
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.ComposeBuild() =
        async {
            try
                let orchestrator = ComposeOrchestrator(outputPort)
                do! orchestrator.Build(this.ComposeFilePath) |> Async.AwaitTask
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.ComposePull() =
        async {
            try
                let orchestrator = ComposeOrchestrator(outputPort)
                do! orchestrator.Pull(this.ComposeFilePath) |> Async.AwaitTask
            with ex -> outputPort.WriteError(ex.Message)
        }
