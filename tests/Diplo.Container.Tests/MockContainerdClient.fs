namespace Diplo.Container.Tests

open System
open System.Text.Json
open Diplo.Abstractions.Interfaces

type MockContainerdClient() =

    let mutable containers = Map.empty<string, Map<string, string>>
    let mutable startedContainers = Set.empty<string>
    let mutable deletedContainers = Set.empty<string>
    let mutable stopCalled = Map.empty<string, int>
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
        member _.GetVersion() = "containerd 1.7.27 (revision: abc123)"

        member _.ListNamespaces() = [ "default"; "moby" ]

        member _.CreateContainer(_namespaceName, id, image, _labels) =
            containers <- containers |> Map.add id (Map.ofList [ "image", image; "id", id ])
            id

        member _.StartContainer(_namespaceName, id) =
            startedContainers <- startedContainers |> Set.add id

        member _.StopContainer(_namespaceName, id, timeoutSeconds) =
            stopCalled <- stopCalled |> Map.add id timeoutSeconds

        member _.DeleteContainer(_namespaceName, id, _force) =
            deletedContainers <- deletedContainers |> Set.add id

        member _.InspectContainer(_namespaceName, id) =
            let labelsJson = createLabelsJson (Map.ofList [ "app", "test"; "env", "dev" ])
            let envJson = createLabelsJson (Map.ofList [ "ASPNETCORE_ENVIRONMENT", "Development" ])
            let json = sprintf """{"id":"%s","image":"mcr.microsoft.com/dotnet/runtime:10.0","created_at":"2025-01-15T10:30:00Z","exit_code":0,"labels":%s,"env":%s}""" id (labelsJson.GetRawText()) (envJson.GetRawText())
            let doc = JsonDocument.Parse(json) |> keepDoc
            doc.RootElement

        member _.TaskInfo(_namespaceName, id) =
            if startedContainers |> Set.contains id then
                let doc = JsonDocument.Parse(sprintf """{"pid":1234,"status":"running"}""") |> keepDoc
                doc.RootElement
            else
                let doc = JsonDocument.Parse(sprintf """{"pid":0,"status":"created"}""") |> keepDoc
                doc.RootElement

        member _.ListContainers(_namespaceName, _all) =
            containers |> Map.toList |> List.map fst

        member _.GetContainerLogs(_namespaceName, _id, _tail) =
            [ "2025-01-15T10:30:01Z Application started"
              "2025-01-15T10:30:02Z Listening on port 8080" ]

        member _.ExecInContainer(_namespaceName, _id, command) =
            sprintf "Output of: %s" (command |> String.concat " ")

        member _.Version() =
            let doc = JsonDocument.Parse("""{"Version":"1.7.27","Revision":"abc123","Go":"go1.22.5","OS":"windows","Arch":"amd64"}""") |> keepDoc
            doc.RootElement

        member _.Namespaces() = [ "default"; "moby" ]

    member this.Mock : IContainerdClient = this :> IContainerdClient
    member _.StopCalled = stopCalled
    member _.DeletedContainers = deletedContainers
    member _.StartedContainers = startedContainers
