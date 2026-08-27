namespace Diplo.Volume.Drivers

open System
open Grpc.Core
open Diplo.Abstractions
open Diplo.Abstractions.Interfaces

/// Options de montage utilisateur acceptées (allow-list) : ro, vers,
/// hard/soft, timeo. Les autres sont ignorées volontairement.
/// Module exposé pour la testabilité (construction d'arguments pure).
module NfsMountOptions =

    let allowed = set [ "ro"; "vers"; "hard"; "soft"; "timeo" ]

    /// Construit la chaîne d'options de montage : « nolock » toujours présent,
    /// suivi des options utilisateur autorisées (« clef » ou « clef=valeur »).
    let buildOptions (opts: Map<string, string>) =
        let userOpts =
            opts
            |> Map.toSeq
            |> Seq.choose (fun (k, v) ->
                if allowed.Contains(k.ToLowerInvariant()) then
                    if v = "" || v = "true" then
                        Some(k.ToLowerInvariant())
                    else
                        // Clé émise normalisée en minuscules : les options de
                        // montage NFS sont sensibles à la casse.
                        Some(sprintf "%s=%s" (k.ToLowerInvariant()) v)
                else
                    None)
            |> Seq.toList

        let all = "nolock" :: userOpts
        String.Join(",", all)

type NfsDriver(dataRoot: string) =

    let store = RemoteVolumeStore(dataRoot, "nfs")

    let extractRemotePath (driverOpts: Map<string, string>) =
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

    let mountNfs (remotePath: string) (targetPath: string) (opts: Map<string, string>) =
        let options = NfsMountOptions.buildOptions opts
        ProcessExec.runUnit "mount" [ "-o"; options; remotePath; targetPath ] (Some ProcessExec.MountTimeoutMs) None

    interface IVolumeDriver with
        member _.CreateVolume(name, driverOpts, labels) =
            let remotePath = extractRemotePath driverOpts
            store.CreateVolume(name, remotePath, labels, driverOpts)

        member _.RemoveVolume(id, _force) = store.RemoveVolume(id)
        member _.InspectVolume(id) = store.InspectVolume(id)
        member _.ListVolumes(_filters) = store.ListVolumes()
        member _.GetVolumeSize(_id) = 0L

        member _.MountVolume(id, targetPath, options) =
            RemoteDriverHelpers.mountVolume store id targetPath options mountNfs

        member _.UnmountVolume(id, targetPath) =
            RemoteDriverHelpers.unmountVolume id targetPath RemoteDriverHelpers.unmountNfsLike

        member _.PruneVolumes() = store.PruneAll()
