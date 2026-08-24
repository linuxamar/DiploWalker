module Diplo.Gui.Tests.TabViewModelTests

open System
open System.IO
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open System.Windows.Input
open Xunit
open FsUnit.Xunit
open Diplo.Core
open Diplo.Core.Output
open Diplo.Grpc.Container
open Diplo.Gui.Services
open Diplo.Gui.ViewModels
open Diplo.TestHelpers

// ── MockOutputPort ──────────────────────────────────────────

[<Fact>]
let ``MockOutputPort.WriteLine enregistre le message`` () =
    let port = MockOutputPort()
    (port :> IOutputPort).WriteLine("hello")
    port.Messages |> should contain "hello"

[<Fact>]
let ``MockOutputPort.WriteError enregistre l'erreur`` () =
    let port = MockOutputPort()
    (port :> IOutputPort).WriteError("err")
    port.Errors |> should contain "err"

[<Fact>]
let ``MockOutputPort.WriteSuccess enregistre le succes`` () =
    let port = MockOutputPort()
    (port :> IOutputPort).WriteSuccess("ok")
    port.Successes |> should contain "ok"

[<Fact>]
let ``MockOutputPort.WriteWarning enregistre l'avertissement`` () =
    let port = MockOutputPort()
    (port :> IOutputPort).WriteWarning("warn")
    port.Warnings |> should contain "warn"

[<Fact>]
let ``MockOutputPort.Clear remet tout à zéro`` () =
    let port = MockOutputPort()
    (port :> IOutputPort).WriteLine("x")
    (port :> IOutputPort).WriteError("y")
    port.Clear()
    port.Messages |> should be Empty
    port.Errors |> should be Empty

// ── ComposeTabViewModel ─────────────────────────────────────

[<Fact>]
let ``ComposeTabViewModel expose les 6 commandes ICommand`` () =
    let port = MockOutputPort()
    let vm = ComposeTabViewModel(port)
    vm.ComposeUpCommand |> should not' (be Null)
    vm.ComposeDownCommand |> should not' (be Null)
    vm.ComposePsCommand |> should not' (be Null)
    vm.ComposeLogsCommand |> should not' (be Null)
    vm.ComposePullCommand |> should not' (be Null)
    vm.ComposeBuildCommand |> should not' (be Null)

[<Fact>]
let ``ComposeTabViewModel etat initial`` () =
    let port = MockOutputPort()
    let vm = ComposeTabViewModel(port)
    vm.ComposeFilePath |> should equal ""
    vm.ComposeServiceName |> should equal ""
    vm.ComposeServices.Count |> should equal 0

[<Fact>]
let ``ComposeTabViewModel proprietes declenchent PropertyChanged`` () =
    let port = MockOutputPort()
    let vm = ComposeTabViewModel(port)
    let mutable changed = []
    vm.PropertyChanged.Add(fun e -> changed <- e.PropertyName :: changed)
    vm.ComposeFilePath <- "/test/docker-compose.yml"
    vm.ComposeServiceName <- "web"
    changed |> should contain "ComposeFilePath"
    changed |> should contain "ComposeServiceName"

// ── ContainerTabViewModel ───────────────────────────────────

[<Fact>]
let ``ContainerTabViewModel expose les commandes ICommand`` () =
    let port = MockOutputPort()
    let vm = new ContainerTabViewModel(port)
    vm.ListContainersCommand |> should not' (be Null)
    vm.StartContainerCommand |> should not' (be Null)
    vm.StopContainerCommand |> should not' (be Null)
    vm.DeleteContainerCommand |> should not' (be Null)
    vm.InspectContainerCommand |> should not' (be Null)
    vm.CreateContainerCommand |> should not' (be Null)
    vm.RenameContainerCommand |> should not' (be Null)
    vm.PullImageCommand |> should not' (be Null)
    vm.VersionCommand |> should not' (be Null)
    vm.TopContainerCommand |> should not' (be Null)
    vm.StatsContainerCommand |> should not' (be Null)
    vm.GetContainerLogsCommand |> should not' (be Null)
    vm.ExecInContainerCommand |> should not' (be Null)
    vm.ListNamespacesCommand |> should not' (be Null)
    vm.ListImagesCommand |> should not' (be Null)
    vm.InspectImageCommand |> should not' (be Null)
    vm.RemoveImageCommand |> should not' (be Null)
    vm.TagImageCommand |> should not' (be Null)
    vm.RegistryLoginCommand |> should not' (be Null)
    vm.RegistryLogoutCommand |> should not' (be Null)

