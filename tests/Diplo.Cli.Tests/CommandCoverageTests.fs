namespace Diplo.Cli.Tests

/// Couverture complémentaire des commandes CLI via fakes injectés : parcours de
/// succès et d'échec des commandes conteneur, volume, réseau, status et
/// init-config non couvertes ailleurs. N'exerce aucun serveur gRPC.
module CommandCoverageTests =

    open System
    open System.IO
    open System.Collections.Generic
    open System.Threading
    open Xunit
    open FsUnit.Xunit
    open Spectre.Console.Cli
    open Diplo.Core.Clients
    open Diplo.TestHelpers
    open Diplo.Grpc.Container
    open Diplo.Grpc.Network
    open Diplo.Grpc.Volume

    /// Exécute une commande (async ou sync) en la traitant comme ICommand<'T>.
    let private run (cmd: ICommand<'T>) (settings: 'T) : int =
        cmd.ExecuteAsync(Unchecked.defaultof<CommandContext>, settings, CancellationToken.None).Result

    let private defaultNetwork () = new FakeNetworkClient()
    let private defaultVolume () = new FakeVolumeClient()

    /// Fabrique de clients par défaut autour d'un client conteneur donné.
    let private withContainer (container: FakeContainerClient) : FakeDiploClients =
        FakeDiploClients(container, defaultNetwork(), defaultVolume())

    let private withNetwork (network: FakeNetworkClient) : FakeDiploClients =
        FakeDiploClients(new FakeContainerClient(), network, defaultVolume())

    let private withVolume (volume: FakeVolumeClient) : FakeDiploClients =
        FakeDiploClients(new FakeContainerClient(), defaultNetwork(), volume)

    // ─── Conteneurs ───────────────────────────────────────────────────
    open Diplo.Cli.Container

    [<Fact>]
    let ``container list vide retourne 0 et prévient`` () =
        let output = MockOutputPort()
        let code = run (ListCommand(output, withContainer(new FakeContainerClient()))) (ListSettings())
        code |> should equal 0
        output.Warnings |> should contain "Aucun conteneur trouvé."

    [<Fact>]
    let ``container list avec conteneurs retourne 0 et table`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(
                list =
                    { Containers =
                          List<ContainerInfo>(
                              [
                                  { Id = "c1"
                                    Name = "app"
                                    Image = "nginx"
                                    State = ContainerState.Running
                                    CreatedAt = "2026-01-01"
                                    Labels = Dictionary<string, string>() }
                              ]
                          ) }
            )

        let code = run (ListCommand(output, withContainer client)) (ListSettings())
        code |> should equal 0
        output.Tables |> should not' (be Empty)
        client.ListCalls |> should equal 1

    [<Fact>]
    let ``container inspect en succès retourne 0 et écrit le détail`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(
                inspect =
                    { Id = "c1"
                      Name = "app"
                      Image = "nginx"
                      State = ContainerState.Running
                      CreatedAt = "a"
                      StartedAt = "b"
                      FinishedAt = "c"
                      Pid = 42
                      ExitCode = 0
                      Labels = Dictionary<string, string>()
                      Env = Dictionary<string, string>()
                      RestartPolicy = ""
                      Ports = List<PortMapping>()
                      Health = ""
                      Mounts = List<string>() }
            )

        let code = run (InspectContainerCommand(output, withContainer client)) (InspectContainerSettings(Id = "c1"))
        code |> should equal 0
        output.Successes |> should contain "Conteneur app"
        output.Lines |> should contain "  ID        : c1"
        client.InspectCalls |> should equal 1

    [<Fact>]
    let ``container start en succès retourne 0 et contacte le client`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(start = { State = ContainerState.Running; Message = "ok" })

        let code = run (StartContainerCommand(output, withContainer client)) (StartSettings(Id = "c1"))
        code |> should equal 0
        client.StartCalls |> should equal 1
        output.Successes |> should contain "Conteneur c1 démarré (Running)"

    [<Fact>]
    let ``container stop en succès retourne 0 et contacte le client`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(stop = { State = ContainerState.Stopped; Message = "ok" })

        let code = run (StopContainerCommand(output, withContainer client)) (StopSettings(Id = "c1", Timeout = 5))
        code |> should equal 0
        client.StopCalls |> should equal 1

    [<Fact>]
    let ``container rename en succès retourne 0`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(rename = { Success = true; Message = "Renommé" })

        let code =
            run
                (RenameContainerCommand(output, withContainer client))
                (RenameContainerSettings(Id = "c1", NewName = "app2"))

        code |> should equal 0
        client.RenameCalls |> should equal 1
        output.Successes |> should contain "Renommé"

    [<Fact>]
    let ``container rename en échec retourne 1 et écrit l'erreur`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(rename = { Success = false; Message = "Échec rename" })

        let code =
            run
                (RenameContainerCommand(output, withContainer client))
                (RenameContainerSettings(Id = "c1", NewName = "app2"))

        code |> should equal 1
        output.Errors |> should contain "Échec rename"

    [<Fact>]
    let ``image pull en succès retourne 0 et écrit le message`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(pull = { Image = "nginx"; Message = "Téléchargé" })

        let code = run (PullImageCommand(output, withContainer client)) (PullSettings(Image = "nginx"))
        code |> should equal 0
        client.PullCalls |> should equal 1
        output.Successes |> should contain "Téléchargé"

    [<Fact>]
    let ``image remove en succès retourne 0`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(removeImage = { Success = true; Message = "Image supprimée" })

        let code = run (ImageRemoveCommand(output, withContainer client)) (ImageRemoveSettings(Ref = "nginx"))
        code |> should equal 0
        client.RemoveImageCalls |> should equal 1

    [<Fact>]
    let ``image remove en échec retourne 1 et écrit l'erreur`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(removeImage = { Success = false; Message = "Impossible" })

        let code = run (ImageRemoveCommand(output, withContainer client)) (ImageRemoveSettings(Ref = "nginx"))
        code |> should equal 1
        output.Errors |> should contain "Impossible"

    [<Fact>]
    let ``image list vide retourne 0 et prévient`` () =
        let output = MockOutputPort()
        let code = run (ImageListCommand(output, withContainer(new FakeContainerClient()))) (ImageListSettings())
        code |> should equal 0
        output.Warnings |> should contain "Aucune image trouvée."

    [<Fact>]
    let ``image list avec images retourne 0 et table`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(
                listImages =
                    { Images =
                          List<ImageInfo>(
                              [
                                  { Ref = "nginx:latest"
                                    Id = "i1"
                                    Repository = "nginx"
                                    Tag = "latest"
                                    Size = 100L
                                    CreatedAt = "" }
                              ]
                          ) }
            )

        let code = run (ImageListCommand(output, withContainer client)) (ImageListSettings())
        code |> should equal 0
        output.Tables |> should not' (be Empty)
        client.ListImagesCalls |> should equal 1

    [<Fact>]
    let ``image inspect en succès retourne 0 et écrit le détail`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(
                inspectImage =
                    { Ref = "nginx"
                      Id = "i1"
                      Repository = "nginx"
                      Tag = "latest"
                      Size = 100L
                      CreatedAt = ""
                      Labels = Dictionary<string, string>() }
            )

        let code = run (ImageInspectCommand(output, withContainer client)) (ImageInspectSettings(Ref = "nginx"))
        code |> should equal 0
        output.Successes |> should contain "Image nginx"
        client.InspectImageCalls |> should equal 1

    [<Fact>]
    let ``image tag en succès retourne 0 et écrit le message`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(tagImage = { Source = "a"; Target = "b"; Message = "Tag créé" })

        let code =
            run (ImageTagCommand(output, withContainer client)) (ImageTagSettings(Source = "a", Target = "b"))

        code |> should equal 0
        client.TagImageCalls |> should equal 1
        output.Successes |> should contain "Tag créé"

    [<Fact>]
    let ``container exec en succès retourne 0 et écrit la sortie`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(
                execOutputs =
                    [ { Stream = "stdout"; Data = System.Text.Encoding.UTF8.GetBytes("bonjour") }
                      { Stream = "stderr"; Data = System.Text.Encoding.UTF8.GetBytes("avertissement") } ]
            )

        let code =
            run
                (ExecContainerCommand(output, withContainer client))
                (ExecContainerSettings(Id = "c1", Command = [| "echo"; "bonjour" |]))

        code |> should equal 0
        client.ExecCalls |> should equal 1
        output.Lines |> should contain "bonjour"

    [<Fact>]
    let ``container top avec processus retourne 0 et table`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(
                top = { Processes = List<ProcessInfo>([ { Pid = 1L; User = "root"; Command = "init"; CpuPercent = 0.0; MemPercent = 0.0; Rss = 0L } ]) }
            )

        let code = run (TopContainerCommand(output, withContainer client)) (TopContainerSettings(Id = "c1"))
        code |> should equal 0
        output.Tables |> should not' (be Empty)
        client.TopCalls |> should equal 1

    [<Fact>]
    let ``container top sans processus retourne 0 et prévient`` () =
        let output = MockOutputPort()
        let client = new FakeContainerClient(top = { Processes = List<ProcessInfo>() })
        let code = run (TopContainerCommand(output, withContainer client)) (TopContainerSettings(Id = "c1"))
        code |> should equal 0
        output.Warnings |> should contain "Aucun processus trouvé dans le conteneur."

    [<Fact>]
    let ``container stats en succès retourne 0 et écrit les métriques`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(
                stats =
                    { CpuUsage = 0.5
                      MemoryUsage = 1L
                      MemoryLimit = 2L
                      NetworkRx = 3L
                      NetworkTx = 4L
                      DiskRead = 5L
                      DiskWrite = 6L
                      Pids = 7 }
            )

        let code = run (StatsContainerCommand(output, withContainer client)) (StatsContainerSettings(Id = "c1"))
        code |> should equal 0
        output.Successes |> should contain "Métriques du conteneur c1"
        output.Lines |> should contain "  CPU       : 0.50"
        client.StatsCalls |> should equal 1

    [<Fact>]
    let ``container logs en succès retourne 0 et écrit les entrées`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(logEntries = [ { Timestamp = "t1"; Stream = "stdout"; Log = "ligne" } ])

        let code = run (LogsContainerCommand(output, withContainer client)) (LogsContainerSettings(Id = "c1"))
        code |> should equal 0
        client.LogsCalls |> should equal 1
        output.Lines |> should contain "[t1] ligne"

    [<Fact>]
    let ``container logs en mode non-suivi retourne 0 et écrit les entrées`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(logEntries = [ { Timestamp = "t"; Stream = "out"; Log = "ok" } ])

        let code =
            run (LogsContainerCommand(output, withContainer client)) (LogsContainerSettings(Id = "c1", Follow = false))

        code |> should equal 0
        output.Lines |> should contain "[t] ok"

    [<Fact>]
    let ``container namespaces avec éléments retourne 0 et liste`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(namespaces = { Namespaces = List<string>([ "default"; "prod" ]) })

        let code = run (NamespacesCommand(output, withContainer client)) (NamespacesSettings())
        code |> should equal 0
        output.Successes |> should contain "Namespaces disponibles :"
        output.Lines |> should contain "  - default"
        client.NamespacesCalls |> should equal 1

    [<Fact>]
    let ``container version en succès retourne 0`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(
                version =
                    { Version = "1.0.0"
                      Revision = "abc"
                      GoVersion = "1.22"
                      Os = "linux"
                      Arch = "amd64" }
            )

        let code = run (VersionCommand(output, withContainer client)) (VersionSettings())
        code |> should equal 0
        output.Successes |> should contain "Diplo"
        output.Lines |> should contain "  Version   : 1.0.0"
        client.VersionCalls |> should equal 1

    [<Fact>]
    let ``registry logout en succès retourne 0`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(logout = { Success = true; Message = "Déconnecté" })

        let code =
            run (RegistryLogoutCommand(output, withContainer client)) (RegistryLogoutSettings(Registry = "reg"))

        code |> should equal 0
        client.LogoutCalls |> should equal 1
        output.Successes |> should contain "Déconnecté"

    [<Fact>]
    let ``registry logout en échec retourne 1 et écrit l'erreur`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(logout = { Success = false; Message = "Échec logout" })

        let code =
            run (RegistryLogoutCommand(output, withContainer client)) (RegistryLogoutSettings(Registry = "reg"))

        code |> should equal 1
        output.Errors |> should contain "Échec logout"

    // ─── Volumes ──────────────────────────────────────────────────────
    open Diplo.Cli.Volume

    [<Fact>]
    let ``volume list vide retourne 0 et prévient`` () =
        let output = MockOutputPort()
        let code = run (ListVolumesCommand(output, withVolume(new FakeVolumeClient()))) (ListVolumesSettings())
        code |> should equal 0
        output.Warnings |> should contain "Aucun volume trouvé."

    [<Fact>]
    let ``volume list avec volumes retourne 0 et table`` () =
        let output = MockOutputPort()

        let volume =
            new FakeVolumeClient(
                list =
                    { Volumes =
                          List<VolumeInfo>(
                              [
                                  { Id = "v1"
                                    Name = "data"
                                    Driver = StorageDriverType.Local
                                    Mountpoint = "C:\\vols\\data"
                                    State = MountState.Mounted
                                    Labels = Dictionary<string, string>()
                                    SizeBytes = 0L }
                              ]
                          ) }
            )

        let code = run (ListVolumesCommand(output, withVolume volume)) (ListVolumesSettings())
        code |> should equal 0
        output.Tables |> should not' (be Empty)
        volume.ListCalls |> should equal 1

    [<Fact>]
    let ``volume inspect en succès retourne 0 et écrit le détail`` () =
        let output = MockOutputPort()

        let volume =
            new FakeVolumeClient(
                inspect =
                    { Id = "v1"
                      Name = "data"
                      Driver = StorageDriverType.Local
                      Mountpoint = "C:\\vols\\data"
                      State = MountState.Mounted
                      SizeBytes = 10L
                      CreatedAt = ""
                      Labels = Dictionary<string, string>()
                      DriverOpts = Dictionary<string, string>() }
            )

        let code = run (InspectVolumeCommand(output, withVolume volume)) (InspectVolumeSettings(Id = "v1"))
        code |> should equal 0
        output.Successes |> should contain "Volume data"
        volume.InspectCalls |> should equal 1

    [<Fact>]
    let ``volume mount en succès retourne 0 et contacte le client`` () =
        let output = MockOutputPort()

        let volume =
            new FakeVolumeClient(mount = { State = MountState.Mounted; Mountpoint = "C:\\mnt"; Message = "ok" })

        let code =
            run (MountVolumeCommand(output, withVolume volume)) (MountSettings(Id = "v1", Target = "C:\\mnt"))

        code |> should equal 0
        volume.MountCalls |> should equal 1
        output.Successes |> should not' (be Empty)

    [<Fact>]
    let ``volume unmount en succès retourne 0 et contacte le client`` () =
        let output = MockOutputPort()
        let volume = new FakeVolumeClient(unmount = { State = MountState.Unmounted; Message = "ok" })

        let code =
            run (UnmountVolumeCommand(output, withVolume volume)) (UnmountSettings(Id = "v1", Target = "C:\\mnt"))

        code |> should equal 0
        volume.UnmountCalls |> should equal 1
        output.Successes |> should not' (be Empty)

    [<Fact>]
    let ``volume remove en succès retourne 0`` () =
        let output = MockOutputPort()

        let volume =
            new FakeVolumeClient(remove = { Success = true; Message = "Volume supprimé" })

        let code = run (RemoveVolumeCommand(output, withVolume volume)) (RemoveVolumeSettings(Id = "v1"))
        code |> should equal 0
        volume.RemoveCalls |> should equal 1
        output.Successes |> should contain "Volume supprimé"

    [<Fact>]
    let ``volume prune avec volumes retourne 0 et liste les suppressions`` () =
        let output = MockOutputPort()

        let volume =
            new FakeVolumeClient(
                prune =
                    { VolumesDeleted = List<string>([ "v1"; "v2" ])
                      Count = 2
                      Message = "2 volumes supprimés" }
            )

        let code = run (PruneVolumesCommand(output, withVolume volume)) (PruneVolumesSettings())
        code |> should equal 0
        output.Successes |> should contain "2 volumes supprimés"
        output.Lines |> should contain "  - v1"
        volume.PruneCalls |> should equal 1

    [<Fact>]
    let ``volume prune sans volume retourne 0 et prévient`` () =
        let output = MockOutputPort()
        let volume = new FakeVolumeClient(prune = { VolumesDeleted = List<string>(); Count = 0; Message = "" })
        let code = run (PruneVolumesCommand(output, withVolume volume)) (PruneVolumesSettings())
        code |> should equal 0
        output.Warnings |> should contain "Aucun volume à supprimer."

    // ─── Réseaux ──────────────────────────────────────────────────────
    open Diplo.Cli.Network

    [<Fact>]
    let ``network list vide retourne 0 et prévient`` () =
        let output = MockOutputPort()
        let code = run (ListNetworksCommand(output, withNetwork(new FakeNetworkClient()))) (ListNetworksSettings())
        code |> should equal 0
        output.Warnings |> should contain "Aucun réseau trouvé."

    [<Fact>]
    let ``network list avec réseaux retourne 0 et table`` () =
        let output = MockOutputPort()

        let network =
            new FakeNetworkClient(
                list =
                    { Networks =
                          List<NetworkInfo>(
                              [
                                  { Id = "n1"
                                    Name = "bridge"
                                    Driver = NetworkDriver.Bridge
                                    Subnet = ""
                                    Gateway = ""
                                    EndpointCount = 0
                                    CreatedAt = "" }
                              ]
                          ) }
            )

        let code = run (ListNetworksCommand(output, withNetwork network)) (ListNetworksSettings())
        code |> should equal 0
        output.Tables |> should not' (be Empty)
        network.ListCalls |> should equal 1

    [<Fact>]
    let ``network inspect en succès retourne 0 et écrit le détail`` () =
        let output = MockOutputPort()

        let network =
            new FakeNetworkClient(
                inspect =
                    { Id = "n1"
                      Name = "bridge"
                      Driver = NetworkDriver.Bridge
                      Subnet = "10.0.0.0/24"
                      Gateway = "10.0.0.1"
                      IpRange = ""
                      CreatedAt = ""
                      Options = Dictionary<string, string>()
                      Labels = Dictionary<string, string>()
                      Endpoints = List<EndpointInfo>() }
            )

        let code = run (InspectNetworkCommand(output, withNetwork network)) (InspectNetworkSettings(Id = "n1"))
        code |> should equal 0
        output.Successes |> should contain "Réseau bridge"
        network.InspectCalls |> should equal 1

    [<Fact>]
    let ``network remove en succès retourne 0`` () =
        let output = MockOutputPort()

        let network =
            new FakeNetworkClient(remove = { Success = true; Message = "Réseau supprimé" })

        let code = run (RemoveNetworkCommand(output, withNetwork network)) (RemoveNetworkSettings(Id = "n1"))
        code |> should equal 0
        network.RemoveCalls |> should equal 1
        output.Successes |> should contain "Réseau supprimé"

    [<Fact>]
    let ``network remove en échec retourne 1 et écrit l'erreur`` () =
        let output = MockOutputPort()

        let network =
            new FakeNetworkClient(remove = { Success = false; Message = "Réseau introuvable" })

        let code = run (RemoveNetworkCommand(output, withNetwork network)) (RemoveNetworkSettings(Id = "n1"))
        code |> should equal 1
        output.Errors |> should contain "Réseau introuvable"

    [<Fact>]
    let ``network connect en succès retourne 0 et contacte le client`` () =
        let output = MockOutputPort()

        let network =
            new FakeNetworkClient(
                connect = { EndpointId = "e1"; Ipv4Address = "10.0.0.2"; MacAddress = "aa"; Message = "Connecté" }
            )

        let code =
            run (ConnectCommand(output, withNetwork network)) (ConnectSettings(NetworkId = "n1", ContainerId = "c1"))

        code |> should equal 0
        network.ConnectCalls |> should equal 1
        output.Successes |> should contain "Connecté"

    [<Fact>]
    let ``network disconnect en succès retourne 0`` () =
        let output = MockOutputPort()

        let network =
            new FakeNetworkClient(disconnect = { Success = true; Message = "Déconnecté" })

        let code =
            run (DisconnectCommand(output, withNetwork network)) (DisconnectSettings(NetworkId = "n1", ContainerId = "c1"))

        code |> should equal 0
        network.DisconnectCalls |> should equal 1
        output.Successes |> should contain "Déconnecté"

    [<Fact>]
    let ``network disconnect en échec retourne 1 et écrit l'erreur`` () =
        let output = MockOutputPort()

        let network =
            new FakeNetworkClient(disconnect = { Success = false; Message = "Échec déconnexion" })

        let code =
            run (DisconnectCommand(output, withNetwork network)) (DisconnectSettings(NetworkId = "n1", ContainerId = "c1"))

        code |> should equal 1
        output.Errors |> should contain "Échec déconnexion"

    [<Fact>]
    let ``network prune avec réseaux retourne 0 et liste les suppressions`` () =
        let output = MockOutputPort()

        let network =
            new FakeNetworkClient(
                prune = { NetworksDeleted = List<string>([ "n1" ]); Count = 1; Message = "1 réseau supprimé" }
            )

        let code = run (PruneNetworksCommand(output, withNetwork network)) (PruneNetworksSettings())
        code |> should equal 0
        output.Successes |> should contain "1 réseau supprimé"
        network.PruneCalls |> should equal 1

    [<Fact>]
    let ``network prune sans réseau retourne 0 et prévient`` () =
        let output = MockOutputPort()
        let network = new FakeNetworkClient(prune = { NetworksDeleted = List<string>(); Count = 0; Message = "" })
        let code = run (PruneNetworksCommand(output, withNetwork network)) (PruneNetworksSettings())
        code |> should equal 0
        output.Warnings |> should contain "Aucun réseau à supprimer."

    // ─── Status ───────────────────────────────────────────────────────
    open Diplo.Cli

    [<Fact>]
    let ``status tous les services opérationnels retourne 0`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(
                version = { Version = "1.0.0"; Revision = ""; GoVersion = ""; Os = "linux"; Arch = "amd64" }
            )

        let clients = FakeDiploClients(client, new FakeNetworkClient(), new FakeVolumeClient())
        let code = run (StatusCommand(output, clients)) (StatusSettings())
        code |> should equal 0
        output.Successes |> should contain "Tous les services sont opérationnels."

    [<Fact>]
    let ``status volume indisponible retourne 1`` () =
        let output = MockOutputPort()

        let client = new FakeContainerClient()
        let volume = new FakeVolumeClient(listError = exn "boom")
        let clients = FakeDiploClients(client, new FakeNetworkClient(), volume)
        let code = run (StatusCommand(output, clients)) (StatusSettings())
        code |> should equal 1
        output.Warnings |> should contain "Certains services ne sont pas disponibles."

    [<Fact>]
    let ``status conteneur en erreur retourne 1`` () =
        let output = MockOutputPort()

        let client = new FakeContainerClient(versionError = exn "connexion refusée")
        let clients = FakeDiploClients(client, new FakeNetworkClient(), new FakeVolumeClient())
        let code = run (StatusCommand(output, clients)) (StatusSettings())
        code |> should equal 1
        output.Errors |> should not' (be Empty)

    // ─── Init config ──────────────────────────────────────────────────
    open System.Text

    [<Fact>]
    let ``init-config tcp par défaut écrit diplo.json dans --path`` () =
        let output = MockOutputPort()
        let root = TestHelpers.createTempDir "cli-config"
        let file = Path.Combine(root, "diplo.json")

        try
            let code =
                run (InitConfigCommand(output)) (InitConfigSettings(Path = file, Transport = "tcp"))

            code |> should equal 0
            File.Exists(file) |> should equal true

            let content = File.ReadAllText(file, Encoding.UTF8)
            content |> should haveSubstring "\"container\""
            output.Successes |> should contain (sprintf "Configuration écrite dans %s" file)
        finally
            TestHelpers.cleanupDir root

    [<Fact>]
    let ``init-config pipe retourne 0 et écrit la config pipe`` () =
        let output = MockOutputPort()
        let root = TestHelpers.createTempDir "cli-config-pipe"
        let file = Path.Combine(root, "diplo.json")

        try
            let code =
                run (InitConfigCommand(output)) (InitConfigSettings(Path = file, Transport = "pipe"))

            code |> should equal 0
            let content = File.ReadAllText(file, Encoding.UTF8)
            content |> should haveSubstring "http://pipe:/diplo-container"
        finally
            TestHelpers.cleanupDir root

    [<Fact>]
    let ``init-config transport inconnu retourne 1`` () =
        let output = MockOutputPort()

        let code =
            run (InitConfigCommand(output)) (InitConfigSettings(Path = "C:\\tmp\\x.json", Transport = "udp"))

        code |> should equal 1
        output.Errors |> should not' (be Empty)
