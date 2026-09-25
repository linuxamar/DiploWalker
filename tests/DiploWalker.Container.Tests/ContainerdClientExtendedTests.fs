namespace DiploWalker.Container.Tests

open System
open System.IO
open System.Threading
open Xunit
open FsUnit.Xunit
open Grpc.Core
open DiploWalker.Container.Clients
open DiploWalker.Container

type ContainerdClientExtendedTests() =

    let createRunner () = MockProcessRunner()

    let shouldContain (substring: string) (text: string) = Assert.Contains(substring, text)

    let asClient (runner: MockProcessRunner) =
        ContainerdClient(runner) :> DiploWalker.Abstractions.Interfaces.IContainerdClient

    let findCommand (runner: MockProcessRunner) (pattern: string) =
        runner.SecureCommands
        |> List.tryFind (fun (_, args) -> (args |> String.concat " ").Contains(pattern))

    // â”€â”€ Pause / Reprise â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    member _.``PauseContainer appelle task pause``() =
        let runner = createRunner ()
        runner.OnCommand("task pause", "")
        let client = asClient runner
        client.PauseContainer("default", "c-1")

        let cmd = findCommand runner "task pause"
        cmd.IsSome |> should be True
        let (_, args) = cmd.Value
        (args |> String.concat " ") |> shouldContain "c-1"
        (args |> String.concat " ") |> shouldContain "default"

    [<Fact>]
    member _.``ResumeContainer appelle task resume``() =
        let runner = createRunner ()
        runner.OnCommand("task resume", "")
        let client = asClient runner
        client.ResumeContainer("default", "c-1")

        findCommand runner "task resume" |> should not' (equal None)

    // â”€â”€ Renommage / top / stats â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    member _.``RenameContainer appelle container rename avec l'ancien et le nouveau nom``() =
        let runner = createRunner ()
        runner.OnCommand("container rename", "")
        let client = asClient runner
        client.RenameContainer("default", "c-1", "c-2")

        let cmd = findCommand runner "container rename"
        cmd.IsSome |> should be True
        let (_, args) = cmd.Value
        let joined = args |> String.concat " "
        joined |> shouldContain "c-1"
        joined |> shouldContain "c-2"

    [<Fact>]
    member _.``TopContainer retourne la sortie de task ps``() =
        let runner = createRunner ()
        runner.OnCommand("task ps", "UID PID PPID CMD\nroot 1 0 sleep")
        let client = asClient runner

        let result = client.TopContainer("default", "c-1")
        result |> shouldContain "PID"

    [<Fact>]
    member _.``TopContainer en erreur retourne un message``() =
        let runner = createRunner ()
        runner.SetFail("ctr a Ã©chouÃ©")
        let client = asClient runner

        let result = client.TopContainer("default", "c-1")
        result |> shouldContain "Erreur"

    [<Fact>]
    member _.``GetContainerStats retourne les metriques parsees``() =
        let runner = createRunner ()
        runner.OnCommand("task metrics", """{"memory":{"usage":123456}}""")
        let client = asClient runner

        let result = client.GetContainerStats("default", "c-1")
        result.GetProperty("memory").GetProperty("usage").GetInt64() |> should equal 123456L

    [<Fact>]
    member _.``GetContainerStats en erreur retourne un objet vide``() =
        let runner = createRunner ()
        runner.SetFail("ctr a Ã©chouÃ©")
        let client = asClient runner

        let result = client.GetContainerStats("default", "c-1")
        result.ToString() |> should equal "{}"

    // â”€â”€ Images â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    member _.``ListImages parse les refs et les tags``() =
        let runner = createRunner ()

        runner.OnCommand(
            "image list",
            "REF TYPE DIGEST SIZE"
            + "\n"
            + "docker.io/library/nginx:latest application/vnd.oci.image.manifest.v1+json sha256:aaa 100"
            + "\n"
            + "docker.io/library/redis@sha256:bbb application/vnd.docker.distribution.manifest.v2+json sha256:bbb 200"
        )

        let client = asClient runner
        let images = client.ListImages("default")
        images |> should haveLength 2

        let nginx = images |> List.find (fun e -> (e.GetProperty "ref").GetString().Contains("nginx"))
        (nginx.GetProperty "ref").GetString() |> shouldContain "nginx:latest"
        (nginx.GetProperty "repository").GetString() |> should equal "docker.io/library/nginx"
        (nginx.GetProperty "tag").GetString() |> should equal "latest"

        let redis = images |> List.find (fun e -> (e.GetProperty "ref").GetString().Contains("redis"))
        (redis.GetProperty "repository").GetString() |> should equal "docker.io/library/redis"
        (redis.GetProperty "tag").GetString() |> should equal ""

        // La ligne d'en-tÃªte "REF" n'est pas restituÃ©e comme une image.
        images
        |> List.exists (fun e -> (e.GetProperty "ref").GetString().Equals("REF", StringComparison.Ordinal))
        |> should be False

    [<Fact>]
    member _.``ListImages en erreur retourne une liste vide``() =
        let runner = createRunner ()
        runner.SetFail("ctr a Ã©chouÃ©")
        let client = asClient runner
        client.ListImages("default") |> should be Empty

    [<Fact>]
    member _.``InspectImage retourne les metadonnees``() =
        let runner = createRunner ()
        runner.OnCommand("image info", """{"name":"nginx","digest":"sha256:abc"}""")
        let client = asClient runner

        let result = client.InspectImage("default", "docker.io/library/nginx:latest")
        result.GetProperty("name").GetString() |> should equal "nginx"

    [<Fact>]
    member _.``InspectImage en erreur retourne un objet vide``() =
        let runner = createRunner ()
        runner.SetFail("ctr a Ã©chouÃ©")
        let client = asClient runner

        let result = client.InspectImage("default", "nginx:latest")
        result.ToString() |> should equal "{}"

    [<Fact>]
    member _.``RemoveImage retourne un message de reussite``() =
        let runner = createRunner ()
        runner.OnCommand("image remove", "")
        let client = asClient runner

        let result = client.RemoveImage("default", "nginx:latest")
        result |> shouldContain "supprimÃ©e"

    [<Fact>]
    member _.``RemoveImage en erreur retourne un message d'erreur``() =
        let runner = createRunner ()
        runner.SetFail("ctr a Ã©chouÃ©")
        let client = asClient runner

        let result = client.RemoveImage("default", "nginx:latest")
        result |> shouldContain "Erreur"

    [<Fact>]
    member _.``TagImage appelle image tag source et cible``() =
        let runner = createRunner ()
        runner.OnCommand("image tag", "")
        let client = asClient runner
        client.TagImage("default", "nginx:latest", "myreg/nginx:v2")

        let cmd = findCommand runner "image tag"
        cmd.IsSome |> should be True
        let (_, args) = cmd.Value
        let joined = args |> String.concat " "
        joined |> shouldContain "nginx:latest"
        joined |> shouldContain "myreg/nginx:v2"

    [<Fact>]
    member _.``ExportImage appelle image export avec archive et ref``() =
        let runner = createRunner ()
        runner.OnCommand("image export", "")
        let client = asClient runner
        client.ExportImage("default", "nginx:latest", "C:\\tmp\\img.tar")

        let cmd = findCommand runner "image export"
        cmd.IsSome |> should be True
        let (_, args) = cmd.Value
        let joined = args |> String.concat " "
        joined |> shouldContain "img.tar"
        joined |> shouldContain "nginx:latest"

    [<Fact>]
    member _.``ImportImage retourne les refs importees``() =
        let runner = createRunner ()
        runner.OnCommand("image import", "sha256:abc\nsha256:def")
        let client = asClient runner

        let result = client.ImportImage("default", "C:\\tmp\\img.tar")
        result |> should haveLength 2

    // â”€â”€ Namespaces â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    member _.``CreateNamespace appelle namespace create``() =
        let runner = createRunner ()
        runner.OnCommand("namespace create", "")
        let client = asClient runner
        client.CreateNamespace("myns")
        findCommand runner "namespace create" |> should not' (equal None)

    [<Fact>]
    member _.``DeleteNamespace appelle namespace remove``() =
        let runner = createRunner ()
        runner.OnCommand("namespace remove", "")
        let client = asClient runner
        client.DeleteNamespace("myns")
        findCommand runner "namespace remove" |> should not' (equal None)

    [<Fact>]
    member _.``CreateNamespace rejette un nom invalide``() =
        let runner = createRunner ()
        let client = asClient runner

        let ex =
            Assert.Throws<Grpc.Core.RpcException>(fun () -> client.CreateNamespace("mauvais nom"))

        ex.StatusCode |> should equal StatusCode.InvalidArgument

    // â”€â”€ TaskInfo â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    member _.``TaskInfo retourne le statut et le PID``() =
        let runner = createRunner ()
        runner.OnCommand("tasks list", "TASK PID STATUS\nc-1 1234 RUNNING")
        let client = asClient runner

        let result = client.TaskInfo("default", "c-1")
        (result.GetProperty "status").GetString() |> should equal "RUNNING"
        (result.GetProperty "pid").GetInt64() |> should equal 1234L

    [<Fact>]
    member _.``TaskInfo pour un conteneur absent retourne un objet vide``() =
        let runner = createRunner ()
        runner.OnCommand("tasks list", "TASK PID STATUS\nother 0 STOPPED")
        let client = asClient runner

        let result = client.TaskInfo("default", "absent")
        result.ToString() |> should equal "{}"

    // â”€â”€ CreateContainer : options avancÃ©es â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    member _.``CreateContainer avec env, memoire, cpu et commande ajoute les bons arguments``() =
        let runner = createRunner ()
        runner.OnCommand("container create", "out")
        let client = asClient runner

        client.CreateContainer(
            "default",
            "c-full",
            "nginx:latest",
            Map.empty,
            Map.ofList [ "APP", "demo" ],
            [| "sh" |],
            [| "-c"; "echo hi" |],
            512L,
            1024,
            0u,
            []
        )
        |> ignore

        let cmd = findCommand runner "container create"
        cmd.IsSome |> should be True
        let (_, args) = cmd.Value
        let joined = args |> String.concat " "
        joined |> shouldContain "--env APP=demo"
        joined |> shouldContain "--memory-limit 512"
        joined |> shouldContain "--cpu-shares 1024"
        joined |> shouldContain "sh"
        joined |> shouldContain "-c"
        joined |> shouldContain "echo hi"

    // â”€â”€ Validations â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    member _.``CreateContainer rejette un namespace invalide``() =
        let runner = createRunner ()
        let client = asClient runner

        let ex =
            Assert.Throws<Grpc.Core.RpcException>(fun () ->
                client.CreateContainer(
                    "mauvais namespace",
                    "c-1",
                    "nginx:latest",
                    Map.empty,
                    Map.empty,
                    Array.empty,
                    Array.empty,
                    0L,
                    0L,
                    0u,
                    []
                )
                |> ignore)

        ex.StatusCode |> should equal StatusCode.InvalidArgument

    [<Fact>]
    member _.``CreateContainer rejette un identifiant de conteneur invalide``() =
        let runner = createRunner ()
        let client = asClient runner

        let ex =
            Assert.Throws<Grpc.Core.RpcException>(fun () ->
                client.CreateContainer(
                    "default",
                    "mauvais id",
                    "nginx:latest",
                    Map.empty,
                    Map.empty,
                    Array.empty,
                    Array.empty,
                    0L,
                    0L,
                    0u,
                    []
                )
                |> ignore)

        ex.StatusCode |> should equal StatusCode.InvalidArgument

    [<Fact>]
    member _.``CreateContainer rejette un nom d'image invalide``() =
        let runner = createRunner ()
        let client = asClient runner

        let ex =
            Assert.Throws<Grpc.Core.RpcException>(fun () ->
                client.CreateContainer(
                    "default",
                    "c-1",
                    "mauvais nom d'image",
                    Map.empty,
                    Map.empty,
                    Array.empty,
                    Array.empty,
                    0L,
                    0L,
                    0u,
                    []
                )
                |> ignore)

        ex.StatusCode |> should equal StatusCode.InvalidArgument

    [<Fact>]
    member _.``ExecInContainer rejette une commande avec un argument vide``() =
        let runner = createRunner ()
        let client = asClient runner

        let ex =
            Assert.Throws<Grpc.Core.RpcException>(fun () ->
                client.ExecInContainer("default", "c-1", [| "sh"; "" |]) |> ignore)

        ex.StatusCode |> should equal StatusCode.InvalidArgument

    // â”€â”€ Attente de sortie â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    member _.``WaitForContainerExit retourne 0 quand le conteneur est STOPPED``() =
        task {
            let runner = createRunner ()
            runner.OnCommand("tasks list", "TASK PID STATUS\nc-1 0 STOPPED")
            let client = asClient runner
            let! code = client.WaitForContainerExit("default", "c-1", 0, System.Threading.CancellationToken.None)
            code |> should equal 0
        }

    [<Fact>]
    member _.``WaitForContainerExit retourne 0 quand le conteneur est DELETED``() =
        task {
            let runner = createRunner ()
            runner.OnCommand("tasks list", "TASK PID STATUS\nc-1 0 DELETED")
            let client = asClient runner
            let! code = client.WaitForContainerExit("default", "c-1", 0, System.Threading.CancellationToken.None)
            code |> should equal 0
        }

    [<Fact>]
    member _.``WaitForContainerExit retourne 0 quand le conteneur est absent``() =
        task {
            let runner = createRunner ()
            runner.OnCommand("tasks list", "TASK PID STATUS\nother 0 STOPPED")
            let client = asClient runner
            let! code = client.WaitForContainerExit("default", "absent", 0, System.Threading.CancellationToken.None)
            code |> should equal 0
        }

    [<Fact>]
    member _.``WaitForContainerExit retourne -1 au delai quand le conteneur tourne``() =
        task {
            let runner = createRunner ()
            runner.OnCommand("tasks list", "TASK PID STATUS\nc-1 123 RUNNING")
            let client = asClient runner
            let! code = client.WaitForContainerExit("default", "c-1", 1, System.Threading.CancellationToken.None)
            code |> should equal -1
        }

    [<Fact>]
    member _.``WaitForContainerExit ne traite pas PAUSED comme terminal``() =
        task {
            let runner = createRunner ()
            runner.OnCommand("tasks list", "TASK PID STATUS\nc-1 123 PAUSED")
            let client = asClient runner
            // PAUSED n'est pas un Ã©tat terminal : on attend la reprise, donc le
            // dÃ©lai est atteint et la fonction retourne -1.
            let! code = client.WaitForContainerExit("default", "c-1", 1, System.Threading.CancellationToken.None)
            code |> should equal -1
        }


