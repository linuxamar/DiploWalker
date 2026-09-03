module Diplo.Gui.Tests.HeadlessBehaviorTests

open System
open System.Collections.Generic
open System.Threading
open System.Windows.Input
open Avalonia.Threading
open Xunit
open FsUnit.Xunit
open Diplo.Core.Clients
open Diplo.Core.Output
open Diplo.Grpc.Container
open Diplo.Grpc.Network
open Diplo.Grpc.Volume
open Diplo.TestHelpers

// Tests des méthodes des ViewModels qui ne peuvent s'exécuter que sur un vrai
// Dispatcher Avalonia : elles remplissent leurs collections via
// Dispatcher.UIThread.Post. Le harnais headless (HeadlessRunner.setupHeadless)
// initialise le Dispatcher sur le thread courant ; RunJobs traite ensuite les
// rappels poster.

let private waitPump (predicate: unit -> bool) =
    let sw = Diagnostics.Stopwatch.StartNew()

    while not (predicate ()) && sw.ElapsedMilliseconds < 3000L do
        Dispatcher.UIThread.RunJobs()
        Thread.Sleep(10)

    predicate ()

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
