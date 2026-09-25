namespace DiploWalker.Volume.Drivers

open System
open Grpc.Core
open DiploWalker.Abstractions
open DiploWalker.Abstractions.Interfaces

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

    // `-o nolock` : dÃ©sactivation du verrouillage des fichiers sur le partage NFS
    // distant (les charges clientes lÃ©gÃ¨res en pÃ¢tissent souvent). Choix dÃ©libÃ©rÃ©
    // maintenu malgrÃ© la revue M3 â€” le trafic se fait via le VPN utilisateur et le
    // driver parent applique dÃ©jÃ  les ACL du processus ; aucun changement de
    // comportement n'a Ã©tÃ© retenu.
    override _.Mount remotePath targetPath _opts =
        ProcessExec.runUnit "mount" [ "-o"; "nolock"; remotePath; targetPath ] (Some ProcessExec.MountTimeoutMs) None None

