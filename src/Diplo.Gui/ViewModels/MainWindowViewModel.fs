namespace Diplo.Gui.ViewModels

open System
open System.IO
open System.Threading.Tasks
open System.Windows.Input
open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Platform.Storage
open Avalonia.Threading
open Diplo.Core.Output
open Diplo.Gui.Services

type MainWindowViewModel() as this =
    inherit ViewModelBase()

    let outputPort = AvaloniaOutputPort()
    let logText = Text.StringBuilder()
    let maxLogLines = 500
    let mutable logOutputCache = ""
    let mutable storageProvider: IStorageProvider = null

    let containerTab = new ContainerTabViewModel(outputPort)
    let imagesTab = new ImagesTabViewModel(outputPort)
    let volumeTab = new VolumeTabViewModel(outputPort)
    let networkTab = new NetworkTabViewModel(outputPort)
    let composeTab = new ComposeTabViewModel(outputPort)
    let settingsTab = new SettingsTabViewModel(outputPort)

    let dropFirstLines (sb: System.Text.StringBuilder) (count: int) =
        if count > 0 && sb.Length > 0 then
            let mutable remaining = count
            let mutable idx = -1
            let mutable i = 0

            while remaining > 0 && i < sb.Length do
                if sb.[i] = '\n' then
                    remaining <- remaining - 1
                    idx <- i

                i <- i + 1

            if idx >= 0 then
                sb.Remove(0, idx + 1) |> ignore

    let mutable renderedLineCount = 0

    let updateLog () =
        let count = outputPort.LogLines.Count

        if renderedLineCount > count then
            // Le journal a été vidé en externe (Clear) : on repart de zéro.
            logText.Clear() |> ignore
            renderedLineCount <- 0

        for i in renderedLineCount .. count - 1 do
            logText.AppendLine(outputPort.LogLines.[i].DisplayText) |> ignore

        renderedLineCount <- count
        logOutputCache <- logText.ToString()
        this.OnPropertyChanged(nameof this.LogOutput)

    let trimLogLines () =
        let excess = outputPort.LogLines.Count - maxLogLines

        if excess > 0 then
            // Les lignes les plus anciennes sont retirées du texte ET de la
            // collection pour conserver l'alignement (pas de balayage O(n²)).
            dropFirstLines logText excess
            renderedLineCount <- renderedLineCount - excess

            for _ in 1 .. excess do
                outputPort.LogLines.RemoveAt(0)

    do
        outputPort.LogLines.CollectionChanged.Add(fun _ ->
            UiThread.Post(fun () ->
                updateLog ()
                trimLogLines ()))

    member _.OutputPort = outputPort :> IOutputPort
    member _.LogOutput = logOutputCache

    member _.ContainerTab = containerTab
    member _.ImagesTab = imagesTab
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

    member _.SetStorageProvider(sp: IStorageProvider) = storageProvider <- sp

    member _.ExportLogCommand: ICommand =
        RelayCommand(Action(fun () -> this.ExportLog() |> ignore))

    /// Écrit l'état actuel du journal (lignes horodatées) dans le fichier donné.
    /// Séparée du sélecteur de fichier pour être testable hors interface.
    member _.ExportJournalTo(path: string) : Task<int> =
        task {
            let lines =
                outputPort.LogLines
                |> Seq.map (fun e -> e.DisplayText)
                |> Seq.toArray

            do! File.WriteAllLinesAsync(path, lines)
            return lines.Length
        }

    member private this.ExportLog() =
        let output = outputPort :> IOutputPort

        Cmd.run outputPort (fun () ->
            task {
                if isNull storageProvider then
                    output.WriteWarning("Fournisseur de stockage non disponible")
                else
                    let! file =
                        storageProvider.SaveFilePickerAsync(
                            FilePickerSaveOptions(
                                Title = "Exporter le journal",
                                SuggestedFileName = "diplo-journal.txt",
                                DefaultExtension = "txt",
                                FileTypeChoices = [ FilePickerFileType("Texte", Patterns = [| "*.txt" |]) ]
                            )
                        )

                    if not (isNull file) then
                        let! count = this.ExportJournalTo file.Path.LocalPath

                        output.WriteSuccess(
                            sprintf "Journal exporté : %d ligne(s) vers %s" count file.Path.LocalPath
                        )
            })

    interface IDisposable with
        member _.Dispose() =
            (containerTab :> IDisposable).Dispose()
            (imagesTab :> IDisposable).Dispose()
            (volumeTab :> IDisposable).Dispose()
            (networkTab :> IDisposable).Dispose()
            (composeTab :> IDisposable).Dispose()
            (settingsTab :> IDisposable).Dispose()
