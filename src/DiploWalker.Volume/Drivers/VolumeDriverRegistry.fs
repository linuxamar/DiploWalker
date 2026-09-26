namespace DiploWalker.Volume.Drivers

open System.Collections.Generic
open Grpc.Core
open DiploWalker.Abstractions.Interfaces
open DiploWalker.Grpc.Volume

type VolumeDriverRegistry() =
    let drivers = Dictionary<StorageDriverType, IVolumeDriver>()

    member _.Register(driverType: StorageDriverType, driver: IVolumeDriver) = drivers.[driverType] <- driver

    member _.Get(driverType: StorageDriverType) =
        match drivers.TryGetValue(driverType) with
        | true, driver -> driver
        | false, _ ->
            raise (
                RpcException(
                    Status(StatusCode.NotFound, sprintf "Aucun driver enregistré pour le type '%O'" driverType)
                )
            )

    member _.GetAll() =
        drivers.Values |> Seq.distinct |> Seq.toList