[<Fact>]
let ``ContainerTabViewModel etat initial`` () =
    let port = MockOutputPort()
    let vm = new ContainerTabViewModel(port)
    vm.ContainerIdInput |> should equal ""
    vm.ContainerNameInput |> should equal ""
    vm.ContainerMounts |> should equal ""
    vm.ContainerTimeout |> should equal 10
    vm.Containers.Count |> should equal 0
    vm.Images.Count |> should equal 0

[<Fact>]
let ``ContainerTabViewModel user pull etat initial`` () =
    let port = MockOutputPort()
    let vm = new ContainerTabViewModel(port)
    vm.ContainerImageUser |> should equal ""

[<Fact>]
let ``ContainerTabViewModel user pull declenche PropertyChanged`` () =
    let port = MockOutputPort()
    let vm = new ContainerTabViewModel(port)
    let mutable changed = []
    vm.PropertyChanged.Add(fun e -> changed <- e.PropertyName :: changed)
    vm.ContainerImageUser <- "inline:secret"
    changed |> should contain "ContainerImageUser"

[<Fact>]
let ``ContainerTabViewModel registres etat initial`` () =
    let port = MockOutputPort()
    let vm = new ContainerTabViewModel(port)
    vm.RegistryInput |> should equal ""
    vm.RegistryUsernameInput |> should equal ""
    vm.RegistryPasswordInput |> should equal ""

[<Fact>]
let ``ContainerTabViewModel proprietes registres declenchent PropertyChanged`` () =
    let port = MockOutputPort()
    let vm = new ContainerTabViewModel(port)
    let mutable changed = []
    vm.PropertyChanged.Add(fun e -> changed <- e.PropertyName :: changed)
    vm.RegistryInput <- "myregistry.azurecr.io"
    vm.RegistryUsernameInput <- "user"
    vm.RegistryPasswordInput <- "secret"
    changed |> should contain "RegistryInput"
    changed |> should contain "RegistryUsernameInput"
    changed |> should contain "RegistryPasswordInput"

[<Fact>]
let ``ContainerTabViewModel proprietes sette declenchent PropertyChanged`` () =
    let port = MockOutputPort()
    let vm = new ContainerTabViewModel(port)
    let mutable changed = []
    vm.PropertyChanged.Add(fun e -> changed <- e.PropertyName :: changed)
    vm.ContainerIdInput <- "abc123"
    vm.ContainerNameInput <- "mon-srv"
    vm.ContainerMounts <- @"src=C:\donnees,dst=C:\conteneur\donnees"
    changed |> should contain "ContainerIdInput"
    changed |> should contain "ContainerNameInput"
    changed |> should contain "ContainerMounts"

// ── VolumeTabViewModel ──────────────────────────────────────

[<Fact>]
let ``VolumeTabViewModel expose les 10 commandes ICommand`` () =
    let port = MockOutputPort()
    let vm = VolumeTabViewModel(port)
    vm.ListVolumesCommand |> should not' (be Null)
    vm.InspectVolumeCommand |> should not' (be Null)
    vm.CreateVolumeCommand |> should not' (be Null)
    vm.RemoveVolumeCommand |> should not' (be Null)
    vm.MountVolumeCommand |> should not' (be Null)
    vm.UnmountVolumeCommand |> should not' (be Null)
    vm.PruneVolumesCommand |> should not' (be Null)
    vm.CreateImageCommand |> should not' (be Null)
    vm.BrowseSourceCommand |> should not' (be Null)
    vm.BrowseDestCommand |> should not' (be Null)

