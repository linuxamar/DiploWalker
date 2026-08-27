namespace Diplo.Volume.Drivers

open System
open Grpc.Core
open Diplo.Abstractions
open Diplo.Abstractions.Interfaces

type CloudGcpDriver(dataRoot: string) =
    inherit RemoteVolumeDriver(dataRoot, "gcp")

    override _.RemotePath driverOpts =
        match driverOpts |> Map.tryFind "ipAddress", driverOpts |> Map.tryFind "volumeName" with
        | Some ip, Some volName -> sprintf "%s:/%s" ip volName
        | _ ->
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        "Les options 'ipAddress' et 'volumeName' sont requises pour le driver GCP"
                    )
                )
            )

    override _.Mount remotePath targetPath _opts =
        ProcessExec.runUnit "mount" [ "-o"; "nolock"; remotePath; targetPath ] (Some ProcessExec.MountTimeoutMs) None
