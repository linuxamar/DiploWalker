module DiploWalker.Gui.Tests.ClientInjectedBehaviorTests

open System
open System.Collections.Generic
open System.Threading
open System.Windows.Input
open Xunit
open FsUnit.Xunit
open DiploWalker.Core.Clients
open DiploWalker.Core.Output
open DiploWalker.Grpc.Container
open DiploWalker.Grpc.Network
open DiploWalker.Grpc.Volume
open DiploWalker.TestHelpers

// Tests des Comportements réels des ViewModels via l'injection de clients
// simulés (IContainerClient/INetworkClient/IVolumeClient). Les réponses gRPC
// sont des enregistrements [<CLIMutable>] : construction avec syntaxe { }.

let private waitUntil (predicate: unit -> bool) =
    let sw = Diagnostics.Stopwatch.StartNew()

    while not (predicate ()) && sw.ElapsedMilliseconds < 2000L do
        Thread.Sleep(20)

    predicate ()

// ── ContainerTabViewModel ──────────────────────────────────────

let private containerVm (port: MockOutputPort) (fake: FakeContainerClient) =
    new DiploWalker.Gui.ViewModels.ContainerTabViewModel(
        port,
        containerClientFactory = fun () -> fake :> IContainerClient
    )

[<Fact>]
let ``ContainerTabViewModel ListContainers ecrit le bilan et contacte le client`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient(list = { Containers = List<ContainerInfo>() })
    let vm = containerVm port fake
    (vm.ListContainersCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.ListCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "0 conteneur(s) trouvé(s)")
    |> should equal true

[<Fact>]
let ``ContainerTabViewModel InspectContainer ecrit les details`` () =
    let port = MockOutputPort()
    let labels = Dictionary<string, string>()
    labels.["com.DiploWalker.role"] <- "web"

    let inspect =
        { Id = "abc"
          Name = "web"
          Image = "nginx"
          State = ContainerState.Running
          CreatedAt = "2026-01-01"
          StartedAt = "2026-01-01"
          FinishedAt = ""
          Labels = labels
          Env = Dictionary<string, string>()
          Pid = 0
          ExitCode = 0
          RestartPolicy = ""
          Ports = List<PortMapping>()
          Health = ""
          Mounts = List<string>() }

    let fake = new FakeContainerClient(inspect = inspect)
    let vm = containerVm port fake
    vm.ContainerIdInput <- "abc"
    (vm.InspectContainerCommand :> ICommand).Execute(null)
    waitUntil (fun () -> port.Messages |> List.contains "  com.DiploWalker.role = web") |> should equal true
    port.Messages |> should contain "ID: abc"
    port.Messages |> should contain "Nom: web"
    port.Messages |> should contain "Labels:"

[<Fact>]
let ``ContainerTabViewModel StartContainer sans identifiant ecrit un avertissement`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient()
    let vm = containerVm port fake
    (vm.StartContainerCommand :> ICommand).Execute(null)
    waitUntil (fun () -> port.Warnings.Length = 1) |> should equal true
    port.Warnings.Head |> should haveSubstring "Aucun identifiant"
    fake.StartCalls |> should equal 0

[<Fact>]
let ``ContainerTabViewModel StartContainer avec identifiant appelle le client`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient(start = { State = ContainerState.Running; Message = "ok" })
    let vm = containerVm port fake
    vm.ContainerIdInput <- "c1"
    (vm.StartContainerCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.StartCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "Conteneur c1 démarré")
    |> should equal true

[<Fact>]
let ``ContainerTabViewModel StopContainer appelle le client`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient(stop = { State = ContainerState.Stopped; Message = "ok" })
    let vm = containerVm port fake
    vm.ContainerIdInput <- "c1"
    vm.ContainerTimeout <- 30
    (vm.StopContainerCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.StopCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "Conteneur c1 arrêté")
    |> should equal true

[<Fact>]
let ``ContainerTabViewModel DeleteContainer en succes ecrit le bilan`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient(delete = { Success = true; Message = "" })
    let vm = containerVm port fake
    vm.ContainerIdInput <- "c1"
    vm.ContainerForce <- true
    (vm.DeleteContainerCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.DeleteCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "Conteneur c1 supprimé")
    |> should equal true

[<Fact>]
let ``ContainerTabViewModel DeleteContainer en echec ecrit un avertissement`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient(delete = { Success = false; Message = "conteneur en cours d'utilisation" })
    let vm = containerVm port fake
    vm.ContainerIdInput <- "c1"
    (vm.DeleteContainerCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.DeleteCalls = 1) |> should equal true
    port.Warnings
    |> Seq.exists (fun w -> w.Contains "conteneur en cours d'utilisation")
    |> should equal true

