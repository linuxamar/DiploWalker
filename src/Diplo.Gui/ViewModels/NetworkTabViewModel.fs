namespace Diplo.Gui.ViewModels

open System
open System.Collections.ObjectModel
open Avalonia.Threading
open Diplo.Core.Clients
open Diplo.Core.Output
open Diplo.Grpc.Network

type NetworkDisplayInfo = {
    Id: string
    Nom: string
    Driver: string
    SousReseau: string
    Passerelle: string
    CrééLe: string
}

type NetworkTabViewModel(outputPort: IOutputPort) as this =
    inherit ViewModelBase()

    let networks = ObservableCollection<NetworkDisplayInfo>()

    let mutable networkIdInput = ""
    let mutable networkNameInput = ""
    let mutable networkDriver = "bridge"
    let mutable networkSubnet = ""
    let mutable networkGateway = ""
    let mutable networkEndpointId = ""
    let mutable networkContainerId = ""
    let mutable networkIpv4 = ""
    let mutable networkForce = false
    let mutable networkCniPluginPath = ""
    let mutable networkCniCommand = "ADD"
    let mutable networkNetnsPath = ""

    member _.Networks = networks

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

    member _.ListNetworksCommand = RelayCommand(Action(fun () -> this.ListNetworks() |> Async.Start))
    member _.InspectNetworkCommand = RelayCommand(Action(fun () -> this.InspectNetwork() |> Async.Start))
    member _.CreateNetworkCommand = RelayCommand(Action(fun () -> this.CreateNetwork() |> Async.Start))
    member _.RemoveNetworkCommand = RelayCommand(Action(fun () -> this.RemoveNetwork() |> Async.Start))
    member _.ConnectNetworkCommand = RelayCommand(Action(fun () -> this.ConnectContainer() |> Async.Start))
    member _.DisconnectNetworkCommand = RelayCommand(Action(fun () -> this.DisconnectContainer() |> Async.Start))
    member _.RunCniPluginCommand = RelayCommand(Action(fun () -> this.RunCniPlugin() |> Async.Start))
    member _.PruneNetworksCommand = RelayCommand(Action(fun () -> this.PruneNetworks() |> Async.Start))

    member private this.ListNetworks() =
        async {
            try
                use client = new NetworkClient()
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
                outputPort.WriteSuccess(sprintf "%d réseau(x) trouvé(s)" response.Networks.Count)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.InspectNetwork() =
        async {
            try
                use client = new NetworkClient()
                let! response = client.InspectAsync(id = this.NetworkIdInput) |> Async.AwaitTask
                outputPort.WriteLine(sprintf "ID: %s" response.Id)
                outputPort.WriteLine(sprintf "Nom: %s" response.Name)
                outputPort.WriteLine(sprintf "Driver: %s" (response.Driver.ToString()))
                outputPort.WriteLine(sprintf "Sous-réseau: %s" response.Subnet)
                outputPort.WriteLine(sprintf "Passerelle: %s" response.Gateway)
                if response.Endpoints.Count > 0 then
                    outputPort.WriteLine(sprintf "Points de connexion: %d" response.Endpoints.Count)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.CreateNetwork() =
        async {
            try
                use client = new NetworkClient()
                let driverEnum =
                    match this.NetworkDriver.ToLowerInvariant() with
                    | "bridge" -> Diplo.Grpc.Network.NetworkDriver.Bridge
                    | "none" -> Diplo.Grpc.Network.NetworkDriver.None
                    | "pod" -> Diplo.Grpc.Network.NetworkDriver.Pod
                    | "cni" -> Diplo.Grpc.Network.NetworkDriver.CustomCni
                    | _ -> Diplo.Grpc.Network.NetworkDriver.Bridge
                let! response = client.CreateAsync(this.NetworkNameInput, driver = driverEnum, subnet = this.NetworkSubnet, gateway = this.NetworkGateway) |> Async.AwaitTask
                outputPort.WriteSuccess(sprintf "Réseau %s créé (ID: %s)" this.NetworkNameInput response.Id)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.RemoveNetwork() =
        async {
            try
                use client = new NetworkClient()
                let! response = client.RemoveAsync(id = this.NetworkIdInput, force = this.NetworkForce) |> Async.AwaitTask
                if response.Success then
                    outputPort.WriteSuccess(sprintf "Réseau %s supprimé" this.NetworkIdInput)
                else
                    outputPort.WriteWarning(response.Message)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.ConnectContainer() =
        async {
            try
                use client = new NetworkClient()
                let! response = client.ConnectAsync(this.NetworkIdInput, this.NetworkContainerId, endpointId = this.NetworkEndpointId, ipv4Address = this.NetworkIpv4) |> Async.AwaitTask
                outputPort.WriteSuccess(sprintf "Conteneur %s connecté au réseau %s - %s" this.NetworkContainerId this.NetworkIdInput response.Message)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.DisconnectContainer() =
        async {
            try
                use client = new NetworkClient()
                let! response = client.DisconnectAsync(this.NetworkIdInput, this.NetworkContainerId, endpointId = this.NetworkEndpointId, force = this.NetworkForce) |> Async.AwaitTask
                if response.Success then
                    outputPort.WriteSuccess(sprintf "Conteneur %s déconnecté du réseau %s" this.NetworkContainerId this.NetworkIdInput)
                else
                    outputPort.WriteWarning(response.Message)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.RunCniPlugin() =
        async {
            try
                use client = new NetworkClient()
                let! response = client.RunCniPluginAsync(pluginPath = this.NetworkCniPluginPath, command = this.NetworkCniCommand, containerId = this.NetworkContainerId, netnsPath = this.NetworkNetnsPath) |> Async.AwaitTask
                if response.Success then
                    outputPort.WriteSuccess(sprintf "Plugin CNI exécuté - %s" response.Message)
                else
                    outputPort.WriteWarning(response.Message)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.PruneNetworks() =
        async {
            try
                use client = new NetworkClient()
                let! response = client.PruneNetworksAsync() |> Async.AwaitTask
                outputPort.WriteSuccess(sprintf "Réseaux nettoyés - %s" response.Message)
            with ex -> outputPort.WriteError(ex.Message)
        }
