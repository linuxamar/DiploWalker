namespace Diplo.Container.Services

open System
open System.Text.Json
open System.Threading.Tasks
open Grpc.Core
open Serilog
open Diplo.Grpc.Container
open Diplo.Abstractions.Interfaces
open Diplo.Abstractions

type ContainerServiceImpl(client: IContainerdClient) =
    inherit ContainerService.ContainerServiceBase()

    [<Literal>]
    static let DefaultNamespace = "default"

    override _.CreateContainer(request, context) =
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
            let id = client.CreateContainer(DefaultNamespace, name, request.Image, labels, env, command, args, request.MemoryLimit, request.CpuShares, request.PidLimit)
            return CreateContainerResponse(
                Id = id,
                Name = request.Name,
                State = ContainerState.Running,
                CreatedAt = DateTime.UtcNow.ToString("o")
            )
        }

    override _.StartContainer(request, context) =
        task {
            if String.IsNullOrEmpty(request.Id) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du conteneur est requis")))
            client.StartContainer(DefaultNamespace, request.Id)
            return StartContainerResponse(State = ContainerState.Running, Message = "Conteneur démarré")
        }

    override _.StopContainer(request, context) =
        task {
            if String.IsNullOrEmpty(request.Id) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du conteneur est requis")))
            let timeout = if request.TimeoutSeconds > 0 then request.TimeoutSeconds else 10
            client.StopContainer(DefaultNamespace, request.Id, timeout)
            return StopContainerResponse(State = ContainerState.Stopped, Message = "Conteneur arrêté")
        }

    override _.DeleteContainer(request, context) =
        task {
            if String.IsNullOrEmpty(request.Id) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du conteneur est requis")))
            client.DeleteContainer(DefaultNamespace, request.Id, request.Force)
            return DeleteContainerResponse(Success = true, Message = "Conteneur supprimé")
        }

    override _.InspectContainer(request, context) =
        task {
            if String.IsNullOrEmpty(request.Id) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du conteneur est requis")))
            let info = client.InspectContainer(DefaultNamespace, request.Id)
            let taskInfo = client.TaskInfo(DefaultNamespace, request.Id)
            let response = InspectContainerResponse()
            response.Id <- request.Id
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
                    response.ExitCode <- temp.GetInt64()
            with ex ->
                Log.Warning(ex, "Erreur lors du parsing des métadonnées du conteneur {ContainerId}", request.Id)
            try
                let mutable temp = Unchecked.defaultof<JsonElement>
                if taskInfo.TryGetProperty("pid", &temp) then
                    response.Pid <- temp.GetInt64()
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

    override _.ListContainers(request, context) =
        task {
            let ids = client.ListContainers(DefaultNamespace, request.All)
            let response = ListContainersResponse()
            for id in ids do
                let ci = ContainerInfo()
                ci.Id <- id
                response.Containers.Add(ci)
            return response
        }

    override _.GetContainerLogs(request, responseStream, context) =
        task {
            if String.IsNullOrEmpty(request.Id) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du conteneur est requis")))
            let tail = if request.Tail > 0 then min request.Tail 10_000 else 100
            let follow = request.Follow
            let since = if String.IsNullOrEmpty(request.Since) then "" else request.Since
            let logs = client.GetContainerLogs(DefaultNamespace, request.Id, tail, follow, since)
            for line in logs do
                let entry = ContainerLogEntry(
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    Stream = "stdout",
                    Log = line
                )
                do! responseStream.WriteAsync(entry)
            return ()
        }

    override _.ExecInContainer(request, responseStream, context) =
        task {
            if String.IsNullOrEmpty(request.Id) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du conteneur est requis")))
            if request.Command.Count = 0 then
                raise (RpcException(Status(StatusCode.InvalidArgument, "Au moins une commande est requise")))
            let command = request.Command |> Seq.toArray
            let result = client.ExecInContainer(DefaultNamespace, request.Id, command)
            let output = ExecOutput(Stream = "stdout", Data = Google.Protobuf.ByteString.CopyFromUtf8(result))
            do! responseStream.WriteAsync(output)
            return ()
        }

    override _.PullImage(request, context) =
        task {
            if String.IsNullOrEmpty(request.Image) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'image à télécharger est requise")))
            let result = client.PullImage(request.Image)
            return PullImageResponse(Image = request.Image, Message = result)
        }

    override _.GetVersion(request, context) =
        task {
            let version = client.Version()
            let response = GetVersionResponse()
            response.Version <- version
            return response
        }

    override _.ListNamespaces(request, context) =
        task {
            let namespaces = client.Namespaces()
            let response = ListNamespacesResponse()
            response.Namespaces.AddRange(namespaces)
            return response
        }

    override _.RenameContainer(request, context) =
        task {
            if String.IsNullOrEmpty(request.Id) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du conteneur est requis")))
            if String.IsNullOrEmpty(request.NewName) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "Le nouveau nom est requis")))
            client.RenameContainer(DefaultNamespace, request.Id, request.NewName)
            return RenameContainerResponse(Success = true, Message = sprintf "Conteneur renommé en '%s'" request.NewName)
        }

    override _.TopContainer(request, context) =
        task {
            if String.IsNullOrEmpty(request.Id) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du conteneur est requis")))
            let output = client.TopContainer(DefaultNamespace, request.Id)
            let response = TopContainerResponse()
            let lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
            for line in lines do
                let parts = line.Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
                if parts.Length >= 3 then
                    let mutable pid = 0L
                    Int64.TryParse(parts[0], &pid) |> ignore
                    let user = if parts.Length > 1 then parts[1] else ""
                    let cmd = if parts.Length > 2 then String.concat " " (Array.skip 2 parts) else ""
                    let info = ProcessInfo(Pid = pid, User = user, Command = cmd)
                    response.Processes.Add(info)
            return response
        }

    override _.GetContainerStats(request, context) =
        task {
            if String.IsNullOrEmpty(request.Id) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du conteneur est requis")))
            let json = client.GetContainerStats(DefaultNamespace, request.Id)
            let response = GetContainerStatsResponse()
            try
                let mutable temp = Unchecked.defaultof<JsonElement>
                if json.TryGetProperty("cpu", &temp) then
                    if temp.TryGetProperty("usage", &temp) then response.CpuUsage <- temp.GetInt64()
                if json.TryGetProperty("memory", &temp) then
                    let memObj = temp
                    if memObj.TryGetProperty("usage", &temp) then response.MemoryUsage <- temp.GetInt64()
                    if memObj.TryGetProperty("limit", &temp) then response.MemoryLimit <- temp.GetInt64()
                if json.TryGetProperty("pids", &temp) then
                    if temp.TryGetProperty("current", &temp) then response.Pids <- temp.GetDouble()
            with ex ->
                Log.Warning(ex, "Erreur lors du parsing des métriques du conteneur {ContainerId}", request.Id)
            return response
        }