[<Fact>]
let ``ContainerTabViewModel RenameContainer appelle le client`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient(rename = { Success = true; Message = "" })
    let vm = containerVm port fake
    vm.ContainerIdInput <- "c1"
    vm.ContainerNewName <- "c2"
    (vm.RenameContainerCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.RenameCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "Conteneur c1 renommé en c2")
    |> should equal true

[<Fact>]
let ``ContainerTabViewModel PullImage appelle le client`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient(pull = { Image = "nginx"; Message = "ok" })
    let vm = containerVm port fake
    vm.ContainerImageInput <- "nginx:latest"
    (vm.PullImageCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.PullCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "Image nginx:latest téléchargée")
    |> should equal true

[<Fact>]
let ``ContainerTabViewModel GetVersion ecrit la version`` () =
    let port = MockOutputPort()
    let version =
        { Version = "1.2.3"
          Revision = "abc123"
          GoVersion = "1.22"
          Os = "linux"
          Arch = "amd64" }

    let fake = new FakeContainerClient(version = version)
    let vm = containerVm port fake
    (vm.VersionCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.VersionCalls = 1) |> should equal true
    port.Messages |> should contain "Version: 1.2.3"
    port.Messages |> should contain "OS/Arch: linux/amd64"

[<Fact>]
let ``ContainerTabViewModel TopContainer ecrit les processus`` () =
    let port = MockOutputPort()
    let proc =
        { Pid = 42L
          User = "root"
          Command = "/bin/sh"
          CpuPercent = 1.5
          MemPercent = 2.5
          Rss = 1000L }

    let fake = new FakeContainerClient(top = { Processes = List<ProcessInfo>([| proc |]) })
    let vm = containerVm port fake
    vm.ContainerIdInput <- "c1"
    (vm.TopContainerCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.TopCalls = 1) |> should equal true
    port.Messages
    |> Seq.exists (fun m -> m.Contains "PID: 42  CMD: /bin/sh")
    |> should equal true

[<Fact>]
let ``ContainerTabViewModel GetContainerStats ecrit les metriques`` () =
    let port = MockOutputPort()
    let stats =
        { CpuUsage = 12.5
          MemoryUsage = 1024L
          MemoryLimit = 2048L
          NetworkRx = 100L
          NetworkTx = 50L
          DiskRead = 0L
          DiskWrite = 0L
          Pids = 3 }

    let fake = new FakeContainerClient(stats = stats)
    let vm = containerVm port fake
    vm.ContainerIdInput <- "c1"
    (vm.StatsContainerCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.StatsCalls = 1) |> should equal true
    port.Messages
    |> Seq.exists (fun m -> m.Contains "CPU: 12.50")
    |> should equal true

[<Fact>]
let ``ContainerTabViewModel InspectImage ecrit la reference et les labels`` () =
    let port = MockOutputPort()
    let labels = Dictionary<string, string>()
    labels.["a"] <- "b"

    let img =
        { Ref = "nginx"
          Id = "sha256:abc"
          Repository = "library/nginx"
          Tag = "latest"
          Size = 1024L
          CreatedAt = "2026-01-01"
          Labels = labels }

    let fake = new FakeContainerClient(inspectImage = img)
    let vm = containerVm port fake
    vm.ContainerImageRef <- "nginx:latest"
    (vm.InspectImageCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.InspectImageCalls = 1) |> should equal true
    port.Messages |> should contain "Référentiel: nginx"
    port.Messages |> should contain "  a = b"

[<Fact>]
let ``ContainerTabViewModel RemoveImage en succes ecrit le bilan`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient(removeImage = { Success = true; Message = "" })
    let vm = containerVm port fake
    vm.ContainerImageRef <- "nginx:latest"
    (vm.RemoveImageCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.RemoveImageCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "Image nginx:latest supprimée")
    |> should equal true

[<Fact>]
let ``ContainerTabViewModel TagImage appelle le client`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient(tagImage = { Source = ""; Target = ""; Message = "ok" })
    let vm = containerVm port fake
    vm.ContainerImageRef <- "nginx:latest"
    vm.ContainerImageTarget <- "myreg/nginx:1.0"
    (vm.TagImageCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.TagImageCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "Image nginx:latest étiquetée en myreg/nginx:1.0")
    |> should equal true

