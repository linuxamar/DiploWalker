namespace Diplo.Gui.ViewModels

open System
open System.Collections.ObjectModel
open System.Windows.Input
open Avalonia.Threading
open Diplo.Core.Clients
open Diplo.Core.Connection
open Diplo.Core.Output
open Diplo.Gui.Services
open Diplo.Grpc.Volume
open Diplo.Grpc.Network

type ContainerInfo = {
    mutable Id: string
    mutable Nom: string
    mutable Image: string
    mutable État: string
    mutable CrééLe: string
}

type VolumeInfo = {
    mutable Id: string
    mutable Nom: string
    mutable Driver: string
    mutable PointDeMontage: string
    mutable Taille: string
}

type NetworkInfo = {
    mutable Id: string
    mutable Nom: string
    mutable Driver: string
    mutable SousReseau: string
    mutable Passerelle: string
    mutable CrééLe: string
}

type ImageInfo = {
    mutable Référentiel: string
    mutable Tag: string
    mutable Taille: string
    mutable CrééLe: string
}

type MainWindowViewModel() as this =
    inherit ViewModelBase()

    let outputPort = AvaloniaOutputPort()
    let containers = ObservableCollection<ContainerInfo>()
    let volumes = ObservableCollection<VolumeInfo>()
    let networks = ObservableCollection<NetworkInfo>()
    let images = ObservableCollection<ImageInfo>()
    let logText = System.Text.StringBuilder()

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

    let mutable volumeIdInput = ""
    let mutable volumeNameInput = ""
    let mutable volumeDriver = "local"
    let mutable volumeTargetPath = ""
    let mutable volumeForce = false

    let mutable networkIdInput = ""
    let mutable networkNameInput = ""
    let mutable networkDriver = "bridge"
    let mutable networkSubnet = ""
    let mutable networkGateway = ""
    let mutable networkEndpointId = ""
    let mutable networkContainerId = ""
    let mutable networkIpv4 = ""
    let mutable networkForce = false
    let mutable containerFollow = false
    let mutable containerTail = 100
    let mutable containerSince = ""
    let mutable containerExecCommand = ""

    let mutable networkCniPluginPath = ""
    let mutable networkCniCommand = "ADD"
    let mutable networkNetnsPath = ""

    let updateLog () =
        logText.Clear() |> ignore
        for entry in outputPort.LogLines do
            logText.AppendLine(entry.DisplayText) |> ignore
        this.OnPropertyChanged(nameof this.LogOutput)

    do outputPort.LogLines.CollectionChanged.Add(fun _ -> Dispatcher.UIThread.Post(updateLog))

    member _.OutputPort = outputPort :> IOutputPort

    member _.Containers = containers
    member _.Volumes = volumes
    member _.Networks = networks
    member _.Images = images

    member _.LogOutput = logText.ToString()

    // --- Container inputs ---
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

    // --- Volume inputs ---
    member _.VolumeIdInput with get () = volumeIdInput and set v = volumeIdInput <- v; this.OnPropertyChanged()
    member _.VolumeNameInput with get () = volumeNameInput and set v = volumeNameInput <- v; this.OnPropertyChanged()
    member _.VolumeDriver with get () = volumeDriver and set v = volumeDriver <- v; this.OnPropertyChanged()
    member _.VolumeTargetPath with get () = volumeTargetPath and set v = volumeTargetPath <- v; this.OnPropertyChanged()
    member _.VolumeForce with get () = volumeForce and set v = volumeForce <- v; this.OnPropertyChanged()

    // --- Network inputs ---
    member _.NetworkIdInput with get () = networkIdInput and set v = networkIdInput <- v; this.OnPropertyChanged()
    member _.NetworkNameInput with get () = networkNameInput and set v = networkNameInput <- v; this.OnPropertyChanged()
    member _.NetworkDriver with get () = networkDriver and set v = networkDriver <- v; this.OnPropertyChanged()
    member _.NetworkSubnet with get () = networkSubnet and set v = networkSubnet <- v; this.OnPropertyChanged()
    member _.NetworkGateway with get () = networkGateway and set v = networkGateway <- v; this.OnPropertyChanged()
    member _.NetworkEndpointId with get () = networkEndpointId and set v = networkEndpointId <- v; this.OnPropertyChanged()
    member _.NetworkContainerId with get () = networkContainerId and set v = networkContainerId <- v; this.OnPropertyChanged()
    member _.NetworkIpv4 with get () = networkIpv4 and set v = networkIpv4 <- v; this.OnPropertyChanged()
    member _.NetworkForce with get () = networkForce and set v = networkForce <- v; this.OnPropertyChanged()

    member _.NetworkCniPluginPath with get () = networkCniPluginPath and set v = networkCniPluginPath <- v; this.OnPropertyChanged()
    member _.NetworkCniCommand with get () = networkCniCommand and set v = networkCniCommand <- v; this.OnPropertyChanged()
    member _.NetworkNetnsPath with get () = networkNetnsPath and set v = networkNetnsPath <- v; this.OnPropertyChanged()

    // --- Container commands ---
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

    // --- Volume commands ---
    member _.ListVolumesCommand = RelayCommand(Action(fun () -> this.ListVolumes() |> Async.Start))
    member _.InspectVolumeCommand = RelayCommand(Action(fun () -> this.InspectVolume() |> Async.Start))
    member _.CreateVolumeCommand = RelayCommand(Action(fun () -> this.CreateVolume() |> Async.Start))
    member _.RemoveVolumeCommand = RelayCommand(Action(fun () -> this.RemoveVolume() |> Async.Start))
    member _.MountVolumeCommand = RelayCommand(Action(fun () -> this.MountVolume() |> Async.Start))
    member _.UnmountVolumeCommand = RelayCommand(Action(fun () -> this.UnmountVolume() |> Async.Start))
    member _.PruneVolumesCommand = RelayCommand(Action(fun () -> this.PruneVolumes() |> Async.Start))

    // --- Network commands ---
    member _.ListNetworksCommand = RelayCommand(Action(fun () -> this.ListNetworks() |> Async.Start))
    member _.InspectNetworkCommand = RelayCommand(Action(fun () -> this.InspectNetwork() |> Async.Start))
    member _.CreateNetworkCommand = RelayCommand(Action(fun () -> this.CreateNetwork() |> Async.Start))
    member _.RemoveNetworkCommand = RelayCommand(Action(fun () -> this.RemoveNetwork() |> Async.Start))
    member _.ConnectNetworkCommand = RelayCommand(Action(fun () -> this.ConnectContainer() |> Async.Start))
    member _.DisconnectNetworkCommand = RelayCommand(Action(fun () -> this.DisconnectContainer() |> Async.Start))
    member _.RunCniPluginCommand = RelayCommand(Action(fun () -> this.RunCniPlugin() |> Async.Start))
    member _.PruneNetworksCommand = RelayCommand(Action(fun () -> this.PruneNetworks() |> Async.Start))

    // --- Menu commands ---
    member _.QuitCommand = RelayCommand(Action(fun () ->
        match Avalonia.Application.Current.ApplicationLifetime with
        | :? Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime as desktop ->
            desktop.Shutdown(0)
        | _ -> ()))

    member _.AboutCommand = RelayCommand(Action(fun () ->
        (outputPort :> IOutputPort).WriteLine("Diplo — Gestion Docker")
        (outputPort :> IOutputPort).WriteLine("Interface graphique Avalonia pour la gestion de conteneurs, volumes et réseaux.")))

    // --- Container operations ---
    member private this.ListContainers() =
        async {
            try
                let client = new ContainerClient()
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
                (outputPort :> IOutputPort).WriteSuccess(sprintf "%d conteneur(s) trouvé(s)" response.Containers.Count)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.InspectContainer() =
        async {
            try
                let client = new ContainerClient()
                let! response = client.InspectAsync(id = this.ContainerIdInput) |> Async.AwaitTask
                (outputPort :> IOutputPort).WriteLine(sprintf "ID: %s" response.Id)
                (outputPort :> IOutputPort).WriteLine(sprintf "Nom: %s" response.Name)
                (outputPort :> IOutputPort).WriteLine(sprintf "Image: %s" response.Image)
                (outputPort :> IOutputPort).WriteLine(sprintf "État: %s" (response.State.ToString()))
                (outputPort :> IOutputPort).WriteLine(sprintf "Créé le: %s" response.CreatedAt)
                if not (String.IsNullOrEmpty(response.StartedAt)) then
                    (outputPort :> IOutputPort).WriteLine(sprintf "Démarré le: %s" response.StartedAt)
                if not (String.IsNullOrEmpty(response.FinishedAt)) then
                    (outputPort :> IOutputPort).WriteLine(sprintf "Arrêté le: %s" response.FinishedAt)
                if response.Labels.Count > 0 then
                    (outputPort :> IOutputPort).WriteLine("Labels:")
                    for kvp in response.Labels do
                        (outputPort :> IOutputPort).WriteLine(sprintf "  %s = %s" kvp.Key kvp.Value)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.StartContainer() =
        async {
            try
                let client = new ContainerClient()
                let! response = client.StartAsync(id = this.ContainerIdInput) |> Async.AwaitTask
                (outputPort :> IOutputPort).WriteSuccess(sprintf "Conteneur %s démarré - %s" this.ContainerIdInput response.Message)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.StopContainer() =
        async {
            try
                let client = new ContainerClient()
                let! response = client.StopAsync(id = this.ContainerIdInput, timeoutSeconds = this.ContainerTimeout) |> Async.AwaitTask
                (outputPort :> IOutputPort).WriteSuccess(sprintf "Conteneur %s arrêté - %s" this.ContainerIdInput response.Message)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.DeleteContainer() =
        async {
            try
                let client = new ContainerClient()
                let! response = client.DeleteAsync(id = this.ContainerIdInput, force = this.ContainerForce) |> Async.AwaitTask
                if response.Success then
                    (outputPort :> IOutputPort).WriteSuccess(sprintf "Conteneur %s supprimé" this.ContainerIdInput)
                else
                    (outputPort :> IOutputPort).WriteWarning(response.Message)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.PullImage() =
        async {
            try
                let client = new ContainerClient()
                let! response = client.PullImageAsync(image = this.ContainerImageInput) |> Async.AwaitTask
                (outputPort :> IOutputPort).WriteSuccess(sprintf "Image %s téléchargée - %s" this.ContainerImageInput response.Message)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.GetVersion() =
        async {
            try
                let client = new ContainerClient()
                let! response = client.GetVersionAsync() |> Async.AwaitTask
                (outputPort :> IOutputPort).WriteLine(sprintf "Version: %s" response.Version)
                (outputPort :> IOutputPort).WriteLine(sprintf "Révision: %s" response.Revision)
                (outputPort :> IOutputPort).WriteLine(sprintf "Go: %s" response.GoVersion)
                (outputPort :> IOutputPort).WriteLine(sprintf "OS/Arch: %s/%s" response.Os response.Arch)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.RenameContainer() =
        async {
            try
                let client = new ContainerClient()
                let! response = client.RenameContainerAsync(id = this.ContainerIdInput, newName = this.ContainerNewName) |> Async.AwaitTask
                (outputPort :> IOutputPort).WriteSuccess(sprintf "Conteneur %s renommé en %s" this.ContainerIdInput this.ContainerNewName)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.TopContainer() =
        async {
            try
                let client = new ContainerClient()
                let! response = client.TopContainerAsync(id = this.ContainerIdInput) |> Async.AwaitTask
                (outputPort :> IOutputPort).WriteLine(sprintf "Processus du conteneur %s:" this.ContainerIdInput)
                for proc in response.Processes do
                    (outputPort :> IOutputPort).WriteLine(sprintf "  PID: %d  CMD: %s" proc.Pid proc.Command)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.GetContainerStats() =
        async {
            try
                let client = new ContainerClient()
                let! response = client.GetContainerStatsAsync(id = this.ContainerIdInput) |> Async.AwaitTask
                (outputPort :> IOutputPort).WriteLine(sprintf "Métriques du conteneur %s:" this.ContainerIdInput)
                (outputPort :> IOutputPort).WriteLine(sprintf "  CPU: %.2f  Mémoire: %d" response.CpuUsage response.MemoryUsage)
                (outputPort :> IOutputPort).WriteLine(sprintf "  Réseau RX: %d  TX: %d" response.NetworkRx response.NetworkTx)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.ListImages() =
        async {
            try
                let client = new ContainerClient()
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
                (outputPort :> IOutputPort).WriteSuccess(sprintf "%d image(s) trouvée(s)" response.Images.Count)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.InspectImage() =
        async {
            try
                let client = new ContainerClient()
                let ns = if String.IsNullOrEmpty(this.ContainerNamespace) then None else Some this.ContainerNamespace
                let! response = client.InspectImageAsync(ref = this.ContainerImageRef, ?namespaceName = ns) |> Async.AwaitTask
                (outputPort :> IOutputPort).WriteLine(sprintf "Référentiel: %s" response.Ref)
                (outputPort :> IOutputPort).WriteLine(sprintf "Tag: %s" response.Tag)
                (outputPort :> IOutputPort).WriteLine(sprintf "Taille: %d octets" response.Size)
                (outputPort :> IOutputPort).WriteLine(sprintf "Créé le: %s" response.CreatedAt)
                if response.Labels.Count > 0 then
                    (outputPort :> IOutputPort).WriteLine("Labels:")
                    for kvp in response.Labels do
                        (outputPort :> IOutputPort).WriteLine(sprintf "  %s = %s" kvp.Key kvp.Value)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.RemoveImage() =
        async {
            try
                let client = new ContainerClient()
                let ns = if String.IsNullOrEmpty(this.ContainerNamespace) then None else Some this.ContainerNamespace
                let! response = client.RemoveImageAsync(ref = this.ContainerImageRef, ?namespaceName = ns) |> Async.AwaitTask
                if response.Success then
                    (outputPort :> IOutputPort).WriteSuccess(sprintf "Image %s supprimée" this.ContainerImageRef)
                else
                    (outputPort :> IOutputPort).WriteWarning(response.Message)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.TagImage() =
        async {
            try
                let client = new ContainerClient()
                let ns = if String.IsNullOrEmpty(this.ContainerNamespace) then None else Some this.ContainerNamespace
                let! response = client.TagImageAsync(source = this.ContainerImageRef, target = this.ContainerImageTarget, ?namespaceName = ns) |> Async.AwaitTask
                (outputPort :> IOutputPort).WriteSuccess(sprintf "Image %s étiquetée en %s - %s" this.ContainerImageRef this.ContainerImageTarget response.Message)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.CreateContainer() =
        async {
            try
                let client = new ContainerClient()
                let! response = client.CreateAsync(name = this.ContainerNameInput, image = this.ContainerImageInput) |> Async.AwaitTask
                (outputPort :> IOutputPort).WriteSuccess(sprintf "Conteneur créé : %s (ID: %s)" response.Name response.Id)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.GetContainerLogs() =
        async {
            try
                let client = new ContainerClient()
                let! entries = client.GetLogs(id = this.ContainerIdInput, follow = this.ContainerFollow, tail = this.ContainerTail, since = this.ContainerSince) |> Async.AwaitTask
                let sb = System.Text.StringBuilder()
                for entry in entries do
                    sb.AppendLine(sprintf "[%s] %s" entry.Timestamp entry.Log) |> ignore
                (outputPort :> IOutputPort).WriteSuccess(sb.ToString())
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.ExecInContainer() =
        async {
            try
                let client = new ContainerClient()
                let parts = this.ContainerExecCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                let! entries = client.Exec(id = this.ContainerIdInput, command = parts) |> Async.AwaitTask
                let sb = System.Text.StringBuilder()
                for entry in entries do
                    sb.Append(System.Text.Encoding.UTF8.GetString(entry.Data)) |> ignore
                (outputPort :> IOutputPort).WriteSuccess(sb.ToString())
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.ListNamespaces() =
        async {
            try
                let client = new ContainerClient()
                let! response = client.ListNamespacesAsync() |> Async.AwaitTask
                let nsList = String.Join(", ", response.Namespaces)
                (outputPort :> IOutputPort).WriteSuccess(sprintf "Namespaces: %s" nsList)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    // --- Volume operations ---
    member private this.ListVolumes() =
        async {
            try
                let client = new VolumeClient()
                let! response = client.ListAsync() |> Async.AwaitTask
                Dispatcher.UIThread.Post(fun () ->
                    volumes.Clear()
                    for v in response.Volumes do
                        volumes.Add({
                            Id = v.Id
                            Nom = v.Name
                            Driver = v.Driver.ToString()
                            PointDeMontage = v.Mountpoint
                            Taille = if v.SizeBytes > 0L then sprintf "%d octets" v.SizeBytes else "-"
                        })
                )
                (outputPort :> IOutputPort).WriteSuccess(sprintf "%d volume(s) trouvé(s)" response.Volumes.Count)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.InspectVolume() =
        async {
            try
                let client = new VolumeClient()
                let! response = client.InspectAsync(id = this.VolumeIdInput) |> Async.AwaitTask
                (outputPort :> IOutputPort).WriteLine(sprintf "ID: %s" response.Id)
                (outputPort :> IOutputPort).WriteLine(sprintf "Nom: %s" response.Name)
                (outputPort :> IOutputPort).WriteLine(sprintf "Driver: %s" (response.Driver.ToString()))
                (outputPort :> IOutputPort).WriteLine(sprintf "Point de montage: %s" response.Mountpoint)
                (outputPort :> IOutputPort).WriteLine(sprintf "État: %s" (response.State.ToString()))
                if response.SizeBytes > 0L then
                    (outputPort :> IOutputPort).WriteLine(sprintf "Taille: %d octets" response.SizeBytes)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.CreateVolume() =
        async {
            try
                let client = new VolumeClient()
                let! response = client.CreateAsync(name = this.VolumeNameInput, driver = StorageDriverType.Parse(this.VolumeDriver, true)) |> Async.AwaitTask
                (outputPort :> IOutputPort).WriteSuccess(sprintf "Volume %s créé (ID: %s)" this.VolumeNameInput response.Id)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.RemoveVolume() =
        async {
            try
                let client = new VolumeClient()
                let! response = client.RemoveAsync(id = this.VolumeIdInput, force = this.VolumeForce) |> Async.AwaitTask
                if response.Success then
                    (outputPort :> IOutputPort).WriteSuccess(sprintf "Volume %s supprimé" this.VolumeIdInput)
                else
                    (outputPort :> IOutputPort).WriteWarning(response.Message)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.MountVolume() =
        async {
            try
                let client = new VolumeClient()
                let! response = client.MountAsync(id = this.VolumeIdInput, targetPath = this.VolumeTargetPath) |> Async.AwaitTask
                (outputPort :> IOutputPort).WriteSuccess(sprintf "Volume %s monté sur %s - %s" this.VolumeIdInput this.VolumeTargetPath response.Message)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.UnmountVolume() =
        async {
            try
                let client = new VolumeClient()
                let! response = client.UnmountAsync(id = this.VolumeIdInput, targetPath = this.VolumeTargetPath) |> Async.AwaitTask
                (outputPort :> IOutputPort).WriteSuccess(sprintf "Volume %s démonté de %s - %s" this.VolumeIdInput this.VolumeTargetPath response.Message)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.PruneVolumes() =
        async {
            try
                let client = new VolumeClient()
                let! response = client.PruneVolumesAsync() |> Async.AwaitTask
                (outputPort :> IOutputPort).WriteSuccess(sprintf "Volumes nettoyés - %s" response.Message)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    // --- Network operations ---
    member private this.ListNetworks() =
        async {
            try
                let client = new NetworkClient()
                let! response = client.ListAsync() |> Async.AwaitTask
                Dispatcher.UIThread.Post(fun () ->
                    networks.Clear()
                    for n in response.Networks do
                        networks.Add({
                            Id = n.Id
                            Nom = n.Name
                            Driver = n.Driver.ToString()
                            SousReseau = n.Subnet
                            Passerelle = n.Gateway
                            CrééLe = n.CreatedAt
                        })
                )
                (outputPort :> IOutputPort).WriteSuccess(sprintf "%d réseau(x) trouvé(s)" response.Networks.Count)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.InspectNetwork() =
        async {
            try
                let client = new NetworkClient()
                let! response = client.InspectAsync(id = this.NetworkIdInput) |> Async.AwaitTask
                (outputPort :> IOutputPort).WriteLine(sprintf "ID: %s" response.Id)
                (outputPort :> IOutputPort).WriteLine(sprintf "Nom: %s" response.Name)
                (outputPort :> IOutputPort).WriteLine(sprintf "Driver: %s" (response.Driver.ToString()))
                (outputPort :> IOutputPort).WriteLine(sprintf "Sous-réseau: %s" response.Subnet)
                (outputPort :> IOutputPort).WriteLine(sprintf "Passerelle: %s" response.Gateway)
                if response.Endpoints.Count > 0 then
                    (outputPort :> IOutputPort).WriteLine(sprintf "Points de connexion: %d" response.Endpoints.Count)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.CreateNetwork() =
        async {
            try
                let client = new NetworkClient()
                let driverEnum =
                    match this.NetworkDriver.ToLowerInvariant() with
                    | "bridge" -> Diplo.Grpc.Network.NetworkDriver.Bridge
                    | "none" -> Diplo.Grpc.Network.NetworkDriver.None
                    | "pod" -> Diplo.Grpc.Network.NetworkDriver.Pod
                    | "cni" -> Diplo.Grpc.Network.NetworkDriver.CustomCni
                    | _ -> Diplo.Grpc.Network.NetworkDriver.Bridge
                let! response = client.CreateAsync(this.NetworkNameInput, driver = driverEnum, subnet = this.NetworkSubnet, gateway = this.NetworkGateway) |> Async.AwaitTask
                (outputPort :> IOutputPort).WriteSuccess(sprintf "Réseau %s créé (ID: %s)" this.NetworkNameInput response.Id)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.RemoveNetwork() =
        async {
            try
                let client = new NetworkClient()
                let! response = client.RemoveAsync(id = this.NetworkIdInput, force = this.NetworkForce) |> Async.AwaitTask
                if response.Success then
                    (outputPort :> IOutputPort).WriteSuccess(sprintf "Réseau %s supprimé" this.NetworkIdInput)
                else
                    (outputPort :> IOutputPort).WriteWarning(response.Message)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.ConnectContainer() =
        async {
            try
                let client = new NetworkClient()
                let! response = client.ConnectAsync(this.NetworkIdInput, this.NetworkContainerId, endpointId = this.NetworkEndpointId, ipv4Address = this.NetworkIpv4) |> Async.AwaitTask
                (outputPort :> IOutputPort).WriteSuccess(sprintf "Conteneur %s connecté au réseau %s - %s" this.NetworkContainerId this.NetworkIdInput response.Message)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.DisconnectContainer() =
        async {
            try
                let client = new NetworkClient()
                let! response = client.DisconnectAsync(this.NetworkIdInput, this.NetworkContainerId, endpointId = this.NetworkEndpointId, force = this.NetworkForce) |> Async.AwaitTask
                if response.Success then
                    (outputPort :> IOutputPort).WriteSuccess(sprintf "Conteneur %s déconnecté du réseau %s" this.NetworkContainerId this.NetworkIdInput)
                else
                    (outputPort :> IOutputPort).WriteWarning(response.Message)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.RunCniPlugin() =
        async {
            try
                let client = new NetworkClient()
                let! response = client.RunCniPluginAsync(pluginPath = this.NetworkCniPluginPath, command = this.NetworkCniCommand, containerId = this.NetworkContainerId, netnsPath = this.NetworkNetnsPath) |> Async.AwaitTask
                if response.Success then
                    (outputPort :> IOutputPort).WriteSuccess(sprintf "Plugin CNI exécuté - %s" response.Message)
                else
                    (outputPort :> IOutputPort).WriteWarning(response.Message)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }

    member private this.PruneNetworks() =
        async {
            try
                let client = new NetworkClient()
                let! response = client.PruneNetworksAsync() |> Async.AwaitTask
                (outputPort :> IOutputPort).WriteSuccess(sprintf "Réseaux nettoyés - %s" response.Message)
            with ex -> (outputPort :> IOutputPort).WriteError(ex.Message)
        }
