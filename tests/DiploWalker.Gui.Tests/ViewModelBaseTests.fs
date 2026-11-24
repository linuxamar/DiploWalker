namespace DiploWalker.Gui.Tests

open System
open Xunit
open FsUnit.Xunit
open DiploWalker.Gui.ViewModels

/// Tests pour la classe de base des ViewModels.
type ViewModelBaseTests() =

    [<Fact>]
    member _.``IsLoading par défaut est false``() =
        let vm = new ViewModelBase()
        vm.IsLoading |> should equal false

    [<Fact>]
    member _.``IsLoading peut être modifié``() =
        let vm = new ViewModelBase()
        vm.IsLoading <- true
        vm.IsLoading |> should equal true

    [<Fact>]
    member _.``LogDebug ne lève pas d'exception``() =
        let vm = new ViewModelBase()
        (fun () -> vm.LogDebug "test" |> ignore) |> should not' (throw typeof<System.Exception>)
