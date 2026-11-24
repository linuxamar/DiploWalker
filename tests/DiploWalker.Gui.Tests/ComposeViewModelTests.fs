namespace DiploWalker.Gui.Tests

open System
open Xunit
open FsUnit.Xunit
open DiploWalker.Gui.ViewModels

/// Tests pour le ComposeViewModel.
type ComposeViewModelTests() =

    [<Fact>]
    member _.``StatusMessage par défaut est vide``() =
        let vm = new ComposeViewModel()
        vm.StatusMessage |> should equal ""

    [<Fact>]
    member _.``ComposeFile par défaut est vide``() =
        let vm = new ComposeViewModel()
        vm.ComposeFile |> should equal ""

    [<Fact>]
    member _.``Services n'est pas null``() =
        let vm = new ComposeViewModel()
        vm.Services |> should not' (equal null)

    [<Fact>]
    member _.``Refresh ne lève pas d'exception``() =
        let vm = new ComposeViewModel()
        (fun () -> vm.Refresh().GetAwaiter().GetResult() |> ignore) |> should not' (throw typeof<System.Exception>)

    [<Fact>]
    member _.``UpCommand n'est pas null``() =
        let vm = new ComposeViewModel()
        vm.UpCommand |> should not' (equal null)

    [<Fact>]
    member _.``DownCommand n'est pas null``() =
        let vm = new ComposeViewModel()
        vm.DownCommand |> should not' (equal null)

    [<Fact>]
    member _.``RefreshCommand n'est pas null``() =
        let vm = new ComposeViewModel()
        vm.RefreshCommand |> should not' (equal null)