[<Fact>]
let ``ContainerTabViewModel CreateContainer appelle le client`` () =
    let port = MockOutputPort()
    let fake =
        new FakeContainerClient(
            create =
                { Id = "c1"
                  Name = "web"
                  State = ContainerState.Created
                  CreatedAt = "" }
        )

    let vm = containerVm port fake
    vm.ContainerNameInput <- "web"
    vm.ContainerImageInput <- "nginx"
    (vm.CreateContainerCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.CreateCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "Conteneur créé : web")
    |> should equal true

[<Fact>]
let ``ContainerTabViewModel ExecInContainer ecrit la sortie`` () =
    let port = MockOutputPort()
    let output = { Stream = "stdout"; Data = [| byte 'h'; byte 'i' |] }
    let out = seq { output }
    let fake = new FakeContainerClient(execOutputs = out)
    let vm = containerVm port fake
    vm.ContainerIdInput <- "c1"
    vm.ContainerExecCommand <- "echo hi"
    (vm.ExecInContainerCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.ExecCalls = 1) |> should equal true
    port.Successes.Head |> should haveSubstring "hi"

[<Fact>]
let ``ContainerTabViewModel ListNamespaces ecrit les namespaces`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient(namespaces = { Namespaces = List<string>([| "diplo"; "system" |]) })
    let vm = containerVm port fake
    (vm.ListNamespacesCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.NamespacesCalls = 1) |> should equal true
    port.Successes.Head |> should haveSubstring "diplo, system"

[<Fact>]
let ``ContainerTabViewModel PauseContainer appelle le client`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient(pause = { State = ContainerState.Paused; Message = "ok" })
    let vm = containerVm port fake
    vm.ContainerIdInput <- "c1"
    (vm.PauseContainerCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.PauseCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "Conteneur c1 suspendu")
    |> should equal true

[<Fact>]
let ``ContainerTabViewModel PauseContainer sans identifiant ecrit un avertissement`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient()
    let vm = containerVm port fake
    (vm.PauseContainerCommand :> ICommand).Execute(null)
    waitUntil (fun () -> port.Warnings.Length = 1) |> should equal true
    port.Warnings.Head |> should haveSubstring "Aucun identifiant"
    fake.PauseCalls |> should equal 0

[<Fact>]
let ``ContainerTabViewModel UnpauseContainer appelle le client`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient(unpause = { State = ContainerState.Running; Message = "ok" })
    let vm = containerVm port fake
    vm.ContainerIdInput <- "c1"
    (vm.UnpauseContainerCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.UnpauseCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "Conteneur c1 repris")
    |> should equal true

[<Fact>]
let ``ContainerTabViewModel WaitContainer appelle le client avec le timeout configure`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient(wait = { ExitCode = 0; State = ContainerState.Stopped; Message = "ok" })
    let vm = containerVm port fake
    vm.ContainerIdInput <- "c1"
    vm.ContainerTimeout <- 25
    (vm.WaitContainerCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.WaitCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "Conteneur c1 terminé (code: 0)")
    |> should equal true

[<Fact>]
let ``ContainerTabViewModel PruneContainers ecrit le bilan`` () =
    let port = MockOutputPort()
    let deleted = List<string>([| "c1"; "c2" |])
    let fake = new FakeContainerClient(pruneContainers = { Deleted = deleted })
    let vm = containerVm port fake
    (vm.PruneContainersCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.PruneContainersCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "2 conteneur(s) arrêté(s) supprimé(s): c1, c2")
    |> should equal true

[<Fact>]
let ``ContainerTabViewModel PruneContainers sans resultat l'indique`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient(pruneContainers = { Deleted = List<string>() })
    let vm = containerVm port fake
    (vm.PruneContainersCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.PruneContainersCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "Aucun conteneur arrêté à supprimer")
    |> should equal true

