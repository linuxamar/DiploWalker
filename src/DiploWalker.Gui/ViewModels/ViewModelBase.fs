namespace DiploWalker.Gui.ViewModels

open ReactiveUI

/// Classe de base pour tous les ViewModels : hérite de ReactiveObject
/// (INotifyPropertyChanged réactif via ReactiveUI).
type ViewModelBase() =
    inherit ReactiveObject()

    /// Indique si le ViewModel est en cours de chargement (pour désactiver
    /// les commandes pendant les opérations asynchrones).
    let mutable isLoading = false

    member this.IsLoading
        with get() = isLoading
        and set(v) =
            isLoading <- v
            this.RaisePropertyChanged("IsLoading") |> ignore

    /// Log simple pour le débogage (pas de dépendance externe).
    member _.LogDebug (msg: string) =
        System.Diagnostics.Debug.WriteLine(sprintf "[Gui] %s" msg)
