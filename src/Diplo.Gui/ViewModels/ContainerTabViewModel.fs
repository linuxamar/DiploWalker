namespace Diplo.Gui.ViewModels

open System
open System.Collections.ObjectModel
open System.Threading
open Avalonia.Threading
open Diplo.Core.Clients
open Diplo.Core.Mounts
open Diplo.Core.Output
open Diplo.Gui.Services

type ContainerInfo = {
    Id: string
    Nom: string
    Image: string
    État: string
    CrééLe: string
}

type ImageInfo = {
    Référentiel: string
    Tag: string
    Taille: string
    CrééLe: string
}

type ContainerTabViewModel(outputPort: IOutputPort, ?logsSourceFactory: unit -> IContainerLogsSource) as this =
    inherit ViewModelBase()

    let logsSourceFactory = defaultArg logsSourceFactory (fun () -> new GrpcContainerLogsSource() :> IContainerLogsSource)

    let containers = ObservableCollection<ContainerInfo>()
    let images = ObservableCollection<ImageInfo>()

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
    let mutable containerFollow = false
    let mutable containerTail = 100
    let mutable containerSince = ""
    let mutable containerExecCommand = ""
    let mutable containerMounts = ""
    let mutable registryInput = ""
    let mutable registryUsernameInput = ""
    let mutable registryPasswordInput = ""

    let mutable logCts: CancellationTokenSource = null

    let cancelPreviousLogStream () =
        if logCts <> null then
            logCts.Cancel()
            logCts.Dispose()
            logCts <- null

    member _.Containers = containers
    member _.Images = images

    member _.ContainerIdInput with get () = containerIdInput and set v = containerIdInput <- v; this.OnPropertyChanged()
    member _.ContainerNameInput with get () = containerNameInput and set v = containerNameInput <- v; this.OnPropertyChanged()
    member _.ContainerImageInput with get () = containerImageInput and set v = containerImageInput <- v; this.OnPropertyChanged()
    member _.ContainerImageUser with get () = containerImageUser and set v = containerImageUser <- v; this.OnPropertyChanged()
    member _.ContainerNamespace with get () = containerNamespace and set v = containerNamespace <- v; this.OnPropertyChanged()
    member _.ContainerAll with get () = containerAll and set v = containerAll <- v; this.OnPropertyChanged()
    member _.ContainerTimeout with get () = containerTimeout and set v = containerTimeout <- v; this.OnPropertyChanged()
    member _.ContainerForce with get () = containerForce and set v = containerForce <- v; this.OnPropertyChanged()
    member _.ContainerNewName with get () = containerNewName and set v = containerNewName <- v; this.OnPropertyChanged()
    member _.ContainerImageRef with get () = containerImageRef and set v = containerImageRef <- v; this.OnPropertyChanged()
    member _.ContainerImageTarget with get () = containerImageTarget and set v = containerImageTarget <- v; this.OnPropertyChanged()
    member _.ContainerFollow with get () = containerFollow and set v = containerFollow <- v; this.OnPropertyChanged()
    member _.ContainerTail with get () = containerTail and set v = containerTail <- v; this.OnPropertyChanged()
    member _.ContainerSince with get () = containerSince and set v = containerSince <- v; this.OnPropertyChanged()
    member _.ContainerExecCommand with get () = containerExecCommand and set v = containerExecCommand <- v; this.OnPropertyChanged()
    member _.ContainerMounts with get () = containerMounts and set v = containerMounts <- v; this.OnPropertyChanged()
    member _.RegistryInput with get () = registryInput and set v = registryInput <- v; this.OnPropertyChanged()
    member _.RegistryUsernameInput with get () = registryUsernameInput and set v = registryUsernameInput <- v; this.OnPropertyChanged()
    member _.RegistryPasswordInput with get () = registryPasswordInput and set v = registryPasswordInput <- v; this.OnPropertyChanged()

    member _.ListContainersCommand = RelayCommand(Action(fun () -> this.ListContainers() |> ignore))
    member _.InspectContainerCommand = RelayCommand(Action(fun () -> this.InspectContainer() |> ignore))
    member _.StartContainerCommand = RelayCommand(Action(fun () -> this.StartContainer() |> ignore))
    member _.StopContainerCommand = RelayCommand(Action(fun () -> this.StopContainer() |> ignore))
    member _.DeleteContainerCommand = RelayCommand(Action(fun () -> this.DeleteContainer() |> ignore))
    member _.PullImageCommand = RelayCommand(Action(fun () -> this.PullImage() |> ignore))
    member _.VersionCommand = RelayCommand(Action(fun () -> this.GetVersion() |> ignore))
    member _.RenameContainerCommand = RelayCommand(Action(fun () -> this.RenameContainer() |> ignore))
    member _.TopContainerCommand = RelayCommand(Action(fun () -> this.TopContainer() |> ignore))
    member _.StatsContainerCommand = RelayCommand(Action(fun () -> this.GetContainerStats() |> ignore))
    member _.ListImagesCommand = RelayCommand(Action(fun () -> this.ListImages() |> ignore))
    member _.InspectImageCommand = RelayCommand(Action(fun () -> this.InspectImage() |> ignore))
    member _.RemoveImageCommand = RelayCommand(Action(fun () -> this.RemoveImage() |> ignore))
    member _.TagImageCommand = RelayCommand(Action(fun () -> this.TagImage() |> ignore))
    member _.CreateContainerCommand = RelayCommand(Action(fun () -> this.CreateContainer() |> ignore))
    member _.GetContainerLogsCommand = RelayCommand(Action(fun () -> this.GetContainerLogs() |> ignore))
    member _.StopFollowLogsCommand = RelayCommand(Action(fun () ->
        this.ContainerFollow <- false
        cancelPreviousLogStream ()))
    member _.ExecInContainerCommand = RelayCommand(Action(fun () -> this.ExecInContainer() |> ignore))
    member _.ListNamespacesCommand = RelayCommand(Action(fun () -> this.ListNamespaces() |> ignore))
    member _.RegistryLoginCommand = RelayCommand(Action(fun () -> this.RegistryLogin() |> ignore))
    member _.RegistryLogoutCommand = RelayCommand(Action(fun () -> this.RegistryLogout() |> ignore))

    member private this.ListContainers() =
        Cmd.run outputPort (fun () ->
            task {
                use client = new ContainerClient()
                let! response = client.ListAsync(all = this.ContainerAll)
                Dispatcher.UIThread.Post(fun () ->
                    containers.Clear()
                    for c in response.Containers do
                        containers.Add({
                            Id = c.Id
                            Nom = c.Name
                            Image = c.Image
                            État = c.State.ToString()
                            CrééLe = c.CreatedAt
                        })
                )
                outputPort.WriteSuccess(sprintf "%d conteneur(s) trouvé(s)" response.Containers.Count)
            })

    member private this.InspectContainer() =
        Cmd.run outputPort (fun () ->
            task {
                use client = new ContainerClient()
                let! response = client.InspectAsync(id = this.ContainerIdInput)
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

    member private this.StartContainer() =
        Cmd.run outputPort (fun () ->
            task {
                use client = new ContainerClient()
                let! response = client.StartAsync(id = this.ContainerIdInput)
                outputPort.WriteSuccess(sprintf "Conteneur %s démarré - %s" this.ContainerIdInput response.Message)
            })

    member private this.StopContainer() =
        Cmd.run outputPort (fun () ->
            task {
                use client = new ContainerClient()
                let! response = client.StopAsync(id = this.ContainerIdInput, timeoutSeconds = this.ContainerTimeout)
                outputPort.WriteSuccess(sprintf "Conteneur %s arrêté - %s" this.ContainerIdInput response.Message)
            })

    member private this.DeleteContainer() =
        Cmd.run outputPort (fun () ->
            task {
                use client = new ContainerClient()
                let! response = client.DeleteAsync(id = this.ContainerIdInput, force = this.ContainerForce)
                if response.Success then
                    outputPort.WriteSuccess(sprintf "Conteneur %s supprimé" this.ContainerIdInput)
                else
                    outputPort.WriteWarning(response.Message)
            })

    member private this.PullImage() =
        Cmd.run outputPort (fun () ->
            task {
                use client = new ContainerClient()
                let! response =
                    client.PullImageAsync(
                        image = this.ContainerImageInput,
                        ?user = (if String.IsNullOrEmpty(this.ContainerImageUser) then None else Some this.ContainerImageUser))
                outputPort.WriteSuccess(sprintf "Image %s téléchargée - %s" this.ContainerImageInput response.Message)
            })

    member private this.GetVersion() =
        Cmd.run outputPort (fun () ->
            task {
                use client = new ContainerClient()
                let! response = client.GetVersionAsync()
                outputPort.WriteLine(sprintf "Version: %s" response.Version)
                outputPort.WriteLine(sprintf "Révision: %s" response.Revision)
                outputPort.WriteLine(sprintf "Go: %s" response.GoVersion)
                outputPort.WriteLine(sprintf "OS/Arch: %s/%s" response.Os response.Arch)
            })

    member private this.RenameContainer() =
        Cmd.run outputPort (fun () ->
            task {
                use client = new ContainerClient()
                let! response = client.RenameContainerAsync(id = this.ContainerIdInput, newName = this.ContainerNewName)
                outputPort.WriteSuccess(sprintf "Conteneur %s renommé en %s" this.ContainerIdInput this.ContainerNewName)
            })

    member private this.TopContainer() =
        Cmd.run outputPort (fun () ->
            task {
                use client = new ContainerClient()
                let! response = client.TopContainerAsync(id = this.ContainerIdInput)
                outputPort.WriteLine(sprintf "Processus du conteneur %s:" this.ContainerIdInput)
                for proc in response.Processes do
                    outputPort.WriteLine(sprintf "  PID: %d  CMD: %s" proc.Pid proc.Command)
            })

    member private this.GetContainerStats() =
        Cmd.run outputPort (fun () ->
            task {
                use client = new ContainerClient()
                let! response = client.GetContainerStatsAsync(id = this.ContainerIdInput)
                outputPort.WriteLine(sprintf "Métriques du conteneur %s:" this.ContainerIdInput)
                outputPort.WriteLine(sprintf "  CPU: %.2f  Mémoire: %d" response.CpuUsage response.MemoryUsage)
                outputPort.WriteLine(sprintf "  Réseau RX: %d  TX: %d" response.NetworkRx response.NetworkTx)
            })

    member private this.ListImages() =
        Cmd.run outputPort (fun () ->
            task {
                use client = new ContainerClient()
                let ns = if String.IsNullOrEmpty(this.ContainerNamespace) then None else Some this.ContainerNamespace
                let! response = client.ListImagesAsync(?namespaceName = ns)
                Dispatcher.UIThread.Post(fun () ->
                    images.Clear()
                    for img in response.Images do
                        images.Add({
                            Référentiel = img.Ref
                            Tag = img.Tag
                            Taille = sprintf "%d octets" img.Size
                            CrééLe = img.CreatedAt
                        })
                )
                outputPort.WriteSuccess(sprintf "%d image(s) trouvée(s)" response.Images.Count)
            })

    member private this.InspectImage() =
        Cmd.run outputPort (fun () ->
            task {
                use client = new ContainerClient()
                let ns = if String.IsNullOrEmpty(this.ContainerNamespace) then None else Some this.ContainerNamespace
                let! response = client.InspectImageAsync(ref = this.ContainerImageRef, ?namespaceName = ns)
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
                use client = new ContainerClient()
                let ns = if String.IsNullOrEmpty(this.ContainerNamespace) then None else Some this.ContainerNamespace
                let! response = client.RemoveImageAsync(ref = this.ContainerImageRef, ?namespaceName = ns)
                if response.Success then
                    outputPort.WriteSuccess(sprintf "Image %s supprimée" this.ContainerImageRef)
                else
                    outputPort.WriteWarning(response.Message)
            })

    member private this.TagImage() =
        Cmd.run outputPort (fun () ->
            task {
                use client = new ContainerClient()
                let ns = if String.IsNullOrEmpty(this.ContainerNamespace) then None else Some this.ContainerNamespace
                let! response = client.TagImageAsync(source = this.ContainerImageRef, target = this.ContainerImageTarget, ?namespaceName = ns)
                outputPort.WriteSuccess(sprintf "Image %s étiquetée en %s - %s" this.ContainerImageRef this.ContainerImageTarget response.Message)
            })

    member private this.CreateContainer() =
        Cmd.run outputPort (fun () ->
            task {
                use client = new ContainerClient()
                let mounts = MountParser.parse this.ContainerMounts
                let! response =
                    client.CreateAsync(
                        name = this.ContainerNameInput,
                        image = this.ContainerImageInput,
                        ?mounts = (if mounts.IsEmpty then None else Some mounts))
                outputPort.WriteSuccess(sprintf "Conteneur créé : %s (ID: %s)" response.Name response.Id)
            })

    member private this.GetContainerLogs() =
        task {
            try
                use source = logsSourceFactory ()
                if this.ContainerFollow then
                    cancelPreviousLogStream ()
                    let cts = new CancellationTokenSource()
                    logCts <- cts
                    try
                        let stream = source.GetStream(this.ContainerIdInput, true, this.ContainerTail, this.ContainerSince, cts.Token)
                        let enumerator = stream.GetAsyncEnumerator(cts.Token)
                        try
                            let mutable moving = true
                            while moving do
                                let! hasNext = enumerator.MoveNextAsync().AsTask()
                                if hasNext then
                                    outputPort.WriteLine(sprintf "[%s] %s" enumerator.Current.Timestamp enumerator.Current.Log)
                                else
                                    moving <- false
                        finally
                            enumerator.DisposeAsync().AsTask() |> ignore
                    finally
                        if logCts = cts then logCts <- null
                        cts.Dispose()
                else
                    let! entries = source.GetSnapshot(this.ContainerIdInput, this.ContainerTail, this.ContainerSince, CancellationToken.None)
                    let sb = Text.StringBuilder()
                    for entry in entries do
                        sb.AppendLine(sprintf "[%s] %s" entry.Timestamp entry.Log) |> ignore
                    outputPort.WriteSuccess(sb.ToString())
            with
            | :? OperationCanceledException -> ()
            | ex -> outputPort.WriteError(ex.Message)
        }

    member private this.ExecInContainer() =
        Cmd.run outputPort (fun () ->
            task {
                use client = new ContainerClient()
                let parts = Diplo.Core.CommandLine.split this.ContainerExecCommand |> Array.ofList
                let! entries = client.Exec(id = this.ContainerIdInput, command = parts)
                let sb = Text.StringBuilder()
                for entry in entries do
                    sb.Append(Text.Encoding.UTF8.GetString(entry.Data)) |> ignore
                outputPort.WriteSuccess(sb.ToString())
            })

    member private this.ListNamespaces() =
        Cmd.run outputPort (fun () ->
            task {
                use client = new ContainerClient()
                let! response = client.ListNamespacesAsync()
                let nsList = String.Join(", ", response.Namespaces)
                outputPort.WriteSuccess(sprintf "Namespaces: %s" nsList)
            })

    member private this.RegistryLogin() =
        Cmd.run outputPort (fun () ->
            task {
                if String.IsNullOrEmpty(this.RegistryInput) then
                    outputPort.WriteError("Le registre est requis (ex. myregistry.azurecr.io)")
                elif String.IsNullOrEmpty(this.RegistryUsernameInput) then
                    outputPort.WriteError("Le nom d'utilisateur est requis")
                elif String.IsNullOrEmpty(this.RegistryPasswordInput) then
                    outputPort.WriteError("Le mot de passe est requis")
                else
                    use client = new ContainerClient()
                    let! response =
                        client.LoginRegistryAsync(
                            registry = this.RegistryInput,
                            username = this.RegistryUsernameInput,
                            password = this.RegistryPasswordInput)
                    if response.Success then
                        outputPort.WriteSuccess(response.Message)
                        this.RegistryPasswordInput <- ""
                    else
                        outputPort.WriteError(response.Message)
            })

    member private this.RegistryLogout() =
        Cmd.run outputPort (fun () ->
            task {
                if String.IsNullOrEmpty(this.RegistryInput) then
                    outputPort.WriteError("Le registre est requis (ex. myregistry.azurecr.io)")
                else
                    use client = new ContainerClient()
                    let! response = client.LogoutRegistryAsync(registry = this.RegistryInput)
                    if response.Success then
                        outputPort.WriteSuccess(response.Message)
                    else
                        outputPort.WriteError(response.Message)
            })
