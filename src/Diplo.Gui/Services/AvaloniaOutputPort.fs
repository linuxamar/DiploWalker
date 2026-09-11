namespace Diplo.Gui.Services

open System
open System.Collections.ObjectModel
open System.Threading
open Avalonia
open Avalonia.Threading
open Diplo.Core.Output

/// Acheminement des actions devant s'exécuter sur le thread UI.
type UiThread =

    /// Exécute l'action sur le thread UI du Dispatcher quand une application
    /// Avalonia est initialisée ; sinon l'exécute immédiatement sur le thread
    /// courant. Évite ainsi de créer le Dispatcher dans les tests non-headless
    /// qui exercent les ViewModels hors de tout contexte Avalonia.
    static member Post(action: unit -> unit) =
        if isNull Application.Current then
            action ()
        else
            Dispatcher.UIThread.Post(action)

type LogLevel =
    | Info
    | Success
    | Warning
    | Error

type LogEntry(text: string, level: LogLevel) =
    member _.Text = text
    member _.Level = level
    member _.Timestamp = DateTime.Now.ToString("HH:mm:ss")
    member this.DisplayText = $"[{this.Timestamp}] {text}"

type AvaloniaOutputPort() =

    let logLines = ObservableCollection<LogEntry>()
    let tableColumns = ObservableCollection<string>()
    let tableRows = ObservableCollection<string[]>()

    let post (action: unit -> unit) =
        if SynchronizationContext.Current <> null then
            action ()
        else
            UiThread.Post(action)

    member _.LogLines = logLines
    member _.TableColumns = tableColumns
    member _.TableRows = tableRows

    member _.Clear() =
        post (fun () ->
            logLines.Clear()
            tableColumns.Clear()
            tableRows.Clear())

    member _.ClearTable() =
        post (fun () ->
            tableColumns.Clear()
            tableRows.Clear())

    interface IOutputPort with
        member _.WriteLine(text) =
            post (fun () -> logLines.Add(LogEntry(text, Info)))

        member _.WriteError(text) =
            post (fun () -> logLines.Add(LogEntry(text, Error)))

        member _.WriteSuccess(text) =
            post (fun () -> logLines.Add(LogEntry(text, Success)))

        member _.WriteWarning(text) =
            post (fun () -> logLines.Add(LogEntry(text, Warning)))

        member _.WriteTable(items, columns, selector) =
            post (fun () ->
                tableColumns.Clear()
                tableRows.Clear()

                for col in columns do
                    tableColumns.Add(col)

                for item in items do
                    let row = selector item
                    tableRows.Add(row))
