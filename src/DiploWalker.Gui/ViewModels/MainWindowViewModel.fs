namespace DiploWalker.Gui.ViewModels

open System
open System.Reactive.Linq
open ReactiveUI

/// ViewModel principal : gère la navigation par onglets et contient
/// tous les ViewModels enfants.
type MainWindowViewModel() =
    inherit ViewModelBase()

    // ViewModels enfants (instanciés paresseusement)
    let containerVm = lazy (new ContainerViewModel())
    let volumeVm = lazy (new VolumeViewModel())
    let networkVm = lazy (new NetworkViewModel())
    let diskVm = lazy (new DiskViewModel())
    let composeVm = lazy (new ComposeViewModel())
    let catalogVm = lazy (new CatalogViewModel())
    let logVm = lazy (new LogViewModel())

    // Onglet sélectionné (0 = Container, 1 = Volume, etc.)
    let mutable selectedTabIndex = 0
    let mutable windowTitle = "DiploWalker GUI"

    /// ViewModel des conteneurs (onglet 0)
    member this.ContainerVm = containerVm.Force()

    /// ViewModel des volumes (onglet 1)
    member this.VolumeVm = volumeVm.Force()

    /// ViewModel des réseaux (onglet 2)
    member this.NetworkVm = networkVm.Force()

    /// ViewModel des disques (onglet 3)
    member this.DiskVm = diskVm.Force()

    /// ViewModel de Compose (onglet 4)
    member this.ComposeVm = composeVm.Force()

    /// ViewModel du catalogue (onglet 5)
    member this.CatalogVm = catalogVm.Force()

    /// ViewModel du journal (onglet 6)
    member this.LogVm = logVm.Force()

    /// Index de l'onglet sélectionné
    member this.SelectedTabIndex
        with get() = selectedTabIndex
        and set(v) =
            selectedTabIndex <- v
            this.RaisePropertyChanged("SelectedTabIndex") |> ignore
            // Optionnel : charger l'onglet au changement
            match v with
            | 0 -> this.ContainerVm.Refresh() |> ignore
            | 1 -> this.VolumeVm.Refresh() |> ignore
            | 2 -> this.NetworkVm.Refresh() |> ignore
            | 3 -> this.DiskVm.Refresh() |> ignore
            | 4 -> this.ComposeVm.Refresh() |> ignore
            | 5 -> if this.CatalogVm.Query <> String.Empty then this.CatalogVm.Search() |> ignore
            | 6 -> () // LogView n'a pas de refresh
            | _ -> ()

    /// Titre de la fenêtre (peut afficher l'état de connexion)
    member this.WindowTitle
        with get() = windowTitle
        and set(v) =
            windowTitle <- v
            this.RaisePropertyChanged("WindowTitle") |> ignore

    /// Commande de rafraîchissement global (Ctrl+R)
    member this.RefreshCommand =
        ReactiveCommand.Create(fun () ->
            match this.SelectedTabIndex with
            | 0 -> this.ContainerVm.Refresh() |> ignore
            | 1 -> this.VolumeVm.Refresh() |> ignore
            | 2 -> this.NetworkVm.Refresh() |> ignore
            | 3 -> this.DiskVm.Refresh() |> ignore
            | 4 -> this.ComposeVm.Refresh() |> ignore
            | 5 -> if this.CatalogVm.Query <> String.Empty then this.CatalogVm.Search() |> ignore
            | _ -> ()
        )

    /// Initialisation : connexion aux services, chargement initial
    member this.Initialize() =
        this.LogDebug "Initialisation de MainWindowViewModel"
        // Charger l'onglet par défaut (Container)
        this.ContainerVm.Refresh() |> ignore
        this.WindowTitle <- "DiploWalker GUI - Connecté"
