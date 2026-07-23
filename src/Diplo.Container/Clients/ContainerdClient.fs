namespace Diplo.Container.Clients

open System
open System.IO
open System.Text.Json
open Serilog
open Diplo.Abstractions
open Diplo.Abstractions.Interfaces

type ContainerdClient(runner: IProcessRunner) =

    let runCtr (args: string list) =
        runner.RunWithArgs("ctr", args)

    let parseJson (text: string) =
        use doc = JsonDocument.Parse(text, JsonDocumentOptions(MaxDepth = 32))
        doc.RootElement.Clone()

    let buildOciSpecJson (env: Map<string, string>) (command: string array) (args: string array) (memoryLimit: int64) (cpuShares: int64) (pidLimit: uint32) =
        let envArray = env |> Map.toList |> List.map (fun (k, v) -> sprintf "%s=%s" k v)
        let processObj =
            let argsValue =
                if command.Length > 0 || args.Length > 0 then
                    let allArgs = Array.append command args
                    Some allArgs
                else None
            match argsValue with
            | Some a ->
                sprintf """{"args":["%s"],"env":["%s"]}"""
                    (a |> String.concat "\",\"")
                    (envArray |> String.concat "\",\"")
            | None ->
                if envArray.IsEmpty then "{}"
                else sprintf """{"env":["%s"]}""" (envArray |> String.concat "\",\"")
        let resourcesParts = ResizeArray<string>()
        if memoryLimit > 0L then
            resourcesParts.Add(sprintf """{"memory":{"limit":%d}}""" memoryLimit)
        if cpuShares > 0L then
            resourcesParts.Add(sprintf """{"cpu":{"shares":%d}}""" cpuShares)
        if pidLimit > 0u then
            resourcesParts.Add(sprintf """{"pids":{"limit":%d}}""" pidLimit)
        if resourcesParts.Count > 0 then
            let linuxResources = resourcesParts |> String.concat ","
            sprintf """{"process":%s,"linux":{"resources":{%s}}}""" processObj linuxResources
        else
            sprintf """{"process":%s}""" processObj

    interface IContainerdClient with
        member _.CreateContainer(namespaceName, id, image, labels, env, command, args, memoryLimit, cpuShares, pidLimit) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            SecurityValidation.validateImage image
            let mutable ctrArgs =
                [ "container"; "create"; "--namespace"; namespaceName ]
            for (k, v) in labels |> Map.toList do
                SecurityValidation.validateLabel k v
                ctrArgs <- ctrArgs @ [ "--label"; sprintf "%s=%s" k v ]
            for (k, v) in env |> Map.toList do
                ctrArgs <- ctrArgs @ [ "--env"; sprintf "%s=%s" k v ]
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
                | Some p -> ctrArgs <- ctrArgs @ [ "--spec"; p ]
                | None -> ()
                ctrArgs <- ctrArgs @ [ id; image ]
                let output = runCtr ctrArgs
                output.Trim()
            finally
                match specPath with
                | Some p -> try File.Delete(p) with _ -> ()
                | None -> ()

        member _.StartContainer(namespaceName, id) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            runCtr [ "task"; "start"; "--namespace"; namespaceName; id ] |> ignore

        member _.StopContainer(namespaceName, id, timeoutSeconds) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            runCtr [ "task"; "kill"; "--namespace"; namespaceName; "--signal"; "SIGTERM"; id ] |> ignore
            if timeoutSeconds > 0 then
                let capped = min timeoutSeconds 300
                System.Threading.Thread.Sleep(capped * 1000)

        member _.DeleteContainer(namespaceName, id, force) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            if force then
                try runCtr [ "task"; "kill"; "--namespace"; namespaceName; "--signal"; "SIGKILL"; id ] |> ignore
                with ex -> Log.Warning(ex, "Erreur lors de l'arrêt forcé du conteneur {ContainerId}", id)
            runCtr [ "container"; "delete"; "--namespace"; namespaceName; id ] |> ignore

        member _.InspectContainer(namespaceName, id) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            let output = runCtr [ "container"; "info"; "--namespace"; namespaceName; id ]
            parseJson output

        member _.ListContainers(namespaceName, all) =
            SecurityValidation.validateId namespaceName "Le namespace"
            let args =
                if all then [ "container"; "list"; "--namespace"; namespaceName; "--quiet" ]
                else [ "container"; "list"; "--namespace"; namespaceName; "--running"; "--quiet" ]
            let output = runCtr args
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
            |> Array.toList

        member _.GetContainerLogs(namespaceName, id, tail, follow, since) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            let mutable logArgs =
                [ "task"; "logs"; "--namespace"; namespaceName; "--tail"; tail.ToString() ]
            if follow then
                logArgs <- logArgs @ [ "--follow" ]
            if not (String.IsNullOrEmpty(since)) then
                logArgs <- logArgs @ [ "--since"; since ]
            logArgs <- logArgs @ [ id ]
            try
                let output = runCtr logArgs
                output.Split('\n')
                |> Array.toList
            with ex ->
                Log.Error(ex, "Erreur lors de la récupération des logs du conteneur {ContainerId}", id)
                [ "Erreur lors de la récupération des logs" ]

        member _.ExecInContainer(namespaceName, id, command) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            SecurityValidation.validateCommand command
            let args =
                [ "exec"; "--namespace"; namespaceName; "--exec-id"; sprintf "exec-%s" (Guid.NewGuid().ToString("N")); id ]
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
                let output = runCtr [ "task"; "info"; "--namespace"; namespaceName; id ]
                parseJson output
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
                let json = parseJson output
                let version = json.GetProperty("Version").GetString()
                let revision = json.GetProperty("Revision").GetString()
                sprintf "%s (revision: %s)" version revision
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
