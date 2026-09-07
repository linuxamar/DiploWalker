namespace Diplo.Core.Compose

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Tasks
open Grpc.Core
open Diplo.Core.Clients
open Diplo.Core.Output
open Diplo.Abstractions
open YamlDotNet.RepresentationModel

type ComposeOrchestrator(output: IOutputPort, ?containerClient: IContainerClient) =

    let containerClient =
        containerClient |> Option.defaultWith (fun () -> new ContainerClient() :> IContainerClient)

    let tryGetChild (node: YamlMappingNode) (key: string) : YamlNode option =
        match node.Children.TryGetValue(YamlScalarNode(key)) with
        | true, v -> Some v
        | false, _ -> None

    let scalarValue (node: YamlNode) : string option =
        match node with
        | :? YamlScalarNode as s -> Some s.Value
        | _ -> None

    interface IDisposable with
        member _.Dispose() =
            (containerClient :> IDisposable).Dispose()

    member _.ParseFile(filePath: string) : ComposeFile =
        SecurityValidation.validateFilePath filePath "Le fichier compose"

        if not (File.Exists filePath) then
            raise (
                RpcException(Status(StatusCode.NotFound, sprintf "Le fichier compose '%s' est introuvable" filePath))
            )

        let yaml = File.ReadAllText(filePath)
        use stream = new StringReader(yaml)
        let doc = YamlStream()
        doc.Load(stream)

        // Document vide ou uniquement des commentaires : rejet explicite plutôt
        // qu'un ArgumentOutOfRangeException brut.
        if doc.Documents.Count = 0 then
            raise (
                RpcException(
                    Status(StatusCode.InvalidArgument, sprintf "Le fichier compose '%s' est vide" filePath)
                )
            )

        let root =
            match doc.Documents.[0].RootNode with
            | :? YamlMappingNode as m -> m
            | _ ->
                raise (
                    RpcException(
                        Status(StatusCode.InvalidArgument, "La racine du fichier compose doit être un mapping YAML")
                    )
                )

        let version =
            match tryGetChild root "version" |> Option.bind scalarValue with
            | Some v -> v
            | _ -> "3.8"

        let projectName =
            match tryGetChild root "name" |> Option.bind scalarValue with
            | Some n -> n
            | _ -> Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant().Replace(" ", "-")

        let services =
            match tryGetChild root "services" with
            | Some(:? YamlMappingNode as sm) ->
                sm.Children
                |> Seq.toList
                |> List.map (fun kv ->
                    let name = kv.Key.ToString()

                    let svc =
                        match kv.Value with
                        | :? YamlMappingNode as m -> m
                        | _ ->
                            raise (
                                RpcException(
                                    Status(
                                        StatusCode.InvalidArgument,
                                        sprintf "Le service '%s' doit être un mapping YAML" name
                                    )
                                )
                            )

                    let buildCtx = tryGetChild svc "build" |> Option.bind scalarValue

                    let image =
                        match tryGetChild svc "image" |> Option.bind scalarValue with
                        | Some img -> img
                        | _ ->
                            match buildCtx with
                            | Some ctx -> sprintf "%s_%s" projectName name
                            | _ ->
                                raise (
                                    RpcException(
                                        Status(
                                            StatusCode.InvalidArgument,
                                            sprintf "Le service '%s' doit avoir une image ou un build" name
                                        )
                                    )
                                )

                    let command =
                        match tryGetChild svc "command" with
                        | Some(:? YamlSequenceNode as seq) ->
                            seq.Children |> Seq.map (fun c -> c.ToString()) |> Seq.toList |> Some
                        | Some(:? YamlScalarNode as s) -> s.Value.Split(' ') |> Array.toList |> Some
                        | _ -> None

                    let args =
                        match tryGetChild svc "entrypoint" with
                        | Some(:? YamlSequenceNode as seq) ->
                            seq.Children |> Seq.map (fun c -> c.ToString()) |> Seq.toList |> Some
                        | Some(:? YamlScalarNode as s) -> s.Value.Split(' ') |> Array.toList |> Some
                        | _ -> None

                    let environment =
                        match tryGetChild svc "environment" with
                        | Some(:? YamlSequenceNode as seq) ->
                            seq.Children
                            |> Seq.map (fun e -> e.ToString())
                            |> Seq.toList
                            |> parseEnvironment
                        | Some(:? YamlMappingNode as m) ->
                            m.Children
                            |> Seq.map (fun kv -> sprintf "%s=%s" (kv.Key.ToString()) (kv.Value.ToString()))
                            |> Seq.toList
                            |> parseEnvironment
                        | _ -> []

                    let ports =
                        match tryGetChild svc "ports" with
                        | Some(:? YamlSequenceNode as seq) ->
                            let specs =
                                seq.Children |> Seq.map (fun p -> p.ToString()) |> Seq.toList

                            let parsed, rejected = parsePorts specs

                            if not rejected.IsEmpty then
                                raise (
                                    RpcException(
                                        Status(
                                            StatusCode.InvalidArgument,
                                            sprintf
                                                "Le service '%s' contient des entrées 'ports' invalides : %s"
                                                name
                                                (String.Join(", ", rejected))
                                        )
                                    )
                                )

                            parsed
                        | _ -> []

                    let volumes =
                        match tryGetChild svc "volumes" with
                        | Some(:? YamlSequenceNode as seq) ->
                            seq.Children |> Seq.map (fun v -> v.ToString()) |> Seq.toList |> parseVolumes
                        | _ -> []

                    let labels =
                        match tryGetChild svc "labels" with
                        | Some(:? YamlSequenceNode as seq) ->
                            seq.Children |> Seq.map (fun l -> l.ToString()) |> Seq.toList |> parseLabels
                        | Some(:? YamlMappingNode as m) ->
                            m.Children
                            |> Seq.map (fun kv -> sprintf "%s=%s" (kv.Key.ToString()) (kv.Value.ToString()))
                            |> Seq.toList
                            |> parseLabels
                        | _ -> dict [] :> IDictionary<string, string>

                    let restart =
                        match tryGetChild svc "restart" |> Option.bind scalarValue with
                        | Some r -> Some r
                        | _ -> None

                    SecurityValidation.validateName name "Nom de service"
                    SecurityValidation.validateImage image

                    if command.IsSome then
                        command.Value |> List.toArray |> SecurityValidation.validateCommand

                    for kv in labels do
                        SecurityValidation.validateLabel kv.Key kv.Value

                    { Name = name
                      Image = image
                      Build = buildCtx
                      Command = command
                      Args = args
                      Environment = environment
                      Ports = ports
                      Volumes = volumes
                      Labels = labels
                      RestartPolicy = restart
                      CpuShares = None
                      MemoryLimit = None
                      PidLimit = None })
            | _ -> []

        { Version = version
          ProjectName = projectName
          Services = services }

    member this.Up(filePath: string, ?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let compose = this.ParseFile(filePath)

            output.WriteSuccess(
                sprintf "Démarrage du projet '%s' (%d service(s))" compose.ProjectName compose.Services.Length
            )

            let mutable created = 0
            let mutable failures = 0

            for svc in compose.Services do
                try
                    let containerName = buildContainerName compose.ProjectName svc.Name 0

                    // Labels Diplo en DERNIER : ils priment sur les labels
                    // utilisateur et ne peuvent pas être détournés par le compose.
                    let allLabels = ResizeArray<string * string>()

                    for kv in svc.Labels do
                        allLabels.Add(kv.Key, kv.Value)

                    for kv in buildServiceLabels compose.ProjectName svc.Name do
                        allLabels.Add(kv.Key, kv.Value)

                    let labels = dict allLabels

                    let env =
                        svc.Environment |> List.map (fun e -> e.Key, e.Value) |> dict

                    // Transmettre les volumes ET les ports déclarés : les ignorer
                    // produirait un déploiement non conforme sans aucun avertissement.
                    let mounts =
                        match svc.Volumes with
                        | [] -> None
                        | vols ->
                            vols
                            |> List.map (fun v -> v.Source, v.Target, v.ReadOnly)
                            |> Some

                    let portMappings =
                        match svc.Ports with
                        | [] -> None
                        | ps ->
                            ps
                            |> List.map (fun p ->
                                (defaultArg p.HostPort p.ContainerPort, p.ContainerPort, p.Protocol))
                            |> Some

                    let! response =
                        containerClient.CreateAsync(
                            name = containerName,
                            image = svc.Image,
                            ?env = (if env.Count > 0 then Some env else None),
                            ?command = svc.Command,
                            ?args = svc.Args,
                            labels = labels,
                            ?pidLimit = svc.PidLimit,
                            ?memoryLimit = svc.MemoryLimit,
                            ?cpuShares = svc.CpuShares,
                            ?mounts = mounts,
                            ?ports = portMappings,
                            ct = ct
                        )

                    output.WriteSuccess(
                        sprintf "  Conteneur %s créé (%s)" response.Name (response.State.ToString())
                    )

                    let! _ = containerClient.StartAsync(response.Id, ct = ct)

                    output.WriteSuccess(sprintf "  Conteneur %s démarré" response.Name)
                    created <- created + 1
                with ex ->
                    // Tolérance aux échecs partiels : continuer les autres services
                    // puis restituer un bilan honnête.
                    failures <- failures + 1
                    output.WriteError(sprintf "  Service '%s' en échec : %s" svc.Name ex.Message)

            if failures > 0 then
                output.WriteWarning(
                    sprintf "Projet '%s' partiellement démarré (%d OK, %d échec(s))" compose.ProjectName created failures
                )
            else
                output.WriteSuccess(sprintf "Projet '%s' démarré (%d conteneur(s))" compose.ProjectName created)
        }

    member this.Down(filePath: string, ?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let compose = this.ParseFile(filePath)
            output.WriteSuccess(sprintf "Arrêt du projet '%s'" compose.ProjectName)

            let! containers = containerClient.ListAsync(all = true, ct = ct)
            let mutable stopped = 0
            let mutable failures = 0

            for c in containers.Containers do
                let hasProjectLabel =
                    c.Labels
                    |> Seq.exists (fun kv -> kv.Key = composeProjectLabel && kv.Value = compose.ProjectName)

                if hasProjectLabel then
                    try
                        let! _ = containerClient.StopAsync(c.Id, 10, ct = ct)
                        let! _ = containerClient.DeleteAsync(c.Id, true, ct = ct)
                        stopped <- stopped + 1

                        output.WriteSuccess(sprintf "  Conteneur %s arrêté et supprimé" c.Name)
                    with ex ->
                        // Un échec sur un conteneur n'interrompt pas le nettoyage
                        // des autres conteneurs du projet.
                        failures <- failures + 1
                        output.WriteError(sprintf "  Conteneur %s : échec de l'arrêt (%s)" c.Name ex.Message)

            if failures > 0 then
                output.WriteWarning(
                    sprintf
                        "Projet '%s' partiellement arrêté (%d OK, %d échec(s))"
                        compose.ProjectName
                        stopped
                        failures
                )
            else
                output.WriteSuccess(sprintf "Projet '%s' arrêté (%d conteneur(s))" compose.ProjectName stopped)
        }

    member this.Ps(filePath: string, ?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let compose = this.ParseFile(filePath)
            let! containers = containerClient.ListAsync(all = true, ct = ct)

            let projectContainers =
                containers.Containers
                |> Seq.filter (fun c ->
                    c.Labels
                    |> Seq.exists (fun kv -> kv.Key = composeProjectLabel && kv.Value = compose.ProjectName))
                |> Seq.toList

            if projectContainers.IsEmpty then
                output.WriteWarning(sprintf "Aucun conteneur pour le projet '%s'" compose.ProjectName)
            else
                output.WriteTable(
                    projectContainers,
                    [| "Service"; "Conteneur"; "Image"; "État"; "ID" |],
                    fun c ->
                        let service =
                            c.Labels
                            |> Seq.tryFind (fun kv -> kv.Key = composeServiceLabel)
                            |> Option.map (fun kv -> kv.Value)
                            |> Option.defaultValue "-"

                        [| service; c.Name; c.Image; c.State.ToString(); c.Id |]
                )
        }

    member this.Logs(filePath: string, serviceName: string option, ?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let compose = this.ParseFile(filePath)
            let! containers = containerClient.ListAsync(all = true, ct = ct)

            for c in containers.Containers do
                let hasProjectLabel =
                    c.Labels
                    |> Seq.exists (fun kv -> kv.Key = composeProjectLabel && kv.Value = compose.ProjectName)

                let matchesService =
                    match serviceName with
                    | None -> true
                    | Some sn -> c.Labels |> Seq.exists (fun kv -> kv.Key = composeServiceLabel && kv.Value = sn)

                if hasProjectLabel && matchesService then
                    let! entries = containerClient.GetLogs(c.Id, follow = false, tail = 100, ct = ct)
                    output.WriteSuccess(sprintf "--- %s ---" c.Name)

                    for entry in entries do
                        output.WriteLine(sprintf "[%s] %s" entry.Timestamp entry.Log)
        }

    member this.Pull(filePath: string, ?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let compose = this.ParseFile(filePath)

            // Les services en build n'ont pas d'image dans un registre : leur
            // nom est synthétique et le pull échouerait avec une erreur obscure.
            let images =
                compose.Services
                |> List.filter (fun s -> s.Build.IsNone)
                |> List.map (fun s -> s.Image)
                |> List.distinct

            for svc in compose.Services do
                if svc.Build.IsSome then
                    output.WriteLine(
                        sprintf "  %s : image locale (build), utilisez 'compose build'" svc.Name
                    )

            for img in images do
                output.WriteSuccess(sprintf "Téléchargement de %s..." img)
                let! response = containerClient.PullImageAsync(img, ct = ct)
                output.WriteSuccess(sprintf "  %s" response.Message)
        }

    member this.Build(filePath: string) =
        task {
            let compose = this.ParseFile(filePath)

            output.WriteSuccess(
                sprintf
                    "Construction des images pour le projet '%s' (%d service(s))"
                    compose.ProjectName
                    compose.Services.Length
            )

            for svc in compose.Services do
                match svc.Build with
                | Some buildCtx ->
                    let resolvedPath =
                        if Path.IsPathRooted buildCtx then
                            buildCtx
                        else
                            let composeDir = Path.GetDirectoryName(Path.GetFullPath(filePath))
                            Path.GetFullPath(Path.Combine(composeDir, buildCtx))

                    let dockerfile = Path.Combine(resolvedPath, "Dockerfile")

                    if not (File.Exists dockerfile) then
                        output.WriteError(
                            sprintf "  Dockerfile introuvable dans '%s' pour le service '%s'" resolvedPath svc.Name
                        )
                    else
                        output.WriteSuccess(sprintf "  Construction de %s → %s..." svc.Name svc.Image)

                        let psi =
                            ProcessStartInfo(
                                FileName = "docker",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            )

                        psi.ArgumentList.Add("build")
                        psi.ArgumentList.Add("-t")
                        psi.ArgumentList.Add(svc.Image)
                        psi.ArgumentList.Add(resolvedPath)
                        psi.EnvironmentVariables.["DOCKER_BUILDKIT"] <- "1"
                        use proc = new Process()
                        proc.StartInfo <- psi

                        proc.OutputDataReceived.Add(fun args ->
                            if not (isNull args.Data) then
                                output.WriteLine("  " + args.Data))

                        proc.ErrorDataReceived.Add(fun args ->
                            if not (isNull args.Data) then
                                output.WriteError("  " + args.Data))

                        proc.Start() |> ignore
                        proc.BeginOutputReadLine()
                        proc.BeginErrorReadLine()
                        do! proc.WaitForExitAsync()

                        if proc.ExitCode = 0 then
                            output.WriteSuccess(sprintf "  ✓ Image '%s' construite" svc.Image)
                        else
                            output.WriteError(
                                sprintf "  ✗ Échec de la construction de '%s' (code %d)" svc.Image proc.ExitCode
                            )
                | None ->
                    output.WriteLine(
                        sprintf "  %s utilise une image existante (%s), téléchargement..." svc.Name svc.Image
                    )

                    let! _ = containerClient.PullImageAsync(svc.Image)
                    output.WriteSuccess(sprintf "  ✓ Image '%s' disponible" svc.Image)
        }
