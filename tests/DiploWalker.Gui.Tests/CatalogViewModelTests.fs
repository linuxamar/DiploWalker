module DiploWalker.Gui.Tests.CatalogViewModelTests

open System
open System.IO
open System.Threading
open System.Windows.Input
open Xunit
open FsUnit.Xunit
open DiploWalker.Core
open DiploWalker.Core.Clients
open DiploWalker.Core.Output
open DiploWalker.TestHelpers

let private waitUntil (predicate: unit -> bool) =
    let sw = Diagnostics.Stopwatch.StartNew()

    while not (predicate ()) && sw.ElapsedMilliseconds < 10000L do
        Thread.Sleep(20)

    predicate ()

let private catalogVm (port: MockOutputPort) (fake: FakeContainerClient) (path: string) =
    new DiploWalker.Gui.ViewModels.ContainerTabViewModel(
        port,
        containerClientFactory = (fun () -> fake :> IContainerClient),
        catalogPathProvider = (fun () -> path)
    )

let private tempCatalog () =
    let dir = TestHelpers.createTempDir "catalog-vm"
    let path = Path.Combine(dir, "diplo-catalog.json")
    (dir, path)

// ── ListCatalog ────────────────────────────────────────────────

[<Fact>]
let ``ContainerTabViewModel ListCatalog ecrit le bilan du catalogue`` () =
    let port = MockOutputPort()
    let dir, path = tempCatalog ()
    ImageCatalog.add path "nginx:latest" (Some "web") |> ignore

    try
        let vm = catalogVm port (new FakeContainerClient()) path
        (vm.ListCatalogCommand :> ICommand).Execute(null)
        waitUntil (fun () -> port.Successes |> List.contains "1 image(s) au catalogue") |> should equal true
    finally
        Directory.Delete(dir, true)

[<Fact>]
let ``ContainerTabViewModel ListCatalog catalogue vide affiche 0`` () =
    let port = MockOutputPort()
    let dir, path = tempCatalog ()

    try
        let vm = catalogVm port (new FakeContainerClient()) path
        (vm.ListCatalogCommand :> ICommand).Execute(null)
        waitUntil (fun () -> port.Successes |> List.contains "0 image(s) au catalogue") |> should equal true
    finally
        Directory.Delete(dir, true)

// ── AddToCatalog ───────────────────────────────────────────────

[<Fact>]
let ``ContainerTabViewModel AddToCatalog tire l'image et inscrit l'entrée`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient()
    let dir, path = tempCatalog ()

    try
        let vm = catalogVm port fake path
        vm.ContainerImageRef <- "nginx:latest"
        vm.CatalogNote <- "web"
        (vm.AddToCatalogCommand :> ICommand).Execute(null)
        waitUntil (fun () -> fake.PullCalls = 1) |> should equal true
        waitUntil (fun () -> port.Successes |> List.exists (fun m -> m.Contains "ajoutée au catalogue")) |> should equal true

        let e = ImageCatalog.load path |> List.head
        e.Ref |> should equal "nginx:latest"
        e.Note |> should equal (Some "web")
    finally
        Directory.Delete(dir, true)

[<Fact>]
let ``ContainerTabViewModel AddToCatalog doublon prévient sans toucher au fichier`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient()
    let dir, path = tempCatalog ()
    ImageCatalog.add path "alpine:3.19" None |> ignore

    try
        let vm = catalogVm port fake path
        vm.ContainerImageRef <- "alpine:3.19"
        (vm.AddToCatalogCommand :> ICommand).Execute(null)
        waitUntil (fun () -> port.Warnings |> List.exists (fun m -> m.Contains "est déjà au catalogue")) |> should equal true
        ImageCatalog.load path |> should haveLength 1
        // RefreshCatalogue relit le fichier en tâche de fond via UiThread.Post :
        // on attend la fin de la relecture avant de supprimer le répertoire,
        // sinon Directory.Delete court avec la lecture du fichier.
        waitUntil (fun () -> vm.Catalogue.Count = 1) |> should equal true
    finally
        Directory.Delete(dir, true)

[<Fact>]
let ``ContainerTabViewModel AddToCatalog sans référence prévient sans pull`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient()
    let dir, path = tempCatalog ()

    try
        let vm = catalogVm port fake path
        (vm.AddToCatalogCommand :> ICommand).Execute(null)
        waitUntil (fun () -> port.Warnings.Length = 1) |> should equal true
        port.Warnings.Head |> should haveSubstring "Une référence d'image est requise"
        fake.PullCalls |> should equal 0
    finally
        Directory.Delete(dir, true)

// ── UpdateCatalog ──────────────────────────────────────────────

[<Fact>]
let ``ContainerTabViewModel UpdateCatalog étiquette et renomme l'entrée`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient()
    let dir, path = tempCatalog ()
    ImageCatalog.add path "nginx:1.25" None |> ignore

    try
        let vm = catalogVm port fake path
        vm.ContainerImageRef <- "nginx:1.25"
        vm.ContainerImageTarget <- "nginx:1.27"
        vm.CatalogNote <- "a jour"
        (vm.UpdateCatalogCommand :> ICommand).Execute(null)
        waitUntil (fun () -> fake.TagImageCalls = 1) |> should equal true

        let updated =
            waitUntil (fun () -> port.Successes |> List.exists (fun m -> m.Contains "mise à jour dans le catalogue"))

        updated |> should equal true

        let e = ImageCatalog.load path |> List.head
        e.Ref |> should equal "nginx:1.27"
        e.Note |> should equal (Some "a jour")
    finally
        Directory.Delete(dir, true)

[<Fact>]
let ``ContainerTabViewModel UpdateCatalog référence absente prévient`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient()
    let dir, path = tempCatalog ()

    try
        let vm = catalogVm port fake path
        vm.ContainerImageRef <- "introuvable:latest"
        (vm.UpdateCatalogCommand :> ICommand).Execute(null)

        let warned =
            waitUntil (fun () -> port.Warnings |> List.exists (fun m -> m.Contains "n'est pas au catalogue"))

        warned |> should equal true

        fake.TagImageCalls |> should equal 0
    finally
        Directory.Delete(dir, true)

// ── RemoveFromCatalog ──────────────────────────────────────────

[<Fact>]
let ``ContainerTabViewModel RemoveFromCatalog supprime et retire l'entrée`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient()
    let dir, path = tempCatalog ()
    ImageCatalog.add path "nginx:latest" None |> ignore

    try
        let vm = catalogVm port fake path
        vm.ContainerImageRef <- "nginx:latest"
        (vm.RemoveFromCatalogCommand :> ICommand).Execute(null)
        waitUntil (fun () -> fake.RemoveImageCalls = 1) |> should equal true

        let removed =
            waitUntil (fun () -> port.Successes |> List.exists (fun m -> m.Contains "retirée du catalogue"))

        removed |> should equal true

        ImageCatalog.load path |> should be Empty
    finally
        Directory.Delete(dir, true)

[<Fact>]
let ``ContainerTabViewModel RemoveFromCatalog référence absente prévient sans Docker`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient()
    let dir, path = tempCatalog ()

    try
        let vm = catalogVm port fake path
        vm.ContainerImageRef <- "absente:latest"
        (vm.RemoveFromCatalogCommand :> ICommand).Execute(null)

        let warned =
            waitUntil (fun () -> port.Warnings |> List.exists (fun m -> m.Contains "n'est pas au catalogue"))

        warned |> should equal true

        fake.RemoveImageCalls |> should equal 0
    finally
        Directory.Delete(dir, true)

