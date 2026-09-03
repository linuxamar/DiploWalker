module Diplo.Gui.Tests.OptionalBehaviorTests

open System
open System.IO
open System.Threading
open System.Windows.Input
open Xunit
open FsUnit.Xunit
open Diplo.Core.Output
open Diplo.Gui.ViewModels
open Diplo.TestHelpers

// Tests des comportements GUI testables SANS serveur gRPC ni refactor
// d'injection : chemins de validation qui s'exécutent avant tout appel
// réseau, et avertissements liés à l'absence de fournisseur de stockage.

let private waitUntil (predicate: unit -> bool) =
    let sw = Diagnostics.Stopwatch.StartNew()

    while not (predicate ()) && sw.ElapsedMilliseconds < 2000L do
        Thread.Sleep(20)

    predicate ()

// ── VolumeTabViewModel : validation de CreateImage ──────────────
// Toutes ces branches s'exécutent AVANT d'appeler FsImage.create : elles
// sont donc testables sans aucune opération disque réelle.

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

// ── VolumeTabViewModel : navigation sans fournisseur de stockage ──

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
    // L'appel ne doit pas lever : le stockage reste remplaçable.
    vm.SetStorageProvider(null)
    (vm.BrowseSourceCommand :> ICommand).Execute(null)
    waitUntil (fun () -> port.Warnings.Length = 1) |> should equal true

// ── ComposeTabViewModel : enregistrement d'un fichier existant ──
// Si un fichier est chargé (FilePath non vide), SaveComposeFile écrit le
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
        // Le setter de ComposeFilePath charge le contenu dans l'éditeur.
        vm.ComposeFilePath <- path
        vm.ComposeEditor.Document.Text <- "services:\n  web:\n    image: alpine:3.18\n"
        (vm.SaveComposeFileCommand :> ICommand).Execute(null)
        waitUntil (fun () -> port.Successes.Length = 1) |> should equal true
        port.Successes.Head |> should haveSubstring "Fichier enregistré"
        File.ReadAllText(path).Contains("alpine:3.18") |> should equal true
    finally
        TestHelpers.cleanupDir dir
