namespace Diplo.Core.Clients

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open System.Threading
open System.Threading.Tasks
open Diplo.Abstractions
open Diplo.Core.Connection
open Diplo.Grpc
open Diplo.Grpc.Container
open Grpc.Net.Client
open ProtoBuf.Grpc.Client

type ContainerClient(channel: GrpcChannel, ownsChannel: bool) =
    inherit GrpcClientBase(channel, ownsChannel)

    let client = channel.CreateGrpcService<IContainerService>()

    new(port: int) =
        let ch = DiploChannel.forContainer port
        new ContainerClient(ch, true)

    new() = new ContainerClient(5001)

    member _.CreateAsync
        ( name: string,
          image: string,
          ?env: IDictionary<string, string>,
          ?command: string list,
          ?args: string list,
          ?labels: IDictionary<string, string>,
          ?pidLimit: int,
          ?memoryLimit: int64,
          ?cpuShares: int,
          ?mounts: (string * string * bool) list,
          ?ct: CancellationToken ) =
        task {
            if String.IsNullOrEmpty(name) then invalidArg (nameof name) "Le nom du conteneur est requis"
            if String.IsNullOrEmpty(image) then invalidArg (nameof image) "L'image est requise"
            let request = { Name = name; Image = image; Env = Dictionary<string, string>(); Command = ResizeArray<string>(); Args = ResizeArray<string>(); Labels = Dictionary<string, string>(); PidLimit = 0; MemoryLimit = 0L; CpuShares = 0; Mounts = ResizeArray<ContainerMount>() }
            env |> Option.iter (fun e -> for kv in e do request.Env.[kv.Key] <- kv.Value)
            command |> Option.iter (fun c -> c |> List.iter (fun s -> request.Command.Add(s)))
            args |> Option.iter (fun a -> a |> List.iter (fun s -> request.Args.Add(s)))
            labels |> Option.iter (fun l -> for kv in l do request.Labels.[kv.Key] <- kv.Value)
            pidLimit |> Option.iter (fun v -> request.PidLimit <- v)
            memoryLimit |> Option.iter (fun v -> request.MemoryLimit <- v)
            cpuShares |> Option.iter (fun v -> request.CpuShares <- v)
            mounts |> Option.iter (fun ms -> for (s, d, ro) in ms do request.Mounts.Add({ Source = s; Destination = d; ReadOnly = ro }))
            let ct = defaultArg ct CancellationToken.None
            let! response = client.CreateContainer(request, ct)
            return response
        }

    member _.StartAsync(id: string, ?ct: CancellationToken) =
        task {
            if String.IsNullOrEmpty(id) then invalidArg (nameof id) "L'identifiant du conteneur est requis"
            let ct = defaultArg ct CancellationToken.None
            let! response = client.StartContainer({ Id = id }, ct)
            return response
        }

    member _.StopAsync(id: string, ?timeoutSeconds: int, ?ct: CancellationToken) =
        task {
            if String.IsNullOrEmpty(id) then invalidArg (nameof id) "L'identifiant du conteneur est requis"
            let timeout = defaultArg timeoutSeconds 10
            let ct = defaultArg ct CancellationToken.None
            let! response = client.StopContainer({ Id = id; TimeoutSeconds = timeout }, ct)
            return response
        }

    member _.DeleteAsync(id: string, ?force: bool, ?ct: CancellationToken) =
        task {
            if String.IsNullOrEmpty(id) then invalidArg (nameof id) "L'identifiant du conteneur est requis"
            let f = defaultArg force false
            let ct = defaultArg ct CancellationToken.None
            let! response = client.DeleteContainer({ Id = id; Force = f }, ct)
            return response
        }

    member _.InspectAsync(id: string, ?ct: CancellationToken) =
        task {
            if String.IsNullOrEmpty(id) then invalidArg (nameof id) "L'identifiant du conteneur est requis"
            let ct = defaultArg ct CancellationToken.None
            let! response = client.InspectContainer({ Id = id }, ct)
            return response
        }

    member _.ListAsync(?all: bool, ?filters: IDictionary<string, string>, ?ct: CancellationToken) =
        task {
            let a = defaultArg all false
            let ct = defaultArg ct CancellationToken.None
            let request = { All = a; Filters = Dictionary<string, string>() }
            filters |> Option.iter (fun f -> for kv in f do request.Filters.[kv.Key] <- kv.Value)
            let! response = client.ListContainers(request, ct)
            return response
        }

    member _.GetLogs(id: string, ?follow: bool, ?tail: int, ?since: string, ?ct: CancellationToken) =
        task {
            if String.IsNullOrEmpty(id) then invalidArg (nameof id) "L'identifiant du conteneur est requis"
            let f = defaultArg follow false
            let t = defaultArg tail 100
            let s = defaultArg since ""
            let ct = defaultArg ct CancellationToken.None
            let request = { Id = id; Follow = f; Tail = t; Since = s }
            let entries = ResizeArray()
            do!
                task {
                    let enumerator = client.GetContainerLogs(request, ct).GetAsyncEnumerator(ct)
                    let mutable moving = true
                    while moving do
                        let! hasNext = enumerator.MoveNextAsync().AsTask()
                        if hasNext then
                            entries.Add(enumerator.Current)
                        else
                            moving <- false
                }
            return entries :> seq<ContainerLogEntry>
        }

    member _.Exec(id: string, command: IEnumerable<string>, ?attachStdout: bool, ?attachStderr: bool, ?ct: CancellationToken) =
        task {
            if String.IsNullOrEmpty(id) then invalidArg (nameof id) "L'identifiant du conteneur est requis"
            let aOut = defaultArg attachStdout true
            let aErr = defaultArg attachStderr true
            let ct = defaultArg ct CancellationToken.None
            let request = { Id = id; Command = ResizeArray<string>(); AttachStdin = false; AttachStdout = aOut; AttachStderr = aErr }
            for c in command do request.Command.Add(c)
            let outputs = ResizeArray()
            do!
                task {
                    let enumerator = client.ExecInContainer(request, ct).GetAsyncEnumerator(ct)
                    let mutable moving = true
                    while moving do
                        let! hasNext = enumerator.MoveNextAsync().AsTask()
                        if hasNext then
                            outputs.Add(enumerator.Current)
                        else
                            moving <- false
                }
            return outputs :> seq<ExecOutput>
        }

    member _.PullImageAsync(image: string, ?ct: CancellationToken) =
        task {
            if String.IsNullOrEmpty(image) then invalidArg (nameof image) "L'image est requise"
            let ct = defaultArg ct CancellationToken.None
            let! response = client.PullImage({ Image = image }, ct)
            return response
        }

    member _.GetVersionAsync(?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let! response = client.GetVersion({ Placeholder = false }, ct)
            return response
        }

    member _.ListNamespacesAsync(?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let! response = client.ListNamespaces({ Placeholder = false }, ct)
            return response
        }

    member _.RenameContainerAsync(id: string, newName: string, ?ct: CancellationToken) =
        task {
            if String.IsNullOrEmpty(id) then invalidArg (nameof id) "L'identifiant du conteneur est requis"
            if String.IsNullOrEmpty(newName) then invalidArg (nameof newName) "Le nouveau nom est requis"
            let ct = defaultArg ct CancellationToken.None
            let! response = client.RenameContainer({ Id = id; NewName = newName }, ct)
            return response
        }

    member _.TopContainerAsync(id: string, ?ct: CancellationToken) =
        task {
            if String.IsNullOrEmpty(id) then invalidArg (nameof id) "L'identifiant du conteneur est requis"
            let ct = defaultArg ct CancellationToken.None
            let! response = client.TopContainer({ Id = id }, ct)
            return response
        }

    member _.GetContainerStatsAsync(id: string, ?ct: CancellationToken) =
        task {
            if String.IsNullOrEmpty(id) then invalidArg (nameof id) "L'identifiant du conteneur est requis"
            let ct = defaultArg ct CancellationToken.None
            let! response = client.GetContainerStats({ Id = id }, ct)
            return response
        }

    member _.ListImagesAsync(?namespaceName: string, ?ct: CancellationToken) =
        task {
            let ns = defaultArg namespaceName ""
            let ct = defaultArg ct CancellationToken.None
            let request = { NamespaceName = "" }
            if not (String.IsNullOrEmpty(ns)) then
                request.NamespaceName <- ns
            let! response = client.ListImages(request, ct)
            return response
        }

    member _.InspectImageAsync(ref: string, ?namespaceName: string, ?ct: CancellationToken) =
        task {
            if String.IsNullOrEmpty(ref) then invalidArg (nameof ref) "La référence de l'image est requise"
            let ns = defaultArg namespaceName ""
            let ct = defaultArg ct CancellationToken.None
            let request : InspectImageRequest = { Ref = ref; NamespaceName = ns }
            let! response = client.InspectImage(request, ct)
            return response
        }

    member _.RemoveImageAsync(ref: string, ?namespaceName: string, ?ct: CancellationToken) =
        task {
            if String.IsNullOrEmpty(ref) then invalidArg (nameof ref) "La référence de l'image est requise"
            let ns = defaultArg namespaceName ""
            let ct = defaultArg ct CancellationToken.None
            let request = { Ref = ref; NamespaceName = ns }
            let! response = client.RemoveImage(request, ct)
            return response
        }

    member _.TagImageAsync(source: string, target: string, ?namespaceName: string, ?ct: CancellationToken) =
        task {
            if String.IsNullOrEmpty(source) then invalidArg (nameof source) "La source de l'image est requise"
            if String.IsNullOrEmpty(target) then invalidArg (nameof target) "La cible de l'image est requise"
            let ns = defaultArg namespaceName ""
            let ct = defaultArg ct CancellationToken.None
            let request = { Source = source; Target = target; NamespaceName = ns }
            let! response = client.TagImage(request, ct)
            return response
        }
