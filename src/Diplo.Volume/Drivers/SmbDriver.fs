namespace Diplo.Volume.Drivers

open System
open Grpc.Core
open Diplo.Abstractions
open Diplo.Abstractions.Interfaces

type SmbDriver(dataRoot: string) =

    let store = RemoteVolumeStore(dataRoot, "smb")

    let extractSharePath (driverOpts: Map<string, string>) =
        match driverOpts |> Map.tryFind "server", driverOpts |> Map.tryFind "share" with
        | Some server, Some share -> sprintf "\\\\%s\\%s" server share
        | _ -> raise (RpcException(Status(StatusCode.InvalidArgument, "Les options 'server' et 'share' sont requises pour le driver SMB")))

    let mountShare (remotePath: string) (targetPath: string) (opts: Map<string, string>) =
        let user = opts |> Map.tryFind "username"
        let password = opts |> Map.tryFind "password"
        match user, password with
        | Some user, Some password ->
            let server = remotePath.TrimStart('\\') |> fun p -> p.Split('\\').[0]
            try
                ProcessExec.runUnit "cmdkey" [ "/add:" + server; "/user:" + user; "/pass:" + password ] (Some 30_000) None
                try
                    ProcessExec.runUnit "net" [ "use"; targetPath; remotePath; "/user:" + user; "/persistent:no" ] (Some 30_000) None
                finally
                    try ProcessExec.runUnit "cmdkey" [ "/delete:" + server ] (Some 30_000) None
                    with _ -> ()
            with _ -> reraise ()
        | Some user, None ->
            ProcessExec.runUnit "net" [ "use"; targetPath; remotePath; "/user:" + user; "/persistent:no" ] (Some 30_000) None
        | None, _ ->
            ProcessExec.runUnit "net" [ "use"; targetPath; remotePath; "/persistent:no" ] (Some 30_000) None

    let unmountSmb (targetPath: string) =
        ProcessExec.runUnit "net" [ "use"; targetPath; "/delete"; "/y" ] (Some 30_000) None

    interface IVolumeDriver with
        member _.CreateVolume(name, driverOpts, labels) =
            let sharePath = extractSharePath driverOpts
            store.CreateVolume(name, sharePath, labels, driverOpts)

        member _.RemoveVolume(id, _force) = store.RemoveVolume(id)
        member _.InspectVolume(id) = store.InspectVolume(id)
        member _.ListVolumes(_filters) = store.ListVolumes()
        member _.GetVolumeSize(_id) = 0L

        member _.MountVolume(id, targetPath, options) =
            RemoteDriverHelpers.mountVolume store id targetPath options mountShare

        member _.UnmountVolume(id, targetPath) =
            RemoteDriverHelpers.unmountVolume id targetPath unmountSmb

        member _.PruneVolumes() = store.PruneAll()
