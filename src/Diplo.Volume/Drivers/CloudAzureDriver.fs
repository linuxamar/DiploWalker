namespace Diplo.Volume.Drivers

open System
open Grpc.Core
open Diplo.Abstractions
open Diplo.Abstractions.Interfaces

/// Construction des arguments de « net use » pour Azure Files. La clé du
/// compte de stockage ne doit JAMAIS figurer dans argv, où elle est visible
/// par tout processus local (WMI Win32_Process, audits) : le « * » force sa
/// lecture sur l'entrée standard. Module exposé pour la testabilité.
module AzureNetUse =

    let buildArgs (sharePath: string) (targetPath: string) (driverOpts: Map<string, string>) =
        let args = ResizeArray<string>()
        args.Add(targetPath)
        args.Add(sharePath)

        let hasKey = driverOpts |> Map.containsKey "storageKey"

        match driverOpts |> Map.tryFind "storageAccount", hasKey with
        | Some account, true ->
            args.Add("/user:AZURE\\" + account)
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

type CloudAzureDriver(dataRoot: string) =
    inherit RemoteVolumeDriver(dataRoot, "azure")

    override _.RemotePath driverOpts =
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

    override _.Mount sharePath targetPath opts =
        let args = AzureNetUse.buildArgs sharePath targetPath opts

        // La clé transite par l'entrée standard, pas par la ligne de commande.
        let secret = opts |> Map.tryFind "storageKey"
        ProcessExec.runUnit "net" ("use" :: args) (Some ProcessExec.MountTimeoutMs) secret

    override _.Unmount targetPath =
        ProcessExec.runUnit "net" [ "use"; targetPath; "/delete"; "/y" ] (Some ProcessExec.MountTimeoutMs) None
