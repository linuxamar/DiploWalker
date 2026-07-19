namespace Diplo.Container.Services

open System
open System.Text.Json
open System.Threading.Tasks
open Grpc.Core
open Serilog
open Diplo.Grpc.Container
open Diplo.Abstractions.Interfaces

type ContainerServiceImpl(client: IContainerdClient) =
    inherit ContainerService.ContainerServiceBase()

    [<Literal>]
    static let DefaultNamespace = "default"

    override _.CreateContainer(request, context) =
        task {
            if String.IsNullOrEmpty(request.Image) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'image du conteneur est requise")))
            let name = if String.IsNullOrEmpty(request.Name) then Guid.NewGuid().ToString("N") else request.Name
            let labels = request.Labels |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
            let id = client.CreateContainer(DefaultNamespace, name, request.Image, labels)
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
                    for prop in labelsValue.EnumerateObject() do
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
            let tail = if request.Tail > 0 then request.Tail else 100
            let logs = client.GetContainerLogs(DefaultNamespace, request.Id, tail)
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
