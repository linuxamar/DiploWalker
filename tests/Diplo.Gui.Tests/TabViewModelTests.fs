module Diplo.Gui.Tests.TabViewModelTests

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open System.Windows.Input
open Xunit
open FsUnit.Xunit
open Diplo.Core.Output
open Diplo.Grpc.Container
open Diplo.Gui.Services
open Diplo.Gui.ViewModels

// ── FakeOutputPort ──────────────────────────────────────────

[<Fact>]
let ``FakeOutputPort.WriteLine enregistre le message`` () =
    let port = FakeOutputPort.FakeOutputPort()
    (port :> IOutputPort).WriteLine("hello")
    port.Messages |> should contain "hello"

[<Fact>]
let ``FakeOutputPort.WriteError enregistre l'erreur`` () =
    let port = FakeOutputPort.FakeOutputPort()
    (port :> IOutputPort).WriteError("err")
    port.Errors |> should contain "err"

[<Fact>]
let ``FakeOutputPort.WriteSuccess enregistre le succes`` () =
    let port = FakeOutputPort.FakeOutputPort()
    (port :> IOutputPort).WriteSuccess("ok")
    port.Successes |> should contain "ok"

[<Fact>]
let ``FakeOutputPort.WriteWarning enregistre l'avertissement`` () =
    let port = FakeOutputPort.FakeOutputPort()
    (port :> IOutputPort).WriteWarning("warn")
    port.Warnings |> should contain "warn"

[<Fact>]
let ``FakeOutputPort.Clear remet tout à zéro`` () =
    let port = FakeOutputPort.FakeOutputPort()
    (port :> IOutputPort).WriteLine("x")
    (port :> IOutputPort).WriteError("y")
    port.Clear()
    port.Messages |> should be Empty
    port.Errors |> should be Empty

// ── ComposeTabViewModel ─────────────────────────────────────

[<Fact>]
let ``ComposeTabViewModel expose les 6 commandes ICommand`` () =
    let port = FakeOutputPort.FakeOutputPort()
    let vm = ComposeTabViewModel(port)
    vm.ComposeUpCommand     :> ICommand |> should not' (be Null)
    vm.ComposeDownCommand   :> ICommand |> should not' (be Null)
    vm.ComposePsCommand     :> ICommand |> should not' (be Null)
    vm.ComposeLogsCommand   :> ICommand |> should not' (be Null)
    vm.ComposePullCommand   :> ICommand |> should not' (be Null)
    vm.ComposeBuildCommand  :> ICommand |> should not' (be Null)

[<Fact>]
let ``ComposeTabViewModel etat initial`` () =
    let port = FakeOutputPort.FakeOutputPort()
    let vm = ComposeTabViewModel(port)
    vm.ComposeFilePath   |> should equal ""
    vm.ComposeServiceName |> should equal ""
    vm.ComposeServices.Count |> should equal 0

[<Fact>]
let ``ComposeTabViewModel proprietes declenchent PropertyChanged`` () =
    let port = FakeOutputPort.FakeOutputPort()
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
    let port = FakeOutputPort.FakeOutputPort()
    let vm = ContainerTabViewModel(port)
    vm.ListContainersCommand     :> ICommand |> should not' (be Null)
    vm.StartContainerCommand     :> ICommand |> should not' (be Null)
    vm.StopContainerCommand      :> ICommand |> should not' (be Null)
    vm.DeleteContainerCommand    :> ICommand |> should not' (be Null)
    vm.InspectContainerCommand   :> ICommand |> should not' (be Null)
    vm.CreateContainerCommand    :> ICommand |> should not' (be Null)
    vm.RenameContainerCommand    :> ICommand |> should not' (be Null)
    vm.PullImageCommand          :> ICommand |> should not' (be Null)
    vm.VersionCommand            :> ICommand |> should not' (be Null)
    vm.TopContainerCommand       :> ICommand |> should not' (be Null)
    vm.StatsContainerCommand     :> ICommand |> should not' (be Null)
    vm.GetContainerLogsCommand   :> ICommand |> should not' (be Null)
    vm.ExecInContainerCommand    :> ICommand |> should not' (be Null)
    vm.ListNamespacesCommand     :> ICommand |> should not' (be Null)
    vm.ListImagesCommand         :> ICommand |> should not' (be Null)
    vm.InspectImageCommand       :> ICommand |> should not' (be Null)
    vm.RemoveImageCommand        :> ICommand |> should not' (be Null)
    vm.TagImageCommand           :> ICommand |> should not' (be Null)
    vm.RegistryLoginCommand      :> ICommand |> should not' (be Null)
    vm.RegistryLogoutCommand     :> ICommand |> should not' (be Null)

[<Fact>]
let ``ContainerTabViewModel etat initial`` () =
    let port = FakeOutputPort.FakeOutputPort()
    let vm = ContainerTabViewModel(port)
    vm.ContainerIdInput     |> should equal ""
    vm.ContainerNameInput   |> should equal ""
    vm.ContainerMounts      |> should equal ""
    vm.ContainerTimeout     |> should equal 10
    vm.Containers.Count     |> should equal 0
    vm.Images.Count         |> should equal 0

[<Fact>]
let ``ContainerTabViewModel user pull etat initial`` () =
    let port = FakeOutputPort.FakeOutputPort()
    let vm = ContainerTabViewModel(port)
    vm.ContainerImageUser |> should equal ""

