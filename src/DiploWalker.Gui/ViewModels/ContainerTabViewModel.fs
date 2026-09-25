namespace DiploWalker.Gui.ViewModels

open System
open System.Collections.ObjectModel
open System.Threading
open Avalonia.Threading
open DiploWalker.Core
open DiploWalker.Core.Clients
open DiploWalker.Core.Mounts
open DiploWalker.Core.Output
open DiploWalker.Gui.Services

type ContainerInfo = { Id: string; Nom: string; Image: string; State: string; CreatedAt: string }

type ImageInfo = { Reference: string; Tag: string; Size: string; CreatedAt: string }

type CatalogRow =
    { Ref: string
      Note: string
      AddedAt: string }

type ContainerTabViewModel
    (
        outputPort: IOutputPort,
        ?logsSourceFactory: unit -> IContainerLogsSource,
        ?containerClientFactory: unit -> IContainerClient,
        ?catalogPathProvider: unit -> string
    ) as this =
    inherit ViewModelBase()

    let logsSourceFactory =
        defaultArg logsSourceFactory (fun () -> new GrpcContainerLogsSource() :> IContainerLogsSource)

    // Source de journaux créée UNE seule fois pour la durée de vie du ViewModel :
    // un seul client gRPC partagé entre les clics (fini la recréation à chaque appel).
    let logsSource = lazy (logsSourceFactory ())

    let containerClient =
        let factory = defaultArg containerClientFactory (fun () -> new ContainerClient() :> IContainerClient)
        factory ()

    let catalogPathProvider =
        defaultArg catalogPathProvider (fun () -> ImageCatalog.catalogPath ())

    let containers = ObservableCollection<ContainerInfo>()
    let images = ObservableCollection<ImageInfo>()
    let catalogue = ObservableCollection<CatalogRow>()

    let mutable containerIdInput = ""
    let mutable containerNameInput = ""
    let mutable containerImageInput = ""
    let mutable containerImageUser = ""
    let mutable containerNamespace = ""
    let mutable containerAll = false
    let mutable containerTimeout = 10
    let mutable containerForce = false
    let mutable containerNewName = ""
    let mutable containerImageRef = ""
    let mutable containerImageTarget = ""
    let mutable catalogNote = ""
    let mutable containerFollow = false
    let mutable containerTail = 100
    let mutable containerSince = ""
    let mutable containerExecCommand = ""
    let mutable containerMounts = ""

    let mutable selectedContainer: ContainerInfo = Unchecked.defaultof<ContainerInfo>

    let getSelectedContainer () = Volatile.Read(&selectedContainer)
    let setSelectedContainer v = Interlocked.Exchange(&selectedContainer, v) |> ignore

    let logCtsGate = obj()
    let mutable logCts: CancellationTokenSource = null

    // Annulation d'un suivi : le jeton est signalé puis détaché sous verrou.
    // La disposition du CTS revient EXCLUSIVEMENT au worker qui l'a créé
    // (finally), ce qui élimine tout double Cancel/Dispose concurrent.
    let cancelPreviousLogStream () =
        lock logCtsGate (fun () ->
            let cts = logCts

            if not (isNull cts) then
                cts.Cancel()
                logCts <- null)

    let eventsCtsGate = obj()
    let mutable eventsCts: CancellationTokenSource = null

    let cancelPreviousEventsStream () =
        lock eventsCtsGate (fun () ->
            let cts = eventsCts

            if not (isNull cts) then
                cts.Cancel()
                eventsCts <- null)

    let listContainersCmd =
        RelayCommand(Action(fun () -> this.ListContainers() |> ignore))

    let inspectContainerCmd =
        RelayCommand(Action(fun () -> this.InspectContainer() |> ignore))

    let startContainerCmd =
        RelayCommand(Action(fun () -> this.StartContainer() |> ignore))

    let stopContainerCmd =
        RelayCommand(Action(fun () -> this.StopContainer() |> ignore))

    let deleteContainerCmd =
        RelayCommand(Action(fun () -> this.DeleteContainer() |> ignore))

    let pullImageCmd = RelayCommand(Action(fun () -> this.PullImage() |> ignore))
    let versionCmd = RelayCommand(Action(fun () -> this.GetVersion() |> ignore))

    let renameContainerCmd =
        RelayCommand(Action(fun () -> this.RenameContainer() |> ignore))

    let topContainerCmd = RelayCommand(Action(fun () -> this.TopContainer() |> ignore))

    let statsContainerCmd =
        RelayCommand(Action(fun () -> this.GetContainerStats() |> ignore))

    let listImagesCmd = RelayCommand(Action(fun () -> this.ListImages() |> ignore))
    let inspectImageCmd = RelayCommand(Action(fun () -> this.InspectImage() |> ignore))
    let removeImageCmd = RelayCommand(Action(fun () -> this.RemoveImage() |> ignore))
    let tagImageCmd = RelayCommand(Action(fun () -> this.TagImage() |> ignore))

    let listCatalogCmd = RelayCommand(Action(fun () -> this.ListCatalog() |> ignore))
    let addToCatalogCmd = RelayCommand(Action(fun () -> this.AddToCatalog() |> ignore))
    let updateCatalogCmd = RelayCommand(Action(fun () -> this.UpdateCatalog() |> ignore))
    let removeFromCatalogCmd = RelayCommand(Action(fun () -> this.RemoveFromCatalog() |> ignore))

    let createContainerCmd =
        RelayCommand(Action(fun () -> this.CreateContainer() |> ignore))

    let getContainerLogsCmd =
        RelayCommand(Action(fun () -> this.GetContainerLogs() |> ignore))

    let stopFollowLogsCmd =
        RelayCommand(
            Action(fun () ->
                this.ContainerFollow <- false
                cancelPreviousLogStream ())
        )

    let execInContainerCmd =
        RelayCommand(Action(fun () -> this.ExecInContainer() |> ignore))

    let listNamespacesCmd =
        RelayCommand(Action(fun () -> this.ListNamespaces() |> ignore))

    let pauseContainerCmd =
        RelayCommand(Action(fun () -> this.PauseContainer() |> ignore))

    let unpauseContainerCmd =
        RelayCommand(Action(fun () -> this.UnpauseContainer() |> ignore))

    let waitContainerCmd =
        RelayCommand(Action(fun () -> this.WaitContainer() |> ignore))

    let pruneContainersCmd =
        RelayCommand(Action(fun () -> this.PruneContainers() |> ignore))

    let pruneImagesCmd =
        RelayCommand(Action(fun () -> this.PruneImages() |> ignore))

    let commitImageCmd =
        RelayCommand(Action(fun () -> this.CommitImage() |> ignore))

    let getContainerEventsCmd =
        RelayCommand(Action(fun () -> this.GetContainerEvents() |> ignore))

    let stopFollowEventsCmd =
        RelayCommand(Action(fun () -> cancelPreviousEventsStream ()))

    member _.Containers = containers
    member _.Images = images

    member _.SelectedContainer
        with get () = getSelectedContainer ()
        and set v =
            setSelectedContainer v
            this.OnPropertyChanged()
            this.OnPropertyChanged(nameof this.HasSelection)

    member _.HasSelection = not (isNull (box (getSelectedContainer ())))

    member _.ContainerIdInput
        with get () = containerIdInput
        and set v =
            containerIdInput <- v
            this.OnPropertyChanged()

    member _.ContainerNameInput
        with get () = containerNameInput
        and set v =
            containerNameInput <- v
            this.OnPropertyChanged()

    member _.ContainerImageInput
        with get () = containerImageInput
        and set v =
            containerImageInput <- v
            this.OnPropertyChanged()

    member _.ContainerImageUser
        with get () = containerImageUser
        and set v =
            containerImageUser <- v
            this.OnPropertyChanged()

    member _.ContainerNamespace
        with get () = containerNamespace
        and set v =
            containerNamespace <- v
            this.OnPropertyChanged()

    member _.ContainerAll
        with get () = containerAll
        and set v =
            containerAll <- v
            this.OnPropertyChanged()

    member _.ContainerTimeout
        with get () = containerTimeout
        and set v =
            containerTimeout <- v
            this.OnPropertyChanged()

    member _.ContainerForce
        with get () = containerForce
        and set v =
            containerForce <- v
            this.OnPropertyChanged()

    member _.ContainerNewName
        with get () = containerNewName
        and set v =
            containerNewName <- v
            this.OnPropertyChanged()

    member _.ContainerImageRef
        with get () = containerImageRef
        and set v =
            containerImageRef <- v
            this.OnPropertyChanged()

    member _.ContainerImageTarget
        with get () = containerImageTarget
        and set v =
            containerImageTarget <- v
            this.OnPropertyChanged()

    member _.ContainerFollow
        with get () = containerFollow
        and set v =
            containerFollow <- v
            this.OnPropertyChanged()

    member _.ContainerTail
        with get () = containerTail
        and set v =
            containerTail <- v
            this.OnPropertyChanged()

    member _.ContainerSince
        with get () = containerSince
        and set v =
            containerSince <- v
            this.OnPropertyChanged()

    member _.ContainerExecCommand
        with get () = containerExecCommand
        and set v =
            containerExecCommand <- v
            this.OnPropertyChanged()

    member _.ContainerMounts
        with get () = containerMounts
        and set v =
            containerMounts <- v
            this.OnPropertyChanged()

    member _.ListContainersCommand = listContainersCmd
    member _.InspectContainerCommand = inspectContainerCmd
    member _.StartContainerCommand = startContainerCmd
    member _.StopContainerCommand = stopContainerCmd
    member _.DeleteContainerCommand = deleteContainerCmd
    member _.PullImageCommand = pullImageCmd
    member _.VersionCommand = versionCmd
    member _.RenameContainerCommand = renameContainerCmd
    member _.TopContainerCommand = topContainerCmd
    member _.StatsContainerCommand = statsContainerCmd
    member _.ListImagesCommand = listImagesCmd
    member _.InspectImageCommand = inspectImageCmd
    member _.RemoveImageCommand = removeImageCmd
    member _.TagImageCommand = tagImageCmd
    member _.ListCatalogCommand = listCatalogCmd
    member _.AddToCatalogCommand = addToCatalogCmd
    member _.UpdateCatalogCommand = updateCatalogCmd
    member _.RemoveFromCatalogCommand = removeFromCatalogCmd

    member _.Catalogue = catalogue

    member _.CatalogNote
        with get () = catalogNote
        and set v =
            catalogNote <- v
            this.OnPropertyChanged()

    member _.CreateContainerCommand = createContainerCmd
    member _.GetContainerLogsCommand = getContainerLogsCmd
    member _.StopFollowLogsCommand = stopFollowLogsCmd
    member _.ExecInContainerCommand = execInContainerCmd
    member _.ListNamespacesCommand = listNamespacesCmd
    member _.PauseContainerCommand = pauseContainerCmd
    member _.UnpauseContainerCommand = unpauseContainerCmd
    member _.WaitContainerCommand = waitContainerCmd
    member _.PruneContainersCommand = pruneContainersCmd
    member _.PruneImagesCommand = pruneImagesCmd
    member _.CommitImageCommand = commitImageCmd
    member _.GetContainerEventsCommand = getContainerEventsCmd
    member _.StopFollowEventsCommand = stopFollowEventsCmd

    member private this.ListContainers() =
        Cmd.run outputPort (fun () ->
            task {
                let! response = containerClient.ListAsync(all = this.ContainerAll)

                UiThread.Post(fun () ->
                    containers.Clear()

                    for c in response.Containers do
                        containers.Add(
                            { Id = c.Id
                              Nom = c.Name
                              Image = c.Image
                              State = c.State.ToString()
                              CreatedAt = c.CreatedAt }
                        ))

                outputPort.WriteSuccess(sprintf "%d conteneur(s) trouvé(s)" response.Containers.Count)
            })

    member private this.InspectContainer() =
        Cmd.run outputPort (fun () ->
            task {
                let! response = containerClient.InspectAsync(id = this.ContainerIdInput)
                outputPort.WriteLine(sprintf "ID: %s" response.Id)
                outputPort.WriteLine(sprintf "Nom: %s" response.Name)
                outputPort.WriteLine(sprintf "Image: %s" response.Image)
                outputPort.WriteLine(sprintf "État: %s" (response.State.ToString()))
                outputPort.WriteLine(sprintf "Créé le: %s" response.CreatedAt)

                if not (String.IsNullOrEmpty(response.StartedAt)) then
                    outputPort.WriteLine(sprintf "Démarré le: %s" response.StartedAt)

                if not (String.IsNullOrEmpty(response.FinishedAt)) then
                    outputPort.WriteLine(sprintf "Arrêté le: %s" response.FinishedAt)

                if response.Labels.Count > 0 then
                    outputPort.WriteLine("Labels:")

                    for kvp in response.Labels do
                        outputPort.WriteLine(sprintf "  %s = %s" kvp.Key kvp.Value)
            })

    /// Résout l'identifiant cible des actions : champ de saisie s'il est
    /// rempli, sinon le conteneur sélectionné dans la grille. Sans ce repli,
    /// les boutons du panneau détail agissaient sur un id vide ou obsolète.
    member private this.ResolveTargetId() : string =
        if not (String.IsNullOrWhiteSpace(this.ContainerIdInput)) then
            this.ContainerIdInput
        else
            let sel = getSelectedContainer ()

            if isNull (box sel) then
                ""

            elif String.IsNullOrWhiteSpace(sel.Id) then
                ""
            else
                sel.Id

    member private this.StartContainer() =
        Cmd.run outputPort (fun () ->
            task {
                let target = this.ResolveTargetId()

                if String.IsNullOrEmpty target then
                    outputPort.WriteWarning("Aucun identifiant de conteneur (saisie ou sélection)")
                else
                    let! response = containerClient.StartAsync(id = target)
                    outputPort.WriteSuccess(sprintf "Conteneur %s démarré - %s" target response.Message)
            })

    member private this.StopContainer() =
        Cmd.run outputPort (fun () ->
            task {
                let target = this.ResolveTargetId()

                if String.IsNullOrEmpty target then
                    outputPort.WriteWarning("Aucun identifiant de conteneur (saisie ou sélection)")
                else
                    let! response = containerClient.StopAsync(id = target, timeoutSeconds = this.ContainerTimeout)

                    outputPort.WriteSuccess(sprintf "Conteneur %s arrêté - %s" target response.Message)
            })

    member private this.DeleteContainer() =
        Cmd.run outputPort (fun () ->
            task {
                let target = this.ResolveTargetId()

                if String.IsNullOrEmpty target then
                    outputPort.WriteWarning("Aucun identifiant de conteneur (saisie ou sélection)")
                else
                    let! response = containerClient.DeleteAsync(id = target, force = this.ContainerForce)

                    if response.Success then
                        outputPort.WriteSuccess(sprintf "Conteneur %s supprimé" target)
                    else
                        outputPort.WriteWarning(response.Message)
            })

    member private this.PullImage() =
        Cmd.run outputPort (fun () ->
            task {
                let! response =
                    containerClient.PullImageAsync(
                        image = this.ContainerImageInput,
                        ?user =
                            (if String.IsNullOrEmpty(this.ContainerImageUser) then
                                 None
                             else
                                 Some this.ContainerImageUser)
                    )

                outputPort.WriteSuccess(sprintf "Image %s téléchargée - %s" this.ContainerImageInput response.Message)
            })

    member private this.GetVersion() =
        Cmd.run outputPort (fun () ->
            task {
                let! response = containerClient.GetVersionAsync()
                outputPort.WriteLine(sprintf "Version: %s" response.Version)
                outputPort.WriteLine(sprintf "Révision: %s" response.Revision)
                outputPort.WriteLine(sprintf "Go: %s" response.GoVersion)
                outputPort.WriteLine(sprintf "OS/Arch: %s/%s" response.Os response.Arch)
            })

    member private this.RenameContainer() =
        Cmd.run outputPort (fun () ->
            task {
                let target = this.ResolveTargetId()

                if String.IsNullOrEmpty target then
                    outputPort.WriteWarning("Aucun identifiant de conteneur (saisie ou sélection)")
                else
                    let! response =
                        containerClient.RenameContainerAsync(id = target, newName = this.ContainerNewName)

                    outputPort.WriteSuccess(sprintf "Conteneur %s renommé en %s" target this.ContainerNewName)
            })

    member private this.TopContainer() =
        Cmd.run outputPort (fun () ->
            task {
                let target = this.ResolveTargetId()

                if String.IsNullOrEmpty target then
                    outputPort.WriteWarning("Aucun identifiant de conteneur (saisie ou sélection)")
                else
                    let! response = containerClient.TopContainerAsync(id = target)
                    outputPort.WriteLine(sprintf "Processus du conteneur %s:" target)

                    for proc in response.Processes do
                        outputPort.WriteLine(sprintf "  PID: %d  CMD: %s" proc.Pid proc.Command)
            })

    member private this.GetContainerStats() =
        Cmd.run outputPort (fun () ->
            task {
                let target = this.ResolveTargetId()

                if String.IsNullOrEmpty target then
                    outputPort.WriteWarning("Aucun identifiant de conteneur (saisie ou sélection)")
                else
                    let! response = containerClient.GetContainerStatsAsync(id = target)
                    outputPort.WriteLine(sprintf "Métriques du conteneur %s:" target)

                    outputPort.WriteLine(
                        sprintf "  CPU: %.2f  Mémoire: %d" response.CpuUsage response.MemoryUsage
                    )

                    outputPort.WriteLine(sprintf "  Réseau RX: %d  TX: %d" response.NetworkRx response.NetworkTx)
            })

    member private this.ListImages() =
        Cmd.run outputPort (fun () ->
            task {
                let ns =
                    if String.IsNullOrEmpty(this.ContainerNamespace) then
                        None
                    else
                        Some this.ContainerNamespace

                let! response = containerClient.ListImagesAsync(?namespaceName = ns)

                UiThread.Post(fun () ->
                    images.Clear()

                    for img in response.Images do
                        images.Add({ Reference = img.Ref; Tag = img.Tag; Size = sprintf "%d octets" img.Size; CreatedAt = img.CreatedAt }))

                outputPort.WriteSuccess(sprintf "%d image(s) trouvée(s)" response.Images.Count)
            })

    member private this.InspectImage() =
        Cmd.run outputPort (fun () ->
            task {
                let ns =
                    if String.IsNullOrEmpty(this.ContainerNamespace) then
                        None
                    else
                        Some this.ContainerNamespace

                let! response = containerClient.InspectImageAsync(ref = this.ContainerImageRef, ?namespaceName = ns)
                outputPort.WriteLine(sprintf "Référentiel: %s" response.Ref)
                outputPort.WriteLine(sprintf "Tag: %s" response.Tag)
                outputPort.WriteLine(sprintf "Taille: %d octets" response.Size)
                outputPort.WriteLine(sprintf "Créé le: %s" response.CreatedAt)

                if response.Labels.Count > 0 then
                    outputPort.WriteLine("Labels:")

                    for kvp in response.Labels do
                        outputPort.WriteLine(sprintf "  %s = %s" kvp.Key kvp.Value)
            })

    member private this.RemoveImage() =
        Cmd.run outputPort (fun () ->
            task {
                let ns =
                    if String.IsNullOrEmpty(this.ContainerNamespace) then
                        None
                    else
                        Some this.ContainerNamespace

                let! response = containerClient.RemoveImageAsync(ref = this.ContainerImageRef, ?namespaceName = ns)

                if response.Success then
                    outputPort.WriteSuccess(sprintf "Image %s supprimée" this.ContainerImageRef)
                else
                    outputPort.WriteWarning(response.Message)
            })

    member private this.TagImage() =
        Cmd.run outputPort (fun () ->
            task {
                let ns =
                    if String.IsNullOrEmpty(this.ContainerNamespace) then
                        None
                    else
                        Some this.ContainerNamespace

                let! response =
                    containerClient.TagImageAsync(
                        source = this.ContainerImageRef,
                        target = this.ContainerImageTarget,
                        ?namespaceName = ns
                    )

                outputPort.WriteSuccess(
                    sprintf
                        "Image %s étiquetée en %s - %s"
                        this.ContainerImageRef
                        this.ContainerImageTarget
                        response.Message
                )
            })

    member private this.RefreshCatalogue(?entries: ImageCatalog.CatalogEntry list) =
        let loaded =
            match entries with
            | Some e -> e
            | None -> ImageCatalog.load (catalogPathProvider ())

        UiThread.Post(fun () ->
            catalogue.Clear()

            for e in loaded do
                catalogue.Add(
                    { Ref = e.Ref
                      Note = defaultArg e.Note ""
                      AddedAt = e.AddedAt }
                ))

    member private this.ListCatalog() =
        Cmd.run outputPort (fun () ->
            task {
                let entries = ImageCatalog.load (catalogPathProvider ())
                this.RefreshCatalogue(entries = entries)
                outputPort.WriteSuccess(sprintf "%d image(s) au catalogue" (List.length entries))
            })

    member private this.AddToCatalog() =
        Cmd.run outputPort (fun () ->
            task {
                if String.IsNullOrWhiteSpace(this.ContainerImageRef) then
                    outputPort.WriteWarning("Une référence d'image est requise (champ Réf. image)")
                else
                    let! response = containerClient.PullImageAsync(image = this.ContainerImageRef)
                    outputPort.WriteSuccess(sprintf "Image %s téléchargée - %s" this.ContainerImageRef response.Message)

                    let note =
                        if String.IsNullOrWhiteSpace(this.CatalogNote) then
                            None
                        else
                            Some this.CatalogNote

                    let path = catalogPathProvider ()

                    if ImageCatalog.add path this.ContainerImageRef note then
                        outputPort.WriteSuccess(sprintf "Image %s ajoutée au catalogue" this.ContainerImageRef)
                    else
                        outputPort.WriteWarning(sprintf "L'image %s est déjà au catalogue" this.ContainerImageRef)

                    this.RefreshCatalogue()
            })

    member private this.UpdateCatalog() =
        Cmd.run outputPort (fun () ->
            task {
                if String.IsNullOrWhiteSpace(this.ContainerImageRef) then
                    outputPort.WriteWarning("Une référence d'image est requise (champ Réf. image)")
                else
                    let hasTarget = not (String.IsNullOrWhiteSpace(this.ContainerImageTarget))

                    if hasTarget then
                        let! response =
                            containerClient.TagImageAsync(
                                source = this.ContainerImageRef,
                                target = this.ContainerImageTarget
                            )

                        outputPort.WriteSuccess(
                            sprintf
                                "Image %s étiquetée en %s - %s"
                                this.ContainerImageRef
                                this.ContainerImageTarget
                                response.Message
                        )

                    let newRef =
                        if hasTarget then
                            Some this.ContainerImageTarget
                        else
                            None

                    let note =
                        if String.IsNullOrWhiteSpace(this.CatalogNote) then
                            None
                        else
                            Some this.CatalogNote

                    let path = catalogPathProvider ()

                    if ImageCatalog.update path this.ContainerImageRef newRef note then
                        outputPort.WriteSuccess(sprintf "Entrée %s mise à jour dans le catalogue" this.ContainerImageRef)
                    else
                        outputPort.WriteWarning(sprintf "L'image %s n'est pas au catalogue" this.ContainerImageRef)

                    this.RefreshCatalogue()
            })

    member private this.RemoveFromCatalog() =
        Cmd.run outputPort (fun () ->
            task {
                if String.IsNullOrWhiteSpace(this.ContainerImageRef) then
                    outputPort.WriteWarning("Une référence d'image est requise (champ Réf. image)")
                else
                    let path = catalogPathProvider ()

                    if not (ImageCatalog.load path |> List.exists (fun e -> e.Ref = this.ContainerImageRef)) then
                        outputPort.WriteWarning(sprintf "L'image %s n'est pas au catalogue" this.ContainerImageRef)
                    else
                        let! response = containerClient.RemoveImageAsync(ref = this.ContainerImageRef)

                        if response.Success then
                            ImageCatalog.remove path this.ContainerImageRef |> ignore
                            outputPort.WriteSuccess(
                                sprintf "Image %s supprimée et retirée du catalogue" this.ContainerImageRef
                            )
                        else
                            outputPort.WriteWarning(response.Message)

                        this.RefreshCatalogue()
            })

    member private this.CreateContainer() =
        Cmd.run outputPort (fun () ->
            task {
                let mounts = MountParser.parse this.ContainerMounts

                let! response =
                    containerClient.CreateAsync(
                        name = this.ContainerNameInput,
                        image = this.ContainerImageInput,
                        ?mounts = (if mounts.IsEmpty then None else Some mounts)
                    )

                outputPort.WriteSuccess(sprintf "Conteneur créé : %s (ID: %s)" response.Name response.Id)
            })

    member private this.GetContainerLogs() =
        Cmd.run outputPort (fun () ->
            task {
                let source = logsSource.Value

                if this.ContainerFollow then
                    cancelPreviousLogStream ()
                    let cts = new CancellationTokenSource()

                    lock logCtsGate (fun () -> logCts <- cts)

                    try
                        let stream =
                            source.GetStream(
                                this.ContainerIdInput,
                                true,
                                this.ContainerTail,
                                this.ContainerSince,
                                cts.Token
                            )

                        let enumerator = stream.GetAsyncEnumerator(cts.Token)

                        try
                            let mutable moving = true
                            let mutable cancelled = false

                            while moving do
                                try
                                    let! hasNext = enumerator.MoveNextAsync().AsTask()

                                    if hasNext then
                                        outputPort.WriteLine(
                                            sprintf "[%s] %s" enumerator.Current.Timestamp enumerator.Current.Log
                                        )
                                    else
                                        moving <- false
                                with :? OperationCanceledException ->
                                    // Arrêt volontaire du suivi (bouton ⏹) :
                                    // ce n'est PAS une erreur à afficher.
                                    cancelled <- true
                                    moving <- false

                            if not cancelled then
                                outputPort.WriteSuccess("Suivi des journaux terminé")
                        finally
                            try
                                // M6 : pas de .Wait() bloquant — la disposition
                                // est démarrée et se termine en arrière-plan.
                                // Task.Run observe l'échec éventuel : aucune
                                // UnobservedTaskException (M7).
                                System.Threading.Tasks.Task.Run(
                                    System.Func<System.Threading.Tasks.Task>(fun () ->
                                        task {
                                            try
                                                do! enumerator.DisposeAsync().AsTask()
                                            with _ ->
                                                ()
                                        })
                                )
                                |> ignore
                            with _ ->
                                ()
                    finally
                        // Le détachement du champ et la disposition se font sous
                        // le même verrou que l'annulation : pas de double Dispose.
                        lock logCtsGate (fun () ->
                            if logCts = cts then
                                logCts <- null

                            cts.Dispose())
                else
                    let! entries =
                        source.GetSnapshot(
                            this.ContainerIdInput,
                            this.ContainerTail,
                            this.ContainerSince,
                            CancellationToken.None
                        )

                    let sb = Text.StringBuilder()

                    for entry in entries do
                        sb.AppendLine(sprintf "[%s] %s" entry.Timestamp entry.Log) |> ignore

                    outputPort.WriteSuccess(sb.ToString())
            })

    member private this.ExecInContainer() =
        Cmd.run outputPort (fun () ->
            task {
                let parts = DiploWalker.Core.CommandLine.split this.ContainerExecCommand |> Array.ofList
                let! entries = containerClient.Exec(id = this.ContainerIdInput, command = parts)
                let sb = Text.StringBuilder()

                for entry in entries do
                    sb.Append(Text.Encoding.UTF8.GetString(entry.Data)) |> ignore

                outputPort.WriteSuccess(sb.ToString())
            })

    member private this.ListNamespaces() =
        Cmd.run outputPort (fun () ->
            task {
                let! response = containerClient.ListNamespacesAsync()
                let nsList = String.Join(", ", response.Namespaces)
                outputPort.WriteSuccess(sprintf "Namespaces: %s" nsList)
            })

    member private this.PauseContainer() =
        Cmd.run outputPort (fun () ->
            task {
                let target = this.ResolveTargetId()

                if String.IsNullOrEmpty target then
                    outputPort.WriteWarning("Aucun identifiant de conteneur (saisie ou sélection)")
                else
                    let! response = containerClient.PauseAsync(id = target)
                    outputPort.WriteSuccess(sprintf "Conteneur %s suspendu - %s" target response.Message)
            })

    member private this.UnpauseContainer() =
        Cmd.run outputPort (fun () ->
            task {
                let target = this.ResolveTargetId()

                if String.IsNullOrEmpty target then
                    outputPort.WriteWarning("Aucun identifiant de conteneur (saisie ou sélection)")
                else
                    let! response = containerClient.UnpauseAsync(id = target)
                    outputPort.WriteSuccess(sprintf "Conteneur %s repris - %s" target response.Message)
            })

    member private this.WaitContainer() =
        Cmd.run outputPort (fun () ->
            task {
                let target = this.ResolveTargetId()

                if String.IsNullOrEmpty target then
                    outputPort.WriteWarning("Aucun identifiant de conteneur (saisie ou sélection)")
                else
                    let! response =
                        containerClient.WaitAsync(id = target, timeoutSeconds = this.ContainerTimeout)

                    outputPort.WriteSuccess(
                        sprintf "Conteneur %s terminé (code: %d) - %s" target response.ExitCode response.Message
                    )
            })

    member private this.PruneContainers() =
        Cmd.run outputPort (fun () ->
            task {
                let! response = containerClient.PruneContainersAsync()

                if response.Deleted.Count = 0 then
                    outputPort.WriteSuccess("Aucun conteneur arrêté à supprimer")
                else
                    outputPort.WriteSuccess(
                        sprintf "%d conteneur(s) arrêté(s) supprimé(s): %s"
                            response.Deleted.Count
                            (String.Join(", ", response.Deleted))
                    )
            })

    member private this.PruneImages() =
        Cmd.run outputPort (fun () ->
            task {
                let! response = containerClient.PruneImagesAsync()

                if response.Deleted.Count = 0 then
                    outputPort.WriteSuccess("Aucune image inutilisée à supprimer")
                else
                    outputPort.WriteSuccess(
                        sprintf "%d image(s) inutilisée(s) supprimée(s): %s"
                            response.Deleted.Count
                            (String.Join(", ", response.Deleted))
                    )
            })

    member private this.CommitImage() =
        Cmd.run outputPort (fun () ->
            task {
                let target = this.ResolveTargetId()

                if String.IsNullOrEmpty target then
                    outputPort.WriteWarning("Aucun identifiant de conteneur (saisie ou sélection)")
                elif String.IsNullOrWhiteSpace(this.ContainerImageTarget) then
                    outputPort.WriteWarning("Une référence d'image est requise (champ Réf. image cible)")
                else
                    let! response =
                        containerClient.CommitImageAsync(containerId = target, imageRef = this.ContainerImageTarget)

                    outputPort.WriteSuccess(
                        sprintf "Image %s créée depuis %s - %s"
                            this.ContainerImageTarget
                            target
                            response.Message
                    )
            })

    member private this.GetContainerEvents() =
        Cmd.run outputPort (fun () ->
            task {
                cancelPreviousEventsStream ()
                let cts = new CancellationTokenSource()

                lock eventsCtsGate (fun () -> eventsCts <- cts)

                try
                    let stream = containerClient.WatchEventsStream(cts.Token)
                    let enumerator = stream.GetAsyncEnumerator(cts.Token)

                    try
                        let mutable moving = true
                        let mutable cancelled = false

                        while moving do
                            try
                                let! hasNext = enumerator.MoveNextAsync().AsTask()

                                if hasNext then
                                    let e = enumerator.Current

                                    outputPort.WriteLine(
                                        sprintf
                                            "[%s] %s %s (%s)"
                                            e.Timestamp
                                            e.EventType
                                            e.Id
                                            e.Status
                                    )
                                else
                                    moving <- false
                            with :? OperationCanceledException ->
                                // Arrêt volontaire du suivi (bouton ⏹) :
                                // ce n'est PAS une erreur à afficher.
                                cancelled <- true
                                moving <- false

                        if not cancelled then
                            outputPort.WriteSuccess("Suivi des événements terminé")
                    finally
                        try
                            // M6 : pas de .Wait() bloquant — la disposition
                            // est démarrée et se termine en arrière-plan.
                            // Task.Run observe l'échec éventuel : aucune
                            // UnobservedTaskException (M7).
                            System.Threading.Tasks.Task.Run(
                                System.Func<System.Threading.Tasks.Task>(fun () ->
                                    (task {
                                        try
                                            do! enumerator.DisposeAsync().AsTask()
                                        with _ ->
                                            ()
                                    } :> System.Threading.Tasks.Task))
                            )
                            |> ignore
                        with _ ->
                            ()
                finally
                    // Le détachement du champ et la disposition se font sous
                    // le même verrou que l'annulation : pas de double Dispose.
                    lock eventsCtsGate (fun () ->
                        if eventsCts = cts then
                            eventsCts <- null

                        cts.Dispose())
            })

    interface IDisposable with
        member _.Dispose() =
            // Annulation uniquement : chaque CTS est disposé une seule fois par
            // le worker qui l'a créé (finally), annulation ou fin de flux.
            cancelPreviousLogStream ()
            cancelPreviousEventsStream ()

            if logsSource.IsValueCreated then
                (logsSource.Value :> IDisposable).Dispose()

            (containerClient :> IDisposable).Dispose()


