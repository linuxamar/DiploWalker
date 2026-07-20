namespace Diplo.Container.Clients

open System
open System.Text.Json
open Serilog
open Diplo.Abstractions
open Diplo.Abstractions.Interfaces

type ContainerdClient(runner: IProcessRunner) =

    let runCtr (args: string list) =
        runner.RunWithArgs("ctr", args)

    let parseJson (text: string) =
        use doc = JsonDocument.Parse(text)
        doc.RootElement.Clone()

    interface IContainerdClient with
        member _.CreateContainer(namespaceName, id, image, labels) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            SecurityValidation.validateImage image
            let mutable args =
                [ "container"; "create"; "--namespace"; namespaceName; id; image ]
            for (k, v) in labels |> Map.toList do
                SecurityValidation.validateLabel k v
                args <- args @ [ "--label"; sprintf "%s=%s" k v ]
            let output = runCtr args
            output.Trim()

        member _.StartContainer(namespaceName, id) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            runCtr [ "task"; "start"; "--namespace"; namespaceName; id ] |> ignore

        member _.StopContainer(namespaceName, id, timeoutSeconds) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            runCtr [ "task"; "kill"; "--namespace"; namespaceName; "--signal"; "SIGTERM"; id ] |> ignore
            if timeoutSeconds > 0 then
                System.Threading.Thread.Sleep(timeoutSeconds * 1000)

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

        member _.GetContainerLogs(namespaceName, id, tail) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            let output = runCtr [ "task"; "logs"; "--namespace"; namespaceName; "--tail"; tail.ToString(); id ]
            try
                output.Split('\n')
                |> Array.toList
            with ex ->
                [ sprintf "Erreur lors de la récupération des logs: %s" ex.Message ]

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
                sprintf "Erreur d'exécution: %s" ex.Message

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
                sprintf "Erreur ctr: %s" ex.Message

        member _.Namespaces() =
            try
                let output = runCtr [ "namespace"; "list"; "--quiet" ]
                output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                |> Array.toList
            with ex ->
                [ sprintf "Erreur: %s" ex.Message ]
