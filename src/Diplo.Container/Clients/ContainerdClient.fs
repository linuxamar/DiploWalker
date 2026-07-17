namespace Diplo.Container.Clients

open System
open System.Diagnostics
open System.Text.Json
open Serilog
open Diplo.Abstractions.Interfaces

type ContainerdClient(containerdSocket: string) =

    let runCtr args =
        let psi = ProcessStartInfo()
        psi.FileName <- "ctr"
        psi.Arguments <- args
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- true
        let proc = Process.Start(psi)
        let stdout = proc.StandardOutput.ReadToEnd()
        let stderr = proc.StandardError.ReadToEnd()
        proc.WaitForExit()
        if proc.ExitCode <> 0 then
            failwithf "ctr a échoué (code %d): %s" proc.ExitCode stderr
        stdout

    let parseJson (text: string) =
        use doc = JsonDocument.Parse(text)
        doc.RootElement

    interface IContainerdClient with
        member _.GetVersion() =
            try
                let output = runCtr "version"
                let json = parseJson output
                let version = json.GetProperty("Version").GetString()
                let revision = json.GetProperty("Revision").GetString()
                sprintf "%s (revision: %s)" version revision
            with ex ->
                sprintf "Erreur ctr: %s" ex.Message

        member _.ListNamespaces() =
            try
                let output = runCtr "namespace list --quiet"
                output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                |> Array.toList
            with ex ->
                [ sprintf "Erreur: %s" ex.Message ]

    member _.CreateContainer(namespaceName: string, id: string, image: string, labels: Map<string, string>) =
        let labelArgs =
            labels
            |> Map.toList
            |> List.map (fun (k, v) -> sprintf "--label %s=%s" k v)
            |> String.concat " "
        let args = sprintf "container create %s --namespace %s %s %s" namespaceName id image labelArgs
        let output = runCtr args
        output.Trim()

    member _.StartContainer(namespaceName: string, id: string) =
        let args = sprintf "task start --namespace %s %s" namespaceName id
        runCtr args |> ignore

    member _.StopContainer(namespaceName: string, id: string, timeoutSeconds: int) =
        let args = sprintf "task kill --namespace %s --signal SIGTERM %s" namespaceName id
        runCtr args |> ignore
        if timeoutSeconds > 0 then
            System.Threading.Thread.Sleep(timeoutSeconds * 1000)

    member _.DeleteContainer(namespaceName: string, id: string, force: bool) =
        if force then
            let killArgs = sprintf "task kill --namespace %s --signal SIGKILL %s" namespaceName id
            try runCtr killArgs |> ignore
            with ex -> Log.Warning(ex, "Erreur lors de l'arrêt forcé du conteneur {ContainerId}", id)
        let args = sprintf "container delete --namespace %s %s" namespaceName id
        runCtr args |> ignore

    member _.InspectContainer(namespaceName: string, id: string) =
        let args = sprintf "container info --namespace %s %s" namespaceName id
        let output = runCtr args
        parseJson output

    member _.ListContainers(namespaceName: string, all: bool) =
        let args =
            if all then sprintf "container list --namespace %s --quiet" namespaceName
            else sprintf "container list --namespace %s --running --quiet" namespaceName
        let output = runCtr args
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
        |> Array.toList

    member _.GetContainerLogs(namespaceName: string, id: string, tail: int) =
        let args = sprintf "task logs --namespace %s --tail %d %s" namespaceName tail id
        try
            let output = runCtr args
            output.Split('\n')
            |> Array.toList
        with ex ->
            [ sprintf "Erreur lors de la récupération des logs: %s" ex.Message ]

    member _.ExecInContainer(namespaceName: string, id: string, command: string array) =
        let cmdStr = command |> String.concat " "
        let args = sprintf "exec --namespace %s --exec-id exec-%s %s %s" namespaceName (Guid.NewGuid().ToString("N")) id cmdStr
        try
            let output = runCtr args
            output
        with ex ->
            sprintf "Erreur d'exécution: %s" ex.Message

    member _.TaskInfo(namespaceName: string, id: string) =
        try
            let args = sprintf "task info --namespace %s %s" namespaceName id
            let output = runCtr args
            parseJson output
        with ex ->
            Log.Warning(ex, "Erreur lors de la récupération des informations de tâche {ContainerId}", id)
            use doc = JsonDocument.Parse("{}")
            doc.RootElement

    member _.Version() =
        let output = runCtr "version"
        parseJson output

    member _.Namespaces() =
        let output = runCtr "namespace list --quiet"
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
        |> Array.toList
