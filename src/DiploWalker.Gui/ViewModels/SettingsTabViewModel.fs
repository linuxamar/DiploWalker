namespace DiploWalker.Gui.ViewModels

open System
open System.Windows.Input
open DiploWalker.Core
open DiploWalker.Core.Output
open DiploWalker.Abstractions

/// Onglet « Paramètres » : lecture et édition de la configuration client `DiploWalker.json`
/// (adresses des services conteneurs, volumes et réseaux).
type SettingsTabViewModel(outputPort: IOutputPort) as this =
    inherit ViewModelBase()

    let mutable containerAddress = ""
    let mutable volumeAddress = ""
    let mutable networkAddress = ""
    let mutable statusMessage = ""

    let saveCmd =
        RelayCommand(
            Action(fun () ->
                if
                    String.IsNullOrWhiteSpace containerAddress
                    || String.IsNullOrWhiteSpace volumeAddress
                    || String.IsNullOrWhiteSpace networkAddress
                then
                    statusMessage <- "Les trois adresses sont obligatoires."
                    this.OnPropertyChanged(nameof this.StatusMessage)
                else
                    Cmd.runSyncWith
                        outputPort
                        (fun msg ->
                            statusMessage <- "Erreur : " + msg
                            this.OnPropertyChanged(nameof this.StatusMessage))
                        (fun () ->
                            DiploWalkerConfig.save
                                this.ConfigPath
                                (containerAddress.Trim())
                                (volumeAddress.Trim())
                                (networkAddress.Trim())

                            DiploWalkerConfig.invalidate ()

                            statusMessage <-
                                sprintf
                                    "Configuration enregistrée (%s) — appliquée aux prochaines opérations."
                                    this.ConfigPath

                            outputPort.WriteLine(sprintf "Configuration client enregistrée dans %s" this.ConfigPath))

                    this.OnPropertyChanged(nameof this.StatusMessage))
        )

    let reloadCmd =
        RelayCommand(
            Action(fun () ->
                this.Reload()
                DiploWalkerConfig.invalidate ()
                statusMessage <- "Configuration relue depuis le disque."
                this.OnPropertyChanged(nameof this.StatusMessage))
        )

    do this.Reload()

    member _.ConfigPath = DiploWalkerConfig.configPath ()

    member _.ContainerAddress
        with get () = containerAddress
        and set value =
            containerAddress <- value
            this.OnPropertyChanged()

    member _.VolumeAddress
        with get () = volumeAddress
        and set value =
            volumeAddress <- value
            this.OnPropertyChanged()

    member _.NetworkAddress
        with get () = networkAddress
        and set value =
            networkAddress <- value
            this.OnPropertyChanged()

    member _.StatusMessage
        with get () = statusMessage
        and set value =
            statusMessage <- value
            this.OnPropertyChanged()

    member private this.Reload() =
        let c, v, n = DiploWalkerConfig.load this.ConfigPath

        // Défauts alignés sur la configuration de build (DiploWalkerPorts) :
        // Debug 5001-5003, Release 6001-6003.
        containerAddress <- defaultArg c (sprintf "localhost:%d" DiploWalkerPorts.Container)
        volumeAddress <- defaultArg v (sprintf "localhost:%d" DiploWalkerPorts.Volume)
        networkAddress <- defaultArg n (sprintf "localhost:%d" DiploWalkerPorts.Network)
        this.OnPropertyChanged(nameof this.ContainerAddress)
        this.OnPropertyChanged(nameof this.VolumeAddress)
        this.OnPropertyChanged(nameof this.NetworkAddress)

    member _.SaveCommand: ICommand = saveCmd
    member _.ReloadCommand: ICommand = reloadCmd

    interface IDisposable with
        member _.Dispose() = ()



