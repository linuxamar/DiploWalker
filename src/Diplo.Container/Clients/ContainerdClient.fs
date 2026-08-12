namespace Diplo.Container.Clients

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Serilog
open Diplo.Abstractions
open Diplo.Abstractions.Interfaces
open Diplo.Container

type ContainerdClient(runner: IProcessRunner, ?logPollIntervalMs: int) =

    let logPollIntervalMs = defaultArg logPollIntervalMs 500

    let runCtr (args: string list) =
        runner.RunWithArgs("ctr", args)

    let nsArgs (namespaceName: string) (subcommand: string list) =
        "--namespace" :: namespaceName :: subcommand

    let parseJson (text: string) =
        use doc = JsonDocument.Parse(text, JsonDocumentOptions(MaxDepth = 32))
        doc.RootElement.Clone()

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
            if memoryLimit > 0L then
                ctrArgs.Add("--memory-limit")
                ctrArgs.Add(string memoryLimit)
            if cpuShares > 0L then
                ctrArgs.Add("--cpu-shares")
                ctrArgs.Add(string cpuShares)
            if pidLimit > 0u then
                Log.Warning("Limite de processus ({PidLimit}) ignorée à la création : non prise en charge par `ctr container create` de cette version", pidLimit)
            ctrArgs.Add(image)
            ctrArgs.Add(id)
            for c in command do ctrArgs.Add(c)
            for a in args do ctrArgs.Add(a)
            let output = runCtr (ctrArgs |> Seq.toList)
            let trimmed = output.Trim()
            if String.IsNullOrEmpty(trimmed) then id else trimmed

        member _.StartContainer(namespaceName, id, detach) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            let args =
                if detach then nsArgs namespaceName [ "task"; "start"; "--detach"; id ]
                else nsArgs namespaceName [ "task"; "start"; id ]
            runCtr args |> ignore

        member _.StartContainerWithLogs(namespaceName, id, logFile) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            try
                let psi = ProcessStartInfo("ctr")
                for a in nsArgs namespaceName [ "task"; "start"; id ] do
                    psi.ArgumentList.Add(a)
                psi.RedirectStandardOutput <- true
                psi.RedirectStandardError <- true
                psi.UseShellExecute <- false
                psi.CreateNoWindow <- true
                let proc = Process.Start(psi)
                let dir = Path.GetDirectoryName(logFile)
                if not (String.IsNullOrEmpty(dir)) then Directory.CreateDirectory(dir) |> ignore
                let writer = new StreamWriter(logFile, true)
                writer.AutoFlush <- true
                let mutable disposed = false
                let append (data: string) =
                    if not (isNull data) then
                        lock writer (fun () ->
                            if not disposed then writer.WriteLine(data))
                let closeWriter () =
                    lock writer (fun () ->
                        if not disposed then
                            writer.Flush()
                            writer.Dispose()
                            disposed <- true)
                proc.OutputDataReceived.AddHandler(DataReceivedEventHandler(fun _ e -> append e.Data))
                proc.ErrorDataReceived.AddHandler(DataReceivedEventHandler(fun _ e -> append e.Data))
                proc.EnableRaisingEvents <- true
                proc.Exited.AddHandler(EventHandler(fun _ _ -> closeWriter ()))
                proc.BeginOutputReadLine()
                proc.BeginErrorReadLine()
                Log.Information("Conteneur {ContainerId} démarré, logs capturés dans {LogFile}", id, logFile)
            with ex ->
                Log.Error(ex, "Erreur lors du démarrage avec capture des logs du conteneur {ContainerId}", id)

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

        member _.PauseContainer(namespaceName, id) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            runCtr (nsArgs namespaceName [ "task"; "pause"; id ]) |> ignore

        member _.ResumeContainer(namespaceName, id) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            runCtr (nsArgs namespaceName [ "task"; "resume"; id ]) |> ignore

        member _.WaitForContainerExit(namespaceName, id, timeoutSeconds) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            let deadline =
                if timeoutSeconds > 0 then Some (DateTime.UtcNow.AddSeconds(float timeoutSeconds))
                else None
            let currentStatus () =
                try
                    let output = runCtr (nsArgs namespaceName [ "tasks"; "list" ])
                    output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                    |> Array.tryFind (fun line ->
                        let parts = line.Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
                        parts.Length >= 1 && parts[0].Equals(id, StringComparison.OrdinalIgnoreCase))
                    |> Option.map (fun line ->
                        let parts = line.Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
                        if parts.Length >= 3 then parts[2] else "unknown")
                    |> Option.defaultValue "unknown"
                with ex ->
                    Log.Warning(ex, "Erreur lors de l'attente de sortie du conteneur {ContainerId}", id)
                    "unknown"
            let rec loop () =
                let status = currentStatus ()
                match status.ToUpperInvariant() with
                | "STOPPED" | "DELETED" | "UNKNOWN" | "PAUSED" -> 0
                | _ ->
                    match deadline with
                    | Some d when DateTime.UtcNow >= d -> -1
                    | _ ->
                        Thread.Sleep(1000)
                        loop ()
            loop ()

        member _.UpdateContainer(namespaceName, id, memoryLimit, cpuShares, pidLimit) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            let args = ResizeArray()
            args.AddRange(nsArgs namespaceName [ "task"; "update" ])
            if memoryLimit > 0L then
                args.Add("--memory")
                args.Add(string memoryLimit)
            if cpuShares > 0 then
                args.Add("--cpu-shares")
                args.Add(string cpuShares)
            if pidLimit > 0 then
                args.Add("--pids-limit")
                args.Add(string pidLimit)
            args.Add(id)
            runCtr (args |> Seq.toList) |> ignore

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
            let lines = ContainerLogs.read id tail since
            if lines.Length = 0 then
                [ "Aucun journal pour ce conteneur (le conteneur doit être démarré en mode détaché pour capturer ses logs)" ]
            else
                lines |> Array.toList

        member _.GetContainerLogsStream(namespaceName, id, tail, since, ct) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            let channel = System.Threading.Channels.Channel.CreateUnbounded<string>()
            let writer = channel.Writer
            let isContainerRunning () =
                try
                    runCtr (nsArgs namespaceName [ "tasks"; "list" ])
                    |> fun output ->
                        output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                        |> Array.exists (fun line ->
                            let parts = line.Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
                            parts.Length >= 3
                            && parts[0].Equals(id, StringComparison.OrdinalIgnoreCase)
                            && parts[2].Equals("RUNNING", StringComparison.OrdinalIgnoreCase))
                with ex ->
                    Log.Warning(ex, "Erreur lors du suivi des logs du conteneur {ContainerId}", id)
                    false
            let producer (cancel: CancellationToken) =
                async {
                    try
                        let snapshot = ContainerLogs.read id tail since
                        for l in snapshot do writer.TryWrite(l) |> ignore
                        let mutable lastOffset = ContainerLogs.fileLength id
                        let mutable running = true
                        while running && not cancel.IsCancellationRequested do
                            let lines, offset = ContainerLogs.readIncremental id lastOffset
                            lastOffset <- offset
                            for l in lines do writer.TryWrite(l) |> ignore
                            if isContainerRunning () then
                                do! Async.Sleep logPollIntervalMs
                            else
                                // courte grâce au writer pour vider les derniers octets
                                do! Async.Sleep 150
                                let fin, _ = ContainerLogs.readIncremental id lastOffset
                                for l in fin do writer.TryWrite(l) |> ignore
                                running <- false
                        writer.TryComplete() |> ignore
                    with ex ->
                        writer.TryComplete(ex) |> ignore
                }
            { new IAsyncEnumerable<string> with
                member _.GetAsyncEnumerator(ct2) =
                    let cts = CancellationTokenSource.CreateLinkedTokenSource(ct, ct2)
                    producer cts.Token |> Async.StartAsTask |> ignore
                    let mutable current = ""
                    { new IAsyncEnumerator<string> with
                        member _.Current = current
                        member _.MoveNextAsync() =
                            let read = channel.Reader.ReadAsync(cts.Token)
                            let task =
                                read.AsTask().ContinueWith(fun (t: Task<string>) ->
                                    if t.IsCompletedSuccessfully then
                                        current <- t.Result
                                        true
                                    else
                                        false)
                            ValueTask<bool>(task)
                        member _.DisposeAsync() =
                            writer.TryComplete() |> ignore
                            ValueTask() } }

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

        member _.StartExec(namespaceName, id, command, stdin, stdout, stderr) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateContainerId id
            SecurityValidation.validateCommand command
            let args =
                [ yield "--namespace"; yield namespaceName
                  yield "tasks"; yield "exec"; yield "--exec-id"; yield sprintf "exec-%s" (Guid.NewGuid().ToString("N")); yield id ]
                @ (command |> Array.toList)
            try
                let psi = ProcessStartInfo("ctr")
                for a in args do psi.ArgumentList.Add(a)
                psi.RedirectStandardInput <- true
                psi.RedirectStandardOutput <- true
                psi.RedirectStandardError <- true
                psi.UseShellExecute <- false
                psi.CreateNoWindow <- true
                use proc = Process.Start(psi)
                let pumpIn = Task.Run(fun () ->
                    stdin.CopyTo(proc.StandardInput.BaseStream)
                    proc.StandardInput.Close())
                let pumpOut = Task.Run(fun () -> proc.StandardOutput.BaseStream.CopyTo(stdout))
                let pumpErr = Task.Run(fun () -> proc.StandardError.BaseStream.CopyTo(stderr))
                proc.WaitForExit()
                Task.WaitAll(pumpOut, pumpErr)
                pumpIn.Wait()
                proc.ExitCode
            with ex ->
                Log.Error(ex, "Erreur lors de l'exécution en flux dans le conteneur {ContainerId}", id)
                -1

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

        member _.PullImage(image, userArg) =
            SecurityValidation.validateImage image
            let registry =
                let firstSegment = image.Split('/').[0]
                if firstSegment.Contains('.') || firstSegment.Contains(':') || firstSegment.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                then firstSegment
                else "docker.io"
            let userArg =
                match userArg with
                | Some u -> Some u
                | None -> RegistryAuth.tryGetUserArg (RegistryAuth.stateFile ()) registry
            let args =
                match userArg with
                | Some userArg -> [ "image"; "pull"; "--user"; userArg; image ]
                | None -> [ "image"; "pull"; image ]
            let output = runCtr args
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

        member _.CreateNamespace(name) =
            SecurityValidation.validateId name "Le namespace"
            runCtr [ "namespace"; "create"; name ] |> ignore

        member _.DeleteNamespace(name) =
            SecurityValidation.validateId name "Le namespace"
            runCtr [ "namespace"; "remove"; name ] |> ignore

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

        member _.ExportImage(namespaceName, imageRef, tarFile) =
            SecurityValidation.validateId namespaceName "Le namespace"
            SecurityValidation.validateImage imageRef
            runCtr (nsArgs namespaceName [ "image"; "export"; tarFile; imageRef ]) |> ignore

        member _.ImportImage(namespaceName, tarFile) =
            SecurityValidation.validateId namespaceName "Le namespace"
            let output = runCtr (nsArgs namespaceName [ "image"; "import"; tarFile ])
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
            |> Array.toList
