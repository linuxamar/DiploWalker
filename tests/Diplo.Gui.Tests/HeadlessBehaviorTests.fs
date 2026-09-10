module Diplo.Gui.Tests.HeadlessBehaviorTests

open System
open System.Collections.Generic
open System.Threading
open System.Windows.Input
open Xunit
open FsUnit.Xunit
open Diplo.Core.Clients
open Diplo.Core.Output
open Diplo.Grpc.Container
open Diplo.Grpc.Network
open Diplo.Grpc.Volume
open Diplo.TestHelpers

// Tests des méthodes des ViewModels qui ne peuvent s'exécuter que sur un vrai
// Dispatcher Avalonia : elles remplissent leurs collections via UiThread.Post.
// Le harnais headless (HeadlessRunner.setupHeadless) possède un thread dédié
// qui pompe le Dispatcher ; waitPump ne fait que poller les collections.

let private waitPump (predicate: unit -> bool) =
    let sw = Diagnostics.Stopwatch.StartNew()
    let mutable ok = predicate ()

    while not ok && sw.ElapsedMilliseconds < 3000L do
        Thread.Sleep(10)
        ok <- predicate ()

    ok

// ── ContainerTabViewModel ──────────────────────────────────────

let private containerVm (port: MockOutputPort) (fake: FakeContainerClient) =
    new Diplo.Gui.ViewModels.ContainerTabViewModel(
        port,
        containerClientFactory = fun () -> fake :> IContainerClient
    )

[<Fact>]
let ``ListContainers peuple la collection sur le dispatcher`` () =
    HeadlessRunner.setupHeadless ()
    let port = MockOutputPort()

    let info =
        { Id = "c1"
          Name = "web"
          Image = "nginx"
          State = ContainerState.Running
          CreatedAt = "2026-01-01"
          Labels = Dictionary<string, string>() }

    let fake = new FakeContainerClient(list = { Containers = List<ContainerInfo>([| info |]) })
    let vm = containerVm port fake
    (vm.ListContainersCommand :> ICommand).Execute(null)
    waitPump (fun () -> vm.Containers.Count = 1) |> should equal true
    vm.Containers.[0].Nom |> should equal "web"
    vm.Containers.[0].Image |> should equal "nginx"

[<Fact>]
let ``ListImages peuple la collection sur le dispatcher`` () =
    HeadlessRunner.setupHeadless ()
    let port = MockOutputPort()

    let img =
        { Ref = "nginx"
          Id = "sha256:1"
          Repository = "library/nginx"
          Tag = "latest"
          Size = 2048L
          CreatedAt = "2026-01-01" }

    let fake = new FakeContainerClient(listImages = { Images = List<ImageInfo>([| img |]) })
    let vm = containerVm port fake
    (vm.ListImagesCommand :> ICommand).Execute(null)
    waitPump (fun () -> vm.Images.Count = 1) |> should equal true
    vm.Images.[0].Référentiel |> should equal "nginx"
    vm.Images.[0].Tag |> should equal "latest"

// ── ImagesTabViewModel ─────────────────────────────────────────

let private imagesVm (port: MockOutputPort) (fake: FakeContainerClient) =
    new Diplo.Gui.ViewModels.ImagesTabViewModel(
        port,
        containerClientFactory = fun () -> fake :> IContainerClient
    )

[<Fact>]
let ``ImagesTabViewModel ListImages peuple la collection sur le dispatcher`` () =
    HeadlessRunner.setupHeadless ()
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
    waitPump (fun () -> vm.Images.Count = 1) |> should equal true
    vm.Images.[0].Ref |> should equal "nginx"
    vm.Images.[0].RefComplet |> should equal "nginx:latest"
    vm.Images.[0].Taille |> should equal "2048 octets"

