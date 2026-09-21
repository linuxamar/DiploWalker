module DiploWalker.Gui.Tests.OptionalBehaviorTests

open System
open System.IO
open System.Threading
open System.Windows.Input
open Xunit
open FsUnit.Xunit
open DiploWalker.Core.Clients
open DiploWalker.Core.Output
open DiploWalker.Gui.ViewModels
open DiploWalker.TestHelpers

// Tests des comportements GUI testables SANS serveur gRPC ni refactor
// d'injection : chemins de validation qui s'exÃ©cutent avant tout appel
// rÃ©seau, et avertissements liÃ©s Ã  l'absence de fournisseur de stockage.

let private waitUntil (predicate: unit -> bool) =
    let sw = Diagnostics.Stopwatch.StartNew()

    while not (predicate ()) && sw.ElapsedMilliseconds < 30000L do
        Thread.Sleep(20)

    predicate ()

// â”€â”€ VolumeTabViewModel : validation de CreateImage â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
// Toutes ces branches s'exÃ©cutent AVANT d'appeler FsImage.create : elles
// sont donc testables sans aucune opÃ©ration disque rÃ©elle.

[<Fact>]
let ``VolumeTabViewModel CreateImage sans repertoire source signale une erreur`` () =
    let port = MockOutputPort()
    let vm = new VolumeTabViewModel(port)
    (vm.CreateImageCommand :> ICommand).Execute(null)
    waitUntil (fun () -> port.Errors.Length = 1) |> should equal true
    port.Errors.Head |> should haveSubstring "répertoire source est requis"

[<Fact>]
let ``VolumeTabViewModel CreateImage avec repertoire source inexistant signale une erreur`` () =
    let port = MockOutputPort()
    let vm = new VolumeTabViewModel(port)
    vm.ImageSourceDir <- Path.Combine(Path.GetTempPath(), "diplo-inexistant-" + Guid.NewGuid().ToString("N"))
    vm.ImageDestPath <- "/tmp/out.img"
    (vm.CreateImageCommand :> ICommand).Execute(null)
    waitUntil (fun () -> port.Errors.Length = 1) |> should equal true
    port.Errors.Head |> should haveSubstring "n'existe pas"

[<Fact>]
let ``VolumeTabViewModel CreateImage sans chemin de destination signale une erreur`` () =
    let dir = TestHelpers.createTempDir "createimg-dest"
    let port = MockOutputPort()
    let vm = new VolumeTabViewModel(port)

    try
        vm.ImageSourceDir <- dir
        vm.ImageDestPath <- ""
        (vm.CreateImageCommand :> ICommand).Execute(null)
        waitUntil (fun () -> port.Errors.Length = 1) |> should equal true
        port.Errors.Head |> should haveSubstring "chemin de destination est requis"
    finally
        TestHelpers.cleanupDir dir

[<Fact>]
let ``VolumeTabViewModel CreateImage avec format inconnu signale une erreur`` () =
    let dir = TestHelpers.createTempDir "createimg-format"
    let dest = Path.Combine(dir, "out.bin")
    let port = MockOutputPort()
    let vm = new VolumeTabViewModel(port)

    try
        vm.ImageSourceDir <- dir
        vm.ImageDestPath <- dest
        vm.ImageFormat <- "zzz"
        (vm.CreateImageCommand :> ICommand).Execute(null)
        waitUntil (fun () -> port.Errors.Length = 1) |> should equal true
        port.Errors.Head |> should haveSubstring "Format inconnu"
    finally
        TestHelpers.cleanupDir dir

// â”€â”€ VolumeTabViewModel : navigation sans fournisseur de stockage â”€â”€

[<Fact>]
let ``VolumeTabViewModel BrowseSource sans fournisseur ecrit un avertissement`` () =
    let port = MockOutputPort()
    let vm = new VolumeTabViewModel(port)
    (vm.BrowseSourceCommand :> ICommand).Execute(null)
    waitUntil (fun () -> port.Warnings.Length = 1) |> should equal true
    port.Warnings.Head |> should haveSubstring "Fournisseur"

[<Fact>]
let ``VolumeTabViewModel BrowseDest sans fournisseur ecrit un avertissement`` () =
    let port = MockOutputPort()
    let vm = new VolumeTabViewModel(port)
    (vm.BrowseDestCommand :> ICommand).Execute(null)
    waitUntil (fun () -> port.Warnings.Length = 1) |> should equal true
    port.Warnings.Head |> should haveSubstring "Fournisseur"

[<Fact>]
let ``VolumeTabViewModel SetStorageProvider enregistre le fournisseur`` () =
    let port = MockOutputPort()
    let vm = new VolumeTabViewModel(port)
    // L'appel ne doit pas lever : le stockage reste remplaÃ§able.
    vm.SetStorageProvider(null)
    (vm.BrowseSourceCommand :> ICommand).Execute(null)
    waitUntil (fun () -> port.Warnings.Length = 1) |> should equal true

// â”€â”€ VolumeTabViewModel : navigation AVEC fournisseur de stockage â”€â”€
// Les pickers du fake retournent un Ã©lÃ©ment injectÃ© : le ViewModel doit alors
// affecter ImageSourceDir / ImageDestPath au lieu de l'avertissement.

