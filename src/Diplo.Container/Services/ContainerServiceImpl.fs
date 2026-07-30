namespace Diplo.Container.Services

open System
open System.Collections.Generic
open System.Linq
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Grpc.Core
open Serilog
open Diplo.Grpc
open Diplo.Grpc.Container
open Diplo.Abstractions.Interfaces
open Diplo.Abstractions

type ContainerServiceImpl(client: IContainerdClient) =

    [<Literal>]
    static let DefaultNamespace = "default"

    let tryGetString (el: JsonElement) (prop: string) =
        let mutable v = Unchecked.defaultof<JsonElement>
        if el.TryGetProperty(prop, &v) then v.GetString() else ""

    let tryGetInt64 (el: JsonElement) (prop: string) =
        let mutable v = Unchecked.defaultof<JsonElement>
        if el.TryGetProperty(prop, &v) then v.GetInt64() else 0L

    let tryGetDouble (el: JsonElement) (prop: string) =
        let mutable v = Unchecked.defaultof<JsonElement>
        if el.TryGetProperty(prop, &v) then v.GetDouble() else 0.0

    let mapState (status: string) =
        match status.ToLowerInvariant() with
        | "running" -> ContainerState.Running
        | "created" -> ContainerState.Created
        | "paused" | "pausing" -> ContainerState.Paused
        | "stopped" | "deleted" -> ContainerState.Stopped
        | "dead" -> ContainerState.Failed
        | _ -> ContainerState.Unknown

    interface IContainerService with

        member _.CreateContainer(request, _context) =
            task {
                if String.IsNullOrEmpty(request.Image) then
                    raise (RpcException(Status(StatusCode.InvalidArgument, "L'image du conteneur est requise")))
                let name = if String.IsNullOrEmpty(request.Name) then Guid.NewGuid().ToString("N") else request.Name
                SecurityValidation.validateName name "Le nom du conteneur"
                SecurityValidation.validateImage request.Image
                let labels = request.Labels |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
                for kv in labels do
                    SecurityValidation.validateLabel kv.Key kv.Value
                let env = request.Env |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
                let command = request.Command |> Seq.toArray
                let args = request.Args |> Seq.toArray
                let id = client.CreateContainer(DefaultNamespace, name, request.Image, labels, env, command, args, request.MemoryLimit, int64 request.CpuShares, uint32 request.PidLimit)
                return
                    { CreateContainerResponse.Id = id
                      Name = request.Name
                      State = ContainerState.Created
                      CreatedAt = DateTime.UtcNow.ToString("o") }
            }

        member _.StartContainer(request, _context) =
            task {
                if String.IsNullOrEmpty(request.Id) then
                    raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du conteneur est requis")))
                client.StartContainer(DefaultNamespace, request.Id)
                return { StartContainerResponse.State = ContainerState.Running; Message = "Conteneur démarré" }
            }

        member _.StopContainer(request, _context) =
            task {
                if String.IsNullOrEmpty(request.Id) then
                    raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du conteneur est requis")))
                let timeout = if request.TimeoutSeconds > 0 then request.TimeoutSeconds else 10
                do! client.StopContainer(DefaultNamespace, request.Id, timeout)
                return { StopContainerResponse.State = ContainerState.Stopped; Message = "Conteneur arrêté" }
            }

        member _.DeleteContainer(request, _context) =
            task {
                if String.IsNullOrEmpty(request.Id) then
                    raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du conteneur est requis")))
                client.DeleteContainer(DefaultNamespace, request.Id, request.Force)
                return { DeleteContainerResponse.Success = true; Message = "Conteneur supprimé" }
            }

        member _.InspectContainer(request, _context) =
            task {
                if String.IsNullOrEmpty(request.Id) then
                    raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du conteneur est requis")))
                let info = client.InspectContainer(DefaultNamespace, request.Id)
                let taskInfo = client.TaskInfo(DefaultNamespace, request.Id)
                let safeEnvVars =
                    set [ "PATH"; "USERNAME"; "USERDOMAIN"; "TEMP"; "TMP"
                          "HOMEDRIVE"; "HOMEPATH"; "SYSTEMROOT"; "OS"
                          "PROCESSOR_ARCHITECTURE"; "NUMBER_OF_PROCESSORS"
                          "ASPNETCORE_ENVIRONMENT"; "DOTNET_ENVIRONMENT"
                          "DOTNET_CLI_TELEMETRY_OPTOUT" ]
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
                    with ex -> Log.Warning(ex, "Erreur lors du parsing des labels")
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
                      ExitCode = tryGetInt64 info "exit_code" |> int }
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
                            Log.Warning(ex, "Erreur lors de l'inspection du conteneur {ContainerId} pour ListContainers", id)
                            { ContainerInfo.Id = id
                              Name = ""
                              Image = ""
                              State = ContainerState.Unknown
                              CreatedAt = ""
                              Labels = Dictionary<string, string>() })
                return { ListContainersResponse.Containers = List<ContainerInfo>(containers) }
            }

        member _.GetContainerLogs(request, _context) =
            if String.IsNullOrEmpty(request.Id) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du conteneur est requis")))
            let tail = if request.Tail > 0 then min request.Tail 10_000 else 100
            let follow = request.Follow
            let since = if String.IsNullOrEmpty(request.Since) then "" else request.Since
            let logs = client.GetContainerLogs(DefaultNamespace, request.Id, tail, follow, since)
            logs.Select(fun line ->
                { ContainerLogEntry.Timestamp = DateTime.UtcNow.ToString("o")
                  Stream = "stdout"
                  Log = line }).ToAsyncEnumerable()

        member _.ExecInContainer(request, _context) =
            if String.IsNullOrEmpty(request.Id) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du conteneur est requis")))
            if request.Command.Count = 0 then
                raise (RpcException(Status(StatusCode.InvalidArgument, "Au moins une commande est requise")))
            let command = request.Command |> Seq.toArray
            let result = client.ExecInContainer(DefaultNamespace, request.Id, command)
            Seq.singleton { ExecOutput.Stream = "stdout"; Data = Encoding.UTF8.GetBytes(result) }
            |> Seq.map id
            |> fun s -> s.ToAsyncEnumerable()

        member _.PullImage(request, _context) =
            task {
                if String.IsNullOrEmpty(request.Image) then
                    raise (RpcException(Status(StatusCode.InvalidArgument, "L'image à télécharger est requise")))
                let result = client.PullImage(request.Image)
                return { PullImageResponse.Image = request.Image; Message = result }
            }

        member _.GetVersion(request, _context) =
            task {
                let version = client.Version()
                return
                    { GetVersionResponse.Version = version
                      Revision = ""
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
                if String.IsNullOrEmpty(request.Id) then
                    raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du conteneur est requis")))
                if String.IsNullOrEmpty(request.NewName) then
                    raise (RpcException(Status(StatusCode.InvalidArgument, "Le nouveau nom est requis")))
                client.RenameContainer(DefaultNamespace, request.Id, request.NewName)
                return { RenameContainerResponse.Success = true; Message = sprintf "Conteneur renommé en '%s'" request.NewName }
            }

        member _.TopContainer(request, _context) =
            task {
                if String.IsNullOrEmpty(request.Id) then
                    raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du conteneur est requis")))
                let output = client.TopContainer(DefaultNamespace, request.Id)
                let response = { TopContainerResponse.Processes = List<ProcessInfo>() }
                let lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                for line in lines do
                    let parts = line.Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
                    if parts.Length >= 3 then
                        let mutable pid = 0L
                        Int64.TryParse(parts[0], &pid) |> ignore
                        let user = if parts.Length > 1 then parts[1] else ""
                        let cmd = if parts.Length > 2 then String.concat " " (Array.skip 2 parts) else ""
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
                if String.IsNullOrEmpty(request.Id) then
                    raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du conteneur est requis")))
                let json = client.GetContainerStats(DefaultNamespace, request.Id)
                let cpuUsage =
                    try
                        let mutable cpu = Unchecked.defaultof<JsonElement>
                        if json.TryGetProperty("cpu", &cpu) then tryGetDouble cpu "usage" else 0.0
                    with ex ->
                        Log.Warning(ex, "Erreur lors du parsing CPU")
                        0.0
                let memoryUsage, memoryLimit =
                    try
                        let mutable mem = Unchecked.defaultof<JsonElement>
                        if json.TryGetProperty("memory", &mem) then
                            tryGetInt64 mem "usage", tryGetInt64 mem "limit"
                        else 0L, 0L
                    with ex ->
                        Log.Warning(ex, "Erreur lors du parsing mémoire")
                        0L, 0L
                let pids =
                    try
                        let mutable p = Unchecked.defaultof<JsonElement>
                        if json.TryGetProperty("pids", &p) then tryGetInt64 p "current" |> int else 0
                    with ex ->
                        Log.Warning(ex, "Erreur lors du parsing PIDs")
                        0
                return
                    { GetContainerStatsResponse.CpuUsage = cpuUsage
                      MemoryUsage = memoryUsage
                      MemoryLimit = memoryLimit
                      NetworkRx = 0L
                      NetworkTx = 0L
                      DiskRead = 0L
                      DiskWrite = 0L
                      Pids = pids }
            }

        member _.ListImages(request, _context) =
            task {
                let ns = if String.IsNullOrEmpty(request.NamespaceName) then DefaultNamespace else request.NamespaceName
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
                if String.IsNullOrEmpty(request.Ref) then
                    raise (RpcException(Status(StatusCode.InvalidArgument, "La référence de l'image est requise")))
                let ns = if String.IsNullOrEmpty(request.NamespaceName) then DefaultNamespace else request.NamespaceName
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
                if String.IsNullOrEmpty(request.Ref) then
                    raise (RpcException(Status(StatusCode.InvalidArgument, "La référence de l'image est requise")))
                let ns = if String.IsNullOrEmpty(request.NamespaceName) then DefaultNamespace else request.NamespaceName
                let message = client.RemoveImage(ns, request.Ref)
                return { RemoveImageResponse.Success = true; Message = message }
            }

        member _.TagImage(request, _context) =
            task {
                if String.IsNullOrEmpty(request.Source) then
                    raise (RpcException(Status(StatusCode.InvalidArgument, "La source de l'image est requise")))
                if String.IsNullOrEmpty(request.Target) then
                    raise (RpcException(Status(StatusCode.InvalidArgument, "La cible de l'image est requise")))
                let ns = if String.IsNullOrEmpty(request.NamespaceName) then DefaultNamespace else request.NamespaceName
                client.TagImage(ns, request.Source, request.Target)
                return
                    { TagImageResponse.Source = request.Source
                      Target = request.Target
                      Message = sprintf "Image marquée de '%s' vers '%s'" request.Source request.Target }
            }
