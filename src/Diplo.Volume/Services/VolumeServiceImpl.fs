namespace Diplo.Volume.Services

open System
open System.ServiceModel
open System.Text.Json
open System.Threading.Tasks
open Grpc.Core
open Serilog
open Diplo.Grpc
open Diplo.Grpc.Volume
open Diplo.Abstractions
open Diplo.Abstractions.Interfaces
open Diplo.Grpc
open Diplo.Volume.Drivers

[<ServiceContract(Name = "IVolumeService")>]
type VolumeServiceImpl(registry: VolumeDriverRegistry) =

    let getDriver (driverType: StorageDriverType) =
        registry.Get(driverType)

    interface IVolumeService with

        member _.CreateVolume(request, _context) =
            task {
                let name = if String.IsNullOrEmpty(request.Name) then Guid.NewGuid().ToString("N") else request.Name
                SecurityValidation.validateName name "Le nom du volume"
                let driverOpts = request.DriverOpts |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
                let labels = request.Labels |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
                for kv in labels do
                    SecurityValidation.validateLabel kv.Key kv.Value
                let driverType =
                    if request.Driver = StorageDriverType.Local && driverOpts.ContainsKey("driver") then
                        DriverMappings.parseVolumeDriver driverOpts.["driver"]
                    else request.Driver
                let driver = getDriver driverType
                let (id, mountpoint) = driver.CreateVolume(name, driverOpts, labels)
                return
                    { CreateVolumeResponse.Id = id
                      Name = name
                      Driver = driverType
                      Mountpoint = mountpoint
                      CreatedAt = DateTime.UtcNow.ToString("o") }
            }

        member _.RemoveVolume(request, _context) =
            task {
                if String.IsNullOrEmpty(request.Id) then
                    raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du volume est requis")))
                SecurityValidation.validateId request.Id "L'identifiant du volume"
                let found =
                    try
                        registry.GetAll()
                        |> List.exists (fun d -> try d.RemoveVolume(request.Id, request.Force) with _ -> false)
                    with ex ->
                        Log.Warning(ex, "Erreur lors de la suppression du volume {VolumeId}", request.Id)
                        false
                if not found then
                    return { RemoveVolumeResponse.Success = false; Message = "Volume introuvable" }
                else
                    return { RemoveVolumeResponse.Success = true; Message = "Volume supprimé" }
            }

        member _.InspectVolume(request, _context) =
            task {
                if String.IsNullOrEmpty(request.Id) then
                    raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du volume est requis")))
                SecurityValidation.validateId request.Id "L'identifiant du volume"
                let tryInspect (driver: IVolumeDriver) =
                    try driver.InspectVolume(request.Id)
                    with _ -> None
                let volResult =
                    registry.GetAll()
                    |> List.tryPick (fun d -> tryInspect d)
                if volResult.IsNone then
                    raise (RpcException(Status(StatusCode.NotFound, sprintf "Volume '%s' introuvable" request.Id)))
                let info = volResult.Value
                let response =
                    { InspectVolumeResponse.Id = ""
                      Name = ""
                      Driver = StorageDriverType.Local
                      Mountpoint = ""
                      State = MountState.Unmounted
                      Labels = System.Collections.Generic.Dictionary<string, string>()
                      DriverOpts = System.Collections.Generic.Dictionary<string, string>()
                      SizeBytes = 0L
                      CreatedAt = "" }
                try
                    let mutable v = Unchecked.defaultof<JsonElement>
                    if info.TryGetProperty("id", &v) then response.Id <- v.GetString()
                    if info.TryGetProperty("name", &v) then response.Name <- v.GetString()
                    if info.TryGetProperty("remotePath", &v) then response.Mountpoint <- v.GetString()
                    if info.TryGetProperty("mountpoint", &v) then response.Mountpoint <- v.GetString()
                    if info.TryGetProperty("state", &v) then
                        response.State <- match v.GetString() with "mounted" -> MountState.Mounted | "error" -> MountState.Error | _ -> MountState.Unmounted
                    if info.TryGetProperty("created_at", &v) then response.CreatedAt <- v.GetString()
                    if info.TryGetProperty("createdAt", &v) then response.CreatedAt <- v.GetString()
                    if info.TryGetProperty("driver", &v) then
                        match v.GetString().ToLowerInvariant() with
                        | "local" -> response.Driver <- StorageDriverType.Local
                        | "nfs" -> response.Driver <- StorageDriverType.Nfs
                        | "smb" -> response.Driver <- StorageDriverType.Smb
                        | "azure" -> response.Driver <- StorageDriverType.CloudAzure
                        | "aws" -> response.Driver <- StorageDriverType.CloudAws
                        | "gcp" -> response.Driver <- StorageDriverType.CloudGcp
                        | "iso" -> response.Driver <- StorageDriverType.Iso
                        | _ -> ()
                    if info.TryGetProperty("labels", &v) then
                        for prop in v.EnumerateObject() do response.Labels.[prop.Name] <- prop.Value.GetString()
                    if info.TryGetProperty("driver_opts", &v) then
                        for prop in v.EnumerateObject() do response.DriverOpts.[prop.Name] <- prop.Value.GetString()
                    if info.TryGetProperty("driverOpts", &v) then
                        for prop in v.EnumerateObject() do response.DriverOpts.[prop.Name] <- prop.Value.GetString()
                    response.SizeBytes <-
                        try
                            registry.GetAll()
                            |> List.tryPick (fun d -> try Some(d.GetVolumeSize(request.Id)) with _ -> None)
                            |> Option.defaultValue 0L
                        with _ -> 0L
                with ex ->
                    Log.Warning(ex, "Erreur lors du parsing des informations du volume {VolumeId}", request.Id)
                return response
            }

        member _.ListVolumes(request, _context) =
            task {
                let filters = request.Filters |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
                let allVolumes =
                    registry.GetAll()
                    |> List.collect (fun d ->
                        try d.ListVolumes(filters)
                        with _ -> [])
                let response =
                    { ListVolumesResponse.Volumes = System.Collections.Generic.List<VolumeInfo>() }
                for vol in allVolumes do
                    let info =
                        { VolumeInfo.Id = ""
                          Name = ""
                          Driver = StorageDriverType.Local
                          Mountpoint = ""
                          State = MountState.Unmounted
                          Labels = System.Collections.Generic.Dictionary<string, string>()
                          SizeBytes = 0L }
                    try
                        let mutable v = Unchecked.defaultof<JsonElement>
                        if vol.TryGetProperty("id", &v) then info.Id <- v.GetString()
                        if vol.TryGetProperty("name", &v) then info.Name <- v.GetString()
                        if vol.TryGetProperty("remotePath", &v) then info.Mountpoint <- v.GetString()
                        if vol.TryGetProperty("mountpoint", &v) then info.Mountpoint <- v.GetString()
                        if vol.TryGetProperty("state", &v) then
                            info.State <- match v.GetString() with "mounted" -> MountState.Mounted | "error" -> MountState.Error | _ -> MountState.Unmounted
                        if vol.TryGetProperty("driver", &v) then
                            match v.GetString().ToLowerInvariant() with
                            | "local" -> info.Driver <- StorageDriverType.Local
                            | "nfs" -> info.Driver <- StorageDriverType.Nfs
                            | "smb" -> info.Driver <- StorageDriverType.Smb
                            | "azure" -> info.Driver <- StorageDriverType.CloudAzure
                            | "aws" -> info.Driver <- StorageDriverType.CloudAws
                            | "gcp" -> info.Driver <- StorageDriverType.CloudGcp
                            | "iso" -> info.Driver <- StorageDriverType.Iso
                            | _ -> ()
                        if vol.TryGetProperty("size_bytes", &v) then info.SizeBytes <- v.GetInt64()
                    with ex ->
                        Log.Warning(ex, "Erreur lors du parsing du volume dans la liste")
                    response.Volumes.Add(info)
                return response
            }

        member _.MountVolume(request, _context) =
            task {
                if String.IsNullOrEmpty(request.Id) then
                    raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du volume est requis")))
                if String.IsNullOrEmpty(request.TargetPath) then
                    raise (RpcException(Status(StatusCode.InvalidArgument, "Le chemin cible est requis")))
                SecurityValidation.validateId request.Id "L'identifiant du volume"
                SecurityValidation.validateVolumePath request.TargetPath "Le chemin cible"
                try
                    let options = request.Options |> Seq.map (fun kv -> kv.Key + "=" + kv.Value) |> String.concat ";"
                    let tryMount (driver: IVolumeDriver) =
                        try
                            let (_, mountpoint) = driver.MountVolume(request.Id, request.TargetPath, options)
                            Some mountpoint
                        with _ -> None
                    let result =
                        registry.GetAll()
                        |> List.tryPick tryMount
                    match result with
                    | Some mountpoint ->
                        return
                            { MountVolumeResponse.State = MountState.Mounted
                              Mountpoint = mountpoint
                              Message = "Volume monté" }
                    | None ->
                        return { MountVolumeResponse.State = MountState.Error; Mountpoint = ""; Message = "Volume introuvable ou erreur de montage" }
                with ex ->
                    Log.Warning(ex, "Erreur lors du montage du volume {VolumeId}", request.Id)
                    return { MountVolumeResponse.State = MountState.Error; Mountpoint = ""; Message = "Erreur interne lors du montage du volume" }
            }

        member _.UnmountVolume(request, _context) =
            task {
                if String.IsNullOrEmpty(request.Id) then
                    raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du volume est requis")))
                if String.IsNullOrEmpty(request.TargetPath) then
                    raise (RpcException(Status(StatusCode.InvalidArgument, "Le chemin cible est requis")))
                SecurityValidation.validateId request.Id "L'identifiant du volume"
                SecurityValidation.validateVolumePath request.TargetPath "Le chemin cible"
                try
                    let tryUnmount (driver: IVolumeDriver) =
                        try
                            let (_, message) = driver.UnmountVolume(request.Id, request.TargetPath)
                            Some message
                        with _ -> None
                    let result =
                        registry.GetAll()
                        |> List.tryPick tryUnmount
                    match result with
                    | Some message ->
                        return { UnmountVolumeResponse.State = MountState.Unmounted; Message = message }
                    | None ->
                        return { UnmountVolumeResponse.State = MountState.Unmounted; Message = "Démonté" }
                with ex ->
                    Log.Warning(ex, "Erreur lors du démontage du volume {VolumeId}", request.Id)
                    return { UnmountVolumeResponse.State = MountState.Error; Message = "Erreur interne lors du démontage du volume" }
            }

        member _.PruneVolumes(request, _context) =
            task {
                let allRemoved =
                    registry.GetAll()
                    |> List.collect (fun d ->
                        try d.PruneVolumes()
                        with _ -> [])
                let count = allRemoved |> List.length
                return
                    { PruneVolumesResponse.VolumesDeleted = System.Collections.Generic.List<string>(allRemoved)
                      Count = count
                      Message = sprintf "%d volume(s) supprimé(s)" count }
            }
