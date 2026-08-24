namespace Diplo.Gui.ViewModels

open System
open System.Collections.ObjectModel
open Avalonia.Platform.Storage
open Avalonia.Threading
open Diplo.Core.Clients
open Diplo.Core.Compose
open Diplo.Core.Output

type ComposeServiceInfo =
    { Service: string
      Conteneur: string
      Image: string
      État: string
      Projet: string }

type ComposeTabViewModel(outputPort: IOutputPort) as this =
    inherit ViewModelBase()

    let composeServices = ObservableCollection<ComposeServiceInfo>()
    let composeClient = new ContainerClient()
    let composeEditor = ComposeEditorViewModel()

    let mutable composeFilePath = ""
    let mutable composeServiceName = ""
    let mutable inspectImageRef = ""
    let mutable storageProvider: IStorageProvider = null

    let setSelectedService (imageRef: string) =
        inspectImageRef <- imageRef
        this.OnPropertyChanged("InspectImageRef")

    let composeUpCmd = RelayCommand(Action(fun () -> this.ComposeUp() |> ignore))
    let composeDownCmd = RelayCommand(Action(fun () -> this.ComposeDown() |> ignore))
    let composePsCmd = RelayCommand(Action(fun () -> this.ComposePs() |> ignore))
    let composeLogsCmd = RelayCommand(Action(fun () -> this.ComposeLogs() |> ignore))
    let composePullCmd = RelayCommand(Action(fun () -> this.ComposePull() |> ignore))
    let composeBuildCmd = RelayCommand(Action(fun () -> this.ComposeBuild() |> ignore))
    let inspectImageCmd = RelayCommand(Action(fun () -> this.InspectImage() |> ignore))

    let openComposeFileCmd =
        RelayCommand(Action(fun () -> this.OpenComposeFile() |> ignore))

    let saveComposeFileCmd =
        RelayCommand(Action(fun () -> this.SaveComposeFile() |> ignore))

    let validateComposeFileCmd =
        RelayCommand(Action(fun () -> this.ValidateComposeFile() |> ignore))

    member _.ComposeServices = composeServices
    member _.ComposeEditor = composeEditor

    member _.SetStorageProvider(sp: IStorageProvider) = storageProvider <- sp

    member _.ComposeFilePath
        with get () = composeFilePath
        and set v =
            composeFilePath <- v
            this.OnPropertyChanged()

            if not (String.IsNullOrEmpty(v)) && IO.File.Exists(v) then
                composeEditor.LoadFile(v)

    member _.ComposeServiceName
        with get () = composeServiceName
        and set v =
            composeServiceName <- v
            this.OnPropertyChanged()

    member _.InspectImageRef
        with get () = inspectImageRef
        and set v =
            inspectImageRef <- v
            this.OnPropertyChanged()

    member _.OnSelectedServiceChanged(imageRef: string) = setSelectedService imageRef

    member _.ComposeUpCommand = composeUpCmd
    member _.ComposeDownCommand = composeDownCmd
    member _.ComposePsCommand = composePsCmd
    member _.ComposeLogsCommand = composeLogsCmd
    member _.ComposePullCommand = composePullCmd
    member _.ComposeBuildCommand = composeBuildCmd
    member _.InspectImageCommand = inspectImageCmd
    member _.OpenComposeFileCommand = openComposeFileCmd
    member _.SaveComposeFileCommand = saveComposeFileCmd
    member _.ValidateComposeFileCommand = validateComposeFileCmd

    member private this.InspectImage() =
        Cmd.run outputPort (fun () ->
            task {
                let! response = composeClient.InspectImageAsync(ref = this.InspectImageRef)
                outputPort.WriteLine(sprintf "Référentiel: %s" response.Ref)
                outputPort.WriteLine(sprintf "Tag: %s" response.Tag)
                outputPort.WriteLine(sprintf "Taille: %d octets" response.Size)
                outputPort.WriteLine(sprintf "Créé le: %s" response.CreatedAt)

                if response.Labels.Count > 0 then
                    outputPort.WriteLine("Labels:")

                    for kvp in response.Labels do
                        outputPort.WriteLine(sprintf "  %s = %s" kvp.Key kvp.Value)
            })

    member private this.OpenComposeFile() =
        Cmd.runSync outputPort (fun () ->
            if isNull storageProvider then
                outputPort.WriteWarning("Fournisseur de stockage non disponible")
            else
                task {
                    let! files =
                        storageProvider.OpenFilePickerAsync(
                            FilePickerOpenOptions(
                                Title = "Ouvrir un fichier Compose",
                                AllowMultiple = false,
                                FileTypeFilter =
                                    [ FilePickerFileType("Fichiers Compose", Patterns = [| "*.yml"; "*.yaml" |])
                                      FilePickerFileType("Tous les fichiers", Patterns = [| "*.*" |]) ]
                            )
                        )

                    if files.Count > 0 then
                        let path = files.[0].Path.LocalPath
                        this.ComposeFilePath <- path
                        composeEditor.LoadFile(path)
                        outputPort.WriteSuccess(sprintf "Fichier ouvert: %s" path)
                }
                |> ignore)

    member private this.SaveComposeFile() =
        Cmd.runSync outputPort (fun () ->
            if String.IsNullOrEmpty(composeEditor.FilePath) then
                this.OpenComposeFile()
            else
                composeEditor.Save()
                outputPort.WriteSuccess(sprintf "Fichier enregistré: %s" composeEditor.FilePath))

    member private this.ValidateComposeFile() =
        Cmd.runSync outputPort (fun () ->
            composeEditor.Validate()

            if composeEditor.Errors.Count = 0 then
                outputPort.WriteSuccess("Aucune erreur détectée")
            else
                for err in composeEditor.Errors do
                    let prefix =
                        if err.Sévérité = "erreur" then
                            "ERREUR"
                        else
                            "AVERTISSEMENT"

                    outputPort.WriteError(sprintf "[%s] Ligne %d : %s" prefix err.Ligne err.Message))

    member private this.ComposeUp() =
        Cmd.run outputPort (fun () ->
            task {
                use orchestrator = new ComposeOrchestrator(outputPort)
                do! orchestrator.Up(this.ComposeFilePath)
            })

    member private this.ComposeDown() =
        Cmd.run outputPort (fun () ->
            task {
                use orchestrator = new ComposeOrchestrator(outputPort)
                do! orchestrator.Down(this.ComposeFilePath)
            })

    member private this.ComposePs() =
        Cmd.run outputPort (fun () ->
            task {
                use orchestrator = new ComposeOrchestrator(outputPort)
                let compose = orchestrator.ParseFile(this.ComposeFilePath)
                let! response = composeClient.ListAsync(all = true)

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

                            composeServices.Add(
                                { Service = service
                                  Conteneur = c.Name
                                  Image = c.Image
                                  État = c.State.ToString()
                                  Projet = compose.ProjectName }
                            ))

                outputPort.WriteSuccess(sprintf "%d conteneur(s) compose trouvé(s)" composeServices.Count)
            })

    member private this.ComposeLogs() =
        Cmd.run outputPort (fun () ->
            task {
                use orchestrator = new ComposeOrchestrator(outputPort)

                let service =
                    if String.IsNullOrEmpty(this.ComposeServiceName) then
                        None
                    else
                        Some this.ComposeServiceName

                do! orchestrator.Logs(this.ComposeFilePath, service)
            })

    member private this.ComposeBuild() =
        Cmd.run outputPort (fun () ->
            task {
                use orchestrator = new ComposeOrchestrator(outputPort)
                do! orchestrator.Build(this.ComposeFilePath)
            })

    member private this.ComposePull() =
        Cmd.run outputPort (fun () ->
            task {
                use orchestrator = new ComposeOrchestrator(outputPort)
                do! orchestrator.Pull(this.ComposeFilePath)
            })