[<Fact>]
let ``VolumeTabViewModel etat initial image disque`` () =
    let port = MockOutputPort()
    let vm = VolumeTabViewModel(port)
    vm.ImageSourceDir |> should equal ""
    vm.ImageDestPath |> should equal ""
    vm.ImageFormat |> should equal "raw"

[<Fact>]
let ``VolumeTabViewModel etat initial`` () =
    let port = MockOutputPort()
    let vm = VolumeTabViewModel(port)
    vm.VolumeIdInput |> should equal ""
    vm.VolumeNameInput |> should equal ""
    vm.Volumes.Count |> should equal 0

// ── NetworkTabViewModel ─────────────────────────────────────█

[<Fact>]
let ``NetworkTabViewModel expose les 8 commandes ICommand`` () =
    let port = MockOutputPort()
    let vm = NetworkTabViewModel(port)
    vm.ListNetworksCommand |> should not' (be Null)
    vm.InspectNetworkCommand |> should not' (be Null)
    vm.CreateNetworkCommand |> should not' (be Null)
    vm.RemoveNetworkCommand |> should not' (be Null)
    vm.ConnectNetworkCommand |> should not' (be Null)
    vm.DisconnectNetworkCommand |> should not' (be Null)
    vm.RunCniPluginCommand |> should not' (be Null)
    vm.PruneNetworksCommand |> should not' (be Null)

[<Fact>]
let ``NetworkTabViewModel etat initial`` () =
    let port = MockOutputPort()
    let vm = NetworkTabViewModel(port)
    vm.NetworkIdInput |> should equal ""
    vm.NetworkNameInput |> should equal ""
    vm.Networks.Count |> should equal 0

// ── SettingsTabViewModel ────────────────────────────────

let private withConfigHome (action: string -> unit) =
    let old = Environment.GetEnvironmentVariable("DIPLO_CONFIG_HOME")

    let home =
        Path.Combine(Path.GetTempPath(), "diplo-gui-config-" + Guid.NewGuid().ToString("N"))

    Environment.SetEnvironmentVariable("DIPLO_CONFIG_HOME", home)

    try
        action home
    finally
        Environment.SetEnvironmentVariable("DIPLO_CONFIG_HOME", old)
        DiploConfig.invalidate ()

        if Directory.Exists home then
            Directory.Delete(home, true)

