namespace DiploWalker.Gui.Tests

open System
open Xunit
open FsUnit.Xunit
open DiploWalker.Gui.ViewModels

/// Tests pour le NetworkViewModel.
type NetworkViewModelTests() =

    [<Fact>]
    member _.``Networks n'est pas null``() =
        let vm = new NetworkViewModel()
        vm.Networks |> should not' (equal null)

    [<Fact>]
    member _.``SelectedNetwork par défaut est None``() =
        let vm = new NetworkViewModel()
        vm.SelectedNetwork |> should equal None

    [<Fact>]
    member _.``StatusMessage par défaut est vide``() =
        let vm = new NetworkViewModel()
        vm.StatusMessage |> should equal ""

    [<Fact>]
    member _.``Refresh ne lève pas d'exception``() =
        let vm = new NetworkViewModel()
        (fun () -> vm.Refresh().GetAwaiter().GetResult() |> ignore) |> should not' (throw typeof<System.Exception>)

    [<Fact>]
    member _.``RemoveCommand n'est pas null``() =
        let vm = new NetworkViewModel()
        vm.RemoveCommand |> should not' (equal null)

    [<Fact>]
    member _.``RefreshCommand n'est pas null``() =
        let vm = new NetworkViewModel()
        vm.RefreshCommand |> should not' (equal null)
