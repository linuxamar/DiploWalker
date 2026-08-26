namespace Diplo.Volume.Drivers

open System
open Grpc.Core
open Diplo.Abstractions
open Diplo.Abstractions.Interfaces

type CloudAzureDriver(dataRoot: string) =

    let store = RemoteVolumeStore(dataRoot, "azure")

    let buildAzureSharePath (driverOpts: Map<string, string>) =
        match driverOpts |> Map.tryFind "storageAccount", driverOpts |> Map.tryFind "shareName" with
        | Some account, Some share -> sprintf "\\\\%s.file.core.windows.net\\%s" account share
        | _ ->
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        "Les options 'storageAccount' et 'shareName' sont requises pour le driver Azure"
                    )
                )
            )

    let buildNetUseArgs (sharePath: string) (targetPath: string) (driverOpts: Map<string, string>) =
        let args = ResizeArray<string>()
        args.Add(targetPath)
        args.Add(sharePath)

        let hasKey = driverOpts |> Map.containsKey "storageKey"

        match driverOpts |> Map.tryFind "storageAccount", hasKey with
        | Some account, true ->
            args.Add("/user:AZURE\\" + account)
            // « * » fait lire le mot de passe sur l'entrée standard : la clé du
            // compte de stockage ne doit JAMAIS figurer dans argv, où elle est
            // visible par tout processus local (WMI Win32_Process, audits).
            args.Add("*")
        | _ ->
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        "Les options 'storageAccount' et 'storageKey' sont requises pour le montage Azure"
                    )
                )
            )

        args.Add("/persistent:no")
        args |> Seq.toList

    let mountAzure (sharePath: string) (targetPath: string) (opts: Map<string, string>) =
        let args = buildNetUseArgs sharePath targetPath opts

        // La clé transite par l'entrée standard, pas par la ligne de commande.
        let secret = opts |> Map.tryFind "storageKey"
        ProcessExec.runUnit "net" ("use" :: args) (Some ProcessExec.MountTimeoutMs) secret

    let unmountAzure (targetPath: string) =
        ProcessExec.runUnit "net" [ "use"; targetPath; "/delete"; "/y" ] (Some ProcessExec.MountTimeoutMs) None

    interface IVolumeDriver with
        member _.CreateVolume(name, driverOpts, labels) =
            let sharePath = buildAzureSharePath driverOpts
            store.CreateVolume(name, sharePath, labels, driverOpts)

        member _.RemoveVolume(id, _force) = store.RemoveVolume(id)
        member _.InspectVolume(id) = store.InspectVolume(id)
        member _.ListVolumes(_filters) = store.ListVolumes()
        member _.GetVolumeSize(_id) = 0L

        member _.MountVolume(id, targetPath, options) =
            RemoteDriverHelpers.mountVolume store id targetPath options mountAzure

        member _.UnmountVolume(id, targetPath) =
            RemoteDriverHelpers.unmountVolume id targetPath unmountAzure

        member _.PruneVolumes() =
            RemoteDriverHelpers.pruneCloudVolumes store