[<Fact>]
let ``ImagesTabViewModel SearchImages peuple les resultats sur le dispatcher`` () =
    HeadlessRunner.setupHeadless ()
    let port = MockOutputPort()

    let fake =
        new FakeContainerClient(
            searchImages =
                { Results =
                      List<RegistrySearchResult>(
                          [|
                              { Registry = "docker.io"
                                Ref = "docker.io/nginx"
                                Description = "Serveur web"
                                Stars = 100 }
                          |]
                      )
                  Message = "" }
        )

    let vm = imagesVm port fake
    vm.ImageSearchInput <- "nginx"
    (vm.SearchImagesCommand :> ICommand).Execute(null)
    waitPump (fun () -> vm.SearchResults.Count = 1 && vm.SearchStatus.Contains("1 résultat(s)")) |> should equal true
    vm.SearchResults.[0].Registre |> should equal "docker.io"
    vm.SearchResults.[0].Ref |> should equal "docker.io/nginx"
    vm.SearchResults.[0].Étoiles |> should equal "100"
    Assert.Contains("1 résultat(s)", vm.SearchStatus)

// ── VolumeTabViewModel ─────────────────────────────────────────

let private volumeVm (port: MockOutputPort) (fake: FakeVolumeClient) =
    new Diplo.Gui.ViewModels.VolumeTabViewModel(
        port,
        volumeClientFactory = fun () -> fake :> IVolumeClient
    )

[<Fact>]
let ``ListVolumes peuple la collection sur le dispatcher`` () =
    HeadlessRunner.setupHeadless ()
    let port = MockOutputPort()

    let vol =
        { Id = "v1"
          Name = "data"
          Driver = StorageDriverType.Local
          Mountpoint = "/var/lib/docker/volumes/data"
          State = MountState.Mounted
          Labels = Dictionary<string, string>()
          SizeBytes = 512L }

    let fake = new FakeVolumeClient(list = { Volumes = List<VolumeInfo>([| vol |]) })
    let vm = volumeVm port fake
    (vm.ListVolumesCommand :> ICommand).Execute(null)
    waitPump (fun () -> vm.Volumes.Count = 1) |> should equal true
    vm.Volumes.[0].Nom |> should equal "data"

// ── NetworkTabViewModel ────────────────────────────────────────

let private networkVm (port: MockOutputPort) (fake: FakeNetworkClient) =
    new Diplo.Gui.ViewModels.NetworkTabViewModel(
        port,
        networkClientFactory = fun () -> fake :> INetworkClient
    )

[<Fact>]
let ``ListNetworks peuple la collection sur le dispatcher`` () =
    HeadlessRunner.setupHeadless ()
    let port = MockOutputPort()

    let net =
        { Id = "n1"
          Name = "bridge"
          Driver = NetworkDriver.Bridge
          Subnet = "172.17.0.0/16"
          Gateway = "172.17.0.1"
          EndpointCount = 2
          CreatedAt = "2026-01-01" }

    let fake = new FakeNetworkClient(list = { Networks = List<NetworkInfo>([| net |]) })
    let vm = networkVm port fake
    (vm.ListNetworksCommand :> ICommand).Execute(null)
    waitPump (fun () -> vm.Networks.Count = 1) |> should equal true
    vm.Networks.[0].Nom |> should equal "bridge"

// ── ComposeTabViewModel ────────────────────────────────────────

let private composeVm (port: MockOutputPort) (fake: FakeContainerClient) =
    new Diplo.Gui.ViewModels.ComposeTabViewModel(
        port,
        containerClientFactory = fun () -> fake :> IContainerClient
    )

[<Fact>]
let ``ComposePs peuple la liste des services du projet`` () =
    HeadlessRunner.setupHeadless ()
    let dir = Diplo.TestHelpers.TestHelpers.createTempDir "compose-ps"
    let path = System.IO.Path.Combine(dir, "docker-compose.yml")
    System.IO.File.WriteAllText(path, "services:\n  web:\n    image: nginx:latest\n")

    try
        let port = MockOutputPort()
        let labels = Dictionary<string, string>()
        labels.[Diplo.Core.Compose.ComposeModels.composeProjectLabel] <- "docker-compose"
        labels.[Diplo.Core.Compose.ComposeModels.composeServiceLabel] <- "web"

        let info =
            { Id = "c1"
              Name = "web"
              Image = "nginx:latest"
              State = ContainerState.Running
              CreatedAt = "2026-01-01"
              Labels = labels }

        let fake = new FakeContainerClient(list = { Containers = List<ContainerInfo>([| info |]) })
        let vm = composeVm port fake
        vm.ComposeFilePath <- path
        (vm.ComposePsCommand :> ICommand).Execute(null)
        waitPump (fun () -> vm.ComposeServices.Count = 1)
        |> should equal true

        vm.ComposeServices
        |> Seq.map (fun s -> s.Service)
        |> Seq.toList
        |> should equal [ "web" ]

        waitPump (fun () ->
            port.Successes
            |> Seq.exists (fun m -> m.Contains "conteneur(s) compose trouvé(s)"))
        |> should equal true
    finally
        Diplo.TestHelpers.TestHelpers.cleanupDir dir

