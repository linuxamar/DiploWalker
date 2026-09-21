namespace DiploWalker.Volume.Services

open System
open System.ServiceModel
open System.Text.Json
open System.Threading.Tasks
open Grpc.Core
open Serilog
open DiploWalker.Grpc
open DiploWalker.Grpc.Volume
open DiploWalker.Abstractions
open DiploWalker.Abstractions.Interfaces
open DiploWalker.Volume.Drivers

[<ServiceContract(Name = "IVolumeService")>]
type VolumeServiceImpl(registry: VolumeDriverRegistry) =

    let getDriver (driverType: StorageDriverType) = registry.Get(driverType)

    let parseVolumeInfo (vol: JsonElement) : VolumeInfo =
        let mutable v = Unchecked.defaultof<JsonElement>

        { VolumeInfo.Id = if vol.TryGetProperty("id", &v) then v.GetString() else ""
          Name = if vol.TryGetProperty("name", &v) then v.GetString() else ""
          Driver =
            if vol.TryGetProperty("driver", &v) then
                DriverMappings.parseVolumeDriver (v.GetString())
            else
                StorageDriverType.Local
          Mountpoint =
            if vol.TryGetProperty("remotePath", &v) then v.GetString()
            elif vol.TryGetProperty("mountpoint", &v) then v.GetString()
            else ""
          State =
            if vol.TryGetProperty("state", &v) then
                match v.GetString() with
                | "mounted" -> MountState.Mounted
                | "error" -> MountState.Error
                | _ -> MountState.Unmounted
            else
                MountState.Unmounted
          Labels =
            let d = System.Collections.Generic.Dictionary<string, string>()

            if vol.TryGetProperty("labels", &v) then
                for prop in v.EnumerateObject() do
                    d[prop.Name] <- prop.Value.GetString()

            d
          SizeBytes =
            if vol.TryGetProperty("size_bytes", &v) then
                v.GetInt64()
            else
                0L }

    interface IVolumeService with

        member _.CreateVolume(request, _context) =
            task {
                let name =
                    if String.IsNullOrEmpty(request.Name) then
                        Guid.NewGuid().ToString("N")
                    else
                        request.Name

                SecurityValidation.validateName name "Le nom du volume"

                let driverOpts =
                    request.DriverOpts |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq

                let labels = request.Labels |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq

                for kv in labels do
                    SecurityValidation.validateLabel kv.Key kv.Value

                let driverType =
                    if request.Driver = StorageDriverType.Local && driverOpts.ContainsKey("driver") then
                        DriverMappings.parseVolumeDriver driverOpts.["driver"]
                    else
                        request.Driver

                try
                    let driver = getDriver driverType
                    let (id, mountpoint) = driver.CreateVolume(name, driverOpts, labels)

                    return
                        { CreateVolumeResponse.Id = id
                          Name = name
                          Driver = driverType
                          Mountpoint = mountpoint
                          CreatedAt = DateTime.UtcNow.ToString("o") }
                with
                | :? RpcException as rpcEx -> return raise rpcEx
                | ex ->
                    Log.Warning(
                        ex,
                        "Erreur lors de la crÃ©ation du volume {Name} avec le driver {Driver}",
                        name,
                        driverType
                    )

                    return
                        raise (
                            RpcException(
                                Status(
                                    StatusCode.Internal,
                                    sprintf "Erreur lors de la crÃ©ation du volume '%s': %s" name ex.Message
                                )
                            )
                        )
            }

        member _.RemoveVolume(request, _context) =
            task {
                ServiceGuards.requireVolumeId request.Id
                SecurityValidation.validateId request.Id "L'identifiant du volume"

                let found =
                    try
                        registry.GetAll()
                        |> List.exists (fun d ->
                            try
                                d.RemoveVolume(request.Id, request.Force)
                            with
                            | :? RpcException -> reraise ()
                            | _ -> false)
                    with
                    | :? RpcException -> reraise ()
                    | ex ->
                        Log.Warning(ex, "Erreur lors de la suppression du volume {VolumeId}", request.Id)
                        false

                if not found then
                    return
                        { RemoveVolumeResponse.Success = false
                          Message = "Volume introuvable" }
                else
                    return
                        { RemoveVolumeResponse.Success = true
                          Message = "Volume supprimÃ©" }
            }

        member _.InspectVolume(request, _context) =
            task {
                ServiceGuards.requireVolumeId request.Id
                SecurityValidation.validateId request.Id "L'identifiant du volume"

                let tryInspect (driver: IVolumeDriver) =
                    try
                        driver.InspectVolume(request.Id)
                    with _ ->
                        None

                let volResult = registry.GetAll() |> List.tryPick (fun d -> tryInspect d)

                if volResult.IsNone then
                    raise (RpcException(Status(StatusCode.NotFound, sprintf "Volume '%s' introuvable" request.Id)))

                let info = volResult.Value

                let parseDict (elem: JsonElement) (key: string) =
                    let d = System.Collections.Generic.Dictionary<string, string>()
                    let mutable v = Unchecked.defaultof<JsonElement>

                    if elem.TryGetProperty(key, &v) then
                        for prop in v.EnumerateObject() do
                            d[prop.Name] <- prop.Value.GetString()

                    d

                try
                    let volInfo = parseVolumeInfo info

                    let createdAt =
                        let mutable v2 = Unchecked.defaultof<JsonElement>

                        if info.TryGetProperty("created_at", &v2) then
                            v2.GetString()
                        elif info.TryGetProperty("createdAt", &v2) then
                            v2.GetString()
                        else
                            ""

                    let driverOpts =
                        let d = parseDict info "driver_opts"
                        let d2 = parseDict info "driverOpts"

                        for kv in d2 do
                            d[kv.Key] <- kv.Value

                        d

                    let sizeBytes =
                        try
                            registry.GetAll()
                            |> List.tryPick (fun d ->
                                try
                                    Some(d.GetVolumeSize(request.Id))
                                with _ ->
                                    None)
                            |> Option.defaultValue 0L
                        with _ ->
                            0L

                    return
                        { InspectVolumeResponse.Id = volInfo.Id
                          Name = volInfo.Name
                          Driver = volInfo.Driver
                          Mountpoint = volInfo.Mountpoint
                          State = volInfo.State
                          Labels = volInfo.Labels
                          DriverOpts = driverOpts
                          SizeBytes = sizeBytes
                          CreatedAt = createdAt }
                with ex ->
                    Log.Warning(ex, "Erreur lors du parsing des informations du volume {VolumeId}", request.Id)

                    // Une erreur interne ne doit pas ressembler Ã  un volume vide :
                    // le client distinguerait mal Â« absent Â» et Â« en panne Â».
                    return
                        raise (
                            RpcException(
                                Status(StatusCode.Internal, "Erreur interne lors de l'inspection du volume")
                            )
                        )
            }

        member _.ListVolumes(request, _context) =
            task {
                let filters = request.Filters |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq

                let allVolumes =
                    registry.GetAll()
                    |> List.collect (fun d ->
                        try
                            d.ListVolumes(filters)
                        with
                        | :? RpcException -> reraise ()
                        | _ -> [])

                let response =
                    { ListVolumesResponse.Volumes = System.Collections.Generic.List<VolumeInfo>() }

                // M14 : borne de liste serveur (cf. ListContainers).
                let volumeCount = List.length allVolumes

                let bounded =
                    if volumeCount > ServiceGuards.MaxListItems then
                        Log.Warning(
                            "Liste des volumes tronquÃ©e Ã  {Limit} Ã©lÃ©ments (reÃ§u {Count})",
                            ServiceGuards.MaxListItems,
                            volumeCount
                        )

                        allVolumes |> List.truncate ServiceGuards.MaxListItems
                    else
                        allVolumes

                for vol in bounded do
                    try
                        response.Volumes.Add(parseVolumeInfo vol)
                    with ex ->
                        Log.Warning(ex, "Erreur lors du parsing du volume dans la liste")

                return response
            }

        member _.MountVolume(request, _context) =
            task {
                ServiceGuards.requireVolumeId request.Id
                ServiceGuards.requireTargetPath request.TargetPath
                SecurityValidation.validateId request.Id "L'identifiant du volume"
                SecurityValidation.validateVolumePath request.TargetPath "Le chemin cible"

                try
                    let options =
                        request.Options
                        |> Seq.map (fun kv -> kv.Key + "=" + kv.Value)
                        |> String.concat ";"

                    let tryMount (driver: IVolumeDriver) =
                        try
                            let (_, mountpoint) = driver.MountVolume(request.Id, request.TargetPath, options)
                            Some mountpoint
                        with
                        | :? RpcException -> reraise ()
                        | ex ->
                            Log.Warning(
                                ex,
                                "Le driver {Driver} n'a pas pu monter le volume {VolumeId}",
                                driver.GetType().Name,
                                request.Id
                            )

                            None

                    let result = registry.GetAll() |> List.tryPick tryMount

                    match result with
                    | Some mountpoint ->
                        return
                            { MountVolumeResponse.State = MountState.Mounted
                              Mountpoint = mountpoint
                              Message = "Volume montÃ©" }
                    | None ->
                        return
                            { MountVolumeResponse.State = MountState.Error
                              Mountpoint = ""
                              Message = "Volume introuvable ou erreur de montage" }
                with
                | :? RpcException as rpcEx ->
                    Log.Warning(rpcEx, "Erreur lors du montage du volume {VolumeId}", request.Id)
                    return raise rpcEx
                | ex ->
                    Log.Warning(ex, "Erreur lors du montage du volume {VolumeId}", request.Id)

                    return
                        { MountVolumeResponse.State = MountState.Error
                          Mountpoint = ""
                          Message = "Erreur interne lors du montage du volume" }
            }

        member _.UnmountVolume(request, _context) =
            task {
                ServiceGuards.requireVolumeId request.Id
                ServiceGuards.requireTargetPath request.TargetPath
                SecurityValidation.validateId request.Id "L'identifiant du volume"
                SecurityValidation.validateVolumePath request.TargetPath "Le chemin cible"

                try
                    let tryUnmount (driver: IVolumeDriver) =
                        try
                            let (_, message) = driver.UnmountVolume(request.Id, request.TargetPath)
                            Some message
                        with
                        | :? RpcException -> reraise ()
                        | ex ->
                            Log.Warning(
                                ex,
                                "Le driver {Driver} n'a pas pu dÃ©monter le volume {VolumeId}",
                                driver.GetType().Name,
                                request.Id
                            )

                            None

                    let result = registry.GetAll() |> List.tryPick tryUnmount

                    match result with
                    | Some message ->
                        return
                            { UnmountVolumeResponse.State = MountState.Unmounted
                              Message = message }
                    | None ->
                        return
                            { UnmountVolumeResponse.State = MountState.Unmounted
                              Message = "Démonté" }
                with
                | :? RpcException as rpcEx ->
                    Log.Warning(rpcEx, "Erreur lors du dÃ©montage du volume {VolumeId}", request.Id)
                    return raise rpcEx
                | ex ->
                    Log.Warning(ex, "Erreur lors du dÃ©montage du volume {VolumeId}", request.Id)

                    return
                        { UnmountVolumeResponse.State = MountState.Error
                          Message = "Erreur interne lors du dÃ©montage du volume" }
            }

        member _.PruneVolumes(request, _context) =
            task {
                let allRemoved =
                    registry.GetAll()
                    |> List.collect (fun d ->
                        try
                            d.PruneVolumes()
                        with
                        | :? RpcException -> reraise ()
                        | _ -> [])

                let count = allRemoved |> List.length

                return
                    { PruneVolumesResponse.VolumesDeleted = System.Collections.Generic.List<string>(allRemoved)
                      Count = count
                      Message = sprintf "%d volume(s) supprimÃ©(s)" count }
            }

