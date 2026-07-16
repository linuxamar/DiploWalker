namespace Diplo.Container.Services

open System
open System.Text.Json
open System.Threading.Tasks
open Grpc.Core
open Diplo.Grpc.Container
open Diplo.Container.Clients

type ContainerServiceImpl(client: ContainerdClient) =
    inherit ContainerService.ContainerServiceBase()

    override _.CreateContainer(request, context) =
        task {
            let name = if String.IsNullOrEmpty(request.Name) then Guid.NewGuid().ToString("N") else request.Name
            let labels = request.Labels |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
            let id = client.CreateContainer("default", name, request.Image, labels)
            return CreateContainerResponse(
                Id = id,
                Name = request.Name,
                State = ContainerState.Running,
                CreatedAt = DateTime.UtcNow.ToString("o")
            )
        }

    override _.StartContainer(request, context) =
        task {
            client.StartContainer("default", request.Id)
            return StartContainerResponse(State = ContainerState.Running, Message = "Conteneur démarré")
        }

    override _.StopContainer(request, context) =
        task {
            let timeout = if request.TimeoutSeconds > 0 then request.TimeoutSeconds else 10
            client.StopContainer("default", request.Id, timeout)
            return StopContainerResponse(State = ContainerState.Stopped, Message = "Conteneur arrêté")
        }

    override _.DeleteContainer(request, context) =
        task {
            client.DeleteContainer("default", request.Id, request.Force)
            return DeleteContainerResponse(Success = true, Message = "Conteneur supprimé")
        }

    override _.InspectContainer(request, context) =
        task {
            let info = client.InspectContainer("default", request.Id)
            let response = InspectContainerResponse()
            response.Id <- request.Id
            try
                let mutable labelsValue = Unchecked.defaultof<JsonElement>
                if info.TryGetProperty("labels", &labelsValue) then
                    for prop in labelsValue.EnumerateObject() do
                        response.Labels[prop.Name] <- prop.Value.GetString()
                let mutable nameValue = Unchecked.defaultof<JsonElement>
                if info.TryGetProperty("id", &nameValue) then
                    response.Name <- nameValue.GetString()
            with _ -> ()
            return response
        }

    override _.ListContainers(request, context) =
        task {
            let ids = client.ListContainers("default", request.All)
            let response = ListContainersResponse()
            for id in ids do
                let ci = ContainerInfo()
                ci.Id <- id
                response.Containers.Add(ci)
            return response
        }

    override _.GetContainerLogs(request, responseStream, context) =
        task {
            let tail = if request.Tail > 0 then request.Tail else 100
            let logs = client.GetContainerLogs("default", request.Id, tail)
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
            let command = request.Command |> Seq.toArray
            let result = client.ExecInContainer("default", request.Id, command)
            let output = ExecOutput(Stream = "stdout", Data = Google.Protobuf.ByteString.CopyFromUtf8(result))
            do! responseStream.WriteAsync(output)
            return ()
        }

    override _.GetVersion(request, context) =
        task {
            let info = client.Version()
            let response = GetVersionResponse()
            try
                let mutable v = Unchecked.defaultof<JsonElement>
                if info.TryGetProperty("Version", &v) then response.Version <- v.GetString()
                if info.TryGetProperty("Revision", &v) then response.Revision <- v.GetString()
            with _ -> response.Version <- "unknown"
            return response
        }

    override _.ListNamespaces(request, context) =
        task {
            let namespaces = client.Namespaces()
            let response = ListNamespacesResponse()
            response.Namespaces.AddRange(namespaces)
            return response
        }
