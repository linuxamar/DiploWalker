namespace DiploWalker.Gui.Tests

open System
open Xunit
open FsUnit.Xunit
open DiploWalker.Gui.ViewModels

/// Tests pour le VolumeViewModel.
type VolumeViewModelTests() =

    [<Fact>]
    member _.``Volumes n'est pas null``() =
        let vm = new VolumeViewModel()
        vm.Volumes |> should not' (equal null)

    [<Fact>]
    member _.``SelectedVolume par défaut est None``() =
        let vm = new VolumeViewModel()
        vm.SelectedVolume |> should equal None

    [<Fact>]
    member _.``StatusMessage par défaut est vide``() =
        let vm = new VolumeViewModel()
        vm.StatusMessage |> should equal ""

    [<Fact>]
    member _.``Refresh ne lève pas d'exception``() =
        let vm = new VolumeViewModel()
        (fun () -> vm.Refresh().GetAwaiter().GetResult() |> ignore) |> should not' (throw typeof<System.Exception>)

    [<Fact>]
    member _.``RemoveCommand n'est pas null``() =
        let vm = new VolumeViewModel()
        vm.RemoveCommand |> should not' (equal null)

    [<Fact>]
    member _.``RefreshCommand n'est pas null``() =
        let vm = new VolumeViewModel()
        vm.RefreshCommand |> should not' (equal null)
