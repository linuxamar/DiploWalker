namespace Diplo.Container.Tests

open System
open System.IO
open System.Text.Json
open System.Threading.Tasks
open Diplo.Abstractions.Interfaces

type MockContainerdClient() =

    let mutable containers = Map.empty<string, Map<string, string>>
    let mutable startedContainers = Set.empty<string>
    let mutable stoppedContainers = Set.empty<string>
    let mutable pausedContainers = Set.empty<string>
    let mutable updatedContainers = Set.empty<string>
    let mutable deletedContainers = Set.empty<string>
    let mutable stopCalled = Map.empty<string, int>
    let mutable pulledImages = Set.empty<string>
    let mutable removedImages = Set.empty<string>
    let mutable createdNamespaces = Set.empty<string>
    let mutable removedNamespaces = Set.empty<string>
    let mutable recordedMounts = Map.empty<string, (string * string * bool) list>
    let ownedDocs = System.Collections.Generic.List<JsonDocument>()

    let keepDoc (doc: JsonDocument) =
        ownedDocs.Add(doc)
        doc

    let createLabelsJson (labels: Map<string, string>) =
        let sb = System.Text.StringBuilder()
        sb.Append("{") |> ignore
        for i, (k, v) in labels |> Map.toList |> List.indexed do
            if i > 0 then sb.Append(",") |> ignore
            sb.AppendFormat("\"{0}\":\"{1}\"", k, v) |> ignore
        sb.Append("}") |> ignore
        let doc = JsonDocument.Parse(sb.ToString()) |> keepDoc
        doc.RootElement

    interface IContainerdClient with
        member _.CreateContainer(_namespaceName, id, image, _labels, _env, _command, _args, _memoryLimit, _cpuShares, _pidLimit, mounts) =
            containers <- containers |> Map.add id (Map.ofList [ "image", image; "id", id ])
            recordedMounts <- recordedMounts |> Map.add id mounts
            id

        member _.StartContainer(_namespaceName, id, _detach) =
            startedContainers <- startedContainers |> Set.add id

        member _.StopContainer(_namespaceName, id, timeoutSeconds) =
            stopCalled <- stopCalled |> Map.add id timeoutSeconds
            stoppedContainers <- stoppedContainers |> Set.add id
            Task.CompletedTask

        member _.DeleteContainer(_namespaceName, id, _force) =
            deletedContainers <- deletedContainers |> Set.add id

        member _.InspectContainer(_namespaceName, id) =
            let labelsJson = createLabelsJson (Map.ofList [ "app", "test"; "env", "dev" ])
            let envJson = createLabelsJson (Map.ofList [ "ASPNETCORE_ENVIRONMENT", "Development" ])
            let json = sprintf """{"id":"%s","image":"mcr.microsoft.com/dotnet/runtime:10.0","created_at":"2025-01-15T10:30:00Z","exit_code":0,"labels":%s,"env":%s}""" id (labelsJson.GetRawText()) (envJson.GetRawText())
            let doc = JsonDocument.Parse(json) |> keepDoc
            doc.RootElement

        member _.TaskInfo(_namespaceName, id) =
            if stoppedContainers |> Set.contains id then
                let doc = JsonDocument.Parse(sprintf """{"pid":0,"status":"stopped"}""") |> keepDoc
                doc.RootElement
            elif startedContainers |> Set.contains id then
                let doc = JsonDocument.Parse(sprintf """{"pid":1234,"status":"running"}""") |> keepDoc
                doc.RootElement
            else
                let doc = JsonDocument.Parse(sprintf """{"pid":0,"status":"created"}""") |> keepDoc
                doc.RootElement

        member _.ListContainers(_namespaceName, _all) =
            containers |> Map.toList |> List.map fst

        member _.GetContainerLogs(_namespaceName, _id, _tail, _follow, _since) =
            [ "2025-01-15T10:30:01Z Application started"
              "2025-01-15T10:30:02Z Listening on port 8080" ]

        member _.GetContainerLogsStream(_namespaceName, _id, _tail, _since, _ct) =
            let mutable yielded = false
            { new System.Collections.Generic.IAsyncEnumerable<string> with
                member _.GetAsyncEnumerator(_ct) =
                    { new System.Collections.Generic.IAsyncEnumerator<string> with
                        member _.Current = if yielded then "2025-01-15T10:30:02Z Listening on port 8080" else "2025-01-15T10:30:01Z Application started"
                        member _.MoveNextAsync() =
                            if yielded then ValueTask<bool>(false)
                            else
                                yielded <- true
                                ValueTask<bool>(true)
                        member _.DisposeAsync() = ValueTask() } }

        member _.ExecInContainer(_namespaceName, _id, command) =
            if command.Length >= 2 && command.[0] = "base64" then
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("contenu-du-fichier"))
            else
                sprintf "Output of: %s" (command |> String.concat " ")

        member _.Version() =
            "1.7.27 (revision: abc123)"

        member _.PullImage(image, _userArg) =
            pulledImages <- pulledImages |> Set.add image
            sprintf "image pulled: %s" image

        member _.Namespaces() = [ "default"; "moby" ]

        member _.RenameContainer(_namespaceName, id, _newName) =
            ()

        member _.TopContainer(_namespaceName, _id) =
            "1234 root dotnet app.dll"

        member _.GetContainerStats(_namespaceName, _id) =
            JsonDocument.Parse("""{"cpu":{"usage":123456},"memory":{"usage":1048576,"limit":536870912},"pids":{"current":3}}""").RootElement

        member _.ListImages(_namespaceName) =
            [
                JsonDocument.Parse("""{"id":"nginx:latest","ref":"docker.io/library/nginx:latest","repository":"library/nginx","tag":"latest","size":18567432,"created_at":"2025-01-10T08:00:00Z"}""").RootElement
                JsonDocument.Parse("""{"id":"redis:7","ref":"docker.io/library/redis:7","repository":"library/redis","tag":"7","size":138670845,"created_at":"2025-01-05T12:00:00Z"}""").RootElement
            ]

        member _.InspectImage(_namespaceName, imageRef) =
            let repo = imageRef.Split([| ':' |]).[0]
            let json = sprintf """{"id":"%s","ref":"%s","repository":"library/%s","tag":"latest","size":18567432,"created_at":"2025-01-10T08:00:00Z","labels":{"maintainer":"nginx"}}""" imageRef imageRef repo
            let doc = JsonDocument.Parse(json) |> keepDoc
            doc.RootElement

        member _.RemoveImage(_namespaceName, imageRef) =
            removedImages <- removedImages |> Set.add imageRef
            sprintf "Image %s supprimée" imageRef

        member _.TagImage(_namespaceName, _source, _target) =
            ()

        member _.StartContainerWithLogs(_namespaceName, id, logFile) =
            startedContainers <- startedContainers |> Set.add id
            File.WriteAllText(logFile, "2025-01-15T10:30:01Z Application started" + Environment.NewLine)

        member _.PauseContainer(_namespaceName, id) =
            pausedContainers <- pausedContainers |> Set.add id

        member _.ResumeContainer(_namespaceName, id) =
            pausedContainers <- pausedContainers |> Set.remove id

        member _.WaitForContainerExit(_namespaceName, _id, _timeoutSeconds) =
            if startedContainers |> Set.isEmpty then -1 else 0

        member _.UpdateContainer(_namespaceName, id, _memoryLimit, _cpuShares, _pidLimit) =
            updatedContainers <- updatedContainers |> Set.add id

        member _.CreateNamespace(name) =
            createdNamespaces <- createdNamespaces |> Set.add name

        member _.DeleteNamespace(name) =
            removedNamespaces <- removedNamespaces |> Set.add name

        member _.ExportImage(_namespaceName, _imageRef, tarFile) =
            File.WriteAllText(tarFile, "fake-image-archive")

        member _.ImportImage(_namespaceName, tarFile) =
            use fs = File.Open(tarFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            use reader = new StreamReader(fs)
            [ reader.ReadToEnd() ]

        member _.StartExec(_namespaceName, _id, _command, stdin, stdout, _stderr) =
            stdin.CopyTo(stdout)
            0

    member this.Mock : IContainerdClient = this :> IContainerdClient
    member _.StopCalled = stopCalled
    member _.StoppedContainers = stoppedContainers
    member _.DeletedContainers = deletedContainers
    member _.StartedContainers = startedContainers
    member _.PausedContainers = pausedContainers
    member _.UpdatedContainers = updatedContainers
    member _.PulledImages = pulledImages
    member _.RemovedImages = removedImages
    member _.CreatedNamespaces = createdNamespaces
    member _.RemovedNamespaces = removedNamespaces
    member _.RecordedMounts = recordedMounts
