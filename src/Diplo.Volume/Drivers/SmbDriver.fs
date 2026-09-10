namespace Diplo.Volume.Drivers

open System
open Grpc.Core
open Diplo.Abstractions
open Diplo.Abstractions.Interfaces

type SmbDriver(dataRoot: string) =
    inherit RemoteVolumeDriver(dataRoot, "smb")

    override _.RemotePath driverOpts =
        match driverOpts |> Map.tryFind "server", driverOpts |> Map.tryFind "share" with
        | Some server, Some share -> sprintf "\\\\%s\\%s" server share
        | _ ->
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        "Les options 'server' et 'share' sont requises pour le driver SMB"
                    )
                )
            )

    override _.Mount remotePath targetPath opts =
        let user = opts |> Map.tryFind "username"
        let password = opts |> Map.tryFind "password"

        match user, password with
        | Some user, Some password ->
            // Le mot de passe est passé via stdin (pas en ligne de commande)
            // pour éviter l'exposition via WMI/Task Manager.
            ProcessExec.runUnit
                "net"
                [ "use"; targetPath; remotePath; "/user:" + user; "/persistent:no" ]
                (Some ProcessExec.MountTimeoutMs)
                (Some(password + "\n"))
                None
        | Some user, None ->
            ProcessExec.runUnit
                "net"
                [ "use"; targetPath; remotePath; "/user:" + user; "/persistent:no" ]
                (Some ProcessExec.MountTimeoutMs)
                None
                None
        | None, _ ->
            ProcessExec.runUnit
                "net"
                [ "use"; targetPath; remotePath; "/persistent:no" ]
                (Some ProcessExec.MountTimeoutMs)
                None
                None

    override _.Unmount targetPath =
        ProcessExec.runUnit "net" [ "use"; targetPath; "/delete"; "/y" ] (Some ProcessExec.MountTimeoutMs) None None
