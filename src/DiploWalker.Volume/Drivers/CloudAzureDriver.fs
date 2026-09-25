namespace DiploWalker.Volume.Drivers

open System
open Grpc.Core
open DiploWalker.Abstractions
open DiploWalker.Abstractions.Interfaces

/// Construction des arguments de Â« net use Â» pour Azure Files. La clÃ© du
/// compte de stockage ne doit JAMAIS figurer dans argv, oÃ¹ elle est visible
/// par tout processus local (WMI Win32_Process, audits) : le Â« * Â» force sa
/// lecture sur l'entrÃ©e standard. Module exposÃ© pour la testabilitÃ©.
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

        // La clÃ© transite par l'entrÃ©e standard, pas par la ligne de commande.
        let secret = opts |> Map.tryFind "storageKey"
        ProcessExec.runUnit "net" ("use" :: args) (Some ProcessExec.MountTimeoutMs) secret None

    override _.Unmount targetPath =
        ProcessExec.runUnit "net" [ "use"; targetPath; "/delete"; "/y" ] (Some ProcessExec.MountTimeoutMs) None None

