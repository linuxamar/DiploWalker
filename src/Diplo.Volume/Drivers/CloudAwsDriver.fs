namespace Diplo.Volume.Drivers

open System
open Grpc.Core
open Diplo.Abstractions
open Diplo.Abstractions.Interfaces

type CloudAwsDriver(dataRoot: string) =
    inherit RemoteVolumeDriver(dataRoot, "aws")

    override _.RemotePath driverOpts =
        match driverOpts |> Map.tryFind "fsId", driverOpts |> Map.tryFind "region" with
        | Some fsId, Some region -> sprintf "%s.efs.%s.amazonaws.com:/" fsId region
        | _ ->
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        "Les options 'fsId' et 'region' sont requises pour le driver AWS"
                    )
                )
            )

    override _.Mount remotePath targetPath _opts =
        // Chiffrement en transit (recommandation AWS pour EFS) : l'option tls
        // du helper de montage EFS tunnelise via stunnel. Sans elle, les
        // données traversent le réseau en clair.
        ProcessExec.runUnit
            "mount"
            [ "-o"; "nfsvers=4.1,tls"; remotePath; targetPath ]
            (Some ProcessExec.MountTimeoutMs)
            None
            None
