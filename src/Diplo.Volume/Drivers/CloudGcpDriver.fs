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

    // `-o nolock` : désactivation du verrouillage des fichiers sur le partage NFS
    // distant (les charges clientes légères en pâtissent souvent). Choix délibéré
    // maintenu malgré la revue M3 — le trafic se fait via le VPN utilisateur et le
    // driver parent applique déjà les ACL du processus ; aucun changement de
    // comportement n'a été retenu.
    override _.Mount remotePath targetPath _opts =
        ProcessExec.runUnit "mount" [ "-o"; "nolock"; remotePath; targetPath ] (Some ProcessExec.MountTimeoutMs) None None