[<Fact>]
let ``ContainerTabViewModel user pull declenche PropertyChanged`` () =
    let port = FakeOutputPort.FakeOutputPort()
    let vm = ContainerTabViewModel(port)
    let mutable changed = []
    vm.PropertyChanged.Add(fun e -> changed <- e.PropertyName :: changed)
    vm.ContainerImageUser <- "inline:secret"
    changed |> should contain "ContainerImageUser"

[<Fact>]
let ``ContainerTabViewModel registres etat initial`` () =
    let port = FakeOutputPort.FakeOutputPort()
    let vm = ContainerTabViewModel(port)
    vm.RegistryInput           |> should equal ""
    vm.RegistryUsernameInput   |> should equal ""
    vm.RegistryPasswordInput   |> should equal ""

[<Fact>]
let ``ContainerTabViewModel proprietes registres declenchent PropertyChanged`` () =
    let port = FakeOutputPort.FakeOutputPort()
    let vm = ContainerTabViewModel(port)
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
    let port = FakeOutputPort.FakeOutputPort()
    let vm = ContainerTabViewModel(port)
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
let ``VolumeTabViewModel expose les 7 commandes ICommand`` () =
    let port = FakeOutputPort.FakeOutputPort()
    let vm = VolumeTabViewModel(port)
    vm.ListVolumesCommand     :> ICommand |> should not' (be Null)
    vm.InspectVolumeCommand   :> ICommand |> should not' (be Null)
    vm.CreateVolumeCommand    :> ICommand |> should not' (be Null)
    vm.RemoveVolumeCommand    :> ICommand |> should not' (be Null)
    vm.MountVolumeCommand     :> ICommand |> should not' (be Null)
    vm.UnmountVolumeCommand   :> ICommand |> should not' (be Null)
    vm.PruneVolumesCommand    :> ICommand |> should not' (be Null)

[<Fact>]
let ``VolumeTabViewModel etat initial`` () =
    let port = FakeOutputPort.FakeOutputPort()
    let vm = VolumeTabViewModel(port)
    vm.VolumeIdInput     |> should equal ""
    vm.VolumeNameInput   |> should equal ""
    vm.Volumes.Count     |> should equal 0

// ── NetworkTabViewModel ─────────────────────────────────────█

[<Fact>]
let ``NetworkTabViewModel expose les 8 commandes ICommand`` () =
    let port = FakeOutputPort.FakeOutputPort()
    let vm = NetworkTabViewModel(port)
    vm.ListNetworksCommand       :> ICommand |> should not' (be Null)
    vm.InspectNetworkCommand     :> ICommand |> should not' (be Null)
    vm.CreateNetworkCommand      :> ICommand |> should not' (be Null)
    vm.RemoveNetworkCommand      :> ICommand |> should not' (be Null)
    vm.ConnectNetworkCommand     :> ICommand |> should not' (be Null)
    vm.DisconnectNetworkCommand  :> ICommand |> should not' (be Null)
    vm.RunCniPluginCommand       :> ICommand |> should not' (be Null)
    vm.PruneNetworksCommand      :> ICommand |> should not' (be Null)

[<Fact>]
let ``NetworkTabViewModel etat initial`` () =
    let port = FakeOutputPort.FakeOutputPort()
    let vm = NetworkTabViewModel(port)
    vm.NetworkIdInput   |> should equal ""
    vm.NetworkNameInput |> should equal ""
    vm.Networks.Count   |> should equal 0

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
                member _.DisposeAsync() = e.Dispose(); ValueTask() } }

let private fakeLogsSource () : IContainerLogsSource =
    { new IContainerLogsSource with
        member _.GetStream(id, follow, tail, since, ct) = streamOf [ "ligne 1"; "ligne 2" ]
        member _.GetSnapshot(id, tail, since, ct) =
            task { return seq { fakeEntry "ligne 1"; fakeEntry "ligne 2" } :> seq<ContainerLogEntry> }
      interface IDisposable with
        member _.Dispose() = () }

let private waitUntil (predicate: unit -> bool) =
    let sw = Diagnostics.Stopwatch.StartNew()
    while not (predicate ()) && sw.ElapsedMilliseconds < 2000L do
        Thread.Sleep(20)
    predicate ()

[<Fact>]
let ``ContainerTabViewModel GetContainerLogs en mode suivi emet chaque ligne au fil de l'eau`` () =
    let port = FakeOutputPort.FakeOutputPort()
    let vm = ContainerTabViewModel(port, logsSourceFactory = fakeLogsSource)
    vm.ContainerIdInput <- "c1"
    vm.ContainerFollow <- true
    (vm.GetContainerLogsCommand :> ICommand).Execute(null)
    // le flux émet 2 lignes : chacune est écrite immédiatement
    waitUntil (fun () -> port.Messages.Length = 2) |> should equal true
    port.Messages |> should contain "[2026-08-12T10:00:00Z] ligne 1"
    port.Messages |> should contain "[2026-08-12T10:00:00Z] ligne 2"

[<Fact>]
let ``ContainerTabViewModel GetContainerLogs sans suivi affiche l'instantané en bloc`` () =
    let port = FakeOutputPort.FakeOutputPort()
    let vm = ContainerTabViewModel(port, logsSourceFactory = fakeLogsSource)
    vm.ContainerIdInput <- "c1"
    vm.ContainerFollow <- false
    (vm.GetContainerLogsCommand :> ICommand).Execute(null)
    waitUntil (fun () -> port.Successes.Length = 1) |> should equal true
    Assert.Contains("[2026-08-12T10:00:00Z] ligne 1", port.Successes.Head)
    Assert.Contains("ligne 2", port.Successes.Head)
