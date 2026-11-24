namespace DiploWalker.Gui.Tests

open System
open Xunit
open FsUnit.Xunit
open DiploWalker.Gui.ViewModels

/// Tests pour le ContainerViewModel.
type ContainerViewModelTests() =

    [<Fact>]
    member _.``Containers n'est pas null``() =
        let vm = new ContainerViewModel()
        vm.Containers |> should not' (equal null)

    [<Fact>]
    member _.``SelectedContainer par défaut est None``() =
        let vm = new ContainerViewModel()
        vm.SelectedContainer |> should equal None

    [<Fact>]
    member _.``StatusMessage par défaut est vide``() =
        let vm = new ContainerViewModel()
        vm.StatusMessage |> should equal ""

    [<Fact>]
    member _.``Refresh ne lève pas d'exception``() =
        let vm = new ContainerViewModel()
        (fun () -> vm.Refresh().GetAwaiter().GetResult() |> ignore) |> should not' (throw typeof<System.Exception>)

    [<Fact>]
    member _.``StartCommand n'est pas null``() =
        let vm = new ContainerViewModel()
        vm.StartCommand |> should not' (equal null)

    [<Fact>]
    member _.``StopCommand n'est pas null``() =
        let vm = new ContainerViewModel()
        vm.StopCommand |> should not' (equal null)

    [<Fact>]
    member _.``DeleteCommand n'est pas null``() =
        let vm = new ContainerViewModel()
        vm.DeleteCommand |> should not' (equal null)

    [<Fact>]
    member _.``RefreshCommand n'est pas null``() =
        let vm = new ContainerViewModel()
        vm.RefreshCommand |> should not' (equal null)
