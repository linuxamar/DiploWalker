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
    let maxLogLines = 500
    let mutable logOutputCache = ""

    let containerTab = new ContainerTabViewModel(outputPort)
    let volumeTab = new VolumeTabViewModel(outputPort)
    let networkTab = new NetworkTabViewModel(outputPort)
    let composeTab = new ComposeTabViewModel(outputPort)
    let settingsTab = new SettingsTabViewModel(outputPort)

    let trimLogLines () =
        while outputPort.LogLines.Count > maxLogLines do
            outputPort.LogLines.RemoveAt(0)

    let updateLog () =
        logText.Clear() |> ignore
        let start = max 0 (outputPort.LogLines.Count - maxLogLines)

        for i in start .. outputPort.LogLines.Count - 1 do
            logText.AppendLine(outputPort.LogLines.[i].DisplayText) |> ignore

        logOutputCache <- logText.ToString()
        this.OnPropertyChanged(nameof this.LogOutput)

    do
        outputPort.LogLines.CollectionChanged.Add(fun _ ->
            UiThread.Post(fun () ->
                trimLogLines ()
                updateLog ()))

    member _.OutputPort = outputPort :> IOutputPort
    member _.LogOutput = logOutputCache

    member _.ContainerTab = containerTab
    member _.VolumeTab = volumeTab
    member _.NetworkTab = networkTab
    member _.ComposeTab = composeTab
    member _.SettingsTab = settingsTab

    member _.QuitCommand: ICommand =
        RelayCommand(
            Action(fun () ->
                match Application.Current with
                | null -> ()
                | app ->
                    match app.ApplicationLifetime with
                    | :? IClassicDesktopStyleApplicationLifetime as desktop -> desktop.Shutdown(0)
                    | _ -> ())
        )

    member _.AboutCommand: ICommand =
        RelayCommand(
            Action(fun () ->
                (outputPort :> IOutputPort).WriteLine("Diplo — Gestion Docker")

                (outputPort :> IOutputPort)
                    .WriteLine("Interface graphique Avalonia pour la gestion de conteneurs, volumes et réseaux."))
        )

    interface IDisposable with
        member _.Dispose() =
            (containerTab :> IDisposable).Dispose()
            (volumeTab :> IDisposable).Dispose()
            (networkTab :> IDisposable).Dispose()
            (composeTab :> IDisposable).Dispose()
