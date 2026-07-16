namespace Diplo.Volume.Services

open System
open System.Text.Json
open System.Threading.Tasks
open Grpc.Core
open Diplo.Grpc.Volume
open Diplo.Volume.Drivers

type VolumeServiceImpl(driver: LocalVolumeDriver) =
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
            try
                let success = driver.RemoveVolume(request.Id, request.Force)
                return RemoveVolumeResponse(Success = success, Message = if success then "Volume supprimé" else "Volume introuvable")
            with ex ->
                return RemoveVolumeResponse(Success = false, Message = ex.Message)
        }

    override _.InspectVolume(request, context) =
        task {
            match driver.InspectVolume(request.Id) with
            | Some info ->
                let response = InspectVolumeResponse()
                try
                    let mutable v = Unchecked.defaultof<JsonElement>
                    if info.TryGetProperty("id", &v) then response.Id <- v.GetString()
                    if info.TryGetProperty("name", &v) then response.Name <- v.GetString()
                    if info.TryGetProperty("mountpoint", &v) then response.Mountpoint <- v.GetString()
                    response.Driver <- StorageDriverType.Local
                    response.State <- MountState.Unmounted
                    response.SizeBytes <- driver.GetVolumeSize(request.Id)
                with _ -> ()
                return response
            | None ->
                return InspectVolumeResponse()
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
                with _ -> ()
                response.Volumes.Add(info)
            return response
        }

    override _.MountVolume(request, context) =
        task {
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
            try
                let (_, message) = driver.UnmountVolume(request.Id, request.TargetPath)
                return UnmountVolumeResponse(State = MountState.Unmounted, Message = message)
            with ex ->
                return UnmountVolumeResponse(State = MountState.Error, Message = ex.Message)
        }
