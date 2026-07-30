namespace Diplo.Volume.Drivers

open System
open System.Diagnostics
open System.Text.Json
open Diplo.Abstractions
open Diplo.Abstractions.Interfaces

type SmbDriver(dataRoot: string) =

    let store = RemoteVolumeStore(dataRoot, "smb")

    let runProcess (fileName: string) (args: string list) =
        let psi = ProcessStartInfo()
        psi.FileName <- fileName
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- true
        for arg in args do psi.ArgumentList.Add(arg)
        use proc = Process.Start(psi)
        if proc |> isNull then failwithf "Impossible de démarrer '%s'" fileName
        let stdout = proc.StandardOutput.ReadToEnd()
        let stderr = proc.StandardError.ReadToEnd()
        if not (proc.WaitForExit(30_000)) then
            try proc.Kill(true) with _ -> ()
            failwithf "Délai d'attente dépassé pour '%s'" fileName
        if proc.ExitCode <> 0 then
            failwithf "La commande '%s' a échoué (code %d): %s" fileName proc.ExitCode stderr
        stdout

    let extractSharePath (driverOpts: Map<string, string>) =
        match driverOpts |> Map.tryFind "server", driverOpts |> Map.tryFind "share" with
        | Some server, Some share -> sprintf "\\\\%s\\%s" server share
        | _ -> failwith "Les options 'server' et 'share' sont requises pour le driver SMB"

    let buildNetUseArgs (sharePath: string) (targetPath: string) (driverOpts: Map<string, string>) =
        let args = ResizeArray<string>()
        args.Add(targetPath)
        args.Add(sharePath)
        match driverOpts |> Map.tryFind "username" with
        | Some user ->
            args.Add("/user:" + user)
            match driverOpts |> Map.tryFind "password" with
            | Some pass -> args.Add(pass)
            | None -> ()
        | None -> ()
        args.Add("/persistent:no")
        args |> Seq.toList

    interface IVolumeDriver with
        member _.CreateVolume(name, driverOpts, labels) =
            let sharePath = extractSharePath driverOpts
            let (id, mountpoint) = store.CreateVolume(name, sharePath, labels, driverOpts)
            (id, mountpoint)

        member _.RemoveVolume(id, _force) =
            store.RemoveVolume(id)

        member _.InspectVolume(id) =
            store.InspectVolume(id)

        member _.ListVolumes(_filters) =
            store.ListVolumes()

        member _.MountVolume(id, targetPath, options) =
            SecurityValidation.validateId id "L'identifiant du volume"
            SecurityValidation.validateVolumePath targetPath "Le chemin cible"
            match store.InspectVolume(id) with
            | None -> failwithf "Volume %s introuvable" id
            | Some info ->
                let mutable v = Unchecked.defaultof<JsonElement>
                let remotePath =
                    if info.TryGetProperty("remotePath", &v) then v.GetString()
                    else failwithf "Aucun chemin distant pour le volume %s" id
                let optsMap =
                    if String.IsNullOrEmpty(options) then Map.empty
                    else
                        options.Split(';')
                        |> Array.choose (fun part ->
                            let idx = part.IndexOf('=')
                            if idx > 0 then Some(part.Substring(0, idx), part.Substring(idx + 1))
                            else None)
                        |> Map.ofSeq
                let mergedOpts =
                    let mutable ov = Unchecked.defaultof<JsonElement>
                    if info.TryGetProperty("driverOpts", &ov) then
                        let mutable existing = optsMap
                        for prop in ov.EnumerateObject() do
                            if not (existing.ContainsKey(prop.Name)) then
                                existing <- existing |> Map.add prop.Name (prop.Value.GetString())
                        existing
                    else optsMap
                let args = buildNetUseArgs remotePath targetPath mergedOpts
                runProcess "net" ("use" :: args) |> ignore
                (true, targetPath)

        member _.UnmountVolume(id, targetPath) =
            SecurityValidation.validateId id "L'identifiant du volume"
            SecurityValidation.validateVolumePath targetPath "Le chemin cible"
            runProcess "net" [ "use"; targetPath; "/delete"; "/y" ] |> ignore
            (true, "Démonté")

        member _.PruneVolumes() =
            if not (store.ListVolumes().IsEmpty) then
                let removed = ResizeArray<string>()
                for vol in store.ListVolumes() do
                    let mutable v = Unchecked.defaultof<JsonElement>
                    let id = if vol.TryGetProperty("id", &v) then v.GetString() else null
                    if not (String.IsNullOrEmpty(id)) then
                        store.RemoveVolume(id) |> ignore
                        removed.Add(id)
                removed |> Seq.toList
            else []

        member _.GetVolumeSize(_id) = 0L
