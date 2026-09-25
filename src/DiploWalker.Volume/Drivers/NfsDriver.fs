namespace DiploWalker.Volume.Drivers

open System
open Grpc.Core
open DiploWalker.Abstractions
open DiploWalker.Abstractions.Interfaces

/// Options de montage utilisateur acceptÃ©es (allow-list) : ro, vers,
/// hard/soft, timeo. Les autres sont ignorÃ©es volontairement.
/// Module exposÃ© pour la testabilitÃ© (construction d'arguments pure).
module NfsMountOptions =

    let allowed = set [ "ro"; "vers"; "hard"; "soft"; "timeo" ]

    /// Construit la chaÃ®ne d'options de montage : Â« nolock Â» toujours prÃ©sent,
    /// suivi des options utilisateur autorisÃ©es (Â« clef Â» ou Â« clef=valeur Â»).
    let buildOptions (opts: Map<string, string>) =
        let userOpts =
            opts
            |> Map.toSeq
            |> Seq.choose (fun (k, v) ->
                if allowed.Contains(k.ToLowerInvariant()) then
                    if v = "" || v = "true" then
                        Some(k.ToLowerInvariant())
                    else
                        // ClÃ© Ã©mise normalisÃ©e en minuscules : les options de
                        // montage NFS sont sensibles Ã  la casse.
                        Some(sprintf "%s=%s" (k.ToLowerInvariant()) v)
                else
                    None)
            |> Seq.toList

        let all = "nolock" :: userOpts
        String.Join(",", all)

type NfsDriver(dataRoot: string) =
    inherit RemoteVolumeDriver(dataRoot, "nfs")

    override _.RemotePath driverOpts =
        match driverOpts |> Map.tryFind "server", driverOpts |> Map.tryFind "export" with
        | Some server, Some export -> sprintf "%s:/%s" server export
        | _ ->
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        "Les options 'server' et 'export' sont requises pour le driver NFS"
                    )
                )
            )

    override _.Mount remotePath targetPath opts =
        let options = NfsMountOptions.buildOptions opts
        ProcessExec.runUnit "mount" [ "-o"; options; remotePath; targetPath ] (Some ProcessExec.MountTimeoutMs) None None

