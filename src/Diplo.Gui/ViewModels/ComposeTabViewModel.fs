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

    member _.ComposeUpCommand = RelayCommand(Action(fun () -> this.ComposeUp() |> ignore))
    member _.ComposeDownCommand = RelayCommand(Action(fun () -> this.ComposeDown() |> ignore))
    member _.ComposePsCommand = RelayCommand(Action(fun () -> this.ComposePs() |> ignore))
    member _.ComposeLogsCommand = RelayCommand(Action(fun () -> this.ComposeLogs() |> ignore))
    member _.ComposePullCommand = RelayCommand(Action(fun () -> this.ComposePull() |> ignore))
    member _.ComposeBuildCommand = RelayCommand(Action(fun () -> this.ComposeBuild() |> ignore))

    member private this.ComposeUp() =
        task {
            try
                let orchestrator = ComposeOrchestrator(outputPort)
                do! orchestrator.Up(this.ComposeFilePath)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.ComposeDown() =
        task {
            try
                let orchestrator = ComposeOrchestrator(outputPort)
                do! orchestrator.Down(this.ComposeFilePath)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.ComposePs() =
        task {
            try
                let orchestrator = ComposeOrchestrator(outputPort)
                let compose = orchestrator.ParseFile(this.ComposeFilePath)
                use client = new ContainerClient()
                let! response = client.ListAsync(all = true)

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
        task {
            try
                let orchestrator = ComposeOrchestrator(outputPort)
                let service = if String.IsNullOrEmpty(this.ComposeServiceName) then None else Some this.ComposeServiceName
                do! orchestrator.Logs(this.ComposeFilePath, service)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.ComposeBuild() =
        task {
            try
                let orchestrator = ComposeOrchestrator(outputPort)
                do! orchestrator.Build(this.ComposeFilePath)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.ComposePull() =
        task {
            try
                let orchestrator = ComposeOrchestrator(outputPort)
                do! orchestrator.Pull(this.ComposeFilePath)
            with ex -> outputPort.WriteError(ex.Message)
        }