[<Fact>]
let ``ContainerTabViewModel PruneImages ecrit le bilan`` () =
    let port = MockOutputPort()
    let deleted = List<string>([| "sha256:abc" |])
    let fake = new FakeContainerClient(pruneImages = { Deleted = deleted })
    let vm = containerVm port fake
    (vm.PruneImagesCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.PruneImagesCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "1 image(s) inutilisée(s) supprimée(s): sha256:abc")
    |> should equal true

[<Fact>]
let ``ContainerTabViewModel CommitImage appelle le client`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient(commit = { ImageRef = "myimg:1.0"; Success = true; Message = "ok" })
    let vm = containerVm port fake
    vm.ContainerIdInput <- "c1"
    vm.ContainerImageTarget <- "myimg:1.0"
    (vm.CommitImageCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.CommitCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "Image myimg:1.0 créée depuis c1")
    |> should equal true

[<Fact>]
let ``ContainerTabViewModel CommitImage sans cible ecrit un avertissement`` () =
    let port = MockOutputPort()
    let fake = new FakeContainerClient()
    let vm = containerVm port fake
    vm.ContainerIdInput <- "c1"
    (vm.CommitImageCommand :> ICommand).Execute(null)
    waitUntil (fun () -> port.Warnings.Length = 1) |> should equal true
    port.Warnings.Head |> should haveSubstring "référence d'image est requise"
    fake.CommitCalls |> should equal 0

[<Fact>]
let ``ContainerTabViewModel GetContainerEvents affiche les evenements au fil de l'eau`` () =
    let port = MockOutputPort()
    let events =
        seq {
            { Timestamp = "t1"; EventType = "start"; Id = "c1"; Status = "ok"; ExitCode = 0 }
            { Timestamp = "t2"; EventType = "pause"; Id = "c1"; Status = "ok"; ExitCode = 0 }
        }

    let fake = new FakeContainerClient(containerEvents = events)
    let vm = containerVm port fake
    (vm.GetContainerEventsCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.EventsCalls = 1) |> should equal true
    waitUntil (fun () -> port.Messages.Length = 2) |> should equal true
    port.Messages |> should contain "[t1] start c1 (ok)"
    port.Messages |> should contain "[t2] pause c1 (ok)"
    port.Successes
    |> Seq.exists (fun m -> m.Contains "Suivi des événements terminé")
    |> should equal true

[<Fact>]
let ``ContainerTabViewModel StopFollowEvents annule le flux d'evenements`` () =
    let port = MockOutputPort()
    let events = seq { { Timestamp = "t1"; EventType = "start"; Id = "c1"; Status = "ok"; ExitCode = 0 } }
    let fake = new FakeContainerClient(containerEvents = events)
    let vm = containerVm port fake
    (vm.GetContainerEventsCommand :> ICommand).Execute(null)
    waitUntil (fun () -> port.Messages.Length = 1) |> should equal true
    (vm.StopFollowEventsCommand :> ICommand).Execute(null)
    // Un nouvel appel annule le précédent sans success final dupliqué.
    (vm.GetContainerEventsCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.EventsCalls = 2) |> should equal true

// ── VolumeTabViewModel ─────────────────────────────────────────

let private volumeVm (port: MockOutputPort) (fake: FakeVolumeClient) =
    new DiploWalker.Gui.ViewModels.VolumeTabViewModel(port, volumeClientFactory = fun () -> fake :> IVolumeClient)

[<Fact>]
let ``VolumeTabViewModel InspectVolume ecrit les details`` () =
    let port = MockOutputPort()
    let inspect =
        { Id = "v1"
          Name = "data"
          Driver = StorageDriverType.Local
          Mountpoint = "/mnt/data"
          State = MountState.Mounted
          Labels = Dictionary<string, string>()
          DriverOpts = Dictionary<string, string>()
          SizeBytes = 2048L
          CreatedAt = "" }

    let fake = new FakeVolumeClient(inspect = inspect)
    let vm = volumeVm port fake
    vm.VolumeIdInput <- "v1"
    (vm.InspectVolumeCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.InspectCalls = 1) |> should equal true
    port.Messages |> should contain "ID: v1"
    port.Messages |> should contain "État: Mounted"
    port.Messages |> should contain "Taille: 2048 octets"

[<Fact>]
let ``VolumeTabViewModel CreateVolume appelle le client`` () =
    let port = MockOutputPort()
    let fake =
        new FakeVolumeClient(
            create =
                { Id = "v2"
                  Name = "data2"
                  Driver = StorageDriverType.Local
                  Mountpoint = ""
                  CreatedAt = "" }
        )

    let vm = volumeVm port fake
    vm.VolumeNameInput <- "data2"
    vm.VolumeDriver <- "local"
    (vm.CreateVolumeCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.CreateCalls = 1) |> should equal true
    port.Successes.Head |> should haveSubstring "Volume data2 créé"

[<Fact>]
let ``VolumeTabViewModel RemoveVolume en succes ecrit le bilan`` () =
    let port = MockOutputPort()
    let fake = new FakeVolumeClient(remove = { Success = true; Message = "" })
    let vm = volumeVm port fake
    vm.VolumeIdInput <- "v1"
    (vm.RemoveVolumeCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.RemoveCalls = 1) |> should equal true
    port.Successes.Head |> should haveSubstring "Volume v1 supprimé"

[<Fact>]
let ``VolumeTabViewModel MountVolume ecrit le bilan`` () =
    let port = MockOutputPort()
    let fake = new FakeVolumeClient(mount = { State = MountState.Mounted; Mountpoint = "/mnt"; Message = "monté" })
    let vm = volumeVm port fake
    vm.VolumeIdInput <- "v1"
    vm.VolumeTargetPath <- "C:\\mount"
    (vm.MountVolumeCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.MountCalls = 1) |> should equal true
    port.Successes
    |> Seq.exists (fun m -> m.Contains "Volume v1 monté sur")
    |> should equal true

[<Fact>]
let ``VolumeTabViewModel UnmountVolume ecrit le bilan`` () =
    let port = MockOutputPort()
    let fake = new FakeVolumeClient(unmount = { State = MountState.Unmounted; Message = "démonté" })
    let vm = volumeVm port fake
    vm.VolumeIdInput <- "v1"
    vm.VolumeTargetPath <- "C:\\mount"
    (vm.UnmountVolumeCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.UnmountCalls = 1) |> should equal true
    port.Successes.Head |> should haveSubstring "Volume v1 démonté de"

[<Fact>]
let ``VolumeTabViewModel PruneVolumes ecrit le bilan`` () =
    let port = MockOutputPort()
    let fake =
        new FakeVolumeClient(prune = { VolumesDeleted = List<string>(); Count = 0; Message = "nettoyé" })

    let vm = volumeVm port fake
    (vm.PruneVolumesCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.PruneCalls = 1) |> should equal true
    port.Successes.Head |> should haveSubstring "Volumes nettoyés"

// ── NetworkTabViewModel ────────────────────────────────────────

let private networkVm (port: MockOutputPort) (fake: FakeNetworkClient) =
    new DiploWalker.Gui.ViewModels.NetworkTabViewModel(port, networkClientFactory = fun () -> fake :> INetworkClient)

[<Fact>]
let ``NetworkTabViewModel InspectNetwork ecrit les details`` () =
    let port = MockOutputPort()
    let inspect =
        { Id = "n1"
          Name = "net1"
          Driver = NetworkDriver.Bridge
          Subnet = "172.28.0.0/16"
          Gateway = "172.28.0.1"
          IpRange = ""
          Options = Dictionary<string, string>()
          Labels = Dictionary<string, string>()
          Endpoints = List<EndpointInfo>()
          CreatedAt = "" }

    let fake = new FakeNetworkClient(inspect = inspect)
    let vm = networkVm port fake
    vm.NetworkIdInput <- "n1"
    (vm.InspectNetworkCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.InspectCalls = 1) |> should equal true
    port.Messages |> should contain "ID: n1"
    port.Messages |> should contain "Sous-réseau: 172.28.0.0/16"

[<Fact>]
let ``NetworkTabViewModel CreateNetwork appelle le client`` () =
    let port = MockOutputPort()
    let fake =
        new FakeNetworkClient(
            create =
                { Id = "n2"
                  Name = "net2"
                  Driver = NetworkDriver.Bridge
                  Subnet = ""
                  Gateway = ""
                  CreatedAt = "" }
        )

    let vm = networkVm port fake
    vm.NetworkNameInput <- "net2"
    vm.NetworkDriver <- "bridge"
    (vm.CreateNetworkCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.CreateCalls = 1) |> should equal true
    port.Successes.Head |> should haveSubstring "Réseau net2 créé"

[<Fact>]
let ``NetworkTabViewModel RemoveNetwork en succes ecrit le bilan`` () =
    let port = MockOutputPort()
    let fake = new FakeNetworkClient(remove = { Success = true; Message = "" })
    let vm = networkVm port fake
    vm.NetworkIdInput <- "n1"
    (vm.RemoveNetworkCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.RemoveCalls = 1) |> should equal true
    port.Successes.Head |> should haveSubstring "Réseau n1 supprimé"

[<Fact>]
let ``NetworkTabViewModel ConnectContainer ecrit le bilan`` () =
    let port = MockOutputPort()
    let fake = new FakeNetworkClient(connect = { EndpointId = ""; Ipv4Address = ""; MacAddress = ""; Message = "connecté" })
    let vm = networkVm port fake
    vm.NetworkIdInput <- "n1"
    vm.NetworkContainerId <- "c1"
    (vm.ConnectNetworkCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.ConnectCalls = 1) |> should equal true
    port.Successes.Head |> should haveSubstring "Conteneur c1 connecté au réseau n1"

[<Fact>]
let ``NetworkTabViewModel DisconnectContainer en succes ecrit le bilan`` () =
    let port = MockOutputPort()
    let fake = new FakeNetworkClient(disconnect = { Success = true; Message = "" })
    let vm = networkVm port fake
    vm.NetworkIdInput <- "n1"
    vm.NetworkContainerId <- "c1"
    (vm.DisconnectNetworkCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.DisconnectCalls = 1) |> should equal true
    port.Successes.Head |> should haveSubstring "Conteneur c1 déconnecté du réseau n1"

[<Fact>]
let ``NetworkTabViewModel RunCniPlugin en succes ecrit le bilan`` () =
    let port = MockOutputPort()
    let fake =
        new FakeNetworkClient(runCni = { Success = true; Ifname = ""; Ipv4Address = ""; Gateway = ""; Message = "cni ok" })

    let vm = networkVm port fake
    vm.NetworkCniPluginPath <- "/opt/cni/bin/bridge"
    vm.NetworkCniCommand <- "ADD"
    vm.NetworkContainerId <- "c1"
    vm.NetworkNetnsPath <- "/run/netns/x"
    (vm.RunCniPluginCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.RunCniCalls = 1) |> should equal true
    port.Successes.Head |> should haveSubstring "Plugin CNI exécuté"

[<Fact>]
let ``NetworkTabViewModel PruneNetworks ecrit le bilan`` () =
    let port = MockOutputPort()
    let fake =
        new FakeNetworkClient(prune = { NetworksDeleted = List<string>(); Count = 0; Message = "nettoyé" })

    let vm = networkVm port fake
    (vm.PruneNetworksCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.PruneCalls = 1) |> should equal true
    port.Successes.Head |> should haveSubstring "Réseaux nettoyés"

// ── ComposeTabViewModel ─────────────────────────────────────────

let private composeVm (port: MockOutputPort) (fake: FakeContainerClient) =
    new DiploWalker.Gui.ViewModels.ComposeTabViewModel(
        port,
        containerClientFactory = fun () -> fake :> IContainerClient
    )

[<Fact>]
let ``ComposeTabViewModel InspectImage ecrit la reference et les labels`` () =
    let port = MockOutputPort()
    let labels = Dictionary<string, string>()
    labels.["com.diplo/projet"] <- "demo"

    let img =
        { Ref = "nginx"
          Id = "sha256:abc"
          Repository = "library/nginx"
          Tag = "latest"
          Size = 2048L
          CreatedAt = "2026-01-01"
          Labels = labels }

    let fake = new FakeContainerClient(inspectImage = img)
    let vm = composeVm port fake
    vm.InspectImageRef <- "nginx:latest"
    (vm.InspectImageCommand :> ICommand).Execute(null)
    waitUntil (fun () -> fake.InspectImageCalls = 1) |> should equal true
    port.Messages |> should contain "Référentiel: nginx"
    port.Messages |> should contain "Tag: latest"
    port.Messages |> should contain "Taille: 2048 octets"
    port.Messages |> should contain "  com.diplo/projet = demo"

let private composeFile (name: string) (content: string) =
    let dir = DiploWalker.TestHelpers.TestHelpers.createTempDir "compose-op"
    let path = IO.Path.Combine(dir, name)
    IO.File.WriteAllText(path, content)
    dir, path

[<Fact>]
let ``ComposeTabViewModel ComposeUp cree et demarre les services du fichier`` () =
    let dir, path = composeFile "docker-compose.yml" "services:\n  web:\n    image: nginx:latest\n"

    try
        let port = MockOutputPort()
        let fake =
            new FakeContainerClient(create = { Id = "c1"; Name = "web"; State = ContainerState.Created; CreatedAt = "" })

        let vm = composeVm port fake
        vm.ComposeFilePath <- path
        (vm.ComposeUpCommand :> ICommand).Execute(null)
        waitUntil (fun () -> fake.CreateCalls = 1) |> should equal true
        port.Successes
        |> Seq.exists (fun m -> m.Contains "Démarrage du projet 'docker-compose'")
        |> should equal true
        port.Successes
        |> Seq.exists (fun m -> m.Contains "Conteneur web créé")
        |> should equal true
        port.Successes
        |> Seq.exists (fun m -> m.Contains "Conteneur web démarré")
        |> should equal true
    finally
        DiploWalker.TestHelpers.TestHelpers.cleanupDir dir

[<Fact>]
let ``ComposeTabViewModel ComposeDown arrete et supprime les conteneurs du projet`` () =
    let dir, path = composeFile "docker-compose.yml" "services:\n  web:\n    image: nginx:latest\n"

    try
        let port = MockOutputPort()
        let labels = Dictionary<string, string>()
        labels.[DiploWalker.Core.Compose.ComposeModels.composeProjectLabel] <- "docker-compose"
        labels.[DiploWalker.Core.Compose.ComposeModels.composeServiceLabel] <- "web"

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
        (vm.ComposeDownCommand :> ICommand).Execute(null)
        waitUntil (fun () -> fake.ListCalls = 1) |> should equal true
        port.Successes
        |> Seq.exists (fun m -> m.Contains "Arrêt du projet 'docker-compose'")
        |> should equal true
        port.Successes
        |> Seq.exists (fun m -> m.Contains "Conteneur web arrêté et supprimé")
        |> should equal true
    finally
        DiploWalker.TestHelpers.TestHelpers.cleanupDir dir

[<Fact>]
let ``ComposeTabViewModel ComposePull telecharge les images des services`` () =
    let dir, path = composeFile "docker-compose.yml" "services:\n  web:\n    image: nginx:latest\n"

    try
        let port = MockOutputPort()
        let fake = new FakeContainerClient(pull = { Image = "nginx:latest"; Message = "ok" })
        let vm = composeVm port fake
        vm.ComposeFilePath <- path
        (vm.ComposePullCommand :> ICommand).Execute(null)
        waitUntil (fun () -> fake.PullCalls = 1) |> should equal true
        port.Successes
        |> Seq.exists (fun m -> m.Contains "Téléchargement de nginx:latest")
        |> should equal true
    finally
        DiploWalker.TestHelpers.TestHelpers.cleanupDir dir

[<Fact>]
let ``ComposeTabViewModel ComposeLogs affiche les journaux des conteneurs du projet`` () =
    let dir, path = composeFile "docker-compose.yml" "services:\n  web:\n    image: nginx:latest\n"

    try
        let port = MockOutputPort()
        let labels = Dictionary<string, string>()
        labels.[DiploWalker.Core.Compose.ComposeModels.composeProjectLabel] <- "docker-compose"

        let info =
            { Id = "c1"
              Name = "web"
              Image = "nginx:latest"
              State = ContainerState.Running
              CreatedAt = "2026-01-01"
              Labels = labels }

        let fake =
            new FakeContainerClient(
                list = { Containers = List<ContainerInfo>([| info |]) },
                logEntries = [ { Timestamp = "t1"; Stream = "stdout"; Log = "hello" } ]
            )

        let vm = composeVm port fake
        vm.ComposeFilePath <- path
        (vm.ComposeLogsCommand :> ICommand).Execute(null)
        waitUntil (fun () -> fake.ListCalls = 1) |> should equal true
        port.Successes
        |> Seq.exists (fun m -> m.Contains "--- web ---")
        |> should equal true
        port.Messages
        |> Seq.exists (fun m -> m.Contains "[t1] hello")
        |> should equal true
    finally
        DiploWalker.TestHelpers.TestHelpers.cleanupDir dir


