namespace Diplo.Gui.ViewModels

open System
open System.ComponentModel
open System.Runtime.CompilerServices
open System.Windows.Input

type RelayCommand(execute: Action<obj>, canExecute: Func<obj, bool>) =

    let canExecuteChanged = Event<EventHandler, EventArgs>()

    new(execute: Action) = RelayCommand(Action<obj>(fun _ -> execute.Invoke()), Func<obj, bool>(fun _ -> true))

    new(execute: Func<obj, bool>) = RelayCommand(Action<obj>(fun o -> ignore (execute.Invoke(o))), execute)

    new(execute: Action, canExecute: Func<bool>) =
        RelayCommand(Action<obj>(fun _ -> execute.Invoke()), Func<obj, bool>(fun _ -> canExecute.Invoke()))

    member _.RaiseCanExecuteChanged() =
        canExecuteChanged.Trigger(null, EventArgs.Empty)

    interface ICommand with
        member _.CanExecute(param) = canExecute.Invoke(param)
        member _.Execute(param) = execute.Invoke(param)

        [<CLIEvent>]
        member _.CanExecuteChanged = canExecuteChanged.Publish

[<AbstractClass>]
type ViewModelBase() =

    let propertyChanged = Event<PropertyChangedEventHandler, PropertyChangedEventArgs>()

    member this.OnPropertyChanged([<CallerMemberName>] ?name: string) =
        name
        |> Option.iter (fun n -> propertyChanged.Trigger(this, PropertyChangedEventArgs(n)))

    [<CLIEvent>]
    member _.PropertyChanged = propertyChanged.Publish

    interface INotifyPropertyChanged with
        [<CLIEvent>]
        member _.PropertyChanged = propertyChanged.Publish
