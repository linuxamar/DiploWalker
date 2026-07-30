namespace Diplo.Volume.Drivers

open System.Collections.Generic
open Diplo.Abstractions.Interfaces
open Diplo.Grpc.Volume

type VolumeDriverRegistry() =
    let drivers = Dictionary<StorageDriverType, IVolumeDriver>()

    member _.Register(driverType: StorageDriverType, driver: IVolumeDriver) =
        drivers.[driverType] <- driver

    member _.Get(driverType: StorageDriverType) =
        match drivers.TryGetValue(driverType) with
        | true, driver -> driver
        | false, _ -> failwithf "Aucun driver enregistré pour le type '%O'" driverType

    member _.GetAll() =
        drivers.Values |> Seq.distinct |> Seq.toList
