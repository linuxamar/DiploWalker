module Diplo.Gui.Tests.ViewModelBaseTests

open System
open System.Windows.Input
open Xunit
open FsUnit.Xunit
open Diplo.Gui.ViewModels

// ── RelayCommand ──────────────────────────────────────────────

[<Fact>]
let ``RelayCommand Action sans paramètre exécute l'action`` () =
    let mutable called = false
    let cmd = RelayCommand(Action(fun () -> called <- true))
    (cmd :> ICommand).Execute(null)
    called |> should be True

[<Fact>]
let ``RelayCommand Func canExecute retourne false`` () =
    let cmd = RelayCommand(Action(fun () -> ()), Func<bool>(fun () -> false))
    (cmd :> ICommand).CanExecute(null) |> should be False

[<Fact>]
let ``RelayCommand Func canExecute retourne true`` () =
    let cmd = RelayCommand(Action(fun () -> ()), Func<bool>(fun () -> true))
    (cmd :> ICommand).CanExecute(null) |> should be True

[<Fact>]
let ``RelayCommand Func paramètre évalue le prédicat`` () =
    let cmd = RelayCommand(Func<obj, bool>(fun o -> (o :?> int) > 10))
    (cmd :> ICommand).CanExecute(5) |> should be False
    (cmd :> ICommand).CanExecute(20) |> should be True

[<Fact>]
let ``RaiseCanExecuteChanged déclenche l'événement`` () =
    let cmd = RelayCommand(Action(fun () -> ()))
    let mutable triggered = false
    (cmd :> ICommand).CanExecuteChanged.AddHandler(EventHandler(fun _ _ -> triggered <- true))
    cmd.RaiseCanExecuteChanged()
    triggered |> should be True

[<Fact>]
let ``RelayCommand sans canExecute personnalisé autorise toujours`` () =
    let cmd = RelayCommand(Action(fun () -> ()))
    (cmd :> ICommand).CanExecute(null) |> should be True

// ── ViewModelBase ─────────────────────────────────────────────

type TestViewModel() =
    inherit ViewModelBase()

    member this.TriggerPropertyChanged([<System.Runtime.CompilerServices.CallerMemberName>] ?name) =
        this.OnPropertyChanged(?name = name)

[<Fact>]
let ``ViewModelBasePropertyChanged se déclenche`` () =
    let vm = TestViewModel()
    let mutable changedProps = []
    vm.PropertyChanged.Add(fun e -> changedProps <- e.PropertyName :: changedProps)
    vm.TriggerPropertyChanged("TestProp")
    changedProps |> should contain "TestProp"

[<Fact>]
let ``ViewModelBase implémente INotifyPropertyChanged`` () =
    let vm = TestViewModel()
    let inpc = vm :> System.ComponentModel.INotifyPropertyChanged
    let mutable triggered = false
    inpc.PropertyChanged.AddHandler(System.ComponentModel.PropertyChangedEventHandler(fun _ _ -> triggered <- true))
    vm.TriggerPropertyChanged("X")
    triggered |> should be True

[<Fact>]
let ``ViewModelBase plusieurs notifications`` () =
    let vm = TestViewModel()
    let mutable count = 0
    vm.PropertyChanged.Add(fun _ -> count <- count + 1)
    vm.TriggerPropertyChanged("A")
    vm.TriggerPropertyChanged("B")
    vm.TriggerPropertyChanged("C")
    count |> should equal 3
