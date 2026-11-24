namespace DiploWalker.Gui.Tests

open System
open Xunit
open FsUnit.Xunit
open DiploWalker.Gui.ViewModels

/// Tests pour le LogViewModel.
type LogViewModelTests() =

    [<Fact>]
    member _.``StatusMessage par défaut est vide``() =
        let vm = new LogViewModel()
        vm.StatusMessage |> should equal ""

    [<Fact>]
    member _.``SelectedLevel par défaut est Tous``() =
        let vm = new LogViewModel()
        vm.SelectedLevel |> should equal "Tous"

    [<Fact>]
    member _.``Entries n'est pas null``() =
        let vm = new LogViewModel()
        vm.Entries |> should not' (equal null)

    [<Fact>]
    member _.``Levels contient les niveaux attendus``() =
        let vm = new LogViewModel()
        vm.Levels |> should contain "Tous"
        vm.Levels |> should contain "Debug"
        vm.Levels |> should contain "Information"
        vm.Levels |> should contain "Avertissement"
        vm.Levels |> should contain "Erreur"
        vm.Levels |> should contain "Critique"

    [<Fact>]
    member _.``AddLogEntry ajoute une entrée``() =
        let vm = new LogViewModel()
        let initialCount = vm.Entries.Count
        vm.AddLogEntry({ Timestamp = DateTime.Now; Level = "Information"; Message = "Test"; Source = "TestSource" })
        vm.Entries.Count |> should equal (initialCount + 1)

    [<Fact>]
    member _.``Clear vide la liste``() =
        let vm = new LogViewModel()
        vm.AddLogEntry({ Timestamp = DateTime.Now; Level = "Information"; Message = "Test"; Source = "TestSource" })
        vm.Clear().GetAwaiter().GetResult() |> ignore
        vm.Entries.Count |> should equal 0

    [<Fact>]
    member _.``ClearCommand n'est pas null``() =
        let vm = new LogViewModel()
        vm.ClearCommand |> should not' (equal null)
