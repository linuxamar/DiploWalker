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

type MainWindowViewModel() as this =
    inherit ViewModelBase()

    let outputPort = AvaloniaOutputPort()
    let containers = ObservableCollection<ContainerInfo>()
    let volumes = ObservableCollection<VolumeInfo>()
    let networks = ObservableCollection<NetworkInfo>()
    let logText = System.Text.StringBuilder()

    let mutable containerIdInput = ""
    let mutable containerNameInput = ""
    let mutable containerImageInput = ""
    let mutable containerNamespace = ""
    let mutable containerAll = false
    let mutable containerTimeout = 10
    let mutable containerForce = false

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

    member _.LogOutput = logText.ToString()

    // --- Container inputs ---
    member _.ContainerIdInput with get () = containerIdInput and set v = containerIdInput <- v; this.OnPropertyChanged()
    member _.ContainerNameInput with get () = containerNameInput and set v = containerNameInput <- v; this.OnPropertyChanged()
    member _.ContainerImageInput with get () = containerImageInput and set v = containerImageInput <- v; this.OnPropertyChanged()
    member _.ContainerNamespace with get () = containerNamespace and set v = containerNamespace <- v; this.OnPropertyChanged()
    member _.ContainerAll with get () = containerAll and set v = containerAll <- v; this.OnPropertyChanged()
    member _.ContainerTimeout with get () = containerTimeout and set v = containerTimeout <- v; this.OnPropertyChanged()
    member _.ContainerForce with get () = containerForce and set v = containerForce <- v; this.OnPropertyChanged()

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

    // --- Container commands ---
    member _.ListContainersCommand = RelayCommand(Action(fun () -> this.ListContainers() |> Async.Start))
    member _.InspectContainerCommand = RelayCommand(Action(fun () -> this.InspectContainer() |> Async.Start))
    member _.StartContainerCommand = RelayCommand(Action(fun () -> this.StartContainer() |> Async.Start))
    member _.StopContainerCommand = RelayCommand(Action(fun () -> this.StopContainer() |> Async.Start))
    member _.DeleteContainerCommand = RelayCommand(Action(fun () -> this.DeleteContainer() |> Async.Start))
    member _.PullImageCommand = RelayCommand(Action(fun () -> this.PullImage() |> Async.Start))
    member _.VersionCommand = RelayCommand(Action(fun () -> this.GetVersion() |> Async.Start))

    // --- Volume commands ---
    member _.ListVolumesCommand = RelayCommand(Action(fun () -> this.ListVolumes() |> Async.Start))
    member _.InspectVolumeCommand = RelayCommand(Action(fun () -> this.InspectVolume() |> Async.Start))
    member _.CreateVolumeCommand = RelayCommand(Action(fun () -> this.CreateVolume() |> Async.Start))
    member _.RemoveVolumeCommand = RelayCommand(Action(fun () -> this.RemoveVolume() |> Async.Start))
    member _.MountVolumeCommand = RelayCommand(Action(fun () -> this.MountVolume() |> Async.Start))
    member _.UnmountVolumeCommand = RelayCommand(Action(fun () -> this.UnmountVolume() |> Async.Start))

    // --- Network commands ---
    member _.ListNetworksCommand = RelayCommand(Action(fun () -> this.ListNetworks() |> Async.Start))
    member _.InspectNetworkCommand = RelayCommand(Action(fun () -> this.InspectNetwork() |> Async.Start))
    member _.CreateNetworkCommand = RelayCommand(Action(fun () -> this.CreateNetwork() |> Async.Start))
    member _.RemoveNetworkCommand = RelayCommand(Action(fun () -> this.RemoveNetwork() |> Async.Start))
    member _.ConnectNetworkCommand = RelayCommand(Action(fun () -> this.ConnectContainer() |> Async.Start))
    member _.DisconnectNetworkCommand = RelayCommand(Action(fun () -> this.DisconnectContainer() |> Async.Start))

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