// ── MainWindowViewModel ────────────────────────────────────────

let private mainWindowVm () =
    HeadlessRunner.setupHeadless ()
    new Diplo.Gui.ViewModels.MainWindowViewModel()

/// Accès au journal brut via le type concret sous-jacent.
let private mainWindowPort (vm: Diplo.Gui.ViewModels.MainWindowViewModel) =
    vm.OutputPort :?> Diplo.Gui.Services.AvaloniaOutputPort

[<Fact>]
let ``MainWindow expose les six onglets non nuls`` () =
    let vm = mainWindowVm ()
    vm.ContainerTab |> should not' (be Null)
    vm.ImagesTab |> should not' (be Null)
    vm.VolumeTab |> should not' (be Null)
    vm.NetworkTab |> should not' (be Null)
    vm.ComposeTab |> should not' (be Null)
    vm.SettingsTab |> should not' (be Null)

[<Fact>]
let ``MainWindow expose les onglets du bon type`` () =
    let vm = mainWindowVm ()

    vm.ContainerTab
    |> should be instanceOfType<Diplo.Gui.ViewModels.ContainerTabViewModel>

    vm.ImagesTab
    |> should be instanceOfType<Diplo.Gui.ViewModels.ImagesTabViewModel>

    vm.VolumeTab
    |> should be instanceOfType<Diplo.Gui.ViewModels.VolumeTabViewModel>

    vm.NetworkTab
    |> should be instanceOfType<Diplo.Gui.ViewModels.NetworkTabViewModel>

    vm.ComposeTab
    |> should be instanceOfType<Diplo.Gui.ViewModels.ComposeTabViewModel>

    vm.SettingsTab
    |> should be instanceOfType<Diplo.Gui.ViewModels.SettingsTabViewModel>

[<Fact>]
let ``MainWindow expose OutputPort en tant qu'IOutputPort non nul`` () =
    let vm = mainWindowVm ()
    vm.OutputPort |> should not' (be Null)
    vm.OutputPort |> should be instanceOfType<Diplo.Core.Output.IOutputPort>

[<Fact>]
let ``MainWindow LogOutput reflète les lignes écrites sur l'OutputPort`` () =
    let vm = mainWindowVm ()
    vm.OutputPort.WriteLine("ligne de test")
    waitPump (fun () -> vm.LogOutput.Contains "ligne de test") |> should equal true
    vm.LogOutput.Contains "ligne de test" |> should equal true

[<Fact>]
let ``MainWindow LogOutput agrège plusieurs lignes avec format horodaté`` () =
    let vm = mainWindowVm ()
    vm.OutputPort.WriteLine("première")
    vm.OutputPort.WriteSuccess("seconde")
    vm.OutputPort.WriteWarning("troisième")
    waitPump (fun () -> vm.LogOutput.Contains "troisième") |> should equal true
    vm.LogOutput.Contains "[" |> should equal true
    vm.LogOutput.Contains "première" |> should equal true
    vm.LogOutput.Contains "seconde" |> should equal true
    vm.LogOutput.Contains "troisième" |> should equal true

[<Fact>]
let ``MainWindow LogOutput est tronqué à 500 lignes`` () =
    let vm = mainWindowVm ()

    for i in 1 .. 520 do
        vm.OutputPort.WriteLine($"message %d{i}")

    waitPump (fun () ->
        (mainWindowPort vm).LogLines.Count >= 500
        && vm.LogOutput.Contains "message 520")
    |> should equal true

    (mainWindowPort vm).LogLines.Count |> should equal 500
    (mainWindowPort vm).LogLines.[0].Text |> should equal "message 21"
    (mainWindowPort vm).LogLines.[(mainWindowPort vm).LogLines.Count - 1].Text
    |> should equal "message 520"

