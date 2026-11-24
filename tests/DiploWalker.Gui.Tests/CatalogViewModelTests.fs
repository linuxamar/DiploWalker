namespace DiploWalker.Gui.Tests

open System
open Xunit
open FsUnit.Xunit
open DiploWalker.Gui.ViewModels

/// Tests pour le CatalogViewModel.
type CatalogViewModelTests() =

    [<Fact>]
    member _.``StatusMessage par défaut est vide``() =
        let vm = new CatalogViewModel()
        vm.StatusMessage |> should equal ""

    [<Fact>]
    member _.``Query par défaut est vide``() =
        let vm = new CatalogViewModel()
        vm.Query |> should equal ""

    [<Fact>]
    member _.``Registry par défaut est vide``() =
        let vm = new CatalogViewModel()
        vm.Registry |> should equal ""

    [<Fact>]
    member _.``Results n'est pas null``() =
        let vm = new CatalogViewModel()
        vm.Results |> should not' (equal null)

    [<Fact>]
    member _.``SelectedImage par défaut est None``() =
        let vm = new CatalogViewModel()
        vm.SelectedImage |> should equal None

    [<Fact>]
    member _.``Registries contient les registres attendus``() =
        let vm = new CatalogViewModel()
        vm.Registries |> should contain "docker.io"
        vm.Registries |> should contain "quay.io"
        vm.Registries |> should contain "ghcr.io"
        vm.Registries |> should contain "mcr.microsoft.com"

    [<Fact>]
    member _.``Search ne lève pas d'exception``() =
        let vm = new CatalogViewModel()
        (fun () -> vm.Search().GetAwaiter().GetResult() |> ignore) |> should not' (throw typeof<System.Exception>)

    [<Fact>]
    member _.``SearchCommand n'est pas null``() =
        let vm = new CatalogViewModel()
        vm.SearchCommand |> should not' (equal null)
