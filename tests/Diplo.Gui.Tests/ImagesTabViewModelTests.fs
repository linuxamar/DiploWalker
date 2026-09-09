module Diplo.Gui.Tests.ImagesTabViewModelTests

open System
open System.Collections.Generic
open System.Threading
open System.Windows.Input
open Xunit
open FsUnit.Xunit
open Diplo.Core.Clients
open Diplo.Core.Output
open Diplo.Grpc.Container
open Diplo.TestHelpers

// Tests de l'onglet Images (lister, télécharger, étiqueter, supprimer,
// inspecter, nettoyer) via l'injection d'un FakeContainerClient. Les réponses
// gRPC sont des enregistrements [<CLIMutable>] : construction avec syntaxe { }.

let private waitUntil (predicate: unit -> bool) =
    let sw = Diagnostics.Stopwatch.StartNew()

    while not (predicate ()) && sw.ElapsedMilliseconds < 2000L do
        Thread.Sleep(20)

    predicate ()

let private imagesVm (port: MockOutputPort) (fake: FakeContainerClient) =
    new Diplo.Gui.ViewModels.ImagesTabViewModel(
        port,
        containerClientFactory = fun () -> fake :> IContainerClient
    )

[<Fact>]
let ``ImagesTabViewModel ListImages ecrit le bilan et contacte le client`` () =
    let port = MockOutputPort()

    let img =
        { Ref = "nginx"
          Id = "sha256:1"
          Repository = "library/nginx"
          Tag = "latest"
          Size = 2048L
          CreatedAt = "2026-01-01" }

    let fake = new FakeContainerClient(listImages = { Images = List<ImageInfo>([| img |]) })
    let vm = imagesVm port fake
    (vm.ListImagesCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.ListImagesCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "1 image(s) trouvée(s)")
    |> should equal true

[<Fact>]
let ``ImagesTabViewModel ListImages invite une recherche vide a garder toutes les images`` () =
    let port = MockOutputPort()

    let imgs =
        List<ImageInfo>(
            [|
                { Ref = "nginx"; Id = "a"; Repository = "nginx"; Tag = "latest"; Size = 1L; CreatedAt = "2026-01-01" }
                { Ref = "redis"; Id = "b"; Repository = "redis"; Tag = "7"; Size = 1L; CreatedAt = "2026-01-01" }
                { Ref = "nginx"; Id = "c"; Repository = "nginx"; Tag = "alpine"; Size = 1L; CreatedAt = "2026-01-01" }
                { Ref = "postgres"; Id = "d"; Repository = "postgres"; Tag = "16"; Size = 1L; CreatedAt = "2026-01-01" }
            |]
        )

    let fake = new FakeContainerClient(listImages = { Images = imgs })
    let vm = imagesVm port fake
    (vm.ListImagesCommand :> ICommand).Execute(null)
    waitUntil (fun () -> vm.Images.Count = 4) |> should equal true
    vm.Images.Count |> should equal 4

[<Fact>]
let ``ImagesTabViewModel ImageSearchInput filtre la liste par reference`` () =
    let port = MockOutputPort()

    let imgs =
        List<ImageInfo>(
            [|
                { Ref = "nginx"; Id = "a"; Repository = "nginx"; Tag = "latest"; Size = 1L; CreatedAt = "2026-01-01" }
                { Ref = "redis"; Id = "b"; Repository = "redis"; Tag = "7"; Size = 1L; CreatedAt = "2026-01-01" }
                { Ref = "nginx"; Id = "c"; Repository = "nginx"; Tag = "alpine"; Size = 1L; CreatedAt = "2026-01-01" }
                { Ref = "postgres"; Id = "d"; Repository = "postgres"; Tag = "16"; Size = 1L; CreatedAt = "2026-01-01" }
            |]
        )

    let fake = new FakeContainerClient(listImages = { Images = imgs })
    let vm = imagesVm port fake
    (vm.ListImagesCommand :> ICommand).Execute(null)
    waitUntil (fun () -> vm.Images.Count = 4) |> should equal true
    vm.ImageSearchInput <- "NGINX"
    vm.Images.Count |> should equal 2
    vm.ImageSearchInput <- "redis"
    vm.Images.Count |> should equal 1
    vm.ImageSearchInput <- "7"
    vm.Images.Count |> should equal 1
    vm.Images.[0].RefComplet |> should equal "redis:7"

[<Fact>]
let ``ImagesTabViewModel ImageSearchInput vide restaure toute la liste`` () =
    let port = MockOutputPort()

    let imgs =
        List<ImageInfo>(
            [|
                { Ref = "nginx"; Id = "a"; Repository = "nginx"; Tag = "latest"; Size = 1L; CreatedAt = "2026-01-01" }
                { Ref = "redis"; Id = "b"; Repository = "redis"; Tag = "7"; Size = 1L; CreatedAt = "2026-01-01" }
            |]
        )

    let fake = new FakeContainerClient(listImages = { Images = imgs })
    let vm = imagesVm port fake
    (vm.ListImagesCommand :> ICommand).Execute(null)
    waitUntil (fun () -> vm.Images.Count = 2) |> should equal true
    vm.ImageSearchInput <- "redis"
    vm.Images.Count |> should equal 1
    vm.ImageSearchInput <- ""
    vm.Images.Count |> should equal 2

[<Fact>]
let ``ImagesTabViewModel PullImage sans reference ecrit une erreur`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient()
    let vm = imagesVm port fake
    (vm.PullImageCommand :> ICommand).Execute(null)
    waitUntil (fun () -> port.Errors.Length = 1) |> should equal true
    port.Errors.Head |> should haveSubstring "à télécharger est requise"
    fake.PullCalls |> should equal 0

[<Fact>]
let ``ImagesTabViewModel PullImage appelle le client avec la reference`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient(pull = { Image = "nginx"; Message = "ok" })
    let vm = imagesVm port fake
    vm.PullRefInput <- "nginx:latest"
    (vm.PullImageCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.PullCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "Image nginx:latest téléchargée")
    |> should equal true

