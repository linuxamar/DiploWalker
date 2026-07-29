namespace Diplo.Gui.ViewModels

open System
open System.Windows.Input
open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Threading
open Diplo.Core.Output
open Diplo.Gui.Services

type MainWindowViewModel() as this =
    inherit ViewModelBase()

    let outputPort = AvaloniaOutputPort()
    let logText = Text.StringBuilder()

    let containerTab = ContainerTabViewModel(outputPort)
    let volumeTab = VolumeTabViewModel(outputPort)
    let networkTab = NetworkTabViewModel(outputPort)
    let composeTab = ComposeTabViewModel(outputPort)

    let updateLog () =
        logText.Clear() |> ignore
        for entry in outputPort.LogLines do
            logText.AppendLine(entry.DisplayText) |> ignore
        this.OnPropertyChanged(nameof this.LogOutput)

    do outputPort.LogLines.CollectionChanged.Add(fun _ -> Dispatcher.UIThread.Post(updateLog))

    member _.OutputPort = outputPort :> IOutputPort
    member _.LogOutput = logText.ToString()

    member _.ContainerTab = containerTab
    member _.VolumeTab = volumeTab
    member _.NetworkTab = networkTab
    member _.ComposeTab = composeTab

    member _.QuitCommand: ICommand =
        RelayCommand(Action(fun () ->
            match Application.Current.ApplicationLifetime with
            | :? IClassicDesktopStyleApplicationLifetime as desktop ->
                desktop.Shutdown(0)
            | _ -> ()))

    member _.AboutCommand: ICommand =
        RelayCommand(Action(fun () ->
            (outputPort :> IOutputPort).WriteLine("Diplo — Gestion Docker")
            (outputPort :> IOutputPort).WriteLine("Interface graphique Avalonia pour la gestion de conteneurs, volumes et réseaux.")))
