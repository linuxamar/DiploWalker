namespace Diplo.Container.Clients

open System
open System.IO
open System.Text.Json
open System.Threading.Tasks
open Serilog
open Diplo.Abstractions
open Diplo.Abstractions.Interfaces

[<Struct>]
type private OciProcess = { args: string array option; env: string array }

[<Struct>]
type private OciMemoryLimit = { limit: int64 }

[<Struct>]
type private OciCpuShares = { shares: int64 }

[<Struct>]
type private OciPidsLimit = { limit: int }

[<Struct>]
type private OciResourceEntry =
    { memory: OciMemoryLimit option
      cpu: OciCpuShares option
      pids: OciPidsLimit option }

[<Struct>]
type private OciLinux = { resources: OciResourceEntry }

[<Struct>]
type private OciSpec =
    { ``process``: string
      linux: OciLinux }

type ContainerdClient(runner: IProcessRunner) =

    let runCtr (args: string list) =
        runner.RunWithArgs("ctr", args)

    let nsArgs (namespaceName: string) (subcommand: string list) =
        "--namespace" :: namespaceName :: subcommand

    let parseJson (text: string) =
        use doc = JsonDocument.Parse(text, JsonDocumentOptions(MaxDepth = 32))
        doc.RootElement.Clone()

    let buildOciSpecJson (env: Map<string, string>) (command: string array) (args: string array) (memoryLimit: int64) (cpuShares: int64) (pidLimit: uint32) =
        let envArray = env |> Map.toList |> List.map (fun (k, v) -> sprintf "%s=%s" k v)
        let processObj =
            let allArgs = if command.Length > 0 || args.Length > 0 then Array.append command args |> Some else None
            let envList = if envArray.IsEmpty then null else envArray |> List.toArray
            let proc: OciProcess = { args = allArgs; env = envList }
            JsonSerializer.Serialize(proc)
        let resources =
            { memory = if memoryLimit > 0L then Some { limit = memoryLimit } else None
              cpu = if cpuShares > 0L then Some { shares = cpuShares } else None
              pids = if pidLimit > 0u then Some { limit = int pidLimit } else None }
        let linux: OciLinux = { resources = resources }
        let spec: OciSpec = { ``process`` = processObj; linux = linux }
        JsonSerializer.Serialize(spec)

    interface IContainerdClient with
        member _.CreateContainer(namespaceName, id, image, labels, env, command, args, memoryLimit, cpuShares, pidLimit, mounts) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            SecurityValidation.validateImage image
            let ctrArgs = ResizeArray()
            ctrArgs.AddRange(nsArgs namespaceName [ "container"; "create" ])
            for (k, v) in labels |> Map.toList do
                SecurityValidation.validateLabel k v
                ctrArgs.Add("--label")
                ctrArgs.Add(sprintf "%s=%s" k v)
            for (k, v) in env |> Map.toList do
                ctrArgs.Add("--env")
                ctrArgs.Add(sprintf "%s=%s" k v)
            for (src, dst, readOnly) in mounts do
                SecurityValidation.validateVolumePath src "La source du volume"
                if String.IsNullOrEmpty(dst) then
                    invalidArg "dst" "La destination du montage ne peut pas être vide"
                if dst.Contains("..") then
                    invalidArg "dst" (sprintf "La destination du montage contient une traversée de répertoire interdite: '%s'" dst)
                ctrArgs.Add("--mount")
                let options = if readOnly then "rbind,ro" else "rbind"
                ctrArgs.Add(sprintf "type=bind,src=%s,dst=%s,options=%s" src dst options)
            let hasSpecContent =
                command.Length > 0 || args.Length > 0 ||
                memoryLimit > 0L || cpuShares > 0L || pidLimit > 0u
            let specPath =
                if hasSpecContent then
                    let json = buildOciSpecJson env command args memoryLimit cpuShares pidLimit
                    let tempFile = Path.Combine(Path.GetTempPath(), sprintf "diplo-spec-%s.json" (Guid.NewGuid().ToString("N")))
                    File.WriteAllText(tempFile, json)
                    Some tempFile
                else None
            try
                match specPath with
                | Some p -> ctrArgs.Add("--spec"); ctrArgs.Add(p)
                | None -> ()
                ctrArgs.Add(image)
                ctrArgs.Add(id)
                let output = runCtr (ctrArgs |> Seq.toList)
                let trimmed = output.Trim()
                if String.IsNullOrEmpty(trimmed) then id else trimmed
            finally
                match specPath with
                | Some p -> try File.Delete(p) with _ -> ()
                | None -> ()

        member _.StartContainer(namespaceName, id) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            runCtr (nsArgs namespaceName [ "task"; "start"; id ]) |> ignore

        member _.StopContainer(namespaceName, id, timeoutSeconds) : Task =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            runCtr (nsArgs namespaceName [ "task"; "kill"; "--signal"; "SIGTERM"; id ]) |> ignore
            task {
                if timeoutSeconds > 0 then
                    let capped = min timeoutSeconds 300
                    do! Task.Delay(capped * 1000)
            } :> Task

        member _.DeleteContainer(namespaceName, id, force) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            if force then
                try runCtr (nsArgs namespaceName [ "task"; "kill"; "--signal"; "SIGKILL"; id ]) |> ignore
                with ex -> Log.Warning(ex, "Erreur lors de l'arrêt forcé du conteneur {ContainerId}", id)
            runCtr (nsArgs namespaceName [ "container"; "delete"; id ]) |> ignore

        member _.InspectContainer(namespaceName, id) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            let output = runCtr (nsArgs namespaceName [ "container"; "info"; id ])
            parseJson output

        member _.ListContainers(namespaceName, all) =
            SecurityValidation.validateId namespaceName "Le namespace"
            let ids =
                runCtr (nsArgs namespaceName [ "container"; "list"; "--quiet" ])
                |> fun output -> output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                |> Array.toList
            if all then ids
            else
                let running =
                    runCtr (nsArgs namespaceName [ "tasks"; "list" ])
                    |> fun output ->
                        output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                        |> Array.choose (fun line ->
                            let parts = line.Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
                            if parts.Length >= 3 && parts[2].Equals("RUNNING", StringComparison.OrdinalIgnoreCase) then Some parts[0]
                            else None)
                        |> Set.ofArray
                ids |> List.filter running.Contains

        member _.GetContainerLogs(namespaceName, id, tail, follow, since) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            Log.Warning("La commande 'ctr tasks logs' a été supprimée dans containerd v2 : les logs ne sont pas récupérables via ctr (Conteneur {ContainerId})", id)
            [ "Logs non disponibles : 'ctr tasks logs' a été supprimé dans containerd v2" ]

        member _.ExecInContainer(namespaceName, id, command) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            SecurityValidation.validateCommand command
            let args =
                [ yield "--namespace"; yield namespaceName
                  yield "tasks"; yield "exec"; yield "--exec-id"; yield sprintf "exec-%s" (Guid.NewGuid().ToString("N")); yield id ]
                @ (command |> Array.toList)
            try
                let output = runCtr args
                output
            with ex ->
                Log.Error(ex, "Erreur lors de l'exécution dans le conteneur {ContainerId}", id)
                "Erreur d'exécution dans le conteneur"

        member _.TaskInfo(namespaceName, id) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            try
                let output = runCtr (nsArgs namespaceName [ "tasks"; "list" ])
                let row =
                    output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                    |> Array.tryFind (fun line ->
                        let parts = line.Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
                        parts.Length >= 1 && parts[0].Equals(id, StringComparison.OrdinalIgnoreCase))
                match row with
                | None ->
                    use doc = JsonDocument.Parse("{}")
                    doc.RootElement.Clone()
                | Some line ->
                    let parts = line.Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
                    let pid = if parts.Length >= 2 && parts[1] <> "-" then parts[1] else "0"
                    let status = if parts.Length >= 3 then parts[2] else "unknown"
                    let json = sprintf """{"status":"%s","pid":%s,"exited_at":""}""" status pid
                    use doc = JsonDocument.Parse(json)
                    doc.RootElement.Clone()
            with ex ->
                Log.Warning(ex, "Erreur lors de la récupération des informations de tâche {ContainerId}", id)
                use doc = JsonDocument.Parse("{}")
                doc.RootElement.Clone()

        member _.PullImage(image) =
            SecurityValidation.validateImage image
            let output = runCtr [ "image"; "pull"; image ]
            output.Trim()

        member _.Version() =
            try
                let output = runCtr [ "version" ]
                let lines =
                    output.Split('\n')
                    |> Array.map (fun line -> line.Trim())
                let valueOf (prefix: string) =
                    lines
                    |> Array.tryPick (fun line ->
                        if line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) then
                            Some(line.Substring(prefix.Length).Trim())
                        else None)
                match valueOf "Version:" with
                | Some version ->
                    let revision = defaultArg (valueOf "Revision:") ""
                    sprintf "%s (revision: %s)" version revision
                | None -> "Version inconnue"
            with ex ->
                Log.Error(ex, "Erreur lors de la récupération de la version ctr")
                "Version inconnue"

        member _.Namespaces() =
            try
                let output = runCtr [ "namespace"; "list"; "--quiet" ]
                output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                |> Array.toList
            with ex ->
                Log.Error(ex, "Erreur lors de la récupération des namespaces")
                [ "Erreur lors de la récupération des namespaces" ]

        member _.RenameContainer(namespaceName, id, newName) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            SecurityValidation.validateName newName "Le nouveau nom"
            runCtr (nsArgs namespaceName [ "container"; "rename"; id; newName ]) |> ignore

        member _.TopContainer(namespaceName, id) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            try
                let output = runCtr (nsArgs namespaceName [ "task"; "ps"; id ])
                output
            with ex ->
                Log.Error(ex, "Erreur lors de la récupération des processus du conteneur {ContainerId}", id)
                "Erreur lors de la récupération des processus"

        member _.GetContainerStats(namespaceName, id) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            try
                let output = runCtr (nsArgs namespaceName [ "task"; "metrics"; id ])
                parseJson output
            with ex ->
                Log.Error(ex, "Erreur lors de la récupération des métriques du conteneur {ContainerId}", id)
                use doc = JsonDocument.Parse("{}")
                doc.RootElement.Clone()

        member _.ListImages(namespaceName) =
            SecurityValidation.validateId namespaceName "Le namespace"
            try
                let output = runCtr (nsArgs namespaceName [ "image"; "list" ])
                let lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                lines
                |> Array.map (fun line ->
                    let parts = line.Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
                    let json =
                        if parts.Length >= 2 then
                            sprintf """{"id":"%s","repository":"%s","ref":"%s"}""" parts[0] parts[0] parts[0]
                        else
                            sprintf """{"id":"%s","repository":"%s","ref":"%s"}""" line line line
                    parseJson json)
                |> Array.toList
            with ex ->
                Log.Error(ex, "Erreur lors de la récupération des images")
                []

        member _.InspectImage(namespaceName, imageRef) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateImage imageRef
            try
                let output = runCtr (nsArgs namespaceName [ "image"; "info"; imageRef ])
                parseJson output
            with ex ->
                Log.Error(ex, "Erreur lors de l'inspection de l'image {ImageRef}", imageRef)
                use doc = JsonDocument.Parse("{}")
                doc.RootElement.Clone()

        member _.RemoveImage(namespaceName, imageRef) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateImage imageRef
            try
                runCtr (nsArgs namespaceName [ "image"; "remove"; imageRef ]) |> ignore
                sprintf "Image %s supprimée" imageRef
            with ex ->
                Log.Error(ex, "Erreur lors de la suppression de l'image {ImageRef}", imageRef)
                sprintf "Erreur lors de la suppression de l'image %s" imageRef

        member _.TagImage(namespaceName, source, target) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateImage source
            SecurityValidation.validateImage target
            runCtr (nsArgs namespaceName [ "image"; "tag"; source; target ]) |> ignore