[<Fact>]
let ``ImagesTabViewModel PullImage transmet le user du pull`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient(pull = { Image = "nginx"; Message = "ok" })
    let vm = imagesVm port fake
    vm.PullRefInput <- "private/nginx:1.0"
    vm.PullUserInput <- "docker:motdepasse"
    (vm.PullImageCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.PullCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "Image private/nginx:1.0 téléchargée")
    |> should equal true

[<Fact>]
let ``ImagesTabViewModel TagImage sans source ecrit une erreur`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient()
    let vm = imagesVm port fake
    (vm.TagImageCommand :> ICommand).Execute(null)
    waitUntil (fun () -> port.Errors.Length = 1) |> should equal true
    port.Errors.Head |> should haveSubstring "source à étiqueter est requise"
    fake.TagImageCalls |> should equal 0

[<Fact>]
let ``ImagesTabViewModel TagImage sans cible ecrit une erreur`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient()
    let vm = imagesVm port fake
    vm.TagSourceInput <- "nginx:latest"
    (vm.TagImageCommand :> ICommand).Execute(null)
    waitUntil (fun () -> port.Errors.Length = 1) |> should equal true
    port.Errors.Head |> should haveSubstring "cible de l'étiquette est requise"
    fake.TagImageCalls |> should equal 0

[<Fact>]
let ``ImagesTabViewModel TagImage appelle le client`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient(tagImage = { Source = ""; Target = ""; Message = "ok" })
    let vm = imagesVm port fake
    vm.TagSourceInput <- "nginx:latest"
    vm.TagTargetInput <- "myreg/nginx:1.0"
    (vm.TagImageCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.TagImageCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "Image nginx:latest étiquetée en myreg/nginx:1.0")
    |> should equal true

[<Fact>]
let ``ImagesTabViewModel InspectImage sans reference ecrit un avertissement`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient()
    let vm = imagesVm port fake
    (vm.InspectImageCommand :> ICommand).Execute(null)
    waitUntil (fun () -> port.Warnings.Length = 1) |> should equal true
    port.Warnings.Head |> should haveSubstring "Aucune référence d'image"
    fake.InspectImageCalls |> should equal 0

[<Fact>]
let ``ImagesTabViewModel InspectImage ecrit la reference et les labels`` () =
    let port = MockOutputPort()

    let labels = Dictionary<string, string>()
    labels.["com.diplo.role"] <- "web"

    let img =
        { Ref = "nginx"
          Id = "sha256:1"
          Repository = "library/nginx"
          Tag = "latest"
          Size = 2048L
          CreatedAt = "2026-01-01"
          Labels = labels }

    let fake = new FakeContainerClient(inspectImage = img)
    let vm = imagesVm port fake
    vm.ImageRefInput <- "nginx:latest"
    (vm.InspectImageCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.InspectImageCalls = 1) |> should equal true
    port.Messages |> should contain "Référentiel: nginx"
    port.Messages |> should contain "  com.diplo.role = web"

[<Fact>]
let ``ImagesTabViewModel RemoveImage sans reference ecrit un avertissement`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient()
    let vm = imagesVm port fake
    (vm.RemoveImageCommand :> ICommand).Execute(null)
    waitUntil (fun () -> port.Warnings.Length = 1) |> should equal true
    port.Warnings.Head |> should haveSubstring "Aucune référence d'image"
    fake.RemoveImageCalls |> should equal 0

[<Fact>]
let ``ImagesTabViewModel RemoveImage en succes ecrit le bilan`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient(removeImage = { Success = true; Message = "" })
    let vm = imagesVm port fake
    vm.ImageRefInput <- "nginx:latest"
    (vm.RemoveImageCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.RemoveImageCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "Image nginx:latest supprimée")
    |> should equal true

[<Fact>]
let ``ImagesTabViewModel RemoveImage reprend la reference de la selection`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient(removeImage = { Success = true; Message = "" })
    let vm = imagesVm port fake
    vm.SelectedImage <- { Ref = "nginx"; Tag = "latest"; Taille = "2048 octets"; CrééLe = "2026-01-01" }
    (vm.RemoveImageCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.RemoveImageCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "Image nginx:latest supprimée")
    |> should equal true

[<Fact>]
let ``ImagesTabViewModel RemoveImage en echec ecrit un avertissement`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient(removeImage = { Success = false; Message = "image en cours d'utilisation" })
    let vm = imagesVm port fake
    vm.ImageRefInput <- "nginx:latest"
    (vm.RemoveImageCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.RemoveImageCalls = 1) |> should equal true
    port.Warnings |> should contain "image en cours d'utilisation"

[<Fact>]
let ``ImagesTabViewModel PruneImages ecrit le bilan par image`` () =
    let port = MockOutputPort()

    let deleted = List<string>()
    deleted.Add("sha256:abc")
    let fake = new FakeContainerClient(pruneImages = { Deleted = deleted })
    let vm = imagesVm port fake
    (vm.PruneImagesCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.PruneImagesCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "1 image(s) suspendue(s) nettoyée(s)")
    |> should equal true
    port.Messages |> should contain "  sha256:abc"

[<Fact>]
let ``ImagesTabViewModel PruneImages sans image ecrit un bilan neutre`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient(pruneImages = { Deleted = List<string>() })
    let vm = imagesVm port fake
    (vm.PruneImagesCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.PruneImagesCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "Aucune image suspendue à nettoyer")
    |> should equal true