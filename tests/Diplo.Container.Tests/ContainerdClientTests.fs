namespace Diplo.Container.Tests

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Xunit
open FsUnit.Xunit
open Grpc.Core
open Diplo.Container.Clients
open Diplo.Container

[<Collection("registry-state")>]
type ContainerdClientTests() =

    let createRunner () =
        let runner = MockProcessRunner()

        runner.OnCommand(
            "version",
            "Client:\n  Version:  v1.7.27\n  Revision: abc123\n  Go version: go1.22.5\n\nServer:\n  Version:  v1.7.27\n  Revision: abc123"
        )

        runner.OnCommand("namespace list --quiet", "default\nmoby")
        runner

    let shouldContain (substring: string) (text: string) = Assert.Contains(substring, text)

    let shouldNotContain (substring: string) (text: string) = Assert.DoesNotContain(substring, text)

    [<Fact>]
    member _.``Version retourne version et revision``() =
        let runner = createRunner ()

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        let result = client.Version()
        result |> should startWith "v1.7.27"
        result |> shouldContain "abc123"

    [<Fact>]
    member _.``Namespaces retourne les namespaces``() =
        let runner = createRunner ()

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        let result = client.Namespaces()
        result |> should haveLength 2
        result |> should contain "default"
        result |> should contain "moby"

    [<Fact>]
    member _.``CreateContainer envoie la bonne commande ctr``() =
        let runner = createRunner ()
        runner.OnCommand("container create", "abc123")

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        let result =
            client.CreateContainer(
                "default",
                "test-123",
                "mcr.microsoft.com/dotnet/runtime:10.0",
                Map.empty,
                Map.empty,
                Array.empty,
                Array.empty,
                0L,
                0L,
                0u,
                []
            )

        result |> should equal "test-123"

        let cmd =
            runner.SecureCommands
            |> List.tryFind (fun (_, args) -> (args |> String.concat " ").Contains("container create"))

        cmd.IsSome |> should be True
        let (_, args) = cmd.Value
        let joined = args |> String.concat " "
        joined |> shouldContain "test-123"
        joined |> shouldContain "--namespace"

    [<Fact>]
    member _.``CreateContainer avec labels ajoute les bons arguments``() =
        let runner = createRunner ()
        runner.OnCommand("container create", "xyz")

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        let labels = Map.ofList [ "app", "web"; "env", "prod" ]

        client.CreateContainer(
            "moby",
            "c-1",
            "nginx:latest",
            labels,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            []
        )
        |> ignore

        let (_, args) =
            runner.SecureCommands
            |> List.find (fun (_, a) -> (a |> String.concat " ").Contains("container create"))

        let joined = args |> String.concat " "
        joined |> shouldContain "--label app=web"
        joined |> shouldContain "--label env=prod"

    [<Fact>]
    member _.``CreateContainer avec montages ajoute les --mount``() =
        let runner = createRunner ()
        runner.OnCommand("container create", "abc123")

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        let dataSrc = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "diplo-data")
        let keysSrc = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "diplo-keys")
        let mounts = [ (dataSrc, "C:\\app", false); (keysSrc, "C:\\keys", true) ]

        client.CreateContainer(
            "default",
            "m-1",
            "nginx:latest",
            Map.empty,
            Map.empty,
            Array.empty,
            Array.empty,
            0L,
            0L,
            0u,
            mounts
        )
        |> ignore

        let (_, args) =
            runner.SecureCommands
            |> List.find (fun (_, a) -> (a |> String.concat " ").Contains("container create"))

        let joined = args |> String.concat " "
        joined |> shouldContain "--mount"

        joined
        |> shouldContain (sprintf "type=bind,src=%s,dst=C:\\app,options=rbind" dataSrc)

        joined
        |> shouldContain (sprintf "type=bind,src=%s,dst=C:\\keys,options=rbind,ro" keysSrc)

    [<Fact>]
    member _.``StartContainer detache passe --detach``() =
        let runner = createRunner ()
        runner.OnCommand("task start", "")

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        client.StartContainer("default", "my-container", true)

        let cmd =
            runner.SecureCommands
            |> List.tryFind (fun (_, args) -> (args |> String.concat " ").Contains("task start"))

        cmd.IsSome |> should be True
        let (_, args) = cmd.Value
        (args |> String.concat " ") |> shouldContain "--detach"
        (args |> String.concat " ") |> shouldContain "my-container"

    [<Fact>]
    member _.``StartContainer attache n'ajoute pas --detach``() =
        let runner = createRunner ()
        runner.OnCommand("task start", "")

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        client.StartContainer("default", "my-container", false)

        let cmd =
            runner.SecureCommands
            |> List.tryFind (fun (_, args) -> (args |> String.concat " ").Contains("task start"))

        cmd.IsSome |> should be True
        let (_, args) = cmd.Value
        Assert.DoesNotContain("--detach", args |> String.concat " ")

    [<Fact>]
    member _.``StopContainer appelle task kill avec SIGTERM``() =
        let runner = createRunner ()
        runner.OnCommand("task kill", "")

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        client.StopContainer("default", "c-1", 0) |> ignore

        let cmd =
            runner.SecureCommands
            |> List.tryFind (fun (_, args) -> (args |> String.concat " ").Contains("task kill"))

        cmd.IsSome |> should be True
        let (_, args) = cmd.Value
        (args |> String.concat " ") |> shouldContain "SIGTERM"

    [<Fact>]
    member _.``DeleteContainer appelle container delete``() =
        let runner = createRunner ()
        runner.OnCommand("container delete", "")

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        client.DeleteContainer("default", "c-1", false)

        let cmd =
            runner.SecureCommands
            |> List.tryFind (fun (_, args) -> (args |> String.concat " ").Contains("container delete"))

        cmd.IsSome |> should be True

    [<Fact>]
    member _.``DeleteContainer avec force appelle SIGKILL puis delete``() =
        let runner = createRunner ()
        runner.OnCommand("task kill", "")
        runner.OnCommand("container delete", "")

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        client.DeleteContainer("default", "c-1", true)

        let killCmd =
            runner.SecureCommands
            |> List.tryFind (fun (_, args) -> (args |> String.concat " ").Contains("SIGKILL"))

        killCmd.IsSome |> should be True

        let deleteCmd =
            runner.SecureCommands
            |> List.tryFind (fun (_, args) -> (args |> String.concat " ").Contains("container delete"))

        deleteCmd.IsSome |> should be True

    [<Fact>]
    member _.``InspectContainer retourne un JsonElement``() =
        let runner = createRunner ()
        runner.OnCommand("container info", """{"id":"c-1","image":"nginx","status":"running"}""")

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        let result = client.InspectContainer("default", "c-1")
        result.GetProperty("id").GetString() |> should equal "c-1"
        result.GetProperty("status").GetString() |> should equal "running"

    [<Fact>]
    member _.``ListContainers retourne les IDs``() =
        let runner = createRunner ()
        runner.OnCommand("container list", "c-1\nc-2\nc-3")

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        let result = client.ListContainers("default", true)
        result |> should haveLength 3
        result |> should contain "c-1"

    [<Fact>]
    member _.``ListContainers sans all filtre les taches RUNNING``() =
        let runner = createRunner ()
        runner.OnCommand("container list", "c-1\nc-2")
        runner.OnCommand("tasks list", "TASK PID STATUS\nc-1 1234 RUNNING\nc-2 0 STOPPED")

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        let result = client.ListContainers("default", false)
        result |> should haveLength 1
        result.Head |> should equal "c-1"

        let cmd =
            runner.SecureCommands
            |> List.tryFind (fun (_, args) -> (args |> String.concat " ").Contains("--running"))

        cmd.IsSome |> should be False

    [<Fact>]
    member _.``GetContainerLogs lit le journal du conteneur``() =
        let dir =
            Path.Combine(Path.GetTempPath(), "diplo-logs-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory dir |> ignore

        try
            ContainerLogs.setLogsDir dir

            File.WriteAllText(
                ContainerLogs.fileFor "c-1",
                "2026-08-11T10:30:01Z line 1"
                + Environment.NewLine
                + "2026-08-11T10:30:02Z line 2"
                + Environment.NewLine
                + "2026-08-11T10:30:03Z line 3"
            )

            let runner = createRunner ()

            let client =
                ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

            let result = client.GetContainerLogs("default", "c-1", 100, false, "")
            result |> should haveLength 3
            result.Head |> shouldContain "line 1"
            runner.SecureCommands |> should be Empty
        finally
            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    [<Fact>]
    member _.``GetContainerLogs applique le tail et le filtre since``() =
        let dir =
            Path.Combine(Path.GetTempPath(), "diplo-logs-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory dir |> ignore

        try
            ContainerLogs.setLogsDir dir

            File.WriteAllText(
                ContainerLogs.fileFor "c-1",
                "2026-08-11T10:30:01Z line 1"
                + Environment.NewLine
                + "2026-08-11T10:30:02Z line 2"
                + Environment.NewLine
                + "2026-08-11T10:30:03Z line 3"
            )

            let runner = createRunner ()

            let client =
                ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

            let result = client.GetContainerLogs("default", "c-1", 2, false, "")
            result |> should haveLength 2
            result.Head |> shouldContain "line 2"
            result.[1] |> shouldContain "line 3"

            let since =
                client.GetContainerLogs("default", "c-1", 100, false, "2026-08-11T10:30:02Z")

            since |> should haveLength 2
        finally
            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    [<Fact>]
    member _.``GetContainerLogs sans journal renvoie un message explicite``() =
        let dir =
            Path.Combine(Path.GetTempPath(), "diplo-logs-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory dir |> ignore

        try
            ContainerLogs.setLogsDir dir
            let runner = createRunner ()

            let client =
                ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

            let result = client.GetContainerLogs("default", "c-1", 100, false, "")
            result |> should haveLength 1
            result.Head |> shouldContain "Aucun journal"
        finally
            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    [<Fact>]
    member _.``readIncremental retourne les lignes completes et avance l'offset``() =
        let dir =
            Path.Combine(Path.GetTempPath(), "diplo-logs-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory dir |> ignore

        try
            ContainerLogs.setLogsDir dir
            let file = ContainerLogs.fileFor "c-1"
            File.WriteAllText(file, "l1" + Environment.NewLine + "l2" + Environment.NewLine)
            let lines, offset = ContainerLogs.readIncremental "c-1" 0L |> Result.defaultWith failwith
            lines |> should equal [| "l1"; "l2" |]
            offset |> should equal (FileInfo(file).Length)
            let lines2, offset2 = ContainerLogs.readIncremental "c-1" offset |> Result.defaultWith failwith
            lines2 |> should equal [||]
            offset2 |> should equal offset
            File.AppendAllText(file, "l3" + Environment.NewLine)
            let lines3, offset3 = ContainerLogs.readIncremental "c-1" offset |> Result.defaultWith failwith
            lines3 |> should equal [| "l3" |]
            File.AppendAllText(file, "l4")
            let lines4, offset4 = ContainerLogs.readIncremental "c-1" offset3 |> Result.defaultWith failwith
            lines4 |> should equal [||]
            offset4 |> should equal offset3
            File.AppendAllText(file, Environment.NewLine + "l5" + Environment.NewLine)
            let lines5, _ = ContainerLogs.readIncremental "c-1" offset3 |> Result.defaultWith failwith
            lines5 |> should equal [| "l4"; "l5" |]
        finally
            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    [<Fact>]
    member _.``readUpTo retourne les lignes, applique since et tail, et remonte la watermark``() =
        let dir =
            Path.Combine(Path.GetTempPath(), "diplo-logs-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory dir |> ignore

        try
            ContainerLogs.setLogsDir dir

            File.WriteAllText(
                ContainerLogs.fileFor "c-1",
                "2026-08-11T10:30:01Z line 1"
                + Environment.NewLine
                + "2026-08-11T10:30:02Z line 2"
                + Environment.NewLine
                + "2026-08-11T10:30:03Z line 3"
            )

            let lines, offset = ContainerLogs.readUpTo "c-1" 0 "" |> Result.defaultWith failwith

            lines
            |> should equal [| "2026-08-11T10:30:01Z line 1"
                               "2026-08-11T10:30:02Z line 2"
                               "2026-08-11T10:30:03Z line 3" |]

            offset |> should equal (FileInfo(ContainerLogs.fileFor "c-1").Length)

            let since =
                ContainerLogs.readUpTo "c-1" 0 "2026-08-11T10:30:02Z" |> Result.defaultWith failwith |> fst

            since |> should haveLength 2
            since.[0] |> shouldContain "line 2"

            let tailed = ContainerLogs.readUpTo "c-1" 1 "" |> Result.defaultWith failwith |> fst
            tailed |> should equal [| "2026-08-11T10:30:03Z line 3" |]

            ContainerLogs.fileLength "c-1" |> should equal (FileInfo(ContainerLogs.fileFor "c-1").Length)
            ContainerLogs.fileLength "inconnu" |> should equal 0L
            ContainerLogs.readUpTo "inconnu" 0 "" |> Result.defaultWith failwith |> fst |> should be Empty
        finally
            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    [<Fact>]
    member _.``GetContainerLogsStream suit les nouvelles lignes jusqu'a la sortie du conteneur``() =
        let dir =
            Path.Combine(Path.GetTempPath(), "diplo-logs-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory dir |> ignore

        try
            ContainerLogs.setLogsDir dir
            let file = ContainerLogs.fileFor "c-1"
            File.WriteAllText(file, "l1" + Environment.NewLine + "l2" + Environment.NewLine)
            let runner = createRunner ()
            runner.OnCommand("tasks list", "TASK PID STATUS\nc-1 1234 RUNNING")

            let client =
                ContainerdClient(runner, logPollIntervalMs = 50) :> Diplo.Abstractions.Interfaces.IContainerdClient

            let collected = System.Collections.Concurrent.ConcurrentQueue<string>()

            let completed =
                TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)

            let consumer =
                task {
                    let enumerable =
                        client.GetContainerLogsStream("default", "c-1", 100, "", CancellationToken.None)

                    let enumerator = enumerable.GetAsyncEnumerator(CancellationToken.None)

                    try
                        let mutable moving = true

                        while moving do
                            let! hasNext = enumerator.MoveNextAsync().AsTask()

                            if hasNext then
                                collected.Enqueue(enumerator.Current)
                            else
                                moving <- false

                        completed.TrySetResult(true) |> ignore
                    finally
                        (enumerator :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
                }

            let mutable waited = 0

            while waited < 3000 && collected.Count < 2 do
                Thread.Sleep 50
                waited <- waited + 50

            collected.Count |> should equal 2

            // Ajouter de nouvelles lignes puis marquer STOPPED
            File.AppendAllText(file, "l3" + Environment.NewLine + "l4" + Environment.NewLine)
            runner.OnCommand("tasks list", "TASK PID STATUS\nc-1 0 STOPPED")

            // Attendre que le producteur ait fini (TryComplete) avant de vérifier
            completed.Task.Wait(5000) |> should equal true
            consumer.Wait()

            (collected |> Seq.toList) |> should equal [ "l1"; "l2"; "l3"; "l4" ]
        finally
            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    [<Fact>]
    member _.``ExecInContainer retourne la sortie``() =
        let runner = createRunner ()
        runner.OnCommand("tasks exec", "output data")

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        let result = client.ExecInContainer("default", "c-1", [| "ls"; "-la" |])
        result |> should equal "output data"

        let cmd =
            runner.SecureCommands
            |> List.tryFind (fun (_, args) -> (args |> String.concat " ").Contains("tasks exec"))

        cmd.IsSome |> should be True
        let (_, args) = cmd.Value
        (args |> String.concat " ") |> shouldContain "ls"

    [<Fact>]
    member _.``StartExec rejette un identifiant de conteneur vide avant de lancer un processus``() =
        let runner = createRunner ()
        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        let stdin = new MemoryStream()
        let stdout = new MemoryStream()
        let stderr = new MemoryStream()

        let ex =
            Assert.Throws<RpcException>(fun () ->
                client.StartExec("default", "", [| "echo"; "ok" |], stdin, stdout, stderr) |> ignore)

        ex.StatusCode |> should equal StatusCode.InvalidArgument
        runner.SecureCommands |> should be Empty

    [<Fact>]
    member _.``StartExec rejette une commande avec un argument vide avant de lancer un processus``() =
        let runner = createRunner ()
        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        let stdin = new MemoryStream()
        let stdout = new MemoryStream()
        let stderr = new MemoryStream()

        let ex =
            Assert.Throws<RpcException>(fun () ->
                client.StartExec("default", "c-1", [| "echo"; "" |], stdin, stdout, stderr) |> ignore)

        ex.StatusCode |> should equal StatusCode.InvalidArgument
        ex.Status.Detail |> shouldContain "argument vide"
        runner.SecureCommands |> should be Empty

    [<Fact>]
    member _.``Version gere les erreurs ctr``() =
        let runner = createRunner ()
        runner.SetFail("ctr n'est pas installé")

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        let result = client.Version()
        result |> shouldContain "Version inconnue"

    [<Fact>]
    member _.``PullImage sans compte enregistre leve AuthentificationRequise``() =
        let runner = createRunner ()
        runner.OnCommand("image pull", "resolved")

        let stateFile =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "diplo-pull-noauth-" + System.Guid.NewGuid().ToString("N") + ".json"
            )

        try
            RegistryAuth.setStateFile stateFile
            let client =
                ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

            let ex =
                Assert.Throws<RpcException>(fun () -> client.PullImage("nginx:latest", None) |> ignore)

            ex.StatusCode |> should equal StatusCode.InvalidArgument
            ex.Status.Detail |> shouldContain "Authentification requise"
            runner.SecureCommands |> should be Empty
        finally
            try
                System.IO.File.Delete stateFile
            with _ ->
                ()

    [<Fact>]
    member _.``PullImage d'un registre non autorise leve InvalidArgument meme avec --user``() =
        let runner = createRunner ()
        runner.OnCommand("image pull", "resolved")

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        let ex =
            Assert.Throws<RpcException>(fun () ->
                client.PullImage("myregistry.azurecr.io/team/app:latest", Some "inline:secret") |> ignore)

        ex.StatusCode |> should equal StatusCode.InvalidArgument
        ex.Status.Detail |> shouldContain "non autorisée"
        ex.Status.Detail |> shouldContain "ghcr.io"
        runner.SecureCommands |> should be Empty

    [<Fact>]
    member _.``PullImage d'un registre non autorise leve InvalidArgument meme avec compte enregistre``() =
        let runner = createRunner ()
        runner.OnCommand("image pull", "resolved")

        let stateFile =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "diplo-pull-" + System.Guid.NewGuid().ToString("N") + ".json"
            )

        try
            RegistryAuth.setStateFile stateFile
            RegistryAuth.add stateFile "myregistry.azurecr.io" "user" "pass"
            let client =
                ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

            let ex =
                Assert.Throws<RpcException>(fun () ->
                    client.PullImage("myregistry.azurecr.io/team/app:latest", None) |> ignore)

            ex.StatusCode |> should equal StatusCode.InvalidArgument
            ex.Status.Detail |> shouldContain "non autorisée"
            runner.SecureCommands |> should be Empty
        finally
            try
                System.IO.File.Delete stateFile
            with _ ->
                ()

    [<Fact>]
    member _.``PullImage avec --user inline utilise l'identifiant explicite``() =
        let runner = createRunner ()
        runner.OnCommand("image pull", "resolved")

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        let result = client.PullImage("ghcr.io/team/app:latest", Some "inline:secret")
        result |> should equal "resolved"

        // H6 : l'identifiant EXPLICITE passe aussi par le hosts-dir du helper —
        // plus aucun secret dans argv.
        let (_, args) =
            runner.SecureCommands
            |> List.find (fun (_, a) -> (a |> String.concat " ").Contains("image pull"))

        let joined = args |> String.concat " "
        joined |> shouldContain "--hosts-dir"
        joined |> shouldNotContain "--user"
        joined |> shouldNotContain "inline:secret"

    [<Fact>]
    member _.``PullImage --user inline prime sur l'identifiant enregistre``() =
        let runner = createRunner ()
        runner.OnCommand("image pull", "resolved")

        let stateFile =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "diplo-pull-" + System.Guid.NewGuid().ToString("N") + ".json"
            )

        try
            RegistryAuth.setStateFile stateFile
            RegistryAuth.add stateFile "ghcr.io" "stored" "pass"

            let client =
                ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

            let result = client.PullImage("ghcr.io/team/app:latest", Some "inline:secret")

            result |> should equal "resolved"

            let (_, args) =
                runner.SecureCommands
                |> List.find (fun (_, a) -> (a |> String.concat " ").Contains("image pull"))

            let joined = args |> String.concat " "
            joined |> shouldContain "--hosts-dir"
            joined |> shouldNotContain "inline:secret"
            joined |> shouldNotContain "stored:pass"
        finally
            try
                System.IO.File.Delete stateFile
            with _ ->
                ()

    [<Fact>]
    member _.``PullImage avec identifiant enregistre passe par le helper sans secret dans argv``() =
        let runner = createRunner ()
        runner.OnCommand("image pull", "resolved")

        let stateFile =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "diplo-pull-" + System.Guid.NewGuid().ToString("N") + ".json"
            )

        try
            RegistryAuth.setStateFile stateFile
            RegistryAuth.add stateFile "ghcr.io" "user" "secret"

            let client =
                ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

            let result = client.PullImage("ghcr.io/team/app:latest", None)
            result |> should equal "resolved"

            let (_, args) =
                runner.SecureCommands
                |> List.find (fun (_, a) -> (a |> String.concat " ").Contains("image pull"))

            let joined = args |> String.concat " "

            // Identifiants STOCKÉS : hosts-dir + helper, aucun secret en argv.
            joined |> shouldNotContain "--user"
            joined |> shouldNotContain "user:secret"
            joined |> shouldContain "--hosts-dir"
        finally
            try
                System.IO.File.Delete stateFile
            with _ ->
                ()

    [<Fact>]
    member _.``PullImage refuse --user sans le format utilisateur:secret (pas de repli argv)``() =
        let runner = createRunner ()
        runner.OnCommand("image pull", "resolved")

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        let ex =
            Assert.Throws<RpcException>(fun () ->
                client.PullImage("ghcr.io/team/app:latest", Some "identifiant") |> ignore)

        ex.StatusCode |> should equal StatusCode.InvalidArgument
        ex.Status.Detail |> shouldContain "utilisateur:secret"

        // Refus catégorique : aucune commande ctr n'est exécutée, aucun --user en argv.
        runner.SecureCommands |> should be Empty

    [<Fact>]
    member _.``PullImage de Docker Hub prepare un hosts-dir pour docker.io``() =
        let runner = createRunner ()
        runner.OnCommand("image pull", "resolved")

        let stateFile =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "diplo-pull-" + System.Guid.NewGuid().ToString("N") + ".json"
            )

        try
            RegistryAuth.setStateFile stateFile
            RegistryAuth.add stateFile "docker.io" "hubuser" "hubpass"

            let client =
                ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

            // Instantané AVANT le pull : l'assertion doit être relative, pas
            // dépendre des résidus d'exécutions précédentes.
            let before =
                System.IO.Directory.GetDirectories(System.IO.Path.GetTempPath(), "diplo-hosts-*")

            client.PullImage("library/nginx:latest", None) |> ignore

            let (_, args) =
                runner.SecureCommands
                |> List.find (fun (_, a) -> (a |> String.concat " ").Contains("image pull"))

            let joined = args |> String.concat " "
            joined |> shouldContain "--hosts-dir"
            joined |> shouldNotContain "hubuser:hubpass"

            // Le répertoire temporaire est nettoyé après le pull : aucun
            // nouveau hosts-dir ne subsiste.
            let after =
                System.IO.Directory.GetDirectories(System.IO.Path.GetTempPath(), "diplo-hosts-*")

            (after.Length - before.Length) |> should equal 0
        finally
            try
                System.IO.File.Delete stateFile
            with _ ->
                ()
            try
                System.IO.File.Delete stateFile
            with _ ->
                ()

    [<Theory>]
    [<InlineData("docker.io", "https://registry-1.docker.io")>]
    [<InlineData("myregistry.azurecr.io", "https://myregistry.azurecr.io")>]
    [<InlineData("https://reg.ex.io/v2", "https://reg.ex.io")>]
    member _.``normalizeRegistryHost mappe les hotes canoniques``(input: string, expected: string) =
        RegistryAuth.normalizeRegistryHost input |> should equal expected

    [<Fact>]
    member _.``prepareHostsDir genere un hosts.toml delegant au helper``() =
        let dir =
            RegistryAuth.prepareHostsDir "myregistry.azurecr.io"
            |> Option.defaultValue ""

        try
            dir |> should not' (be Null)
            dir |> should not' (equal "")

            let toml =
                System.IO.File.ReadAllText(
                    System.IO.Path.Combine(dir, "myregistry.azurecr.io", "hosts.toml")
                )

            toml |> shouldContain "[host.\"https://myregistry.azurecr.io\"]"
            toml |> shouldContain "auth = '"
            toml |> shouldNotContain "secret"
        finally
            try
                System.IO.Directory.Delete(dir, true)
            with _ ->
                ()

    [<Fact>]
    member _.``DeleteContainer avec force sur conteneur inexistant leve sur le delete``() =
        let runner = createRunner ()
        runner.SetFail("ctr a échoué")

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        // Le kill est best-effort, mais l'échec de la SUPPRESSION doit remonter :
        // un succès mensonger libérerait prématurément les volumes montés.
        Assert.Throws<System.Exception>(fun () -> client.DeleteContainer("default", "absent-1", true))

    [<Fact>]
    member _.``UpdateContainer avec limites demandees leve Unimplemented``() =
        let runner = createRunner ()

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        let ex =
            Assert.Throws<Grpc.Core.RpcException>(fun () -> client.UpdateContainer("default", "c-1", 512L, 1024, 64))

        ex.StatusCode |> should equal StatusCode.Unimplemented

    [<Fact>]
    member _.``UpdateContainer sans limite reste un succes silencieux``() =
        let runner = createRunner ()

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        client.UpdateContainer("default", "c-1", 0L, 0, 0)

    // ── Segments --mount : injections interdites ────────────────
    //
    // La spec --mount est une liste séparée par virgules : une destination
    // OU une source contenant «, », un espace ou « .. » permettrait
    // d'injecter des options arbitraires (ex. annuler le « ro » imposé).

    [<Fact>]
    member _.``CreateContainer refuse une destination contenant une virgule``() =
        let runner = createRunner ()

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        let src = Path.Combine(Path.GetTempPath(), "diplo-data")

        let inj dst =
            fun () ->
                client.CreateContainer(
                    "default",
                    "inj",
                    "nginx:latest",
                    Map.empty,
                    Map.empty,
                    Array.empty,
                    Array.empty,
                    0L,
                    0L,
                    0u,
                    [ (src, dst, true) ]
                )
                |> ignore

        Assert.Throws<ArgumentException>(inj "C:\\app,options=rbind")

    [<Fact>]
    member _.``CreateContainer refuse une destination contenant un espace``() =
        let runner = createRunner ()

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        let src = Path.Combine(Path.GetTempPath(), "diplo-data")

        let inj dst =
            fun () ->
                client.CreateContainer(
                    "default",
                    "inj",
                    "nginx:latest",
                    Map.empty,
                    Map.empty,
                    Array.empty,
                    Array.empty,
                    0L,
                    0L,
                    0u,
                    [ (src, dst, true) ]
                )
                |> ignore

        Assert.Throws<ArgumentException>(inj "C:\\app data")

    [<Fact>]
    member _.``CreateContainer refuse une destination avec traversée ..``() =
        let runner = createRunner ()

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        let src = Path.Combine(Path.GetTempPath(), "diplo-data")

        let inj dst =
            fun () ->
                client.CreateContainer(
                    "default",
                    "inj",
                    "nginx:latest",
                    Map.empty,
                    Map.empty,
                    Array.empty,
                    Array.empty,
                    0L,
                    0L,
                    0u,
                    [ (src, dst, true) ]
                )
                |> ignore

        Assert.Throws<ArgumentException>(inj "C:\\..\\escape")

    [<Fact>]
    member _.``CreateContainer refuse une destination vide``() =
        let runner = createRunner ()

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        let src = Path.Combine(Path.GetTempPath(), "diplo-data")

        let inj dst =
            fun () ->
                client.CreateContainer(
                    "default",
                    "inj",
                    "nginx:latest",
                    Map.empty,
                    Map.empty,
                    Array.empty,
                    Array.empty,
                    0L,
                    0L,
                    0u,
                    [ (src, dst, true) ]
                )
                |> ignore

        Assert.Throws<ArgumentException>(inj "")

    [<Fact>]
    member _.``CreateContainer refuse une source contenant une virgule``() =
        let runner = createRunner ()
        runner.OnCommand("container create", "abc")

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        // Chemin sous %TEMP% (répertoire autorisé) mais contenant une virgule :
        // validateVolumePath le laisse passer, le contrôle de segment doit le rejeter.
        let src = Path.Combine(Path.GetTempPath(), "diplo,data")

        let inj s =
            fun () ->
                client.CreateContainer(
                    "default",
                    "inj",
                    "nginx:latest",
                    Map.empty,
                    Map.empty,
                    Array.empty,
                    Array.empty,
                    0L,
                    0L,
                    0u,
                    [ (s, "C:\\app", true) ]
                )
                |> ignore

        Assert.Throws<ArgumentException>(inj src)

    [<Fact>]
    member _.``CreateContainer refuse une source contenant un espace``() =
        let runner = createRunner ()
        runner.OnCommand("container create", "abc")

        let client =
            ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient

        let src = Path.Combine(Path.GetTempPath(), "diplo data")

        let inj s =
            fun () ->
                client.CreateContainer(
                    "default",
                    "inj",
                    "nginx:latest",
                    Map.empty,
                    Map.empty,
                    Array.empty,
                    Array.empty,
                    0L,
                    0L,
                    0u,
                    [ (s, "C:\\app", true) ]
                )
                |> ignore

        Assert.Throws<ArgumentException>(inj src)