[<Fact>]
let ``MainWindow AboutCommand écrit les deux lignes d'information`` () =
    let vm = mainWindowVm ()
    vm.AboutCommand.Execute(null)

    waitPump (fun () -> (mainWindowPort vm).LogLines.Count >= 2)
    |> should equal true

    vm.LogOutput.Contains "Diplo — Gestion Docker" |> should equal true
    vm.LogOutput.Contains "Interface graphique Avalonia" |> should equal true

[<Fact>]
let ``MainWindow QuitCommand est exposé`` () =
    let vm = mainWindowVm ()
    vm.QuitCommand |> should not' (be Null)

[<Fact>]
let ``MainWindow ExportLogCommand sans fournisseur de stockage ecrit un avertissement`` () =
    let vm = mainWindowVm ()
    vm.ExportLogCommand.Execute(null)

    waitPump (fun () ->
        (mainWindowPort vm).LogLines
        |> Seq.exists (fun l -> l.Text.Contains "Fournisseur de stockage non disponible"))
    |> should equal true

[<Fact>]
let ``MainWindow ExportLogCommand avec un picker annule reste silencieux`` () =
    let vm = mainWindowVm ()
    // Fake sans fichier retourné : SaveFilePickerAsync renvoie null.
    vm.SetStorageProvider(new FakeStorageProvider())
    vm.OutputPort.WriteLine("ligne avant export")
    waitPump (fun () -> vm.LogOutput.Contains "ligne avant export") |> should equal true

    vm.ExportLogCommand.Execute(null)

    // Aucun succès ni erreur : l'annulation n'écrit rien.
    waitPump (fun () ->
        (mainWindowPort vm).LogLines
        |> Seq.exists (fun l -> l.Text.Contains "Journal exporté"))
    |> should equal false

    waitPump (fun () ->
        (mainWindowPort vm).LogLines
        |> Seq.exists (fun l -> l.Text.Contains "Fournisseur de stockage non disponible"))
    |> should equal false

[<Fact>]
let ``MainWindow ExportLogCommand avec un fournisseur écrit le journal dans le fichier choisi`` () =
    let vm = mainWindowVm ()
    vm.OutputPort.WriteLine("ligne un")
    vm.OutputPort.WriteSuccess("ligne deux")
    waitPump (fun () -> vm.LogOutput.Contains "ligne deux") |> should equal true

    let dir = Diplo.TestHelpers.TestHelpers.createTempDir "journal-export-succes"
    let target = System.IO.Path.Combine(dir, "journal.txt")

    try
        vm.SetStorageProvider(new FakeStorageProvider(saveFile = new FakeStorageFile(target)))
        vm.ExportLogCommand.Execute(null)

        waitPump (fun () ->
            (mainWindowPort vm).LogLines
            |> Seq.exists (fun l ->
                l.Text.Contains "Journal exporté"
                && l.Text.Contains "journal.txt"))
        |> should equal true

        System.IO.File.Exists target |> should equal true
        let content = System.IO.File.ReadAllLines target
        content.Length |> should equal 2
        content.[0].Contains "ligne un" |> should equal true
        content.[1].Contains "ligne deux" |> should equal true
    finally
        Diplo.TestHelpers.TestHelpers.cleanupDir dir

[<Fact>]
let ``MainWindow ExportJournalTo ecrit les lignes du journal dans un fichier`` () =
    let vm = mainWindowVm ()
    vm.OutputPort.WriteLine("première ligne")
    vm.OutputPort.WriteSuccess("seconde ligne")
    waitPump (fun () -> vm.LogOutput.Contains "seconde ligne") |> should equal true

    let dir = Diplo.TestHelpers.TestHelpers.createTempDir "journal-export"
    let path = System.IO.Path.Combine(dir, "journal.txt")

    try
        let count = vm.ExportJournalTo(path) |> Async.AwaitTask |> Async.RunSynchronously
        count |> should equal 2

        let content = System.IO.File.ReadAllLines path
        content.Length |> should equal 2
        content.[0].Contains "première ligne" |> should equal true
        content.[1].Contains "seconde ligne" |> should equal true

        content
        |> Array.forall (fun l -> l.Contains "[")
        |> should equal true
    finally
        Diplo.TestHelpers.TestHelpers.cleanupDir dir
