namespace Diplo.Gui.ViewModels

open System
open System.Windows.Input
open Diplo.Core
open Diplo.Core.Output

/// Onglet « Paramètres » : lecture et édition de la configuration client `diplo.json`
/// (adresses des services conteneurs, volumes et réseaux).
type SettingsTabViewModel(outputPort: IOutputPort) as this =
    inherit ViewModelBase()

    let mutable containerAddress = ""
    let mutable volumeAddress = ""
    let mutable networkAddress = ""
    let mutable statusMessage = ""

    let saveCmd = RelayCommand(Action(fun () ->
        if String.IsNullOrWhiteSpace containerAddress
           || String.IsNullOrWhiteSpace volumeAddress
           || String.IsNullOrWhiteSpace networkAddress then
            statusMessage <- "Les trois adresses sont obligatoires."
            this.OnPropertyChanged(nameof this.StatusMessage)
        else
            Cmd.runSyncWith outputPort (fun msg ->
                statusMessage <- "Erreur : " + msg
                this.OnPropertyChanged(nameof this.StatusMessage)) (fun () ->
                DiploConfig.save this.ConfigPath
                    (containerAddress.Trim()) (volumeAddress.Trim()) (networkAddress.Trim())
                DiploConfig.invalidate()
                statusMessage <- sprintf "Configuration enregistrée (%s) — appliquée aux prochaines opérations." this.ConfigPath
                outputPort.WriteLine(sprintf "Configuration client enregistrée dans %s" this.ConfigPath))
            this.OnPropertyChanged(nameof this.StatusMessage)))

    let reloadCmd = RelayCommand(Action(fun () ->
        this.Reload()
        DiploConfig.invalidate()
        statusMessage <- "Configuration relue depuis le disque."
        this.OnPropertyChanged(nameof this.StatusMessage)))

    do this.Reload()

    member _.ConfigPath = DiploConfig.configPath()

    member _.ContainerAddress
        with get () = containerAddress
        and set value = containerAddress <- value; this.OnPropertyChanged()

    member _.VolumeAddress
        with get () = volumeAddress
        and set value = volumeAddress <- value; this.OnPropertyChanged()

    member _.NetworkAddress
        with get () = networkAddress
        and set value = networkAddress <- value; this.OnPropertyChanged()

    member _.StatusMessage
        with get () = statusMessage
        and set value = statusMessage <- value; this.OnPropertyChanged()

    member private this.Reload() =
        let c, v, n = DiploConfig.load this.ConfigPath
        containerAddress <- defaultArg c "localhost:5001"
        volumeAddress <- defaultArg v "localhost:5002"
        networkAddress <- defaultArg n "localhost:5003"
        this.OnPropertyChanged(nameof this.ContainerAddress)
        this.OnPropertyChanged(nameof this.VolumeAddress)
        this.OnPropertyChanged(nameof this.NetworkAddress)

    member _.SaveCommand: ICommand = saveCmd
    member _.ReloadCommand: ICommand = reloadCmd