[<Fact>]
let ``VolumeTabViewModel BrowseSource affecte le dossier retourne`` () =
    let dir = TestHelpers.createTempDir "browse-src"
    let port = MockOutputPort()
    let vm = new VolumeTabViewModel(port)

    try
        vm.SetStorageProvider(new FakeStorageProvider(folder = new FakeStorageFolder(dir)))
        (vm.BrowseSourceCommand :> ICommand).Execute(null)
        waitUntil (fun () -> not (String.IsNullOrEmpty vm.ImageSourceDir)) |> should equal true
        vm.ImageSourceDir |> should equal dir
    finally
        TestHelpers.cleanupDir dir

[<Fact>]
let ``VolumeTabViewModel BrowseDest affecte le fichier retourne`` () =
    let dir = TestHelpers.createTempDir "browse-dest"
    let port = MockOutputPort()
    let vm = new VolumeTabViewModel(port)

    try
        let dest = Path.Combine(dir, "disque.img")
        vm.SetStorageProvider(new FakeStorageProvider(file = new FakeStorageFile(dest)))
        (vm.BrowseDestCommand :> ICommand).Execute(null)
        waitUntil (fun () -> not (String.IsNullOrEmpty vm.ImageDestPath)) |> should equal true
        vm.ImageDestPath |> should equal dest
    finally
        TestHelpers.cleanupDir dir

[<Fact>]
let ``VolumeTabViewModel BrowseSource avec une liste vide n'affecte rien`` () =
    let port = MockOutputPort()
    let vm = new VolumeTabViewModel(port)
    vm.SetStorageProvider(new FakeStorageProvider())
    (vm.BrowseSourceCommand :> ICommand).Execute(null)
    // Aucune sortie, aucun chemin : le picker annulÃ© laisse la saisie intacte.
    Thread.Sleep 250
    port.HasOutput |> should equal false
    vm.ImageSourceDir |> should equal ""

// â”€â”€ VolumeTabViewModel : CreateImage rÃ©ussi â”€â”€
// La validation franchie, FsImage.create tourne rÃ©ellement (format raw) sur un
// rÃ©pertoire source temporaire : net sur disque, sans aucun serveur gRPC.

[<Fact>]
let ``VolumeTabViewModel CreateImage cree une image raw depuis le repertoire source`` () =
    let dir = TestHelpers.createTempDir "createimg-ok"
    let port = MockOutputPort()
    let vm = new VolumeTabViewModel(port)

    try
        let src = Path.Combine(dir, "src")
        Directory.CreateDirectory(src) |> ignore
        File.WriteAllText(Path.Combine(src, "hello.txt"), "contenu")

        let dest = Path.Combine(dir, "out.img")
        vm.ImageSourceDir <- src
        vm.ImageDestPath <- dest
        vm.ImageFormat <- "raw"
        (vm.CreateImageCommand :> ICommand).Execute(null)
        waitUntil (fun () -> port.Successes.Length = 1) |> should equal true
        port.Successes.Head |> should haveSubstring "Image créée"
        File.Exists dest |> should equal true
    finally
        TestHelpers.cleanupDir dir

// â”€â”€ ComposeTabViewModel : enregistrement d'un fichier existant â”€â”€
// Si un fichier est chargÃ© (FilePath non vide), SaveComposeFile Ã©crit le
// buffer via composeEditor.Save() sans passer par le fournisseur : testable
// sans client gRPC.

[<Fact>]
let ``ComposeTabViewModel SaveComposeFile avec fichier charge enregistre le buffer`` () =
    let dir = TestHelpers.createTempDir "compose-save"
    let port = MockOutputPort()
    let vm = new ComposeTabViewModel(port)

    try
        let path = Path.Combine(dir, "docker-compose.yml")
        File.WriteAllText(path, "services:\n  web:\n    image: nginx\n")
        // Le setter de ComposeFilePath charge le contenu dans l'Ã©diteur.
        vm.ComposeFilePath <- path
        vm.ComposeEditor.Document.Text <- "services:\n  web:\n    image: alpine:3.18\n"
        (vm.SaveComposeFileCommand :> ICommand).Execute(null)
        waitUntil (fun () -> port.Successes.Length = 1) |> should equal true
        port.Successes.Head |> should haveSubstring "Fichier enregistré"
        File.ReadAllText(path).Contains("alpine:3.18") |> should equal true
    finally
        TestHelpers.cleanupDir dir

// â”€â”€ ComposeTabViewModel : ComposeBuild â”€â”€
// La vÃ©rification de l'existence du Dockerfile prÃ©cÃ¨de le lancement du
// processus docker rÃ©el : un contexte sans Dockerfile est donc testable sans
// binaire docker ni serveur gRPC.

[<Fact>]
let ``ComposeTabViewModel ComposeBuild signale un Dockerfile introuvable`` () =
    let dir = TestHelpers.createTempDir "compose-build"
    let port = MockOutputPort()
    let fake = new FakeContainerClient()
    let vm = new ComposeTabViewModel(port, containerClientFactory = fun () -> fake :> IContainerClient)

    try
        Directory.CreateDirectory(Path.Combine(dir, "app")) |> ignore

        let path = Path.Combine(dir, "docker-compose.yml")
        File.WriteAllText(path, "services:\n  web:\n    build: ./app\n")
        vm.ComposeFilePath <- path
        (vm.ComposeBuildCommand :> ICommand).Execute(null)
        waitUntil (fun () -> port.Errors.Length = 1) |> should equal true
        port.Errors.Head |> should haveSubstring "Dockerfile introuvable"
    finally
        TestHelpers.cleanupDir dir

