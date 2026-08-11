namespace Diplo.Container.Tests

open Xunit
open FsUnit.Xunit
open Diplo.Container.Clients
open Diplo.Container

[<Collection("registry-state")>]
type ContainerdClientTests() =

    let createRunner () =
        let runner = MockProcessRunner()
        runner.OnCommand("version", "Client:\n  Version:  v1.7.27\n  Revision: abc123\n  Go version: go1.22.5\n\nServer:\n  Version:  v1.7.27\n  Revision: abc123")
        runner.OnCommand("namespace list --quiet", "default\nmoby")
        runner

    let shouldContain (substring: string) (text: string) =
        Assert.Contains(substring, text)

    let shouldNotContain (substring: string) (text: string) =
        Assert.DoesNotContain(substring, text)

    [<Fact>]
    member _.``Version retourne version et revision``() =
        let runner = createRunner ()
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        let result = client.Version()
        result |> should startWith "v1.7.27"
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
        let result = client.CreateContainer("default", "test-123", "mcr.microsoft.com/dotnet/runtime:10.0", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u, [])
        result |> should equal "abc123"
        let cmd = runner.SecureCommands |> List.tryFind (fun (_, args) -> (args |> String.concat " ").Contains("container create"))
        cmd.IsSome |> should be True
        let (_, args) = cmd.Value
        let joined = args |> String.concat " "
        joined |> shouldContain "test-123"
        joined |> shouldContain "--namespace"

    [<Fact>]
    member _.``CreateContainer avec labels ajoute les bons arguments``() =
        let runner = createRunner ()
        runner.OnCommand("container create", "xyz")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        let labels = Map.ofList [ "app", "web"; "env", "prod" ]
        client.CreateContainer("moby", "c-1", "nginx:latest", labels, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u, []) |> ignore
        let (_, args) = runner.SecureCommands |> List.find (fun (_, a) -> (a |> String.concat " ").Contains("container create"))
        let joined = args |> String.concat " "
        joined |> shouldContain "--label app=web"
        joined |> shouldContain "--label env=prod"

    [<Fact>]
    member _.``CreateContainer avec montages ajoute les --mount``() =
        let runner = createRunner ()
        runner.OnCommand("container create", "abc123")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        let dataSrc = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "diplo-data")
        let keysSrc = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "diplo-keys")
        let mounts = [ (dataSrc, "C:\\app", false); (keysSrc, "C:\\keys", true) ]
        client.CreateContainer("default", "m-1", "nginx:latest", Map.empty, Map.empty, Array.empty, Array.empty, 0L, 0L, 0u, mounts) |> ignore
        let (_, args) = runner.SecureCommands |> List.find (fun (_, a) -> (a |> String.concat " ").Contains("container create"))
        let joined = args |> String.concat " "
        joined |> shouldContain "--mount"
        joined |> shouldContain (sprintf "type=bind,src=%s,dst=C:\\app,options=rbind" dataSrc)
        joined |> shouldContain (sprintf "type=bind,src=%s,dst=C:\\keys,options=rbind,ro" keysSrc)

    [<Fact>]
    member _.``StartContainer detache passe --detach``() =
        let runner = createRunner ()
        runner.OnCommand("task start", "")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        client.StartContainer("default", "my-container", true)
        let cmd = runner.SecureCommands |> List.tryFind (fun (_, args) -> (args |> String.concat " ").Contains("task start"))
        cmd.IsSome |> should be True
        let (_, args) = cmd.Value
        (args |> String.concat " ") |> shouldContain "--detach"
        (args |> String.concat " ") |> shouldContain "my-container"

    [<Fact>]
    member _.``StartContainer attache n'ajoute pas --detach``() =
        let runner = createRunner ()
        runner.OnCommand("task start", "")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        client.StartContainer("default", "my-container", false)
        let cmd = runner.SecureCommands |> List.tryFind (fun (_, args) -> (args |> String.concat " ").Contains("task start"))
        cmd.IsSome |> should be True
        let (_, args) = cmd.Value
        Assert.DoesNotContain("--detach", args |> String.concat " ")

    [<Fact>]
    member _.``StopContainer appelle task kill avec SIGTERM``() =
        let runner = createRunner ()
        runner.OnCommand("task kill", "")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        client.StopContainer("default", "c-1", 0) |> ignore
        let cmd = runner.SecureCommands |> List.tryFind (fun (_, args) -> (args |> String.concat " ").Contains("task kill"))
        cmd.IsSome |> should be True
        let (_, args) = cmd.Value
        (args |> String.concat " ") |> shouldContain "SIGTERM"

    [<Fact>]
    member _.``DeleteContainer appelle container delete``() =
        let runner = createRunner ()
        runner.OnCommand("container delete", "")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        client.DeleteContainer("default", "c-1", false)
        let cmd = runner.SecureCommands |> List.tryFind (fun (_, args) -> (args |> String.concat " ").Contains("container delete"))
        cmd.IsSome |> should be True

    [<Fact>]
    member _.``DeleteContainer avec force appelle SIGKILL puis delete``() =
        let runner = createRunner ()
        runner.OnCommand("task kill", "")
        runner.OnCommand("container delete", "")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        client.DeleteContainer("default", "c-1", true)
        let killCmd = runner.SecureCommands |> List.tryFind (fun (_, args) -> (args |> String.concat " ").Contains("SIGKILL"))
        killCmd.IsSome |> should be True
        let deleteCmd = runner.SecureCommands |> List.tryFind (fun (_, args) -> (args |> String.concat " ").Contains("container delete"))
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
    member _.``ListContainers sans all filtre les taches RUNNING``() =
        let runner = createRunner ()
        runner.OnCommand("container list", "c-1\nc-2")
        runner.OnCommand("tasks list", "TASK PID STATUS\nc-1 1234 RUNNING\nc-2 0 STOPPED")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        let result = client.ListContainers("default", false)
        result |> should haveLength 1
        result.Head |> should equal "c-1"
        let cmd = runner.SecureCommands |> List.tryFind (fun (_, args) -> (args |> String.concat " ").Contains("--running"))
        cmd.IsSome |> should be False

    [<Fact>]
    member _.``GetContainerLogs indique la non-disponibilite en v2``() =
        let runner = createRunner ()
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        let result = client.GetContainerLogs("default", "c-1", 100, false, "")
        result |> should haveLength 1
        result.Head |> shouldContain "non disponibles"
        runner.SecureCommands |> should be Empty

    [<Fact>]
    member _.``ExecInContainer retourne la sortie``() =
        let runner = createRunner ()
        runner.OnCommand("tasks exec", "output data")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        let result = client.ExecInContainer("default", "c-1", [| "ls"; "-la" |])
        result |> should equal "output data"
        let cmd = runner.SecureCommands |> List.tryFind (fun (_, args) -> (args |> String.concat " ").Contains("tasks exec"))
        cmd.IsSome |> should be True
        let (_, args) = cmd.Value
        (args |> String.concat " ") |> shouldContain "ls"

    [<Fact>]
    member _.``Version gere les erreurs ctr``() =
        let runner = createRunner ()
        runner.SetFail("ctr n'est pas installé")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        let result = client.Version()
        result |> shouldContain "Version inconnue"

    [<Fact>]
    member _.``PullImage sans identifiant n'ajoute pas --user``() =
        let runner = createRunner ()
        runner.OnCommand("image pull", "resolved")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        let result = client.PullImage("nginx:latest", None)
        result |> should equal "resolved"
        let (_, args) = runner.SecureCommands |> List.find (fun (_, a) -> (a |> String.concat " ").Contains("image pull"))
        (args |> String.concat " ") |> shouldNotContain "--user"

    [<Fact>]
    member _.``PullImage avec --user inline utilise l'identifiant explicite``() =
        let runner = createRunner ()
        runner.OnCommand("image pull", "resolved")
        let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
        let result = client.PullImage("myregistry.azurecr.io/team/app:latest", Some "inline:secret")
        result |> should equal "resolved"
        let (_, args) = runner.SecureCommands |> List.find (fun (_, a) -> (a |> String.concat " ").Contains("image pull"))
        let joined = args |> String.concat " "
        joined |> shouldContain "--user"
        joined |> shouldContain "inline:secret"

    [<Fact>]
    member _.``PullImage --user inline prime sur l'identifiant enregistre``() =
        let runner = createRunner ()
        runner.OnCommand("image pull", "resolved")
        let stateFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "diplo-pull-" + System.Guid.NewGuid().ToString("N") + ".json")
        try
            RegistryAuth.setStateFile stateFile
            RegistryAuth.add stateFile "myregistry.azurecr.io" "stored" "pass"
            let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
            let result = client.PullImage("myregistry.azurecr.io/team/app:latest", Some "inline:secret")
            result |> should equal "resolved"
            let (_, args) = runner.SecureCommands |> List.find (fun (_, a) -> (a |> String.concat " ").Contains("image pull"))
            let joined = args |> String.concat " "
            joined |> shouldContain "inline:secret"
            joined |> shouldNotContain "stored:pass"
        finally
            try System.IO.File.Delete stateFile with _ -> ()

    [<Fact>]
    member _.``PullImage utilise --user quand un identifiant est enregistre``() =
        let runner = createRunner ()
        runner.OnCommand("image pull", "resolved")
        let stateFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "diplo-pull-" + System.Guid.NewGuid().ToString("N") + ".json")
        try
            RegistryAuth.setStateFile stateFile
            RegistryAuth.add stateFile "myregistry.azurecr.io" "user" "secret"
            let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
            let result = client.PullImage("myregistry.azurecr.io/team/app:latest", None)
            result |> should equal "resolved"
            let (_, args) = runner.SecureCommands |> List.find (fun (_, a) -> (a |> String.concat " ").Contains("image pull"))
            let joined = args |> String.concat " "
            joined |> shouldContain "--user"
            joined |> shouldContain "user:secret"
        finally
            try System.IO.File.Delete stateFile with _ -> ()

    [<Fact>]
    member _.``PullImage de Docker Hub consulte le registre docker.io``() =
        let runner = createRunner ()
        runner.OnCommand("image pull", "resolved")
        let stateFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "diplo-pull-" + System.Guid.NewGuid().ToString("N") + ".json")
        try
            RegistryAuth.setStateFile stateFile
            RegistryAuth.add stateFile "docker.io" "hubuser" "hubpass"
            let client = ContainerdClient(runner) :> Diplo.Abstractions.Interfaces.IContainerdClient
            client.PullImage("library/nginx:latest", None) |> ignore
            let (_, args) = runner.SecureCommands |> List.find (fun (_, a) -> (a |> String.concat " ").Contains("image pull"))
            (args |> String.concat " ") |> shouldContain "hubuser:hubpass"
        finally
            try System.IO.File.Delete stateFile with _ -> ()
