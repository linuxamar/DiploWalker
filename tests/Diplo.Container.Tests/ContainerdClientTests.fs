namespace Diplo.Container.Tests

open Xunit
open FsUnit.Xunit
open Diplo.Container.Clients

type ContainerdClientTests() =

    let createRunner () =
        let runner = MockProcessRunner()
        runner.OnCommand("version", """{"Version":"1.7.27","Revision":"abc123","Go":"go1.22.5","OS":"windows","Arch":"amd64"}""")
        runner.OnCommand("namespace list --quiet", "default\nmoby")
        runner

    let shouldContain (substring: string) (text: string) =
        Assert.Contains(substring, text)

    [<Fact>]
    member _.``Version retourne version et revision``() =
        let runner = createRunner ()
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        let result = client.Version()
        result |> should startWith "1.7.27"
        result |> shouldContain "abc123"

    [<Fact>]
    member _.``Namespaces retourne les namespaces``() =
        let runner = createRunner ()
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        let result = client.Namespaces()
        result |> should haveLength 2
        result |> should contain "default"
        result |> should contain "moby"

    [<Fact>]
    member _.``CreateContainer envoie la bonne commande ctr``() =
        let runner = createRunner ()
        runner.OnCommand("container create", "abc123")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        let result = client.CreateContainer("default", "test-123", "mcr.microsoft.com/dotnet/runtime:10.0", Map.empty)
        result |> should equal "abc123"
        let cmd = runner.Commands |> List.tryFind (fun (_, args) -> args.Contains("container create"))
        cmd.IsSome |> should be True
        let (_, args) = cmd.Value
        args |> shouldContain "test-123"
        args |> shouldContain "--namespace default"

    [<Fact>]
    member _.``CreateContainer avec labels ajoute les bons arguments``() =
        let runner = createRunner ()
        runner.OnCommand("container create", "xyz")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        let labels = Map.ofList [ "app", "web"; "env", "prod" ]
        client.CreateContainer("moby", "c-1", "nginx:latest", labels) |> ignore
        let (_, args) = runner.Commands |> List.find (fun (_, a) -> a.Contains("container create"))
        args |> shouldContain "--label app=web"
        args |> shouldContain "--label env=prod"

    [<Fact>]
    member _.``StartContainer appelle task start``() =
        let runner = createRunner ()
        runner.OnCommand("task start", "")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        client.StartContainer("default", "my-container")
        let cmd = runner.Commands |> List.tryFind (fun (_, args) -> args.Contains("task start"))
        cmd.IsSome |> should be True
        let (_, args) = cmd.Value
        args |> shouldContain "my-container"

    [<Fact>]
    member _.``StopContainer appelle task kill avec SIGTERM``() =
        let runner = createRunner ()
        runner.OnCommand("task kill", "")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        client.StopContainer("default", "c-1", 0)
        let cmd = runner.Commands |> List.tryFind (fun (_, args) -> args.Contains("task kill"))
        cmd.IsSome |> should be True
        let (_, args) = cmd.Value
        args |> shouldContain "SIGTERM"

    [<Fact>]
    member _.``DeleteContainer appelle container delete``() =
        let runner = createRunner ()
        runner.OnCommand("container delete", "")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        client.DeleteContainer("default", "c-1", false)
        let cmd = runner.Commands |> List.tryFind (fun (_, args) -> args.Contains("container delete"))
        cmd.IsSome |> should be True

    [<Fact>]
    member _.``DeleteContainer avec force appelle SIGKILL puis delete``() =
        let runner = createRunner ()
        runner.OnCommand("task kill", "")
        runner.OnCommand("container delete", "")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        client.DeleteContainer("default", "c-1", true)
        let killCmd = runner.Commands |> List.tryFind (fun (_, args) -> args.Contains("SIGKILL"))
        killCmd.IsSome |> should be True
        let deleteCmd = runner.Commands |> List.tryFind (fun (_, args) -> args.Contains("container delete"))
        deleteCmd.IsSome |> should be True

    [<Fact>]
    member _.``InspectContainer retourne un JsonElement``() =
        let runner = createRunner ()
        runner.OnCommand("container info", """{"id":"c-1","image":"nginx","status":"running"}""")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        let result = client.InspectContainer("default", "c-1")
        result.GetProperty("id").GetString() |> should equal "c-1"
        result.GetProperty("status").GetString() |> should equal "running"

    [<Fact>]
    member _.``ListContainers retourne les IDs``() =
        let runner = createRunner ()
        runner.OnCommand("container list", "c-1\nc-2\nc-3")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        let result = client.ListContainers("default", true)
        result |> should haveLength 3
        result |> should contain "c-1"

    [<Fact>]
    member _.``ListContainers sans all utilise --running``() =
        let runner = createRunner ()
        runner.OnCommand("container list --running", "c-1")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        let result = client.ListContainers("default", false)
        result |> should haveLength 1
        let cmd = runner.Commands |> List.tryFind (fun (_, args) -> args.Contains("--running"))
        cmd.IsSome |> should be True

    [<Fact>]
    member _.``GetContainerLogs retourne les lignes``() =
        let runner = createRunner ()
        runner.OnCommand("task logs", "line1\nline2\nline3")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        let result = client.GetContainerLogs("default", "c-1", 100)
        result |> should haveLength 3

    [<Fact>]
    member _.``ExecInContainer retourne la sortie``() =
        let runner = createRunner ()
        runner.OnCommand("exec", "output data")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        let result = client.ExecInContainer("default", "c-1", [| "ls"; "-la" |])
        result |> should equal "output data"
        let cmd = runner.Commands |> List.tryFind (fun (_, args) -> args.Contains("exec"))
        cmd.IsSome |> should be True
        let (_, args) = cmd.Value
        args |> shouldContain "ls -la"

    [<Fact>]
    member _.``Version gere les erreurs ctr``() =
        let runner = createRunner ()
        runner.SetFail("ctr n'est pas installé")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        let result = client.Version()
        result |> shouldContain "Erreur ctr"
        result |> shouldContain "ctr n'est pas installé"
