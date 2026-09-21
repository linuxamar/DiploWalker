namespace DiploWalker.Cli.Tests

/// Parcours de succÃ¨s et d'Ã©chec gÃ©rÃ©s par les commandes CLI, via des clients
/// injectÃ©s (FakeDiploClients) : la sortie et le code de retour ne dÃ©pendent
/// pas d'un serveur gRPC.
module SuccessPathTests =

    open System.Threading
    open System.IO
    open Xunit
    open FsUnit.Xunit
    open Spectre.Console.Cli
    open DiploWalker.Core.Clients
    open DiploWalker.TestHelpers
    open DiploWalker.Grpc.Container
    open DiploWalker.Grpc.Network
    open DiploWalker.Grpc.Volume

    let private run (cmd: AsyncCommand<'T>) (settings: 'T) : int =
        let command = cmd :> ICommand<'T>
        command.ExecuteAsync(Unchecked.defaultof<CommandContext>, settings, CancellationToken.None).Result

    // â”€â”€â”€ Conteneurs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    open DiploWalker.Cli.Container

    [<Fact>]
    let ``container delete en succÃ¨s retourne 0 et Ã©crit le message`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(
                { Success = true
                  Message = "Conteneur supprimÃ©" },
                { Id = "c1"
                  Name = "app"
                  State = ContainerState.Stopped
                  CreatedAt = "" },
                { Success = true; Message = "ok" }
            )

        let clients =
            FakeDiploClients(
                client,
                new FakeNetworkClient(Unchecked.defaultof<_>, Unchecked.defaultof<_>),
                new FakeVolumeClient(Unchecked.defaultof<_>, Unchecked.defaultof<_>)
            )

        let code =
            run (DeleteContainerCommand(output, clients)) (DeleteSettings(Id = "abc"))

        code |> should equal 0
        output.Successes |> should contain "Conteneur supprimÃ©"
        client.DeleteCalls |> should equal 1

    [<Fact>]
    let ``container delete en Ã©chec retourne 1 et Ã©crit l'erreur`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(
                { Success = false
                  Message = "Conteneur introuvable" },
                { Id = "c1"
                  Name = "app"
                  State = ContainerState.Stopped
                  CreatedAt = "" },
                { Success = true; Message = "ok" }
            )

        let clients =
            FakeDiploClients(
                client,
                new FakeNetworkClient(Unchecked.defaultof<_>, Unchecked.defaultof<_>),
                new FakeVolumeClient(Unchecked.defaultof<_>, Unchecked.defaultof<_>)
            )

        let code =
            run (DeleteContainerCommand(output, clients)) (DeleteSettings(Id = "abc"))

        code |> should equal 1
        output.Errors |> should contain "Conteneur introuvable"

    [<Fact>]
    let ``container create en succÃ¨s retourne 0 et contacte le client`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(
                { Success = true; Message = "ok" },
                { Id = "c1"
                  Name = "app"
                  State = ContainerState.Running
                  CreatedAt = "2026-01-01" },
                { Success = true; Message = "ok" }
            )

        let clients =
            FakeDiploClients(
                client,
                new FakeNetworkClient(Unchecked.defaultof<_>, Unchecked.defaultof<_>),
                new FakeVolumeClient(Unchecked.defaultof<_>, Unchecked.defaultof<_>)
            )

        let code =
            run (CreateContainerCommand(output, clients)) (CreateContainerSettings(Name = "app", Image = "nginx"))

        code |> should equal 0
        client.CreateCalls |> should equal 1
        output.Successes |> should not' (be Empty)

    [<Fact>]
    let ``registry login en succÃ¨s retourne 0`` () =
        let output = MockOutputPort()

        let client =
            new FakeContainerClient(
                { Success = true; Message = "ok" },
                { Id = "c1"
                  Name = "app"
                  State = ContainerState.Stopped
                  CreatedAt = "" },
                { Success = true
                  Message = "Connexion Ã©tablie" }
            )

        let clients =
            FakeDiploClients(
                client,
                new FakeNetworkClient(Unchecked.defaultof<_>, Unchecked.defaultof<_>),
                new FakeVolumeClient(Unchecked.defaultof<_>, Unchecked.defaultof<_>)
            )

        let code =
            run
                (RegistryLoginCommand(output, clients))
                (RegistryLoginSettings(Registry = "reg", Username = "u", Password = "p"))

        code |> should equal 0
        client.LoginCalls |> should equal 1
        output.Successes |> should contain "Connexion Ã©tablie"

    // â”€â”€â”€ Volumes â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    open DiploWalker.Cli.Volume

    [<Fact>]
    let ``volume create en succÃ¨s retourne 0`` () =
        let output = MockOutputPort()

        let volume =
            new FakeVolumeClient(
                { Id = "v1"
                  Name = "data"
                  Driver = StorageDriverType.Local
                  Mountpoint = "C:\\vols\\data"
                  CreatedAt = "" },
                { Success = true; Message = "ok" }
            )

        let clients =
            FakeDiploClients(
                new FakeContainerClient(Unchecked.defaultof<_>, Unchecked.defaultof<_>, Unchecked.defaultof<_>),
                new FakeNetworkClient(Unchecked.defaultof<_>, Unchecked.defaultof<_>),
                volume
            )

        let code =
            run (CreateVolumeCommand(output, clients)) (CreateVolumeSettings(Name = "data"))

        code |> should equal 0
        volume.CreateCalls |> should equal 1
        output.Successes |> should not' (be Empty)

    [<Fact>]
    let ``volume remove en Ã©chec retourne 1 et Ã©crit l'erreur`` () =
        let output = MockOutputPort()

        let volume =
            new FakeVolumeClient(
                { Id = "v1"
                  Name = "data"
                  Driver = StorageDriverType.Local
                  Mountpoint = ""
                  CreatedAt = "" },
                { Success = false
                  Message = "Volume non trouvÃ©" }
            )

        let clients =
            FakeDiploClients(
                new FakeContainerClient(Unchecked.defaultof<_>, Unchecked.defaultof<_>, Unchecked.defaultof<_>),
                new FakeNetworkClient(Unchecked.defaultof<_>, Unchecked.defaultof<_>),
                volume
            )

        let code =
            run (RemoveVolumeCommand(output, clients)) (RemoveVolumeSettings(Id = "v1"))

        code |> should equal 1
        volume.RemoveCalls |> should equal 1
        output.Errors |> should contain "Volume non trouvÃ©"

    // â”€â”€â”€ RÃ©seaux â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    open DiploWalker.Cli.Network

    [<Fact>]
    let ``network create en succÃ¨s retourne 0`` () =
        let output = MockOutputPort()

        let network =
            new FakeNetworkClient(
                { Id = "n1"
                  Name = "bridge1"
                  Driver = NetworkDriver.Bridge
                  Subnet = ""
                  Gateway = ""
                  CreatedAt = "" },
                { Success = true
                  Ifname = ""
                  Ipv4Address = ""
                  Gateway = ""
                  Message = "" }
            )

        let clients =
            FakeDiploClients(
                new FakeContainerClient(Unchecked.defaultof<_>, Unchecked.defaultof<_>, Unchecked.defaultof<_>),
                network,
                new FakeVolumeClient(Unchecked.defaultof<_>, Unchecked.defaultof<_>)
            )

        let code =
            run (CreateNetworkCommand(output, clients)) (CreateNetworkSettings(Name = "bridge1"))

        code |> should equal 0
        network.CreateCalls |> should equal 1
        output.Successes |> should not' (be Empty)

    [<Fact>]
    let ``run-cni-plugin en succÃ¨s retourne 0 et affiche l'interface`` () =
        let output = MockOutputPort()

        let network =
            new FakeNetworkClient(
                { Id = "n1"
                  Name = "b"
                  Driver = NetworkDriver.Bridge
                  Subnet = ""
                  Gateway = ""
                  CreatedAt = "" },
                { Success = true
                  Ifname = "eth0"
                  Ipv4Address = "10.0.0.2"
                  Gateway = "10.0.0.1"
                  Message = "" }
            )

        let clients =
            FakeDiploClients(
                new FakeContainerClient(Unchecked.defaultof<_>, Unchecked.defaultof<_>, Unchecked.defaultof<_>),
                network,
                new FakeVolumeClient(Unchecked.defaultof<_>, Unchecked.defaultof<_>)
            )

        let code =
            run
                (RunCniPluginCommand(output, clients))
                (RunCniPluginSettings(
                    PluginPath = "C:\\plugins\\x.exe",
                    CniCommand = "ADD",
                    ContainerId = "c",
                    NetnsPath = "C:\\ns\\1"
                ))

        code |> should equal 0
        network.RunCniCalls |> should equal 1
        output.Successes |> should contain "Plugin CNI exÃ©cutÃ© avec succÃ¨s"
        output.Lines |> should contain (sprintf "  IPv4      : %s" "10.0.0.2")

    [<Fact>]
    let ``run-cni-plugin en Ã©chec retourne 1 et Ã©crit l'erreur`` () =
        let output = MockOutputPort()

        let network =
            new FakeNetworkClient(
                { Id = "n1"
                  Name = "b"
                  Driver = NetworkDriver.Bridge
                  Subnet = ""
                  Gateway = ""
                  CreatedAt = "" },
                { Success = false
                  Ifname = ""
                  Ipv4Address = ""
                  Gateway = ""
                  Message = "ADD a Ã©chouÃ©" }
            )

        let clients =
            FakeDiploClients(
                new FakeContainerClient(Unchecked.defaultof<_>, Unchecked.defaultof<_>, Unchecked.defaultof<_>),
                network,
                new FakeVolumeClient(Unchecked.defaultof<_>, Unchecked.defaultof<_>)
            )

        let code =
            run
                (RunCniPluginCommand(output, clients))
                (RunCniPluginSettings(
                    PluginPath = "C:\\plugins\\x.exe",
                    CniCommand = "ADD",
                    ContainerId = "c",
                    NetnsPath = "C:\\ns\\1"
                ))

        code |> should equal 1

        output.Errors
        |> should contain (sprintf "Ã‰chec du plugin CNI: %s" "ADD a Ã©chouÃ©")

    // â”€â”€â”€ Disk â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    open DiploWalker.Cli.Disk

    [<Fact>]
    let ``disk create-image en succÃ¨s retourne 0 et Ã©crit le succÃ¨s`` () =
        let output = MockOutputPort()
        let root = TestHelpers.createTempDir "cli-disk"

        try
            let src = Path.Combine(root, "src")
            let dest = Path.Combine(root, "out", "test.vhd")
            Directory.CreateDirectory(src) |> ignore
            File.WriteAllText(Path.Combine(src, "hello.txt"), "contenu")

            let code =
                run (CreateImageCommand(output)) (CreateImageSettings(Source = src, Dest = dest))

            code |> should equal 0
            output.Successes |> should not' (be Empty)
            File.Exists(dest) |> should equal true
        finally
            TestHelpers.cleanupDir root

