namespace Diplo.Gui.ViewModels

open System
open System.Collections.ObjectModel
open Avalonia.Threading
open Diplo.Core.Clients
open Diplo.Grpc
open Diplo.Core.Output
open Diplo.Grpc.Network

type NetworkDisplayInfo =
    { Id: string
      Nom: string
      Driver: string
      SousReseau: string
      Passerelle: string
      CrééLe: string }

type NetworkTabViewModel(outputPort: IOutputPort, ?networkClientFactory: unit -> INetworkClient) as this =
    inherit ViewModelBase()

    let networks = ObservableCollection<NetworkDisplayInfo>()

    let networkClient =
        let factory = defaultArg networkClientFactory (fun () -> new NetworkClient() :> INetworkClient)
        factory ()

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

    let listNetworksCmd = RelayCommand(Action(fun () -> this.ListNetworks() |> ignore))

    let inspectNetworkCmd =
        RelayCommand(Action(fun () -> this.InspectNetwork() |> ignore))

    let createNetworkCmd =
        RelayCommand(Action(fun () -> this.CreateNetwork() |> ignore))

    let removeNetworkCmd =
        RelayCommand(Action(fun () -> this.RemoveNetwork() |> ignore))

    let connectNetworkCmd =
        RelayCommand(Action(fun () -> this.ConnectContainer() |> ignore))

    let disconnectNetworkCmd =
        RelayCommand(Action(fun () -> this.DisconnectContainer() |> ignore))

    let runCniPluginCmd = RelayCommand(Action(fun () -> this.RunCniPlugin() |> ignore))

    let pruneNetworksCmd =
        RelayCommand(Action(fun () -> this.PruneNetworks() |> ignore))

    member _.Networks = networks

    member _.NetworkIdInput
        with get () = networkIdInput
        and set v =
            networkIdInput <- v
            this.OnPropertyChanged()

    member _.NetworkNameInput
        with get () = networkNameInput
        and set v =
            networkNameInput <- v
            this.OnPropertyChanged()

    member _.NetworkDriver
        with get () = networkDriver
        and set v =
            networkDriver <- v
            this.OnPropertyChanged()

    member _.NetworkSubnet
        with get () = networkSubnet
        and set v =
            networkSubnet <- v
            this.OnPropertyChanged()

    member _.NetworkGateway
        with get () = networkGateway
        and set v =
            networkGateway <- v
            this.OnPropertyChanged()

    member _.NetworkEndpointId
        with get () = networkEndpointId
        and set v =
            networkEndpointId <- v
            this.OnPropertyChanged()

    member _.NetworkContainerId
        with get () = networkContainerId
        and set v =
            networkContainerId <- v
            this.OnPropertyChanged()

    member _.NetworkIpv4
        with get () = networkIpv4
        and set v =
            networkIpv4 <- v
            this.OnPropertyChanged()

    member _.NetworkForce
        with get () = networkForce
        and set v =
            networkForce <- v
            this.OnPropertyChanged()

    member _.NetworkCniPluginPath
        with get () = networkCniPluginPath
        and set v =
            networkCniPluginPath <- v
            this.OnPropertyChanged()

    member _.NetworkCniCommand
        with get () = networkCniCommand
        and set v =
            networkCniCommand <- v
            this.OnPropertyChanged()

    member _.NetworkNetnsPath
        with get () = networkNetnsPath
        and set v =
            networkNetnsPath <- v
            this.OnPropertyChanged()

    member _.ListNetworksCommand = listNetworksCmd
    member _.InspectNetworkCommand = inspectNetworkCmd
    member _.CreateNetworkCommand = createNetworkCmd
    member _.RemoveNetworkCommand = removeNetworkCmd
    member _.ConnectNetworkCommand = connectNetworkCmd
    member _.DisconnectNetworkCommand = disconnectNetworkCmd
    member _.RunCniPluginCommand = runCniPluginCmd
    member _.PruneNetworksCommand = pruneNetworksCmd

    member private this.ListNetworks() =
        Cmd.run outputPort (fun () ->
            task {
                let! response = networkClient.ListAsync()

                Dispatcher.UIThread.Post(fun () ->
                    networks.Clear()

                    for n in response.Networks do
                        networks.Add(
                            { Id = n.Id
                              Nom = n.Name
                              Driver = n.Driver.ToString()
                              SousReseau = n.Subnet
                              Passerelle = n.Gateway
                              CrééLe = n.CreatedAt }
                        ))

                outputPort.WriteSuccess(sprintf "%d réseau(x) trouvé(s)" response.Networks.Count)
            })

    member private this.InspectNetwork() =
        Cmd.run outputPort (fun () ->
            task {
                let! response = networkClient.InspectAsync(id = this.NetworkIdInput)
                outputPort.WriteLine(sprintf "ID: %s" response.Id)
                outputPort.WriteLine(sprintf "Nom: %s" response.Name)
                outputPort.WriteLine(sprintf "Driver: %s" (response.Driver.ToString()))
                outputPort.WriteLine(sprintf "Sous-réseau: %s" response.Subnet)
                outputPort.WriteLine(sprintf "Passerelle: %s" response.Gateway)

                if response.Endpoints.Count > 0 then
                    outputPort.WriteLine(sprintf "Points de connexion: %d" response.Endpoints.Count)
            })

    member private this.CreateNetwork() =
        Cmd.run outputPort (fun () ->
            task {
                let driverEnum = DriverMappings.parseNetworkDriver this.NetworkDriver

                let! response =
                    networkClient.CreateAsync(
                        this.NetworkNameInput,
                        driver = driverEnum,
                        subnet = this.NetworkSubnet,
                        gateway = this.NetworkGateway
                    )

                outputPort.WriteSuccess(sprintf "Réseau %s créé (ID: %s)" this.NetworkNameInput response.Id)
            })

    member private this.RemoveNetwork() =
        Cmd.run outputPort (fun () ->
            task {
                let! response = networkClient.RemoveAsync(id = this.NetworkIdInput, force = this.NetworkForce)

                if response.Success then
                    outputPort.WriteSuccess(sprintf "Réseau %s supprimé" this.NetworkIdInput)
                else
                    outputPort.WriteWarning(response.Message)
            })

    member private this.ConnectContainer() =
        Cmd.run outputPort (fun () ->
            task {
                let! response =
                    networkClient.ConnectAsync(
                        this.NetworkIdInput,
                        this.NetworkContainerId,
                        endpointId = this.NetworkEndpointId,
                        ipv4Address = this.NetworkIpv4
                    )

                outputPort.WriteSuccess(
                    sprintf
                        "Conteneur %s connecté au réseau %s - %s"
                        this.NetworkContainerId
                        this.NetworkIdInput
                        response.Message
                )
            })

    member private this.DisconnectContainer() =
        Cmd.run outputPort (fun () ->
            task {
                let! response =
                    networkClient.DisconnectAsync(
                        this.NetworkIdInput,
                        this.NetworkContainerId,
                        endpointId = this.NetworkEndpointId,
                        force = this.NetworkForce
                    )

                if response.Success then
                    outputPort.WriteSuccess(
                        sprintf "Conteneur %s déconnecté du réseau %s" this.NetworkContainerId this.NetworkIdInput
                    )
                else
                    outputPort.WriteWarning(response.Message)
            })

    member private this.RunCniPlugin() =
        Cmd.run outputPort (fun () ->
            task {
                let! response =
                    networkClient.RunCniPluginAsync(
                        pluginPath = this.NetworkCniPluginPath,
                        command = this.NetworkCniCommand,
                        containerId = this.NetworkContainerId,
                        netnsPath = this.NetworkNetnsPath
                    )

                if response.Success then
                    outputPort.WriteSuccess(sprintf "Plugin CNI exécuté - %s" response.Message)
                else
                    outputPort.WriteWarning(response.Message)
            })

    member private this.PruneNetworks() =
        Cmd.run outputPort (fun () ->
            task {
                let! response = networkClient.PruneNetworksAsync()
                outputPort.WriteSuccess(sprintf "Réseaux nettoyés - %s" response.Message)
            })

    interface IDisposable with
        member _.Dispose() = (networkClient :> IDisposable).Dispose()
