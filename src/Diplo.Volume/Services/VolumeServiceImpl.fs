namespace Diplo.Volume.Services

open System
open System.Text.Json
open System.Threading.Tasks
open Grpc.Core
open Serilog
open Diplo.Grpc.Volume
open Diplo.Abstractions.Interfaces

type VolumeServiceImpl(driver: IVolumeDriver) =
    inherit VolumeService.VolumeServiceBase()

    override _.CreateVolume(request, context) =
        task {
            let name = if String.IsNullOrEmpty(request.Name) then Guid.NewGuid().ToString("N") else request.Name
            let driverOpts = request.DriverOpts |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
            let labels = request.Labels |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
            let (id, mountpoint) = driver.CreateVolume(name, driverOpts, labels)
            return CreateVolumeResponse(
                Id = id,
                Name = name,
                Driver = StorageDriverType.Local,
                Mountpoint = mountpoint,
                CreatedAt = DateTime.UtcNow.ToString("o")
            )
        }

    override _.RemoveVolume(request, context) =
        task {
            if String.IsNullOrEmpty(request.Id) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du volume est requis")))
            try
                let success = driver.RemoveVolume(request.Id, request.Force)
                if not success then
                    raise (RpcException(Status(StatusCode.NotFound, sprintf "Volume '%s' introuvable" request.Id)))
                return RemoveVolumeResponse(Success = true, Message = "Volume supprimé")
            with ex when not (ex :? RpcException) ->
                return RemoveVolumeResponse(Success = false, Message = ex.Message)
        }

    override _.InspectVolume(request, context) =
        task {
            if String.IsNullOrEmpty(request.Id) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du volume est requis")))
            let volResult = driver.InspectVolume(request.Id)
            if volResult.IsNone then
                raise (RpcException(Status(StatusCode.NotFound, sprintf "Volume '%s' introuvable" request.Id)))
            let info = volResult.Value
            let response = InspectVolumeResponse()
            try
                let mutable v = Unchecked.defaultof<JsonElement>
                if info.TryGetProperty("id", &v) then response.Id <- v.GetString()
                if info.TryGetProperty("name", &v) then response.Name <- v.GetString()
                if info.TryGetProperty("mountpoint", &v) then response.Mountpoint <- v.GetString()
                response.Driver <- StorageDriverType.Local
                response.State <- MountState.Unmounted
                response.SizeBytes <- driver.GetVolumeSize(request.Id)
            with ex ->
                Log.Warning(ex, "Erreur lors du parsing des informations du volume {VolumeId}", request.Id)
            return response
        }

    override _.ListVolumes(request, context) =
        task {
            let filters = request.Filters |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
            let volumes = driver.ListVolumes(filters)
            let response = ListVolumesResponse()
            for vol in volumes do
                let info = VolumeInfo()
                try
                    let mutable v = Unchecked.defaultof<JsonElement>
                    if vol.TryGetProperty("id", &v) then info.Id <- v.GetString()
                    if vol.TryGetProperty("name", &v) then info.Name <- v.GetString()
                    info.Driver <- StorageDriverType.Local
                with ex ->
                    Log.Warning(ex, "Erreur lors du parsing du volume dans la liste")
                response.Volumes.Add(info)
            return response
        }

    override _.MountVolume(request, context) =
        task {
            if String.IsNullOrEmpty(request.Id) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du volume est requis")))
            if String.IsNullOrEmpty(request.TargetPath) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "Le chemin cible est requis")))
            try
                let options = request.Options |> Seq.map (fun kv -> kv.Key + "=" + kv.Value) |> String.concat ";"
                let (_, mountpoint) = driver.MountVolume(request.Id, request.TargetPath, options)
                return MountVolumeResponse(
                    State = MountState.Mounted,
                    Mountpoint = mountpoint,
                    Message = "Volume monté"
                )
            with ex ->
                return MountVolumeResponse(State = MountState.Error, Message = ex.Message)
        }

    override _.UnmountVolume(request, context) =
        task {
            if String.IsNullOrEmpty(request.Id) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du volume est requis")))
            if String.IsNullOrEmpty(request.TargetPath) then
                raise (RpcException(Status(StatusCode.InvalidArgument, "Le chemin cible est requis")))
            try
                let (_, message) = driver.UnmountVolume(request.Id, request.TargetPath)
                return UnmountVolumeResponse(State = MountState.Unmounted, Message = message)
            with ex ->
                return UnmountVolumeResponse(State = MountState.Error, Message = ex.Message)
        }
