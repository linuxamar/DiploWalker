namespace Diplo.Container.Services

open System
open System.Collections.Generic
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

    let toAsyncEnumerable (source: seq<'T>) : IAsyncEnumerable<'T> =
        { new IAsyncEnumerable<'T> with
            member _.GetAsyncEnumerator(_ct: Threading.CancellationToken) =
                let enumerator = source.GetEnumerator()
                { new IAsyncEnumerator<'T> with
                    member _.Current = enumerator.Current
                    member _.MoveNextAsync() =
                        ValueTask<bool>(enumerator.MoveNext())
                    member _.DisposeAsync() =
                        enumerator.Dispose()
                        ValueTask()
                }
        }

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
                client.StopContainer(DefaultNamespace, request.Id, timeout)
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
                let response =
                    { InspectContainerResponse.Id = request.Id
                      Name = ""
                      Image = ""
                      State = ContainerState.Unknown
                      CreatedAt = ""
                      StartedAt = ""
                      FinishedAt = ""
                      Labels = Dictionary<string, string>()
                      Env = Dictionary<string, string>()
                      Pid = 0
                      ExitCode = 0 }
                try
                    let mutable temp = Unchecked.defaultof<JsonElement>
                    if info.TryGetProperty("id", &temp) then
                        response.Name <- temp.GetString()
                    if info.TryGetProperty("image", &temp) then
                        response.Image <- temp.GetString()
                    if info.TryGetProperty("created_at", &temp) then
                        response.CreatedAt <- temp.GetString()
                    let mutable labelsValue = Unchecked.defaultof<JsonElement>
                    if info.TryGetProperty("labels", &labelsValue) then
                        for prop in labelsValue.EnumerateObject() do
                            response.Labels[prop.Name] <- prop.Value.GetString()
                    if info.TryGetProperty("env", &labelsValue) then
                        let safeEnvVars =
                            set [ "PATH"; "USERNAME"; "USERDOMAIN"; "TEMP"; "TMP"
                                  "HOMEDRIVE"; "HOMEPATH"; "SYSTEMROOT"; "OS"
                                  "PROCESSOR_ARCHITECTURE"; "NUMBER_OF_PROCESSORS"
                                  "ASPNETCORE_ENVIRONMENT"; "DOTNET_ENVIRONMENT"
                                  "DOTNET_CLI_TELEMETRY_OPTOUT" ]
                        for prop in labelsValue.EnumerateObject() do
                            if safeEnvVars |> Set.contains prop.Name then
                                response.Env[prop.Name] <- prop.Value.GetString()
                    if info.TryGetProperty("exit_code", &temp) then
                        response.ExitCode <- temp.GetInt64() |> int
                with ex ->
                    Log.Warning(ex, "Erreur lors du parsing des métadonnées du conteneur {ContainerId}", request.Id)
                try
                    let mutable temp = Unchecked.defaultof<JsonElement>
                    if taskInfo.TryGetProperty("pid", &temp) then
                        response.Pid <- temp.GetInt64() |> int
                    if taskInfo.TryGetProperty("status", &temp) then
                        let status = temp.GetString()
                        response.State <-
                            match status.ToLowerInvariant() with
                            | "running" -> ContainerState.Running
                            | "created" -> ContainerState.Created
                            | "paused" | "pausing" -> ContainerState.Paused
                            | "stopped" | "deleted" -> ContainerState.Stopped
                            | "dead" -> ContainerState.Failed
                            | _ -> ContainerState.Unknown
                    if taskInfo.TryGetProperty("exited_at", &temp) then
                        response.FinishedAt <- temp.GetString()
                with ex ->
                    Log.Warning(ex, "Erreur lors du parsing de la tâche du conteneur {ContainerId}", request.Id)
                return response
            }

        member _.ListContainers(request, _context) =
            task {
                let ids = client.ListContainers(DefaultNamespace, request.All)
                let response =
                    { ListContainersResponse.Containers = List<ContainerInfo>() }
                for id in ids do
                    let ci =
                        { ContainerInfo.Id = id
                          Name = ""
                          Image = ""
                          State = ContainerState.Unknown
                          CreatedAt = ""
                          Labels = Dictionary<string, string>() }
                    try
                        let info = client.InspectContainer(DefaultNamespace, id)
                        let mutable temp = Unchecked.defaultof<JsonElement>
                        if info.TryGetProperty("image", &temp) then ci.Image <- temp.GetString()
                        let mutable taskInfo = Unchecked.defaultof<JsonElement>
                        let ti = client.TaskInfo(DefaultNamespace, id)
                        if ti.TryGetProperty("status", &taskInfo) then
                            ci.State <-
                                match taskInfo.GetString().ToLowerInvariant() with
                                | "running" -> ContainerState.Running
                                | "created" -> ContainerState.Created
                                | "paused" | "pausing" -> ContainerState.Paused
                                | "stopped" | "deleted" -> ContainerState.Stopped
                                | "dead" -> ContainerState.Failed
                                | _ -> ContainerState.Unknown
                    with ex ->
                        Log.Warning(ex, "Erreur lors de l'inspection du conteneur {ContainerId} pour ListContainers", id)
                    response.Containers.Add(ci)
                return response
            }

        member _.GetContainerLogs(request, _context) =
            if String.IsNullOrEmpty(request.Id) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du conteneur est requis")))
            let tail = if request.Tail > 0 then min request.Tail 10_000 else 100
            let follow = request.Follow
            let since = if String.IsNullOrEmpty(request.Since) then "" else request.Since
            let logs = client.GetContainerLogs(DefaultNamespace, request.Id, tail, follow, since)
            toAsyncEnumerable (seq {
                for line in logs do
                    yield
                        { ContainerLogEntry.Timestamp = DateTime.UtcNow.ToString("o")
                          Stream = "stdout"
                          Log = line }
            })

        member _.ExecInContainer(request, _context) =
            if String.IsNullOrEmpty(request.Id) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du conteneur est requis")))
            if request.Command.Count = 0 then
                raise (RpcException(Status(StatusCode.InvalidArgument, "Au moins une commande est requise")))
            let command = request.Command |> Seq.toArray
            let result = client.ExecInContainer(DefaultNamespace, request.Id, command)
            toAsyncEnumerable (seq {
                yield { ExecOutput.Stream = "stdout"; Data = Encoding.UTF8.GetBytes(result) }
            })

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
                let response =
                    { GetContainerStatsResponse.CpuUsage = 0.0
                      MemoryUsage = 0L
                      MemoryLimit = 0L
                      NetworkRx = 0L
                      NetworkTx = 0L
                      DiskRead = 0L
                      DiskWrite = 0L
                      Pids = 0 }
                try
                    let mutable temp = Unchecked.defaultof<JsonElement>
                    if json.TryGetProperty("cpu", &temp) then
                        if temp.TryGetProperty("usage", &temp) then response.CpuUsage <- temp.GetDouble()
                    if json.TryGetProperty("memory", &temp) then
                        let memObj = temp
                        if memObj.TryGetProperty("usage", &temp) then response.MemoryUsage <- temp.GetInt64()
                        if memObj.TryGetProperty("limit", &temp) then response.MemoryLimit <- temp.GetInt64()
                    if json.TryGetProperty("pids", &temp) then
                        if temp.TryGetProperty("current", &temp) then response.Pids <- temp.GetInt64() |> int
                with ex ->
                    Log.Warning(ex, "Erreur lors du parsing des métriques du conteneur {ContainerId}", request.Id)
                return response
            }

        member _.ListImages(request, _context) =
            task {
                let ns = if String.IsNullOrEmpty(request.NamespaceName) then DefaultNamespace else request.NamespaceName
                let images = client.ListImages(ns)
                let response = { ListImagesResponse.Images = List<ImageInfo>() }
                for imgJson in images do
                    let info =
                        { ImageInfo.Ref = ""
                          Id = ""
                          Repository = ""
                          Tag = ""
                          Size = 0L
                          CreatedAt = "" }
                    let mutable temp = Unchecked.defaultof<JsonElement>
                    if imgJson.TryGetProperty("ref", &temp) then info.Ref <- temp.GetString()
                    if imgJson.TryGetProperty("id", &temp) then info.Id <- temp.GetString()
                    if imgJson.TryGetProperty("repository", &temp) then info.Repository <- temp.GetString()
                    if imgJson.TryGetProperty("tag", &temp) then info.Tag <- temp.GetString()
                    if imgJson.TryGetProperty("size", &temp) then info.Size <- temp.GetInt64()
                    if imgJson.TryGetProperty("created_at", &temp) then info.CreatedAt <- temp.GetString()
                    response.Images.Add(info)
                return response
            }

        member _.InspectImage(request, _context) =
            task {
                if String.IsNullOrEmpty(request.Ref) then
                    raise (RpcException(Status(StatusCode.InvalidArgument, "La référence de l'image est requise")))
                let ns = if String.IsNullOrEmpty(request.NamespaceName) then DefaultNamespace else request.NamespaceName
                let imgJson = client.InspectImage(ns, request.Ref)
                let response =
                    { InspectImageResponse.Ref = ""
                      Id = ""
                      Repository = ""
                      Tag = ""
                      Size = 0L
                      CreatedAt = ""
                      Labels = Dictionary<string, string>() }
                let mutable temp = Unchecked.defaultof<JsonElement>
                if imgJson.TryGetProperty("ref", &temp) then response.Ref <- temp.GetString()
                if imgJson.TryGetProperty("id", &temp) then response.Id <- temp.GetString()
                if imgJson.TryGetProperty("repository", &temp) then response.Repository <- temp.GetString()
                if imgJson.TryGetProperty("tag", &temp) then response.Tag <- temp.GetString()
                if imgJson.TryGetProperty("size", &temp) then response.Size <- temp.GetInt64()
                if imgJson.TryGetProperty("created_at", &temp) then response.CreatedAt <- temp.GetString()
                let mutable labelsValue = Unchecked.defaultof<JsonElement>
                if imgJson.TryGetProperty("labels", &labelsValue) then
                    for prop in labelsValue.EnumerateObject() do
                        response.Labels[prop.Name] <- prop.Value.GetString()
                return response
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