[<Fact>]
let ``SettingsTabViewModel expose les commandes ICommand`` () =
    withConfigHome (fun _ ->
        let port = MockOutputPort()
        let vm = SettingsTabViewModel(port)
        vm.SaveCommand |> should not' (be Null)
        vm.ReloadCommand |> should not' (be Null))

[<Fact>]
let ``SettingsTabViewModel etat initial avec valeurs par défaut`` () =
    withConfigHome (fun home ->
        let port = MockOutputPort()
        let vm = SettingsTabViewModel(port)
        vm.ConfigPath |> should equal (Path.Combine(home, "diplo.json"))
        vm.ContainerAddress |> should equal "localhost:5001"
        vm.VolumeAddress |> should equal "localhost:5002"
        vm.NetworkAddress |> should equal "localhost:5003")

[<Fact>]
let ``SettingsTabViewModel SaveCommand ecrit la configuration sur le disque`` () =
    withConfigHome (fun home ->
        let port = MockOutputPort()
        let vm = SettingsTabViewModel(port)
        vm.ContainerAddress <- "http://pipe:/diplo-container"
        vm.VolumeAddress <- "localhost:9002"
        vm.NetworkAddress <- "localhost:9003"
        (vm.SaveCommand).Execute(null)
        let path = Path.Combine(home, "diplo.json")
        File.Exists path |> should equal true
        let (c, v, n) = DiploConfig.load path
        c |> should equal (Some "http://pipe:/diplo-container")
        v |> should equal (Some "http://localhost:9002")
        n |> should equal (Some "http://localhost:9003")

        port.Messages
        |> Seq.exists (fun m -> m.Contains "Configuration client enregistrée")
        |> should equal true

        vm.StatusMessage |> should haveSubstring "Configuration enregistrée")

[<Fact>]
let ``SettingsTabViewModel SaveCommand avec adresse vide signale une erreur`` () =
    withConfigHome (fun _ ->
        let port = MockOutputPort()
        let vm = SettingsTabViewModel(port)
        vm.ContainerAddress <- ""
        (vm.SaveCommand).Execute(null)
        vm.StatusMessage |> should haveSubstring "obligatoires")

[<Fact>]
let ``SettingsTabViewModel SaveCommand applique la configuration sans redemarrage`` () =
    withConfigHome (fun _ ->
        let port = MockOutputPort()
        let vm = SettingsTabViewModel(port)
        vm.ContainerAddress <- "http://pipe:/diplo-container"
        vm.VolumeAddress <- "localhost:9002"
        vm.NetworkAddress <- "localhost:9003"
        (vm.SaveCommand).Execute(null)

        DiploConfig.containerAddress ()
        |> should equal (Some "http://pipe:/diplo-container")

        DiploConfig.volumeAddress () |> should equal (Some "http://localhost:9002"))

[<Fact>]
let ``SettingsTabViewModel ReloadCommand relit la configuration depuis le disque`` () =
    withConfigHome (fun home ->
        let path = Path.Combine(home, "diplo.json")
        DiploConfig.save path "localhost:7001" "localhost:7002" "localhost:7003"
        let port = MockOutputPort()
        let vm = SettingsTabViewModel(port)
        vm.ContainerAddress |> should equal "http://localhost:7001"
        vm.VolumeAddress |> should equal "http://localhost:7002"
        vm.NetworkAddress |> should equal "http://localhost:7003"
        DiploConfig.save path "localhost:8001" "localhost:8002" "localhost:8003"
        (vm.ReloadCommand).Execute(null)
        vm.ContainerAddress |> should equal "http://localhost:8001"
        vm.StatusMessage |> should haveSubstring "relue")

// ── Journaux (source injectée) ─────────────────────────────────

let private fakeEntry (line: string) =
    { ContainerLogEntry.Timestamp = "2026-08-12T10:00:00Z"
      Stream = "stdout"
      Log = line }

let private streamOf (lines: string list) : IAsyncEnumerable<ContainerLogEntry> =
    { new IAsyncEnumerable<ContainerLogEntry> with
        member _.GetAsyncEnumerator(_ct) =
            let e = (lines |> List.map fakeEntry |> Seq.ofList).GetEnumerator()

            { new IAsyncEnumerator<ContainerLogEntry> with
                member _.Current = e.Current
                member _.MoveNextAsync() = ValueTask<bool>(e.MoveNext())

                member _.DisposeAsync() =
                    e.Dispose()
                    ValueTask() } }

let private fakeLogsSource () : IContainerLogsSource =
    { new IContainerLogsSource with
        member _.GetStream(id, follow, tail, since, ct) = streamOf [ "ligne 1"; "ligne 2" ]

        member _.GetSnapshot(id, tail, since, ct) =
            task {
                return
                    seq {
                        fakeEntry "ligne 1"
                        fakeEntry "ligne 2"
                    }
            }
      interface IDisposable with
          member _.Dispose() = () }

let private waitUntil (predicate: unit -> bool) =
    let sw = Diagnostics.Stopwatch.StartNew()

    while not (predicate ()) && sw.ElapsedMilliseconds < 2000L do
        Thread.Sleep(20)

    predicate ()

[<Fact>]
let ``ContainerTabViewModel GetContainerLogs en mode suivi emet chaque ligne au fil de l'eau`` () =
    let port = MockOutputPort()
    let vm = new ContainerTabViewModel(port, logsSourceFactory = fakeLogsSource)
    vm.ContainerIdInput <- "c1"
    vm.ContainerFollow <- true
    (vm.GetContainerLogsCommand :> ICommand).Execute(null)
    // le flux émet 2 lignes : chacune est écrite immédiatement
    waitUntil (fun () -> port.Messages.Length = 2) |> should equal true
    port.Messages |> should contain "[2026-08-12T10:00:00Z] ligne 1"
    port.Messages |> should contain "[2026-08-12T10:00:00Z] ligne 2"

[<Fact>]
let ``ContainerTabViewModel GetContainerLogs sans suivi affiche l'instantané en bloc`` () =
    let port = MockOutputPort()
    let vm = new ContainerTabViewModel(port, logsSourceFactory = fakeLogsSource)
    vm.ContainerIdInput <- "c1"
    vm.ContainerFollow <- false
    (vm.GetContainerLogsCommand :> ICommand).Execute(null)
    waitUntil (fun () -> port.Successes.Length = 1) |> should equal true
    Assert.Contains("[2026-08-12T10:00:00Z] ligne 1", port.Successes.Head)
    Assert.Contains("ligne 2", port.Successes.Head)

// ── ComposeEditorViewModel ────────────────────────────────────

[<Fact>]
let ``ComposeEditorViewModel etat initial`` () =
    let vm = ComposeEditorViewModel()
    vm.FilePath |> should equal ""
    vm.Errors.Count |> should equal 0
    vm.Document.Text |> should equal ""

[<Fact>]
let ``ComposeEditorViewModel LoadFile charge le contenu`` () =
    let dir = TestHelpers.createTempDir "editor-test"

    try
        let path = IO.Path.Combine(dir, "docker-compose.yml")
        IO.File.WriteAllText(path, "version: \"3.8\"\nservices:\n  web:\n    image: nginx\n")
        let vm = ComposeEditorViewModel()
        vm.LoadFile(path)
        vm.FilePath |> should equal path
        vm.Document.Text.Contains("nginx") |> should equal true
    finally
        TestHelpers.cleanupDir dir

[<Fact>]
let ``ComposeEditorViewModel Validate detecte un document YAML valide`` () =
    let vm = ComposeEditorViewModel()
    vm.Document.Text <- "version: \"3.8\"\nservices:\n  web:\n    image: nginx\n"
    vm.Validate()
    vm.Errors.Count |> should equal 0

[<Fact>]
let ``ComposeEditorViewModel Validate detecte l'absence de services`` () =
    let vm = ComposeEditorViewModel()
    vm.Document.Text <- "version: \"3.8\"\n"
    vm.Validate()
    vm.Errors.Count |> should be (greaterThan 0)
    vm.Errors.[0].Message |> should haveSubstring "services"

[<Fact>]
let ``ComposeEditorViewModel Validate detecte un service sans image ni build`` () =
    let vm = ComposeEditorViewModel()
    vm.Document.Text <- "services:\n  web:\n    command: echo\n"
    vm.Validate()
    vm.Errors.Count |> should be (greaterThan 0)
    vm.Errors.[0].Message |> should haveSubstring "image"
    vm.Errors.[0].Sévérité |> should equal "erreur"

[<Fact>]
let ``ComposeEditorViewModel Validate detecte un YAML invalide`` () =
    let vm = ComposeEditorViewModel()
    vm.Document.Text <- "services:\n  web:\n    image: nginx\n  [\n"
    vm.Validate()
    vm.Errors.Count |> should be (greaterThan 0)

[<Fact>]
let ``ComposeEditorViewModel Validate detecte un document vide`` () =
    let vm = ComposeEditorViewModel()
    vm.Document.Text <- ""
    vm.Validate()
    vm.Errors.Count |> should be (greaterThan 0)
    vm.Errors.[0].Sévérité |> should equal "avertissement"

[<Fact>]
let ``ComposeEditorViewModel Save ecrit sur le disque`` () =
    let dir = TestHelpers.createTempDir "editor-save"

    try
        let path = IO.Path.Combine(dir, "docker-compose.yml")
        let vm = ComposeEditorViewModel()
        vm.Document.Text <- "services:\n  web:\n    image: nginx\n"
        vm.SaveAs(path)
        vm.FilePath |> should equal path
        IO.File.ReadAllText(path).Contains("nginx") |> should equal true
    finally
        TestHelpers.cleanupDir dir

[<Fact>]
let ``ComposeEditorViewModel proprietes declenchent PropertyChanged`` () =
    let vm = ComposeEditorViewModel()
    let mutable changed = []
    vm.PropertyChanged.Add(fun e -> changed <- e.PropertyName :: changed)
    vm.FilePath <- "/test/path.yml"
    vm.SyntaxHighlightingName <- "Custom"
    changed |> should contain "FilePath"
    changed |> should contain "SyntaxHighlightingName"

// ── ComposeTabViewModel — nouvelles fonctionnalités ───────────

[<Fact>]
let ``ComposeTabViewModel expose les commandes de l'editeur`` () =
    let port = MockOutputPort()
    let vm = ComposeTabViewModel(port)
    vm.OpenComposeFileCommand |> should not' (be Null)
    vm.SaveComposeFileCommand |> should not' (be Null)
    vm.ValidateComposeFileCommand |> should not' (be Null)
    vm.InspectImageCommand |> should not' (be Null)

[<Fact>]
let ``ComposeTabViewModel ComposeEditor n'est pas null`` () =
    let port = MockOutputPort()
    let vm = ComposeTabViewModel(port)
    vm.ComposeEditor |> should not' (be Null)

[<Fact>]
let ``ComposeTabViewModel InspectImageRef etat initial`` () =
    let port = MockOutputPort()
    let vm = ComposeTabViewModel(port)
    vm.InspectImageRef |> should equal ""

[<Fact>]
let ``ComposeTabViewModel InspectImageRef declenche PropertyChanged`` () =
    let port = MockOutputPort()
    let vm = ComposeTabViewModel(port)
    let mutable changed = []
    vm.PropertyChanged.Add(fun e -> changed <- e.PropertyName :: changed)
    vm.InspectImageRef <- "nginx:latest"
    changed |> should contain "InspectImageRef"

[<Fact>]
let ``ComposeTabViewModel OnSelectedServiceChanged met a jour InspectImageRef`` () =
    let port = MockOutputPort()
    let vm = ComposeTabViewModel(port)
    vm.OnSelectedServiceChanged("alpine:3.18")
    vm.InspectImageRef |> should equal "alpine:3.18"

[<Fact>]
let ``ComposeTabViewModel ValidateComposeFile sans fichier affiche avertissement`` () =
    let port = MockOutputPort()
    let vm = ComposeTabViewModel(port)
    (vm.ValidateComposeFileCommand :> System.Windows.Input.ICommand).Execute(null)
    // Pas de fichier chargé, validate fonctionne sur le document vide
    vm.ComposeEditor.Errors.Count |> should be (greaterThan 0)

[<Fact>]
let ``ComposeTabViewModel SaveComposeFile sans fichier ouvre le dialogue`` () =
    let port = MockOutputPort()
    let vm = ComposeTabViewModel(port)
    (vm.SaveComposeFileCommand :> System.Windows.Input.ICommand).Execute(null)
    // Sans storageProvider, écrit un avertissement
    port.Warnings
    |> Seq.exists (fun w -> w.Contains "Fournisseur")
    |> should equal true

[<Fact>]
let ``ComposeTabViewModel ComposeFilePath charge l'editeur`` () =
    let dir = TestHelpers.createTempDir "compose-load"

    try
        let path = IO.Path.Combine(dir, "docker-compose.yml")
        IO.File.WriteAllText(path, "services:\n  web:\n    image: nginx\n")
        let port = MockOutputPort()
        let vm = ComposeTabViewModel(port)
        vm.ComposeFilePath <- path
        vm.ComposeEditor.Document.Text.Contains("nginx") |> should equal true
    finally
        TestHelpers.cleanupDir dir
