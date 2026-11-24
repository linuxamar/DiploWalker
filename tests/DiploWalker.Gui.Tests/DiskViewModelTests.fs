namespace DiploWalker.Gui.Tests

open System
open Xunit
open FsUnit.Xunit
open DiploWalker.Gui.ViewModels

/// Tests pour le DiskViewModel.
type DiskViewModelTests() =

    [<Fact>]
    member _.``StatusMessage par défaut est vide``() =
        let vm = new DiskViewModel()
        vm.StatusMessage |> should equal ""

    [<Fact>]
    member _.``SourceDir par défaut est vide``() =
        let vm = new DiskViewModel()
        vm.SourceDir |> should equal ""

    [<Fact>]
    member _.``DestPath par défaut est vide``() =
        let vm = new DiskViewModel()
        vm.DestPath |> should equal ""

    [<Fact>]
    member _.``SelectedFormat par défaut est raw``() =
        let vm = new DiskViewModel()
        vm.SelectedFormat |> should equal "raw"

    [<Fact>]
    member _.``Formats contient les formats attendus``() =
        let vm = new DiskViewModel()
        vm.Formats |> should contain "raw"
        vm.Formats |> should contain "vhd"
        vm.Formats |> should contain "vhdx"
        vm.Formats |> should contain "vmdk"
        vm.Formats |> should contain "vdi"

    [<Fact>]
    member _.``Refresh ne lève pas d'exception``() =
        let vm = new DiskViewModel()
        (fun () -> vm.Refresh().GetAwaiter().GetResult() |> ignore) |> should not' (throw typeof<System.Exception>)

    [<Fact>]
    member _.``CreateCommand n'est pas null``() =
        let vm = new DiskViewModel()
        vm.CreateCommand |> should not' (equal null)

    [<Fact>]
    member _.``ExtractCommand n'est pas null``() =
        let vm = new DiskViewModel()
        vm.ExtractCommand |> should not' (equal null)
