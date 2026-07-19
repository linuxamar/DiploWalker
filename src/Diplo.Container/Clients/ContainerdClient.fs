namespace Diplo.Container.Clients

open System
open System.Text.Json
open Serilog
open Diplo.Abstractions.Interfaces

type ContainerdClient(runner: IProcessRunner) =

    let runCtr args =
        runner.Run("ctr", args)

    let parseJson (text: string) =
        use doc = JsonDocument.Parse(text)
        doc.RootElement.Clone()

    interface IContainerdClient with
        member _.CreateContainer(namespaceName, id, image, labels) =
            let labelArgs =
                labels
                |> Map.toList
                |> List.map (fun (k, v) -> sprintf "--label %s=%s" k v)
                |> String.concat " "
            let args = sprintf "container create --namespace %s %s %s %s" namespaceName id image labelArgs
            let output = runCtr args
            output.Trim()

        member _.StartContainer(namespaceName, id) =
            let args = sprintf "task start --namespace %s %s" namespaceName id
            runCtr args |> ignore

        member _.StopContainer(namespaceName, id, timeoutSeconds) =
            let args = sprintf "task kill --namespace %s --signal SIGTERM %s" namespaceName id
            runCtr args |> ignore
            if timeoutSeconds > 0 then
                System.Threading.Thread.Sleep(timeoutSeconds * 1000)

        member _.DeleteContainer(namespaceName, id, force) =
            if force then
                let killArgs = sprintf "task kill --namespace %s --signal SIGKILL %s" namespaceName id
                try runCtr killArgs |> ignore
                with ex -> Log.Warning(ex, "Erreur lors de l'arrêt forcé du conteneur {ContainerId}", id)
            let args = sprintf "container delete --namespace %s %s" namespaceName id
            runCtr args |> ignore

        member _.InspectContainer(namespaceName, id) =
            let args = sprintf "container info --namespace %s %s" namespaceName id
            let output = runCtr args
            parseJson output

        member _.ListContainers(namespaceName, all) =
            let args =
                if all then sprintf "container list --namespace %s --quiet" namespaceName
                else sprintf "container list --namespace %s --running --quiet" namespaceName
            let output = runCtr args
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
            |> Array.toList

        member _.GetContainerLogs(namespaceName, id, tail) =
            let args = sprintf "task logs --namespace %s --tail %d %s" namespaceName tail id
            try
                let output = runCtr args
                output.Split('\n')
                |> Array.toList
            with ex ->
                [ sprintf "Erreur lors de la récupération des logs: %s" ex.Message ]

        member _.ExecInContainer(namespaceName, id, command) =
            let cmdStr = command |> String.concat " "
            let args = sprintf "exec --namespace %s --exec-id exec-%s %s %s" namespaceName (Guid.NewGuid().ToString("N")) id cmdStr
            try
                let output = runCtr args
                output
            with ex ->
                sprintf "Erreur d'exécution: %s" ex.Message

        member _.TaskInfo(namespaceName, id) =
            try
                let args = sprintf "task info --namespace %s %s" namespaceName id
                let output = runCtr args
                parseJson output
            with ex ->
                Log.Warning(ex, "Erreur lors de la récupération des informations de tâche {ContainerId}", id)
                use doc = JsonDocument.Parse("{}")
                doc.RootElement.Clone()

        member _.PullImage(image) =
            let args = sprintf "image pull %s" image
            let output = runCtr args
            output.Trim()

        member _.Version() =
            try
                let output = runCtr "version"
                let json = parseJson output
                let version = json.GetProperty("Version").GetString()
                let revision = json.GetProperty("Revision").GetString()
                sprintf "%s (revision: %s)" version revision
            with ex ->
                sprintf "Erreur ctr: %s" ex.Message

        member _.Namespaces() =
            try
                let output = runCtr "namespace list --quiet"
                output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                |> Array.toList
            with ex ->
                [ sprintf "Erreur: %s" ex.Message ]
