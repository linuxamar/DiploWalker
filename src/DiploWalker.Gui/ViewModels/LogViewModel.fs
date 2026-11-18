namespace DiploWalker.Gui.ViewModels

open System
open System.Collections.ObjectModel
open System.Threading.Tasks
open ReactiveUI

/// Représente une entrée de log.
type LogEntry =
    { Timestamp: DateTime
      Level: string
      Message: string
      Source: string }

/// ViewModel pour l'affichage des logs.
type LogViewModel() =
    inherit ViewModelBase()

    let mutable statusMessage = String.Empty
    let mutable selectedLevel = "Tous"
    let entries = ObservableCollection<LogEntry>()

    member this.StatusMessage
        with get() = statusMessage
        and set(v) =
            statusMessage <- v
            this.RaisePropertyChanged("StatusMessage")

    member this.SelectedLevel
        with get() = selectedLevel
        and set(v) =
            selectedLevel <- v
            this.RaisePropertyChanged("SelectedLevel")
            this.FilterEntries() |> Async.AwaitTask |> Async.RunSynchronously |> ignore

    member this.Entries = entries

    member this.Levels =
        [ "Tous"; "Debug"; "Information"; "Avertissement"; "Erreur"; "Critique" ]

    /// Ajoute une entrée de log.
    member this.AddLogEntry(entry: LogEntry) =
        this.Entries.Insert(0, entry)
        if this.Entries.Count > 500 then
            while this.Entries.Count > 500 do
                this.Entries.RemoveAt(this.Entries.Count - 1)

    /// Filtre les entrées selon le niveau sélectionné.
    member private this.FilterEntries() : Task =
        task {
            let filtered =
                if selectedLevel = "Tous" then
                    this.Entries |> Seq.cast<LogEntry> |> Seq.toList
                else
                    this.Entries
                    |> Seq.cast<LogEntry>
                    |> Seq.filter (fun e -> e.Level = selectedLevel)
                    |> Seq.toList

            this.Entries.Clear()
            for e in filtered do
                this.Entries.Add(e)
        }

    /// Efface tous les logs.
    member this.Clear() : Task =
        task {
            this.Entries.Clear()
            this.StatusMessage <- "Logs effacés"
        }

    member this.ClearCommand =
        ReactiveCommand.CreateFromTask(this.Clear)