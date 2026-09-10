namespace Diplo.Container.Services

open System
open System.ServiceModel
open System.Collections.Generic
open System.Collections.Concurrent
open System.IO
open System.Linq
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Grpc.Core
open Serilog
open Diplo.Grpc
open Diplo.Grpc.Container
open Diplo.Abstractions.Interfaces
open Diplo.Abstractions
open Diplo.Disk
open Diplo.Container

/// Aides au streaming gRPC basées sur System.Threading.Channels.
module private ContainerStreaming =

    /// Flux basé sur un canal de SEGMENTS (byte[]) : connecte un flux entrant
    /// gRPC et les flux sortants. Le découpage par segments évite le coût d'un
    /// WriteAsync par octet, et la lecture rend les données dès qu'un segment
    /// est disponible (une sortie interactive courte n'attend plus 8 Ko).
    type ChannelStream() =
        inherit Stream()

        let channel =
            Channel.CreateBounded<byte[]>(BoundedChannelOptions(32, SingleReader = false, SingleWriter = false))

        let reader = channel.Reader
        let writer = channel.Writer

        // Segment en cours de consommation (lecture partielle entre deux appels).
        let mutable pending: (byte[] * int) option = None

        /// Lit au plus `count` octets : consomme d'abord le segment partiel
        /// restant, puis attend un nouveau segment ; retourne dès que `count`
        /// octets sont réunis OU que tous les segments disponibles sont épuisés.
        let readInto (buffer: byte[]) (offset: int) (count: int) : int =
            let mutable total = 0
            let mutable cont = true

            while cont do
                match pending with
                | Some(chunk, pos) ->
                    let available = chunk.Length - pos
                    let take = min available (count - total)
                    Array.blit chunk pos buffer (offset + total) take
                    total <- total + take

                    if pos + take >= chunk.Length then
                        pending <- None
                    else
                        pending <- Some(chunk, pos + take)

                    if total >= count then cont <- false
                | None ->
                    if total > 0 then
                        // Des octets déjà lus : ne pas bloquer sur un nouveau
                        // segment, livrer ce qui est disponible maintenant.
                        cont <- false
                    else
                        let has = reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult()

                        if has then
                            let mutable chunk: byte[] = null

                            if reader.TryRead(&chunk) && not (isNull chunk) && chunk.Length > 0 then
                                pending <- Some(chunk, 0)
                        else
                            cont <- false

            total

        interface IDisposable with
            member _.Dispose() = writer.TryComplete() |> ignore

        member _.Complete() = writer.TryComplete() |> ignore

        override _.CanRead = true
        override _.CanWrite = true
        override _.CanSeek = false

        override _.Flush() = ()
        override _.Length = raise (NotSupportedException())
        override _.SetLength(_) = raise (NotSupportedException())

        override _.Position
            with get () = raise (NotSupportedException())
            and set _ = raise (NotSupportedException())

        override _.Seek(_, _) = raise (NotSupportedException())

        override _.Read(buffer: byte[], offset: int, count: int) : int = readInto buffer offset count

        override _.Write(buffer: byte[], offset: int, count: int) =
            if count > 0 then
                let chunk = Array.sub buffer offset count
                writer.WriteAsync(chunk).AsTask().GetAwaiter().GetResult() |> ignore

        override _.ReadAsync(buffer, offset, count, ct) =
            task {
                let mutable total = 0
                let mutable cont = true

                while cont do
                    match pending with
                    | Some(chunk, pos) ->
                        let available = chunk.Length - pos
                        let take = min available (count - total)
                        Array.blit chunk pos buffer (offset + total) take
                        total <- total + take

                        if pos + take >= chunk.Length then
                            pending <- None
                        else
                            pending <- Some(chunk, pos + take)

                        if total >= count then cont <- false
                    | None ->
                        if total > 0 then
                            cont <- false
                        else
                            let! has = reader.WaitToReadAsync(ct).AsTask()

                            if has then
                                let mutable chunk: byte[] = null

                                if reader.TryRead(&chunk) && not (isNull chunk) && chunk.Length > 0 then
                                    pending <- Some(chunk, 0)
                            else
                                cont <- false

                return total
            }

        override _.WriteAsync(buffer, offset, count, ct) =
            task {
                if count > 0 then
                    let chunk = Array.sub buffer offset count
                    do! writer.WriteAsync(chunk, ct).AsTask()
            }

    /// Convertit un lecteur de canal en IAsyncEnumerable (streaming gRPC).
    let toAsyncEnumerable (reader: ChannelReader<'T>) : IAsyncEnumerable<'T> =
        { new IAsyncEnumerable<'T> with
            member _.GetAsyncEnumerator(_ct) =
                let mutable current = Unchecked.defaultof<'T>

                { new IAsyncEnumerator<'T> with
                    member _.Current = current

                    member this.MoveNextAsync() =
                        let t =
                            task {
                                try
                                    let! item = reader.ReadAsync(_ct).AsTask()
                                    current <- item
                                    return true
                                with
                                | :? ChannelClosedException -> return false
                                | :? OperationCanceledException -> return false
                            }

                        ValueTask<bool>(t)

                    member _.DisposeAsync() = ValueTask() } }

    /// Lance un producer task et observe ses exceptions pour éviter
    /// UnobservedTaskException quand le GC finalise un Task faulté.
    let observeProducerTask (name: string) (task: Task) =
        task.ContinueWith(
            (fun (t: Task) ->
                if t.IsFaulted then
                    Log.Warning(t.Exception, "Producer task terminé en erreur: {TaskName}", name)),
            TaskContinuationOptions.OnlyOnFaulted
        )
        |> ignore

open ContainerStreaming

[<ServiceContract(Name = "IContainerService")>]
type ContainerServiceImpl(client: IContainerdClient, mounter: IDiskMounter, ?registrySearchClient: HttpClient) =

    [<Literal>]
    static let DefaultNamespace = "default"

    /// Volumes montés par conteneur : retenus jusqu'à la suppression du
    /// conteneur, où les modifications sont réécrites dans l'image source.
    let mountedVolumes = ConcurrentDictionary<string, MountedVolume list>()

    let persistMounts () =
        mountedVolumes
        |> Seq.map (fun kv ->
            kv.Key,
            kv.Value
            |> List.map (fun v ->
                { MountState.Source = v.Source
                  MountState.HostPath = v.HostPath
                  MountState.Destination = v.Destination
                  MountState.ReadOnly = v.ReadOnly }))
        |> MountState.save (MountState.stateFile ())

    let tryPersistMounts () =
        try
            persistMounts ()
        with ex ->
            Log.Warning(ex, "Erreur lors de la persistance de l'état des volumes montés")

    do
        // Restauration des volumes montés persistés après un redémarrage du
        // service : le write-back redevient actif sans ré-extraire l'image.
        try
            MountState.load (MountState.stateFile ())
            |> Map.iter (fun id entries ->
                let volumes =
                    entries
                    |> List.choose (fun e ->
                        try
                            Some(DiskMounter.rehydrate e.Source e.HostPath e.Destination e.ReadOnly)
                        with ex ->
                            Log.Warning(ex, "Erreur lors de la réhydratation du volume {HostPath}", e.HostPath)
                            None)

                if not (List.isEmpty volumes) then
                    mountedVolumes[id] <- volumes)
        with ex ->
            Log.Warning(ex, "Erreur lors de la restauration des volumes montés au démarrage")

    let tryGetString (el: JsonElement) (prop: string) = JsonHelpers.tryGetString el prop
    let tryGetInt64 (el: JsonElement) (prop: string) = JsonHelpers.tryGetInt64 el prop
    let tryGetDouble (el: JsonElement) (prop: string) = JsonHelpers.tryGetDouble el prop

    let stateString (s: ContainerState) =
        match s with
        | ContainerState.Running -> "running"
        | ContainerState.Created -> "created"
        | ContainerState.Paused -> "paused"
        | ContainerState.Stopped -> "stopped"
        | ContainerState.Failed -> "dead"
        | _ -> "unknown"

    let buildStats (json: JsonElement) =
        let cpuUsage =
            try
                let mutable cpu = Unchecked.defaultof<JsonElement>

                if json.TryGetProperty("cpu", &cpu) then
                    tryGetDouble cpu "usage"
                else
                    0.0
            with ex ->
                Log.Warning(ex, "Erreur lors du parsing CPU")
                0.0

        let memoryUsage, memoryLimit =
            try
                let mutable mem = Unchecked.defaultof<JsonElement>

                if json.TryGetProperty("memory", &mem) then
                    tryGetInt64 mem "usage", tryGetInt64 mem "limit"
                else
                    0L, 0L
            with ex ->
                Log.Warning(ex, "Erreur lors du parsing mémoire")
                0L, 0L

        let pids =
            try
                let mutable p = Unchecked.defaultof<JsonElement>

                if json.TryGetProperty("pids", &p) then
                    tryGetInt64 p "current" |> int
                else
                    0
            with ex ->
                Log.Warning(ex, "Erreur lors du parsing PIDs")
                0

        { GetContainerStatsResponse.CpuUsage = cpuUsage
          MemoryUsage = memoryUsage
          MemoryLimit = memoryLimit
          NetworkRx = 0L
          NetworkTx = 0L
          DiskRead = 0L
          DiskWrite = 0L
          Pids = pids }

    let mapState (status: string) =
        match status.ToLowerInvariant() with
        | "running" -> ContainerState.Running
        | "created" -> ContainerState.Created
        | "paused"
        | "pausing" -> ContainerState.Paused
        | "stopped"
        | "deleted" -> ContainerState.Stopped
        | "dead" -> ContainerState.Failed
        | _ -> ContainerState.Unknown

    let safeEnvVars =
        set
            [ "PATH"
              "USERNAME"
              "USERDOMAIN"
              "TEMP"
              "TMP"
              "HOMEDRIVE"
              "HOMEPATH"
              "SYSTEMROOT"
              "OS"
              "PROCESSOR_ARCHITECTURE"
              "NUMBER_OF_PROCESSORS"
              "ASPNETCORE_ENVIRONMENT"
              "DOTNET_ENVIRONMENT"
              "DOTNET_CLI_TELEMETRY_OPTOUT" ]

    interface IContainerService with

        member _.CreateContainer(request, _context) =
            task {
                ServiceGuards.requireNonEmpty request.Image "L'image du conteneur"

                let name =
                    if String.IsNullOrEmpty(request.Name) then
                        Guid.NewGuid().ToString("N")
                    else
                        request.Name

                SecurityValidation.validateName name "Le nom du conteneur"
                SecurityValidation.validateImage request.Image
                let labels = request.Labels |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq

                for kv in labels do
                    SecurityValidation.validateLabel kv.Key kv.Value

                let env = request.Env |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
                let command = request.Command |> Seq.toArray
                let args = request.Args |> Seq.toArray

                let requestedMounts =
                    request.Mounts
                    |> Seq.map (fun m ->
                        SecurityValidation.validateVolumePath m.Source "La source du volume"
                        ServiceGuards.requireNonEmpty m.Destination "La destination du montage"
                        m.Source, m.Destination, m.ReadOnly)
                    |> Seq.toList

                let mounted = ResizeArray<MountedVolume>()

                let id =
                    try
                        for (source, destination, readOnly) in requestedMounts do
                            mounted.Add(mounter.Mount(source, destination, readOnly))

                        let resolvedMounts =
                            mounted
                            |> Seq.map (fun v -> v.HostPath, v.Destination, v.ReadOnly)
                            |> Seq.toList

                        let id =
                            client.CreateContainer(
                                DefaultNamespace,
                                name,
                                request.Image,
                                labels,
                                env,
                                command,
                                args,
                                request.MemoryLimit,
                                int64 request.CpuShares,
                                uint32 request.PidLimit,
                                resolvedMounts
                            )

                        mountedVolumes[id] <- mounted |> Seq.toList
                        tryPersistMounts ()
                        id
                    with ex ->
                        for v in mounted do
                            try
                                v.Dispose()
                            with _ ->
                                ()

                        mounted.Clear()
                        reraise ()

                return
                    { CreateContainerResponse.Id = id

                      // Renvoyer le nom RÉSOLU : un nom auto-généré (vide dans
                      // la requête) doit être connu du client.
                      Name = name
                      State = ContainerState.Created
                      CreatedAt = DateTime.UtcNow.ToString("o") }
            }

        member _.StartContainer(request, _context) =
            task {
                ServiceGuards.requireContainerId request.Id

                if request.Attach then
                    client.StartContainer(DefaultNamespace, request.Id, false)
                else
                    client.StartContainerWithLogs(DefaultNamespace, request.Id, ContainerLogs.fileFor request.Id)

                return
                    { StartContainerResponse.State = ContainerState.Running
                      Message = "Conteneur démarré" }
            }

        member _.StopContainer(request, _context) =
            task {
                ServiceGuards.requireContainerId request.Id

                let timeout =
                    if request.TimeoutSeconds > 0 then
                        request.TimeoutSeconds
                    else
                        10

                do! client.StopContainer(DefaultNamespace, request.Id, timeout)

                return
                    { StopContainerResponse.State = ContainerState.Stopped
                      Message = "Conteneur arrêté" }
            }

        member _.DeleteContainer(request, _context) =
            task {
                ServiceGuards.requireContainerId request.Id
                client.DeleteContainer(DefaultNamespace, request.Id, request.Force)

                match mountedVolumes.TryRemove(request.Id) with
                | true, volumes ->
                    for v in volumes do
                        try
                            v.Dispose()
                        with ex ->
                            Log.Warning(
                                ex,
                                "Erreur lors de la libération du volume du conteneur {ContainerId}",
                                request.Id
                            )
                | false, _ -> ()

                tryPersistMounts ()

                return
                    { DeleteContainerResponse.Success = true
                      Message = "Conteneur supprimé" }
            }

        member _.InspectContainer(request, _context) =
            task {
                ServiceGuards.requireContainerId request.Id
                let info = client.InspectContainer(DefaultNamespace, request.Id)
                let taskInfo = client.TaskInfo(DefaultNamespace, request.Id)

                let env =
                    let mutable e = Dictionary<string, string>()

                    try
                        let mutable envEl = Unchecked.defaultof<JsonElement>

                        if info.TryGetProperty("env", &envEl) then
                            for prop in envEl.EnumerateObject() do
                                if safeEnvVars |> Set.contains prop.Name then
                                    e[prop.Name] <- prop.Value.GetString()
                    with ex ->
                        Log.Warning(ex, "Erreur lors du parsing des variables d'environnement")

                    e

                let labels =
                    let mutable d = Dictionary<string, string>()

                    try
                        let mutable lbl = Unchecked.defaultof<JsonElement>

                        if info.TryGetProperty("labels", &lbl) then
                            for prop in lbl.EnumerateObject() do
                                d[prop.Name] <- prop.Value.GetString()
                    with ex ->
                        Log.Warning(ex, "Erreur lors du parsing des labels")

                    d

                let state, pid, finishedAt =
                    try
                        let s = mapState (tryGetString taskInfo "status")
                        let p = tryGetInt64 taskInfo "pid" |> int
                        let f = tryGetString taskInfo "exited_at"
                        s, p, f
                    with ex ->
                        Log.Warning(ex, "Erreur lors du parsing de la tâche")
                        ContainerState.Unknown, 0, ""

                let mountPaths =
                    match mountedVolumes.TryGetValue(request.Id) with
                    | true, vols -> vols |> List.map (fun v -> v.HostPath) |> List<string>
                    | false, _ -> List<string>()

                return
                    { InspectContainerResponse.Id = request.Id
                      Name = tryGetString info "id"
                      Image = tryGetString info "image"
                      State = state
                      CreatedAt = tryGetString info "created_at"
                      StartedAt = ""
                      FinishedAt = finishedAt
                      Labels = labels
                      Env = env
                      Pid = pid
                      ExitCode = tryGetInt64 info "exit_code" |> int
                      RestartPolicy = tryGetString info "restart_policy"
                      Ports = List<PortMapping>()
                      Health = tryGetString info "health"
                      Mounts = mountPaths }
            }

        member _.ListContainers(request, _context) =
            task {
                let ids = client.ListContainers(DefaultNamespace, request.All) |> Seq.toArray

                let containers =
                    ids
                    |> Array.Parallel.map (fun id ->
                        try
                            let info = client.InspectContainer(DefaultNamespace, id)
                            let ti = client.TaskInfo(DefaultNamespace, id)

                            { ContainerInfo.Id = id
                              Name = ""
                              Image = tryGetString info "image"
                              State = mapState (tryGetString ti "status")
                              CreatedAt = tryGetString info "created_at"
                              Labels = Dictionary<string, string>() }
                        with ex ->
                            Log.Warning(
                                ex,
                                "Erreur lors de l'inspection du conteneur {ContainerId} pour ListContainers",
                                id
                            )

                            { ContainerInfo.Id = id
                              Name = ""
                              Image = ""
                              State = ContainerState.Unknown
                              CreatedAt = ""
                              Labels = Dictionary<string, string>() })

                return { ListContainersResponse.Containers = List<ContainerInfo>(containers) }
            }

        member _.GetContainerLogs(request, context) =
            ServiceGuards.requireContainerId request.Id
            let tail = if request.Tail > 0 then min request.Tail 10_000 else 100
            let follow = request.Follow

            let since =
                if String.IsNullOrEmpty(request.Since) then
                    ""
                else
                    request.Since

            let toEntry (line: string) =
                { ContainerLogEntry.Timestamp = DateTime.UtcNow.ToString("o")
                  Stream = "stdout"
                  Log = line }

            if follow then
                client.GetContainerLogsStream(DefaultNamespace, request.Id, tail, since, context)
                |> fun e -> e.Select(toEntry)
            else
                client.GetContainerLogs(DefaultNamespace, request.Id, tail, false, since)
                |> List.toSeq
                |> fun l -> l.Select(toEntry).ToAsyncEnumerable()

        member _.ExecInContainer(request, _context) =
            ServiceGuards.requireContainerId request.Id

            if request.Command.Count = 0 then
                raise (RpcException(Status(StatusCode.InvalidArgument, "Au moins une commande est requise")))

            let command = request.Command |> Seq.toArray
            let result = client.ExecInContainer(DefaultNamespace, request.Id, command)

            Seq.singleton
                { ExecOutput.Stream = "stdout"
                  Data = Encoding.UTF8.GetBytes(result) }
            |> fun s -> s.ToAsyncEnumerable()

        member _.PullImage(request, _context) =
            task {
                ServiceGuards.requireNonEmpty request.Image "L'image à télécharger"

                let userArg =
                    if String.IsNullOrEmpty request.User then
                        None
                    else
                        Some request.User

                let result = client.PullImage(request.Image, userArg)

                return
                    { PullImageResponse.Image = request.Image
                      Message = result }
            }

        member _.GetVersion(request, _context) =
            task {
                let versionString = client.Version()

                let version, revision =
                    let idx = versionString.IndexOf("(revision: ", StringComparison.Ordinal)

                    if idx >= 0 then
                        let v = versionString.Substring(0, idx).Trim()
                        let start = idx + "(revision: ".Length
                        let rest = versionString.Substring(start)
                        let endIdx = rest.IndexOf(')')

                        let rev =
                            if endIdx >= 0 then
                                rest.Substring(0, endIdx).Trim()
                            else
                                rest.Trim()

                        (v, rev)
                    else
                        (versionString, "")

                return
                    { GetVersionResponse.Version = version
                      Revision = revision
                      GoVersion = ""
                      Os = ""
                      Arch = "" }
            }

        member _.ListNamespaces(request, _context) =
            task {
                let namespaces = client.Namespaces()
                return { ListNamespacesResponse.Namespaces = List<string>(namespaces) }
            }

        member _.RenameContainer(request, _context) =
            task {
                ServiceGuards.requireContainerId request.Id
                ServiceGuards.requireNonEmpty request.NewName "Le nouveau nom"
                client.RenameContainer(DefaultNamespace, request.Id, request.NewName)

                return
                    { RenameContainerResponse.Success = true
                      Message = sprintf "Conteneur renommé en '%s'" request.NewName }
            }

        member _.TopContainer(request, _context) =
            task {
                ServiceGuards.requireContainerId request.Id
                let output = client.TopContainer(DefaultNamespace, request.Id)
                let response = { TopContainerResponse.Processes = List<ProcessInfo>() }

                let lines =
                    output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)

                for line in lines do
                    let parts = line.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)

                    if parts.Length >= 3 then
                        let mutable pid = 0L
                        Int64.TryParse(parts[0], &pid) |> ignore
                        let user = if parts.Length > 1 then parts[1] else ""

                        let cmd =
                            if parts.Length > 2 then
                                String.concat " " (Array.skip 2 parts)
                            else
                                ""

                        let info =
                            { ProcessInfo.Pid = pid
                              User = user
                              Command = cmd
                              CpuPercent = 0.0
                              MemPercent = 0.0
                              Rss = 0L }

                        response.Processes.Add(info)

                return response
            }

        member _.GetContainerStats(request, _context) =
            task {
                ServiceGuards.requireContainerId request.Id
                let json = client.GetContainerStats(DefaultNamespace, request.Id)
                return buildStats json
            }

        member _.ListImages(request, _context) =
            task {
                let ns =
                    if String.IsNullOrEmpty(request.NamespaceName) then
                        DefaultNamespace
                    else
                        request.NamespaceName

                let images = client.ListImages(ns) |> Seq.toArray

                let infos =
                    images
                    |> Array.Parallel.map (fun imgJson ->
                        { ImageInfo.Ref = tryGetString imgJson "ref"
                          Id = tryGetString imgJson "id"
                          Repository = tryGetString imgJson "repository"
                          Tag = tryGetString imgJson "tag"
                          Size = tryGetInt64 imgJson "size"
                          CreatedAt = tryGetString imgJson "created_at" })

                return { ListImagesResponse.Images = List<ImageInfo>(infos) }
            }

        member _.InspectImage(request, _context) =
            task {
                ServiceGuards.requireNonEmpty request.Ref "La référence de l'image"

                let ns =
                    if String.IsNullOrEmpty(request.NamespaceName) then
                        DefaultNamespace
                    else
                        request.NamespaceName

                let imgJson = client.InspectImage(ns, request.Ref)

                let labels =
                    let mutable d = Dictionary<string, string>()
                    let mutable lbl = Unchecked.defaultof<JsonElement>

                    if imgJson.TryGetProperty("labels", &lbl) then
                        for prop in lbl.EnumerateObject() do
                            d[prop.Name] <- prop.Value.GetString()

                    d

                return
                    { InspectImageResponse.Ref = tryGetString imgJson "ref"
                      Id = tryGetString imgJson "id"
                      Repository = tryGetString imgJson "repository"
                      Tag = tryGetString imgJson "tag"
                      Size = tryGetInt64 imgJson "size"
                      CreatedAt = tryGetString imgJson "created_at"
                      Labels = labels }
            }

        member _.RemoveImage(request, _context) =
            task {
                ServiceGuards.requireNonEmpty request.Ref "La référence de l'image"

                let ns =
                    if String.IsNullOrEmpty(request.NamespaceName) then
                        DefaultNamespace
                    else
                        request.NamespaceName

                let message = client.RemoveImage(ns, request.Ref)

                return
                    { RemoveImageResponse.Success = true
                      Message = message }
            }

        member _.TagImage(request, _context) =
            task {
                ServiceGuards.requireNonEmpty request.Source "La source de l'image"
                ServiceGuards.requireNonEmpty request.Target "La cible de l'image"

                let ns =
                    if String.IsNullOrEmpty(request.NamespaceName) then
                        DefaultNamespace
                    else
                        request.NamespaceName

                client.TagImage(ns, request.Source, request.Target)

                return
                    { TagImageResponse.Source = request.Source
                      Target = request.Target
                      Message = sprintf "Image marquée de '%s' vers '%s'" request.Source request.Target }
            }

        // ─── Recherche d'images dans les catalogues en ligne ────────────────

        member _.SearchRegistry(request, ct) =
            task {
                ServiceGuards.requireNonEmpty request.Query "La requête de recherche"

                let limit = if request.Limit <= 0 then 25 else min request.Limit 100

                let registry, message =
                    if String.IsNullOrWhiteSpace(request.Registry) then
                        None, ""
                    else
                        match RegistryProviders.tryResolve request.Registry with
                        | Some host -> Some host, ""
                        | None ->
                            None,
                            sprintf "Registre « %s » non autorisé : recherche sur tous les registres" request.Registry

                let! hits =
                    match registrySearchClient with
                    | Some c -> RegistrySearch.searchWith c registry request.Query limit ct
                    | None -> RegistrySearch.search registry request.Query limit ct

                let results =
                    hits
                    |> List.map (fun h ->
                        { RegistrySearchResult.Registry = h.Registry
                          Ref = sprintf "%s/%s" h.Registry h.Repository
                          Description = h.Description
                          Stars = h.Stars })

                return
                    { SearchRegistryResponse.Results = List<RegistrySearchResult>(results)
                      Message = message }
            }

        // ─── Pause / Unpause ────────────────────────────────────────────────

        member _.PauseContainer(request, _context) =
            task {
                ServiceGuards.requireContainerId request.Id
                client.PauseContainer(DefaultNamespace, request.Id)

                return
                    { PauseContainerResponse.State = ContainerState.Paused
                      Message = "Conteneur en pause" }
            }

        member _.UnpauseContainer(request, _context) =
            task {
                ServiceGuards.requireContainerId request.Id
                client.ResumeContainer(DefaultNamespace, request.Id)

                return
                    { UnpauseContainerResponse.State = ContainerState.Running
                      Message = "Conteneur repris" }
            }

        // ─── Wait ───────────────────────────────────────────────────────────

        member _.WaitContainer(request, _context) =
            task {
                ServiceGuards.requireContainerId request.Id

                let exitCode =
                    client.WaitForContainerExit(DefaultNamespace, request.Id, request.TimeoutSeconds)

                if exitCode = -1 then
                    return
                        { WaitContainerResponse.ExitCode = -1
                          State = ContainerState.Running
                          Message = "Timeout en attendant la sortie du conteneur" }
                else
                    let exitCodeFinal =
                        try
                            let info = client.InspectContainer(DefaultNamespace, request.Id)
                            tryGetInt64 info "exit_code" |> int
                        with ex ->
                            Log.Warning(ex, "Impossible de récupérer le code de sortie de {ContainerId}", request.Id)
                            exitCode

                    return
                        { WaitContainerResponse.ExitCode = exitCodeFinal
                          State = ContainerState.Stopped
                          Message = "Conteneur terminé" }
            }

        // ─── Update ─────────────────────────────────────────────────────────

        member _.UpdateContainer(request, _context) =
            task {
                ServiceGuards.requireContainerId request.Id

                client.UpdateContainer(
                    DefaultNamespace,
                    request.Id,
                    request.MemoryLimit,
                    request.CpuShares,
                    request.PidLimit
                )

                return
                    { UpdateContainerResponse.Success = true
                      Message = "Conteneur mis à jour" }
            }

        // ─── Prune ──────────────────────────────────────────────────────────

        member _.PruneContainers(request, _context) =
            task {
                let deleted = List<string>()
                let ids = client.ListContainers(DefaultNamespace, true)

                for id in ids do
                    let state =
                        try
                            mapState (tryGetString (client.TaskInfo(DefaultNamespace, id)) "status")
                        with ex ->
                            Log.Warning(
                                ex,
                                "Erreur lors de l'inspection du conteneur {ContainerId} pour PruneContainers",
                                id
                            )

                            ContainerState.Unknown

                    if state = ContainerState.Stopped then
                        client.DeleteContainer(DefaultNamespace, id, false)
                        deleted.Add(id)

                return { PruneContainersResponse.Deleted = deleted }
            }

        member _.PruneImages(request, _context) =
            task {
                let deleted = List<string>()
                let images = client.ListImages(DefaultNamespace)

                for img in images do
                    let refValue = tryGetString img "ref"
                    let tag = tryGetString img "tag"

                    if not (String.IsNullOrEmpty refValue) && String.IsNullOrEmpty tag then
                        client.RemoveImage(DefaultNamespace, refValue) |> ignore
                        deleted.Add(refValue)

                return { PruneImagesResponse.Deleted = deleted }
            }

        // ─── Stats streaming ────────────────────────────────────────────────

        member _.GetContainerStatsStream(request, _context) =
            ServiceGuards.requireContainerId request.Id
            let channel = Channel.CreateUnbounded<GetContainerStatsResponse>()

            let intervalMs =
                if request.IntervalSeconds > 0 then
                    request.IntervalSeconds * 1000
                else
                    2000

            let run () =
                task {
                    try
                        while not _context.IsCancellationRequested do
                            let json = client.GetContainerStats(DefaultNamespace, request.Id)
                            do! channel.Writer.WriteAsync(buildStats json, _context)
                            do! Task.Delay(intervalMs, _context)

                        channel.Writer.TryComplete() |> ignore
                    with
                    | :? OperationCanceledException -> channel.Writer.TryComplete() |> ignore
                    | ex ->
                        Log.Error(ex, "Erreur lors du streaming des stats du conteneur {ContainerId}", request.Id)
                        channel.Writer.TryComplete(ex) |> ignore
                }

            observeProducerTask "stats-stream" (run () :> Task)
            |> ignore

            toAsyncEnumerable channel.Reader

        // ─── Événements ─────────────────────────────────────────────────────

        member _.WatchEvents(request, _context) =
            let channel = Channel.CreateUnbounded<ContainerEvent>()

            let run () =
                task {
                    let mutable previous = Map.empty<string, ContainerState>

                    try
                        while not _context.IsCancellationRequested do
                            let current = Dictionary<string, ContainerState>()

                            for id in client.ListContainers(DefaultNamespace, true) do
                                let state =
                                    try
                                        mapState (tryGetString (client.TaskInfo(DefaultNamespace, id)) "status")
                                    with ex ->
                                        Log.Debug(ex, "Impossible de lire l'état de {ContainerId}", id)
                                        ContainerState.Unknown

                                current[id] <- state

                            for kv in current do
                                match previous.TryGetValue(kv.Key) with
                                | true, oldState when oldState <> kv.Value ->
                                    let evtType =
                                        match oldState, kv.Value with
                                        | ContainerState.Created, ContainerState.Running -> "start"
                                        | ContainerState.Running, ContainerState.Paused -> "pause"
                                        | ContainerState.Paused, ContainerState.Running -> "unpause"
                                        | ContainerState.Running, ContainerState.Stopped -> "stop"
                                        | _, ContainerState.Stopped -> "exit"
                                        | _ -> "update"

                                    do!
                                        channel.Writer.WriteAsync(
                                            { ContainerEvent.Timestamp = DateTime.UtcNow.ToString("o")
                                              EventType = evtType
                                              Id = kv.Key
                                              Status = stateString kv.Value
                                              ExitCode = 0 },
                                            _context
                                        )
                                | true, _ -> ()
                                | false, _ ->
                                    do!
                                        channel.Writer.WriteAsync(
                                            { ContainerEvent.Timestamp = DateTime.UtcNow.ToString("o")
                                              EventType = "create"
                                              Id = kv.Key
                                              Status = stateString kv.Value
                                              ExitCode = 0 },
                                            _context
                                        )

                            for id in previous.Keys do
                                if not (current.ContainsKey id) then
                                    do!
                                        channel.Writer.WriteAsync(
                                            { ContainerEvent.Timestamp = DateTime.UtcNow.ToString("o")
                                              EventType = "delete"
                                              Id = id
                                              Status = "deleted"
                                              ExitCode = 0 },
                                            _context
                                        )

                            previous <- current |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
                            do! Task.Delay(2000, _context)

                        channel.Writer.TryComplete() |> ignore
                    with
                    | :? OperationCanceledException -> channel.Writer.TryComplete() |> ignore
                    | ex ->
                        Log.Error(ex, "Erreur lors du suivi des événements")
                        channel.Writer.TryComplete(ex) |> ignore
                }

            observeProducerTask "events-stream" (run () :> Task)
            |> ignore

            toAsyncEnumerable channel.Reader

        // ─── Exec bidirectionnel ────────────────────────────────────────────

        member _.ExecContainerStream(requests, _context) =
            let channel = Channel.CreateUnbounded<ExecOutput>()

            let run () =
                task {
                    let enumerator = requests.GetAsyncEnumerator(_context)

                    try
                        let! hasFirst = enumerator.MoveNextAsync().AsTask()

                        if not hasFirst then
                            channel.Writer.TryComplete() |> ignore
                        else
                            let first = enumerator.Current

                            if String.IsNullOrEmpty(first.Id) then
                                channel.Writer.TryComplete(
                                    RpcException(Status(StatusCode.InvalidArgument, ServiceGuards.ContainerIdRequired))
                                )
                                |> ignore
                            elif first.Command.Count = 0 then
                                channel.Writer.TryComplete(
                                    RpcException(
                                        Status(StatusCode.InvalidArgument, "Au moins une commande est requise")
                                    )
                                )
                                |> ignore
                            else
                                let id = first.Id
                                let command = first.Command |> Seq.toArray
                                SecurityValidation.validateContainerId id
                                SecurityValidation.validateCommand command
                                use inputStream = new ChannelStream()
                                use outputStream = new ChannelStream()
                                use errorStream = new ChannelStream()

                                if not (isNull first.Data) && first.Data.Length > 0 then
                                    do! inputStream.WriteAsync(first.Data, 0, first.Data.Length, _context)

                                if first.Eof then
                                    inputStream.Complete()

                                let pump (label: string) (stream: ChannelStream) =
                                    task {
                                        let buffer = Array.zeroCreate<byte> 8192
                                        let mutable cont = true

                                        while cont do
                                            let! n = stream.ReadAsync(buffer, 0, buffer.Length, _context)

                                            if n > 0 then
                                                let data = if n = buffer.Length then buffer else buffer[0 .. n - 1]

                                                do!
                                                    channel.Writer.WriteAsync(
                                                        { ExecOutput.Stream = label
                                                          Data = data },
                                                        _context
                                                    )
                                            else
                                                cont <- false
                                    }

                                let pumpTask =
                                    task {
                                        let! _ = pump "stdout" outputStream
                                        let! _ = pump "stderr" errorStream
                                        channel.Writer.TryComplete() |> ignore
                                    }

                                let execTask =
                                    Task.Run(fun () ->
                                        try
                                            client.StartExec(
                                                DefaultNamespace,
                                                id,
                                                command,
                                                inputStream,
                                                outputStream,
                                                errorStream
                                            )
                                        finally
                                            outputStream.Complete()
                                            errorStream.Complete())

                                let mutable cont = true

                                while cont do
                                    let! has = enumerator.MoveNextAsync().AsTask()

                                    if has then
                                        let m = enumerator.Current

                                        if not (isNull m.Data) && m.Data.Length > 0 then
                                            do! inputStream.WriteAsync(m.Data, 0, m.Data.Length, _context)

                                        if m.Eof then
                                            inputStream.Complete()
                                    else
                                        cont <- false

                                inputStream.Complete()
                                let! _ = execTask
                                let! _ = pumpTask
                                return ()
                    finally
                        try
                            enumerator.DisposeAsync().AsTask().Wait(10_000) |> ignore
                        with _ ->
                            ()
                        channel.Writer.TryComplete() |> ignore
                }

            observeProducerTask "exec-stream" (run () :> Task)
            |> ignore

            toAsyncEnumerable channel.Reader

        // ─── Copie de fichiers ──────────────────────────────────────────────

        member _.ReadFile(request, _context) =
            task {
                ServiceGuards.requireContainerId request.Id
                ServiceGuards.requireNonEmpty request.Path "Le chemin du fichier"
                SecurityValidation.validateContainerPath request.Path "Le chemin du fichier"
                let command = [| "base64"; request.Path |]
                SecurityValidation.validateCommand command

                try
                    // ExecInContainer propage désormais les erreurs : pas de
                    // sentinelle textuelle, et une sortie vide est traitée comme
                    // un échec (conteneur sans base64 → données vides trompeuses).
                    let output = client.ExecInContainer(DefaultNamespace, request.Id, command)
                    let trimmed = output.Trim()

                    if String.IsNullOrEmpty trimmed then
                        return
                            { ReadFileResponse.Data = Array.empty
                              Success = false
                              Message = "Sortie vide (base64 indisponible dans le conteneur ?)" }
                    else
                        let data = Convert.FromBase64String(trimmed)

                        return
                            { ReadFileResponse.Data = data
                              Success = true
                              Message = "" }
                with ex ->
                    Log.Warning(
                        ex,
                        "Impossible de lire le fichier {Path} dans le conteneur {ContainerId}",
                        request.Path,
                        request.Id
                    )

                    return
                        { ReadFileResponse.Data = Array.empty
                          Success = false
                          Message = "Impossible de lire le fichier : " + ex.Message }
            }

        member _.WriteFile(request, _context) =
            task {
                ServiceGuards.requireContainerId request.Id
                ServiceGuards.requireNonEmpty request.Path "Le chemin du fichier"
                SecurityValidation.validateContainerPath request.Path "Le chemin du fichier"
                let command = [| "sh"; "-c"; sprintf "base64 -d > '%s'" request.Path |]

                try
                    let base64 = Convert.ToBase64String(request.Data)
                    use stdin = new MemoryStream(Encoding.UTF8.GetBytes(base64))
                    use stdout = new MemoryStream()
                    use stderr = new MemoryStream()

                    let exitCode =
                        client.StartExec(DefaultNamespace, request.Id, command, stdin, stdout, stderr)

                    if exitCode = 0 then
                        return
                            { WriteFileResponse.Success = true
                              Message = "Fichier écrit" }
                    else
                        let err = Encoding.UTF8.GetString(stderr.ToArray())

                        return
                            { WriteFileResponse.Success = false
                              Message = sprintf "Échec de l'écriture du fichier (code %d) : %s" exitCode err }
                with ex ->
                    Log.Warning(
                        ex,
                        "Impossible d'écrire le fichier {Path} dans le conteneur {ContainerId}",
                        request.Path,
                        request.Id
                    )

                    return
                        { WriteFileResponse.Success = false
                          Message = "Impossible d'écrire le fichier : " + ex.Message }
            }

        // ─── Commit ─────────────────────────────────────────────────────────

        member _.CommitImage(request, _context) =
            task {
                ServiceGuards.requireContainerId request.ContainerId
                ServiceGuards.requireNonEmpty request.ImageRef "La référence de l'image"
                SecurityValidation.validateImage request.ImageRef

                // Le write-back des volumes n'a lieu qu'au Dispose (suppression
                // du conteneur) : exporter l'image source maintenant produirait
                // un commit mensonger sans les modifications du conteneur.
                match mountedVolumes.TryGetValue(request.ContainerId) with
                | true, volumes when not volumes.IsEmpty ->
                    return
                        { CommitImageResponse.ImageRef = ""
                          Success = false
                          Message =
                              "Commit impossible : des volumes sont montés et leurs modifications ne seront "
                              + "réécrites dans l'image qu'à la suppression du conteneur. Supprimez le "
                              + "conteneur puis recréez l'image depuis celle-ci." }
                | _ ->
                    try
                        let info = client.InspectContainer(DefaultNamespace, request.ContainerId)
                        let sourceRef = tryGetString info "image"

                        if String.IsNullOrEmpty sourceRef then
                            return
                                { CommitImageResponse.ImageRef = ""
                                  Success = false
                                  Message = "Aucune image source trouvée pour le conteneur" }
                        else
                            let tmp =
                                Path.Combine(Path.GetTempPath(), "diplo-commit-" + Guid.NewGuid().ToString("N") + ".tar")

                            try
                                client.ExportImage(DefaultNamespace, sourceRef, tmp)
                                client.ImportImage(DefaultNamespace, tmp) |> ignore
                                client.TagImage(DefaultNamespace, sourceRef, request.ImageRef)

                                return
                                    { CommitImageResponse.ImageRef = request.ImageRef
                                      Success = true
                                      Message = sprintf "Image '%s' créée depuis le conteneur" request.ImageRef }
                            finally
                                try
                                    File.Delete(tmp)
                                with _ ->
                                    ()
                    with ex ->
                        Log.Warning(
                            ex,
                            "Erreur lors du commit de l'image depuis le conteneur {ContainerId}",
                            request.ContainerId
                        )

                        return
                            { CommitImageResponse.ImageRef = ""
                              Success = false
                              Message = "Erreur lors du commit : " + ex.Message }
            }

        // ─── Export / Import ────────────────────────────────────────────────

        member _.ExportImage(request, _context) =
            ServiceGuards.requireNonEmpty request.ImageRef "La référence de l'image"

            let ns =
                if String.IsNullOrEmpty(request.NamespaceName) then
                    DefaultNamespace
                else
                    request.NamespaceName

            SecurityValidation.validateImage request.ImageRef
            let channel = Channel.CreateUnbounded<ImageChunk>()

            let run () =
                task {
                    let tmp =
                        Path.Combine(Path.GetTempPath(), "diplo-export-" + Guid.NewGuid().ToString("N") + ".tar")

                    try
                        try
                            client.ExportImage(ns, request.ImageRef, tmp)
                            use fs = new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.Read)
                            let buffer = Array.zeroCreate<byte> (64 * 1024)
                            let mutable cont = true

                            while cont do
                                _context.ThrowIfCancellationRequested()
                                let! n = fs.ReadAsync(buffer, 0, buffer.Length, _context)

                                if n = 0 then
                                    cont <- false
                                else
                                    let data = if n = buffer.Length then buffer else buffer[0 .. n - 1]
                                    do! channel.Writer.WriteAsync({ ImageChunk.Data = data }, _context)

                            channel.Writer.TryComplete() |> ignore
                        with
                        | :? OperationCanceledException -> channel.Writer.TryComplete() |> ignore
                        | ex ->
                            Log.Error(ex, "Erreur lors de l'export de l'image {ImageRef}", request.ImageRef)
                            channel.Writer.TryComplete(ex) |> ignore
                    finally
                        try
                            File.Delete(tmp)
                        with _ ->
                            ()
                }

            observeProducerTask "export-stream" (run () :> Task)
            |> ignore

            toAsyncEnumerable channel.Reader

        member _.ImportImage(requests, _context) =
            task {
                let tmp =
                    Path.Combine(Path.GetTempPath(), "diplo-import-" + Guid.NewGuid().ToString("N") + ".tar")

                try
                    let fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.Read)

                    try
                        let enumerator = requests.GetAsyncEnumerator(_context)

                        try
                            let mutable cont = true

                            while cont do
                                let! has = enumerator.MoveNextAsync().AsTask()

                                if has then
                                    let chunk = enumerator.Current

                                    if not (isNull chunk.Data) && chunk.Data.Length > 0 then
                                        do! fs.WriteAsync(chunk.Data, 0, chunk.Data.Length, _context)
                                else
                                    cont <- false
                        finally
                            try
                                enumerator.DisposeAsync().AsTask().Wait(10_000) |> ignore
                            with _ ->
                                ()

                        fs.Flush()
                    finally
                        fs.Dispose()

                    let refs = client.ImportImage(DefaultNamespace, tmp)

                    return
                        { ImportImageResponse.ImageRefs = List<string>(refs)
                          Message = sprintf "%d image(s) importée(s)" refs.Length }
                finally
                    try
                        File.Delete(tmp)
                    with _ ->
                        ()
            }

        // ─── Registres ──────────────────────────────────────────────────────

        member _.LoginRegistry(request, _context) =
            task {
                ServiceGuards.requireNonEmpty request.Registry "L'adresse du registre"
                ServiceGuards.requireNonEmpty request.Username "Le nom d'utilisateur"

                // Sans garde, un Password null atteint UTF8.GetBytes dans
                // RegistryAuth.protect et sort en Unknown au lieu d'InvalidArgument.
                ServiceGuards.requireNonEmpty request.Password "Le mot de passe"

                let registry =
                    match RegistryProviders.tryResolve request.Registry with
                    | Some host -> host
                    | None ->
                        raise (
                            RpcException(
                                Status(
                                    StatusCode.InvalidArgument,
                                    sprintf
                                        "Registre non pris en charge : '%s'. Fournisseurs autorisés : %s"
                                        request.Registry
                                        RegistryProviders.label
                                )
                            )
                        )

                RegistryAuth.add (RegistryAuth.stateFile ()) registry request.Username request.Password

                return
                    { LoginRegistryResponse.Success = true
                      Message = sprintf "Authentification configurée pour le registre '%s'" registry }
            }

        member _.LogoutRegistry(request, _context) =
            task {
                ServiceGuards.requireNonEmpty request.Registry "L'adresse du registre"

                let registry =
                    match RegistryProviders.tryResolve request.Registry with
                    | Some host -> host
                    | None ->
                        raise (
                            RpcException(
                                Status(
                                    StatusCode.InvalidArgument,
                                    sprintf
                                        "Registre non pris en charge : '%s'. Fournisseurs autorisés : %s"
                                        request.Registry
                                        RegistryProviders.label
                                )
                            )
                        )

                RegistryAuth.remove (RegistryAuth.stateFile ()) registry

                return
                    { LogoutRegistryResponse.Success = true
                      Message = sprintf "Déconnexion du registre '%s' effectuée" registry }
            }

        // ─── Namespaces ─────────────────────────────────────────────────────

        member _.CreateNamespace(request, _context) =
            task {
                ServiceGuards.requireNonEmpty request.Name "Le nom du namespace"
                SecurityValidation.validateId request.Name "Le namespace"
                client.CreateNamespace(request.Name)

                return
                    { CreateNamespaceResponse.Success = true
                      Message = sprintf "Namespace '%s' créé" request.Name }
            }

        member _.DeleteNamespace(request, _context) =
            task {
                ServiceGuards.requireNonEmpty request.Name "Le nom du namespace"
                SecurityValidation.validateId request.Name "Le namespace"
                client.DeleteNamespace(request.Name)

                return
                    { DeleteNamespaceResponse.Success = true
                      Message = sprintf "Namespace '%s' supprimé" request.Name }
            }
