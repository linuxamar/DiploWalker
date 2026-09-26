namespace DiploWalker.Core.Tests

module ComposeOrchestratorTests =

    open System
    open System.Collections.Generic
    open System.IO
    open System.Threading
    open System.Threading.Tasks
    open Xunit
    open FsUnit.Xunit
    open DiploWalker.Core.Clients
    open DiploWalker.Core.Compose
    open DiploWalker.Core.Output
    open DiploWalker.Grpc
    open DiploWalker.Grpc.Container
    open DiploWalker.TestHelpers

    /// Fake IContainerClient qui enregistre les appels du ComposeOrchestrator
    /// et renvoie des réponses prévisibles, sans serveur gRPC.
    type RecordingContainerClient() =

        let mutable created = ResizeArray<string * string>()
        let mutable createdPorts = ResizeArray<(int * int * string) list>()
        let mutable createdLabels = ResizeArray<IDictionary<string, string>>()
        let mutable createdEnv = ResizeArray<IDictionary<string, string>>()
        let mutable createdCommand = ResizeArray<string list option>()
        let mutable createdArgs = ResizeArray<string list option>()
        let mutable createdMounts = ResizeArray<(string * string * bool) list option>()
        let mutable started = ResizeArray<string>()
        let mutable stopped = ResizeArray<string>()
        let mutable deleted = ResizeArray<string>()
        let mutable pulled = ResizeArray<string>()
        let mutable listed = ResizeArray<ContainerInfo>()
        let mutable logEntries = ResizeArray<ContainerLogEntry>()
        let mutable failFromCreate = Int32.MaxValue
        let mutable createCount = 0
        let failOnStopIds = ResizeArray<string>()

        member _.Created = created |> Seq.toList
        member _.CreatedPorts = createdPorts |> Seq.toList
        member _.CreatedLabels = createdLabels |> Seq.toList
        member _.CreatedEnv = createdEnv |> Seq.toList
        member _.CreatedCommand = createdCommand |> Seq.toList
        member _.CreatedArgs = createdArgs |> Seq.toList
        member _.CreatedMounts = createdMounts |> Seq.toList
        member _.Started = started |> Seq.toList
        member _.Stopped = stopped |> Seq.toList
        member _.Deleted = deleted |> Seq.toList
        member _.PulledImages = pulled |> Seq.toList

        member _.FailFromCreate
            with get () = failFromCreate
            and set v = failFromCreate <- v

        member _.SetListedContainers(items: seq<ContainerInfo>) =
            listed <- ResizeArray(items)

        member _.SetLogEntries(entries: seq<ContainerLogEntry>) =
            logEntries <- ResizeArray(entries)

        member _.FailOnStopIds = failOnStopIds

        interface IDisposable with
            member _.Dispose() = ()

        interface IContainerClient with
            member _.CreateAsync
                (name, image, ?_env, ?_command, ?_args, ?labels, ?_pidLimit, ?_memoryLimit, ?_cpuShares, ?_mounts, ?_ports, ?_ct)
                =
                createCount <- createCount + 1

                if createCount >= failFromCreate then
                    raise (Exception("échec simulé"))

                created.Add(name, image)
                createdPorts.Add(defaultArg _ports [])
                createdLabels.Add(defaultArg labels (dict []))
                createdEnv.Add(defaultArg _env (dict []))
                createdCommand.Add(_command)
                createdArgs.Add(_args)
                createdMounts.Add(_mounts)

                Task.FromResult(
                    { CreateContainerResponse.Id = "id-" + name
                      Name = name
                      State = ContainerState.Created
                      CreatedAt = "2026-01-01T00:00:00Z" }
                )

            member _.StartAsync(id, ?_attach, ?_ct) =
                started.Add(id)
                Task.FromResult({ StartContainerResponse.State = ContainerState.Running; Message = "Démarré" })

            member _.StopAsync(id, ?_timeoutSeconds, ?_ct) =
                if failOnStopIds.Contains(id) then
                    raise (Exception("échec simulé de l'arrêt"))

                stopped.Add(id)
                Task.FromResult({ StopContainerResponse.State = ContainerState.Stopped; Message = "Arrêté" })

            member _.DeleteAsync(id, ?_force, ?_ct) =
                deleted.Add(id)
                Task.FromResult({ DeleteContainerResponse.Success = true; Message = "Supprimé" })

            member _.InspectAsync(_id, ?_ct) = raise (NotImplementedException())

            member _.ListAsync(?_namespaceName, ?_all, ?_filters, ?_ct) =
                Task.FromResult(
                    { ListContainersResponse.Containers = listed }
                )

            member _.GetLogsStream(_id, ?_follow, ?_tail, ?_since, ?_ct) = raise (NotImplementedException())

            member _.GetLogs(_id, ?_follow, ?_tail, ?_since, ?_ct) =
                Task.FromResult(logEntries :> seq<ContainerLogEntry>)

            member _.Exec(_id, _command, ?_attachStdout, ?_attachStderr, ?_ct) = raise (NotImplementedException())

            member _.PullImageAsync(image, ?_user, ?_ct) =
                pulled.Add(image)

                Task.FromResult(
                    { PullImageResponse.Image = image
                      Message = "Image téléchargée" }
                )

            member _.LoginRegistryAsync(_registry, _username, _password, ?_ct) = raise (NotImplementedException())
            member _.LogoutRegistryAsync(_registry, ?_ct) = raise (NotImplementedException())
            member _.GetVersionAsync(?_ct) = raise (NotImplementedException())
            member _.ListNamespacesAsync(?_ct) = raise (NotImplementedException())
            member _.RenameContainerAsync(_id, _newName, ?_ct) = raise (NotImplementedException())
            member _.TopContainerAsync(_id, ?_ct) = raise (NotImplementedException())
            member _.GetContainerStatsAsync(_id, ?_ct) = raise (NotImplementedException())

            member _.ListImagesAsync(?_namespaceName, ?_ct) = raise (NotImplementedException())
            member _.InspectImageAsync(_ref, ?_namespaceName, ?_ct) = raise (NotImplementedException())
            member _.RemoveImageAsync(_ref, ?_namespaceName, ?_ct) = raise (NotImplementedException())
            member _.TagImageAsync(_source, _target, ?_namespaceName, ?_ct) = raise (NotImplementedException())
            member _.SearchImagesAsync(_query, ?_registry, ?_limit, ?_ct) = raise (NotImplementedException())

            member _.PauseAsync(_id, ?_ct) = raise (NotImplementedException())
            member _.UnpauseAsync(_id, ?_ct) = raise (NotImplementedException())
            member _.WaitAsync(_id, ?_timeoutSeconds, ?_ct) = raise (NotImplementedException())
            member _.PruneContainersAsync(?_ct) = raise (NotImplementedException())
            member _.PruneImagesAsync(?_ct) = raise (NotImplementedException())
            member _.CommitImageAsync(_containerId, _imageRef, ?_message, ?_author, ?_ct) = raise (NotImplementedException())
            member _.ReadFileAsync(_id, _path, ?_ct) = raise (NotImplementedException())
            member _.WriteFileAsync(_id, _path, _data, ?_ct) = raise (NotImplementedException())
            member _.ExportImageStream(_imageRef, ?_namespaceName, ?_ct) = raise (NotImplementedException())
            member _.ImportImage(_chunks, ?_ct) = raise (NotImplementedException())
            member _.WatchEventsStream(?_ct) = raise (NotImplementedException())
            member _.GetContainerStatsStream(_id, ?_intervalSeconds, ?_ct) = raise (NotImplementedException())

    let private run (t: Task) = t.GetAwaiter().GetResult()

    let private container (id: string, name: string, image: string, state: ContainerState, labels: (string * string) list) =
        { ContainerInfo.Id = id
          Name = name
          Image = image
          State = state
          CreatedAt = "2026-01-01T00:00:00Z"
          Labels = Dictionary<string, string>(dict labels) }

    let private writeCompose (dir: string) (name: string) (content: string) =
        let path = Path.Combine(dir, name)
        File.WriteAllText(path, content)
        path

    [<Fact>]
    let ``ParseFile retourne le modele compose`` () =
        let dir = TestHelpers.createTempDir "compose"

        try
            let path =
                writeCompose dir "compose.yaml"
                    """version: "3.8"
name: app
services:
  web:
    image: nginx:latest
    ports:
      - "8080:80"
      - "8443:443"
    labels:
      - "tier=front"
  db:
    image: postgres:16
"""

            let output = MockOutputPort()
            let client = new RecordingContainerClient()
            use orchestrator = new ComposeOrchestrator(output, client)
            let compose = orchestrator.ParseFile(path)
            compose.Version |> should equal "3.8"
            compose.ProjectName |> should equal "app"
            compose.Services |> should haveLength 2
            let web = compose.Services.[0]
            web.Name |> should equal "web"
            web.Image |> should equal "nginx:latest"
            web.Ports |> should haveLength 2
            web.Ports.[0].HostPort |> should equal (Some 8080)
            web.Ports.[0].ContainerPort |> should equal 80
            web.Ports.[0].Protocol |> should equal "tcp"
            web.Ports.[1].HostPort |> should equal (Some 8443)
            web.Labels.["tier"] |> should equal "front"
        finally
            TestHelpers.cleanupDir dir

    [<Fact>]
    let ``ParseFile leve NotFound (RpcException) si le fichier est introuvable`` () =
        let dir = TestHelpers.createTempDir "compose"

        try
            let output = MockOutputPort()
            let client = new RecordingContainerClient()
            use orchestrator = new ComposeOrchestrator(output, client)
            let raised =
                try
                    orchestrator.ParseFile(Path.Combine(dir, "absent.yaml")) |> ignore
                    false
                with :? Grpc.Core.RpcException as ex -> ex.Status.StatusCode |> should equal Grpc.Core.StatusCode.NotFound; true

            raised |> should equal true
        finally
            TestHelpers.cleanupDir dir

    [<Fact>]
    let ``ParseFile leve InvalidArgument si le fichier est vide`` () =
        let dir = TestHelpers.createTempDir "compose"

        try
            let path = writeCompose dir "vide.yaml" ""
            let output = MockOutputPort()
            let client = new RecordingContainerClient()
            use orchestrator = new ComposeOrchestrator(output, client)
            let raised =
                try
                    orchestrator.ParseFile(path) |> ignore
                    false
                with :? Grpc.Core.RpcException as ex ->
                    ex.Status.StatusCode |> should equal Grpc.Core.StatusCode.InvalidArgument
                    true

            raised |> should equal true
        finally
            TestHelpers.cleanupDir dir

    [<Fact>]
    let ``ParseFile leve InvalidArgument pour un service sans image ni build`` () =
        let dir = TestHelpers.createTempDir "compose"

        try
            let path =
                writeCompose dir "compose.yaml"
                    """services:
  web:
    ports:
      - "8080:80"
"""

            let output = MockOutputPort()
            let client = new RecordingContainerClient()
            use orchestrator = new ComposeOrchestrator(output, client)
            let raised =
                try
                    orchestrator.ParseFile(path) |> ignore
                    false
                with :? Grpc.Core.RpcException as ex ->
                    ex.Status.StatusCode |> should equal Grpc.Core.StatusCode.InvalidArgument
                    true

            raised |> should equal true
        finally
            TestHelpers.cleanupDir dir

    [<Fact>]
    let ``ParseFile leve InvalidArgument pour des ports invalides`` () =
        let dir = TestHelpers.createTempDir "compose"

        try
            let path =
                writeCompose dir "compose.yaml"
                    """services:
  web:
    image: nginx:latest
    ports:
      - "n'importe quoi"
"""

            let output = MockOutputPort()
            let client = new RecordingContainerClient()
            use orchestrator = new ComposeOrchestrator(output, client)
            let raised =
                try
                    orchestrator.ParseFile(path) |> ignore
                    false
                with :? Grpc.Core.RpcException as ex ->
                    ex.Status.StatusCode |> should equal Grpc.Core.StatusCode.InvalidArgument
                    true

            raised |> should equal true
        finally
            TestHelpers.cleanupDir dir

    [<Fact>]
    let ``Up cree et demarre les conteneurs du projet`` () =
        let dir = TestHelpers.createTempDir "compose"

        try
            let path =
                writeCompose dir "compose.yaml"
                    """name: app
services:
  web:
    image: nginx:latest
    ports:
      - "8080:80"
  db:
    image: postgres:16
"""

            let output = MockOutputPort()
            let client = new RecordingContainerClient()
            use orchestrator = new ComposeOrchestrator(output, client)
            run (orchestrator.Up(path))

            client.Created
            |> should equal [ "app_web_0", "nginx:latest"; "app_db_0", "postgres:16" ]

            client.Started |> should equal [ "id-app_web_0"; "id-app_db_0" ]

            client.CreatedPorts
            |> List.head
            |> should equal [ (8080, 80, "tcp") ]

            client.CreatedPorts
            |> List.last
            |> should be Empty

            client.CreatedLabels
            |> List.head
            |> fun labels ->
                labels.[composeProjectLabel] |> should equal "app"
                labels.[composeServiceLabel] |> should equal "web"

            output.Successes |> should contain "Projet 'app' démarré (2 conteneur(s))"
        finally
            TestHelpers.cleanupDir dir

    [<Fact>]
    let ``Up signale un demarrage partiel en cas d'echec`` () =
        let dir = TestHelpers.createTempDir "compose"

        try
            let path =
                writeCompose dir "compose.yaml"
                    """name: app
services:
  web:
    image: nginx:latest
  db:
    image: postgres:16
"""

            let output = MockOutputPort()
            let client = new RecordingContainerClient()
            client.FailFromCreate <- 2
            use orchestrator = new ComposeOrchestrator(output, client)
            run (orchestrator.Up(path))

            client.Started |> should haveLength 1
            output.Warnings |> should contain "Projet 'app' partiellement démarré (1 OK, 1 échec(s))"

            output.Errors
            |> List.exists (fun e -> e.Contains("Service 'db' en échec"))
            |> should equal true
        finally
            TestHelpers.cleanupDir dir

    [<Fact>]
    let ``Down arrete et supprime uniquement les conteneurs du projet`` () =
        let dir = TestHelpers.createTempDir "compose"

        try
            let path =
                writeCompose dir "compose.yaml"
                    """name: app
services:
  web:
    image: nginx:latest
"""

            let output = MockOutputPort()
            let client = new RecordingContainerClient()

            client.SetListedContainers(
                [ container ("ctr-web", "app_web_0", "nginx:latest", ContainerState.Running, [ composeProjectLabel, "app"; composeServiceLabel, "web" ])
                  container ("ctr-autre", "autre_0", "alpine:3", ContainerState.Running, [ composeProjectLabel, "autre" ])
                  container ("ctr-sans", "sans_0", "alpine:3", ContainerState.Running, []) ]
            )

            use orchestrator = new ComposeOrchestrator(output, client)
            run (orchestrator.Down(path))

            client.Stopped |> should equal [ "ctr-web" ]
            client.Deleted |> should equal [ "ctr-web" ]
            output.Successes |> should contain "Projet 'app' arrêté (1 conteneur(s))"
        finally
            TestHelpers.cleanupDir dir

    [<Fact>]
    let ``Ps affiche la table des conteneurs du projet`` () =
        let dir = TestHelpers.createTempDir "compose"

        try
            let path =
                writeCompose dir "compose.yaml"
                    """name: app
services:
  web:
    image: nginx:latest
"""

            let output = MockOutputPort()
            let client = new RecordingContainerClient()

            client.SetListedContainers(
                [ container ("ctr-web", "app_web_0", "nginx:latest", ContainerState.Running, [ composeProjectLabel, "app"; composeServiceLabel, "web" ]) ]
            )

            use orchestrator = new ComposeOrchestrator(output, client)
            run (orchestrator.Ps(path))

            output.Tables |> should haveLength 1

            output.Tables.[0]
            |> should equal [| "Service"; "Conteneur"; "Image"; "État"; "ID" |]
        finally
            TestHelpers.cleanupDir dir

    [<Fact>]
    let ``Ps avertit lorsqu'il n'y a aucun conteneur`` () =
        let dir = TestHelpers.createTempDir "compose"

        try
            let path =
                writeCompose dir "compose.yaml"
                    """name: app
services:
  web:
    image: nginx:latest
"""

            let output = MockOutputPort()
            let client = new RecordingContainerClient()
            use orchestrator = new ComposeOrchestrator(output, client)
            run (orchestrator.Ps(path))

            output.Warnings |> should contain "Aucun conteneur pour le projet 'app'"
        finally
            TestHelpers.cleanupDir dir

    [<Fact>]
    let ``Logs affiche les journaux des conteneurs du projet`` () =
        let dir = TestHelpers.createTempDir "compose"

        try
            let path =
                writeCompose dir "compose.yaml"
                    """name: app
services:
  web:
    image: nginx:latest
  db:
    image: postgres:16
"""

            let output = MockOutputPort()
            let client = new RecordingContainerClient()

            client.SetListedContainers(
                [ container ("ctr-web", "app_web_0", "nginx:latest", ContainerState.Running, [ composeProjectLabel, "app"; composeServiceLabel, "web" ])
                  container ("ctr-db", "app_db_0", "postgres:16", ContainerState.Running, [ composeProjectLabel, "app"; composeServiceLabel, "db" ])
                  container ("ctr-autre", "autre_0", "alpine:3", ContainerState.Running, [ composeProjectLabel, "autre" ]) ]
            )

            client.SetLogEntries(
                [ { ContainerLogEntry.Timestamp = "2026-01-01T00:00:00Z"
                    Stream = "stdout"
                    Log = "ligne 1" }
                  { Timestamp = "2026-01-01T00:00:01Z"
                    Stream = "stderr"
                    Log = "erreur" } ]
            )

            use orchestrator = new ComposeOrchestrator(output, client)
            run (orchestrator.Logs(path, None))

            output.Successes |> should contain "--- app_web_0 ---"
            output.Successes |> should contain "--- app_db_0 ---"
            output.Lines |> should contain "[2026-01-01T00:00:00Z] ligne 1"
            output.Lines |> should contain "[2026-01-01T00:00:01Z] erreur"

            output.Successes
            |> List.exists (fun s -> s.Contains "autre_0")
            |> should equal false
        finally
            TestHelpers.cleanupDir dir

    [<Fact>]
    let ``Logs filtre par service`` () =
        let dir = TestHelpers.createTempDir "compose"

        try
            let path =
                writeCompose dir "compose.yaml"
                    """name: app
services:
  web:
    image: nginx:latest
  db:
    image: postgres:16
"""

            let output = MockOutputPort()
            let client = new RecordingContainerClient()

            client.SetListedContainers(
                [ container ("ctr-web", "app_web_0", "nginx:latest", ContainerState.Running, [ composeProjectLabel, "app"; composeServiceLabel, "web" ])
                  container ("ctr-db", "app_db_0", "postgres:16", ContainerState.Running, [ composeProjectLabel, "app"; composeServiceLabel, "db" ]) ]
            )

            use orchestrator = new ComposeOrchestrator(output, client)
            run (orchestrator.Logs(path, Some "web"))

            output.Successes |> should contain "--- app_web_0 ---"

            output.Successes
            |> List.exists (fun s -> s.Contains "app_db_0")
            |> should equal false
        finally
            TestHelpers.cleanupDir dir

    [<Fact>]
    let ``Pull telecharge les images distinctes uniquement`` () =
        let dir = TestHelpers.createTempDir "compose"

        try
            let path =
                writeCompose dir "compose.yaml"
                    """name: app
services:
  web:
    image: nginx:latest
  api:
    image: nginx:latest
  worker:
    build: ./worker
"""

            let output = MockOutputPort()
            let client = new RecordingContainerClient()
            use orchestrator = new ComposeOrchestrator(output, client)
            run (orchestrator.Pull(path))

            client.PulledImages |> should equal [ "nginx:latest" ]
            output.Successes |> should contain "Téléchargement de nginx:latest..."

            output.Lines
            |> should contain "  worker : image locale (build), utilisez 'compose build'"
        finally
            TestHelpers.cleanupDir dir

    [<Fact>]
    let ``Build signale un Dockerfile manquant et tire les images existantes`` () =
        let dir = TestHelpers.createTempDir "compose"
        let missingCtx = Path.Combine(dir, "contexts", "worker")
        let spec =
            sprintf "name: app\nservices:\n  web:\n    image: nginx:latest\n  worker:\n    build: %s\n" (missingCtx.Replace("\\", "/"))

        try
            let path = writeCompose dir "compose.yaml" spec

            let output = MockOutputPort()
            let client = new RecordingContainerClient()
            use orchestrator = new ComposeOrchestrator(output, client)
            run (orchestrator.Build(path))

            client.PulledImages |> should equal [ "nginx:latest" ]
            output.Successes |> should contain "  ✓ Image 'nginx:latest' disponible"

            output.Errors
            |> List.exists (fun e -> e.Contains "Dockerfile introuvable" && e.Contains "worker")
            |> should equal true
        finally
            TestHelpers.cleanupDir dir

    [<Fact>]
    let ``ParseFile derive le nom de projet du nom du fichier si absent`` () =
        let dir = TestHelpers.createTempDir "compose"

        try
            let path =
                writeCompose dir "mon projet.yaml"
                    """services:
  web:
    image: nginx:latest
"""

            let output = MockOutputPort()
            let client = new RecordingContainerClient()
            use orchestrator = new ComposeOrchestrator(output, client)
            let compose = orchestrator.ParseFile(path)
            compose.ProjectName |> should equal "mon-projet"
            compose.Version |> should equal "3.8"
        finally
            TestHelpers.cleanupDir dir

    [<Fact>]
    let ``ParseFile utilise build comme image de repli`` () =
        let dir = TestHelpers.createTempDir "compose"

        try
            let path =
                writeCompose dir "compose.yaml"
                    """name: app
services:
  web:
    build: ./web
"""

            let output = MockOutputPort()
            let client = new RecordingContainerClient()
            use orchestrator = new ComposeOrchestrator(output, client)
            let compose = orchestrator.ParseFile(path)
            let svc = compose.Services.Head
            svc.Build |> should equal (Some "./web")
            svc.Image |> should equal "app_web"
        finally
            TestHelpers.cleanupDir dir

    [<Fact>]
    let ``ParseFile analyse l'environnement au format mapping`` () =
        let dir = TestHelpers.createTempDir "compose"

        try
            let path =
                writeCompose dir "compose.yaml"
                    """name: app
services:
  web:
    image: nginx:latest
    environment:
      MODE: prod
      DEBUG: "1"
"""

            let output = MockOutputPort()
            let client = new RecordingContainerClient()
            use orchestrator = new ComposeOrchestrator(output, client)
            let compose = orchestrator.ParseFile(path)
            let env = compose.Services.Head.Environment
            env |> should haveLength 2
            env |> List.exists (fun e -> e.Key = "MODE" && e.Value = "prod") |> should equal true
            env |> List.exists (fun e -> e.Key = "DEBUG" && e.Value = "1") |> should equal true
        finally
            TestHelpers.cleanupDir dir

    [<Fact>]
    let ``ParseFile analyse command (sequence et scalaire) et entrypoint`` () =
        let dir = TestHelpers.createTempDir "compose"

        try
            let path =
                writeCompose dir "compose.yaml"
                    """name: app
services:
  web:
    image: nginx:latest
    command:
      - echo
      - bonjour
    entrypoint:
      - sh
      - -c
  db:
    image: postgres:16
    command: echo demarrage
"""

            let output = MockOutputPort()
            let client = new RecordingContainerClient()
            use orchestrator = new ComposeOrchestrator(output, client)
            let compose = orchestrator.ParseFile(path)

            let web = compose.Services.[0]
            web.Command |> should equal (Some [ "echo"; "bonjour" ])
            web.Args |> should equal (Some [ "sh"; "-c" ])

            let db = compose.Services.[1]
            db.Command |> should equal (Some [ "echo"; "demarrage" ])
        finally
            TestHelpers.cleanupDir dir

    [<Fact>]
    let ``ParseFile leve InvalidArgument pour un nom de service invalide`` () =
        let dir = TestHelpers.createTempDir "compose"

        try
            let path =
                writeCompose dir "compose.yaml"
                    """name: app
services:
  "mauvais nom":
    image: nginx:latest
"""

            let output = MockOutputPort()
            let client = new RecordingContainerClient()
            use orchestrator = new ComposeOrchestrator(output, client)
            let raised =
                try
                    orchestrator.ParseFile(path) |> ignore
                    false
                with :? Grpc.Core.RpcException as ex ->
                    ex.Status.StatusCode |> should equal Grpc.Core.StatusCode.InvalidArgument
                    true

            raised |> should equal true
        finally
            TestHelpers.cleanupDir dir

    [<Fact>]
    let ``ParseFile leve InvalidArgument pour une image invalide`` () =
        let dir = TestHelpers.createTempDir "compose"

        try
            let path =
                writeCompose dir "compose.yaml"
                    """name: app
services:
  web:
    image: "mauvaise image"
"""

            let output = MockOutputPort()
            let client = new RecordingContainerClient()
            use orchestrator = new ComposeOrchestrator(output, client)
            let raised =
                try
                    orchestrator.ParseFile(path) |> ignore
                    false
                with :? Grpc.Core.RpcException as ex ->
                    ex.Status.StatusCode |> should equal Grpc.Core.StatusCode.InvalidArgument
                    true

            raised |> should equal true
        finally
            TestHelpers.cleanupDir dir

    [<Fact>]
    let ``ParseFile leve InvalidArgument pour une commande invalide`` () =
        let dir = TestHelpers.createTempDir "compose"

        try
            let path =
                writeCompose dir "compose.yaml"
                    """name: app
services:
  web:
    image: nginx:latest
    command:
      - ""
"""

            let output = MockOutputPort()
            let client = new RecordingContainerClient()
            use orchestrator = new ComposeOrchestrator(output, client)
            let raised =
                try
                    orchestrator.ParseFile(path) |> ignore
                    false
                with :? Grpc.Core.RpcException as ex ->
                    ex.Status.StatusCode |> should equal Grpc.Core.StatusCode.InvalidArgument
                    true

            raised |> should equal true
        finally
            TestHelpers.cleanupDir dir

    [<Fact>]
    let ``ParseFile leve InvalidArgument quand la racine n'est pas un mapping`` () =
        let dir = TestHelpers.createTempDir "compose"

        try
            let path = writeCompose dir "compose.yaml" "- element1\n- element2\n"
            let output = MockOutputPort()
            let client = new RecordingContainerClient()
            use orchestrator = new ComposeOrchestrator(output, client)
            let raised =
                try
                    orchestrator.ParseFile(path) |> ignore
                    false
                with :? Grpc.Core.RpcException as ex ->
                    ex.Status.StatusCode |> should equal Grpc.Core.StatusCode.InvalidArgument
                    true

            raised |> should equal true
        finally
            TestHelpers.cleanupDir dir

    [<Fact>]
    let ``Up transmet environnement, commande, arguments et volumes`` () =
        let dir = TestHelpers.createTempDir "compose"

        try
            let path =
                writeCompose dir "compose.yaml"
                    """name: app
services:
  web:
    image: nginx:latest
    environment:
      MODE: prod
    command:
      - echo
      - bonjour
    entrypoint:
      - sh
    volumes:
      - "./data:/app/data"
      - "./cfg:/etc/cfg:ro"
"""

            let output = MockOutputPort()
            let client = new RecordingContainerClient()
            use orchestrator = new ComposeOrchestrator(output, client)
            run (orchestrator.Up(path))

            client.CreatedEnv |> List.head |> fun env -> env.["MODE"] |> should equal "prod"
            client.CreatedCommand |> List.head |> should equal (Some [ "echo"; "bonjour" ])
            client.CreatedArgs |> List.head |> should equal (Some [ "sh" ])

            client.CreatedMounts
            |> List.head
            |> should equal (Some [ ("./data", "/app/data", false); ("./cfg", "/etc/cfg", true) ])
        finally
            TestHelpers.cleanupDir dir

    [<Fact>]
    let ``Down signale un arret partiel en cas d'echec`` () =
        let dir = TestHelpers.createTempDir "compose"

        try
            let path =
                writeCompose dir "compose.yaml"
                    """name: app
services:
  web:
    image: nginx:latest
"""

            let output = MockOutputPort()
            let client = new RecordingContainerClient()

            client.SetListedContainers(
                [ container ("ctr-web", "app_web_0", "nginx:latest", ContainerState.Running, [ composeProjectLabel, "app"; composeServiceLabel, "web" ])
                  container ("ctr-db", "app_db_0", "postgres:16", ContainerState.Running, [ composeProjectLabel, "app"; composeServiceLabel, "db" ]) ]
            )

            client.FailOnStopIds.Add("ctr-db")
            use orchestrator = new ComposeOrchestrator(output, client)
            run (orchestrator.Down(path))

            client.Stopped |> should equal [ "ctr-web" ]
            client.Deleted |> should equal [ "ctr-web" ]
            output.Warnings |> should contain "Projet 'app' partiellement arrêté (1 OK, 1 échec(s))"

            output.Errors
            |> List.exists (fun e -> e.Contains "app_db_0" && e.Contains "échec de l'arrêt")
            |> should equal true
        finally
            TestHelpers.cleanupDir dir
