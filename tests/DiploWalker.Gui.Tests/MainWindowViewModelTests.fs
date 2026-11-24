namespace DiploWalker.Gui.Tests

open System
open Xunit
open FsUnit.Xunit
open DiploWalker.Gui.ViewModels

/// Tests pour le MainWindowViewModel.
type MainWindowViewModelTests() =

    [<Fact>]
    member _.``SelectedTabIndex par défaut est 0``() =
        let vm = new MainWindowViewModel()
        vm.SelectedTabIndex |> should equal 0

    [<Fact>]
    member _.``WindowTitle par défaut est DiploWalker GUI``() =
        let vm = new MainWindowViewModel()
        vm.WindowTitle |> should equal "DiploWalker GUI"

    [<Fact>]
    member _.``SelectedTabIndex peut être modifié``() =
        let vm = new MainWindowViewModel()
        vm.SelectedTabIndex <- 3
        vm.SelectedTabIndex |> should equal 3

    [<Fact>]
    member _.``WindowTitle peut être modifié``() =
        let vm = new MainWindowViewModel()
        vm.WindowTitle <- "Test"
        vm.WindowTitle |> should equal "Test"

    [<Fact>]
    member _.``ContainerVm n'est pas null``() =
        let vm = new MainWindowViewModel()
        vm.ContainerVm |> should not' (equal null)

    [<Fact>]
    member _.``VolumeVm n'est pas null``() =
        let vm = new MainWindowViewModel()
        vm.VolumeVm |> should not' (equal null)

    [<Fact>]
    member _.``NetworkVm n'est pas null``() =
        let vm = new MainWindowViewModel()
        vm.NetworkVm |> should not' (equal null)

    [<Fact>]
    member _.``DiskVm n'est pas null``() =
        let vm = new MainWindowViewModel()
        vm.DiskVm |> should not' (equal null)

    [<Fact>]
    member _.``ComposeVm n'est pas null``() =
        let vm = new MainWindowViewModel()
        vm.ComposeVm |> should not' (equal null)

    [<Fact>]
    member _.``CatalogVm n'est pas null``() =
        let vm = new MainWindowViewModel()
        vm.CatalogVm |> should not' (equal null)

    [<Fact>]
    member _.``LogVm n'est pas null``() =
        let vm = new MainWindowViewModel()
        vm.LogVm |> should not' (equal null)

    [<Fact>]
    member _.``RefreshCommand n'est pas null``() =
        let vm = new MainWindowViewModel()
        vm.RefreshCommand |> should not' (equal null)

    [<Fact>]
    member _.``Initialize ne lève pas d'exception``() =
        let vm = new MainWindowViewModel()
        (fun () -> vm.Initialize() |> ignore) |> should not' (throw typeof<System.Exception>)
