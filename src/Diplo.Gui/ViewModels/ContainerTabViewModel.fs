namespace Diplo.Gui.ViewModels

open System
open System.Collections.ObjectModel
open Avalonia.Threading
open Diplo.Core.Clients
open Diplo.Core.Output

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

type ContainerTabViewModel(outputPort: IOutputPort) as this =
    inherit ViewModelBase()

    let containers = ObservableCollection<ContainerInfo>()
    let images = ObservableCollection<ImageInfo>()

    let mutable containerIdInput = ""
    let mutable containerNameInput = ""
    let mutable containerImageInput = ""
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

    member _.Containers = containers
    member _.Images = images

    member _.ContainerIdInput with get () = containerIdInput and set v = containerIdInput <- v; this.OnPropertyChanged()
    member _.ContainerNameInput with get () = containerNameInput and set v = containerNameInput <- v; this.OnPropertyChanged()
    member _.ContainerImageInput with get () = containerImageInput and set v = containerImageInput <- v; this.OnPropertyChanged()
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

    member _.ListContainersCommand = RelayCommand(Action(fun () -> this.ListContainers() |> Async.Start))
    member _.InspectContainerCommand = RelayCommand(Action(fun () -> this.InspectContainer() |> Async.Start))
    member _.StartContainerCommand = RelayCommand(Action(fun () -> this.StartContainer() |> Async.Start))
    member _.StopContainerCommand = RelayCommand(Action(fun () -> this.StopContainer() |> Async.Start))
    member _.DeleteContainerCommand = RelayCommand(Action(fun () -> this.DeleteContainer() |> Async.Start))
    member _.PullImageCommand = RelayCommand(Action(fun () -> this.PullImage() |> Async.Start))
    member _.VersionCommand = RelayCommand(Action(fun () -> this.GetVersion() |> Async.Start))
    member _.RenameContainerCommand = RelayCommand(Action(fun () -> this.RenameContainer() |> Async.Start))
    member _.TopContainerCommand = RelayCommand(Action(fun () -> this.TopContainer() |> Async.Start))
    member _.StatsContainerCommand = RelayCommand(Action(fun () -> this.GetContainerStats() |> Async.Start))
    member _.ListImagesCommand = RelayCommand(Action(fun () -> this.ListImages() |> Async.Start))
    member _.InspectImageCommand = RelayCommand(Action(fun () -> this.InspectImage() |> Async.Start))
    member _.RemoveImageCommand = RelayCommand(Action(fun () -> this.RemoveImage() |> Async.Start))
    member _.TagImageCommand = RelayCommand(Action(fun () -> this.TagImage() |> Async.Start))
    member _.CreateContainerCommand = RelayCommand(Action(fun () -> this.CreateContainer() |> Async.Start))
    member _.GetContainerLogsCommand = RelayCommand(Action(fun () -> this.GetContainerLogs() |> Async.Start))
    member _.ExecInContainerCommand = RelayCommand(Action(fun () -> this.ExecInContainer() |> Async.Start))
    member _.ListNamespacesCommand = RelayCommand(Action(fun () -> this.ListNamespaces() |> Async.Start))

    member private this.ListContainers() =
        async {
            try
                use client = new ContainerClient()
                let! response = client.ListAsync(all = this.ContainerAll) |> Async.AwaitTask
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
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.InspectContainer() =
        async {
            try
                use client = new ContainerClient()
                let! response = client.InspectAsync(id = this.ContainerIdInput) |> Async.AwaitTask
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
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.StartContainer() =
        async {
            try
                use client = new ContainerClient()
                let! response = client.StartAsync(id = this.ContainerIdInput) |> Async.AwaitTask
                outputPort.WriteSuccess(sprintf "Conteneur %s démarré - %s" this.ContainerIdInput response.Message)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.StopContainer() =
        async {
            try
                use client = new ContainerClient()
                let! response = client.StopAsync(id = this.ContainerIdInput, timeoutSeconds = this.ContainerTimeout) |> Async.AwaitTask
                outputPort.WriteSuccess(sprintf "Conteneur %s arrêté - %s" this.ContainerIdInput response.Message)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.DeleteContainer() =
        async {
            try
                use client = new ContainerClient()
                let! response = client.DeleteAsync(id = this.ContainerIdInput, force = this.ContainerForce) |> Async.AwaitTask
                if response.Success then
                    outputPort.WriteSuccess(sprintf "Conteneur %s supprimé" this.ContainerIdInput)
                else
                    outputPort.WriteWarning(response.Message)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.PullImage() =
        async {
            try
                use client = new ContainerClient()
                let! response = client.PullImageAsync(image = this.ContainerImageInput) |> Async.AwaitTask
                outputPort.WriteSuccess(sprintf "Image %s téléchargée - %s" this.ContainerImageInput response.Message)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.GetVersion() =
        async {
            try
                use client = new ContainerClient()
                let! response = client.GetVersionAsync() |> Async.AwaitTask
                outputPort.WriteLine(sprintf "Version: %s" response.Version)
                outputPort.WriteLine(sprintf "Révision: %s" response.Revision)
                outputPort.WriteLine(sprintf "Go: %s" response.GoVersion)
                outputPort.WriteLine(sprintf "OS/Arch: %s/%s" response.Os response.Arch)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.RenameContainer() =
        async {
            try
                use client = new ContainerClient()
                let! response = client.RenameContainerAsync(id = this.ContainerIdInput, newName = this.ContainerNewName) |> Async.AwaitTask
                outputPort.WriteSuccess(sprintf "Conteneur %s renommé en %s" this.ContainerIdInput this.ContainerNewName)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.TopContainer() =
        async {
            try
                use client = new ContainerClient()
                let! response = client.TopContainerAsync(id = this.ContainerIdInput) |> Async.AwaitTask
                outputPort.WriteLine(sprintf "Processus du conteneur %s:" this.ContainerIdInput)
                for proc in response.Processes do
                    outputPort.WriteLine(sprintf "  PID: %d  CMD: %s" proc.Pid proc.Command)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.GetContainerStats() =
        async {
            try
                use client = new ContainerClient()
                let! response = client.GetContainerStatsAsync(id = this.ContainerIdInput) |> Async.AwaitTask
                outputPort.WriteLine(sprintf "Métriques du conteneur %s:" this.ContainerIdInput)
                outputPort.WriteLine(sprintf "  CPU: %.2f  Mémoire: %d" response.CpuUsage response.MemoryUsage)
                outputPort.WriteLine(sprintf "  Réseau RX: %d  TX: %d" response.NetworkRx response.NetworkTx)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.ListImages() =
        async {
            try
                use client = new ContainerClient()
                let ns = if String.IsNullOrEmpty(this.ContainerNamespace) then None else Some this.ContainerNamespace
                let! response = client.ListImagesAsync(?namespaceName = ns) |> Async.AwaitTask
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
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.InspectImage() =
        async {
            try
                use client = new ContainerClient()
                let ns = if String.IsNullOrEmpty(this.ContainerNamespace) then None else Some this.ContainerNamespace
                let! response = client.InspectImageAsync(ref = this.ContainerImageRef, ?namespaceName = ns) |> Async.AwaitTask
                outputPort.WriteLine(sprintf "Référentiel: %s" response.Ref)
                outputPort.WriteLine(sprintf "Tag: %s" response.Tag)
                outputPort.WriteLine(sprintf "Taille: %d octets" response.Size)
                outputPort.WriteLine(sprintf "Créé le: %s" response.CreatedAt)
                if response.Labels.Count > 0 then
                    outputPort.WriteLine("Labels:")
                    for kvp in response.Labels do
                        outputPort.WriteLine(sprintf "  %s = %s" kvp.Key kvp.Value)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.RemoveImage() =
        async {
            try
                use client = new ContainerClient()
                let ns = if String.IsNullOrEmpty(this.ContainerNamespace) then None else Some this.ContainerNamespace
                let! response = client.RemoveImageAsync(ref = this.ContainerImageRef, ?namespaceName = ns) |> Async.AwaitTask
                if response.Success then
                    outputPort.WriteSuccess(sprintf "Image %s supprimée" this.ContainerImageRef)
                else
                    outputPort.WriteWarning(response.Message)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.TagImage() =
        async {
            try
                use client = new ContainerClient()
                let ns = if String.IsNullOrEmpty(this.ContainerNamespace) then None else Some this.ContainerNamespace
                let! response = client.TagImageAsync(source = this.ContainerImageRef, target = this.ContainerImageTarget, ?namespaceName = ns) |> Async.AwaitTask
                outputPort.WriteSuccess(sprintf "Image %s étiquetée en %s - %s" this.ContainerImageRef this.ContainerImageTarget response.Message)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.CreateContainer() =
        async {
            try
                use client = new ContainerClient()
                let! response = client.CreateAsync(name = this.ContainerNameInput, image = this.ContainerImageInput) |> Async.AwaitTask
                outputPort.WriteSuccess(sprintf "Conteneur créé : %s (ID: %s)" response.Name response.Id)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.GetContainerLogs() =
        async {
            try
                use client = new ContainerClient()
                let! entries = client.GetLogs(id = this.ContainerIdInput, follow = this.ContainerFollow, tail = this.ContainerTail, since = this.ContainerSince) |> Async.AwaitTask
                let sb = Text.StringBuilder()
                for entry in entries do
                    sb.AppendLine(sprintf "[%s] %s" entry.Timestamp entry.Log) |> ignore
                outputPort.WriteSuccess(sb.ToString())
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.ExecInContainer() =
        async {
            try
                use client = new ContainerClient()
                let parts = this.ContainerExecCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                let! entries = client.Exec(id = this.ContainerIdInput, command = parts) |> Async.AwaitTask
                let sb = Text.StringBuilder()
                for entry in entries do
                    sb.Append(Text.Encoding.UTF8.GetString(entry.Data)) |> ignore
                outputPort.WriteSuccess(sb.ToString())
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.ListNamespaces() =
        async {
            try
                use client = new ContainerClient()
                let! response = client.ListNamespacesAsync() |> Async.AwaitTask
                let nsList = String.Join(", ", response.Namespaces)
                outputPort.WriteSuccess(sprintf "Namespaces: %s" nsList)
            with ex -> outputPort.WriteError(ex.Message)
        }
