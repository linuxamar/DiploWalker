namespace Diplo.Core.Clients

open System
open System.Collections.Generic
open System.Threading
open Diplo.Core.Connection
open Diplo.Grpc.Container
open Grpc.Net.Client

[<Sealed>]
type ContainerClient(channel: GrpcChannel, ownsChannel: bool) =

    let client = ContainerService.ContainerServiceClient(channel)

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
          ?pidLimit: uint32,
          ?memoryLimit: int64,
          ?cpuShares: int64,
          ?ct: CancellationToken ) =
        task {
            let request = CreateContainerRequest(Name = name, Image = image)
            env |> Option.iter (fun e -> request.Env.Add(e))
            command |> Option.iter (fun c -> c |> List.iter request.Command.Add)
            args |> Option.iter (fun a -> a |> List.iter request.Args.Add)
            labels |> Option.iter (fun l -> request.Labels.Add(l))
            pidLimit |> Option.iter (fun v -> request.PidLimit <- v)
            memoryLimit |> Option.iter (fun v -> request.MemoryLimit <- v)
            cpuShares |> Option.iter (fun v -> request.CpuShares <- v)
            let ct = defaultArg ct CancellationToken.None
            let! response = client.CreateContainerAsync(request, cancellationToken = ct).ResponseAsync
            return response
        }

    member _.StartAsync(id: string, ?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let! response = client.StartContainerAsync(StartContainerRequest(Id = id), cancellationToken = ct).ResponseAsync
            return response
        }

    member _.StopAsync(id: string, ?timeoutSeconds: int, ?ct: CancellationToken) =
        task {
            let timeout = defaultArg timeoutSeconds 10
            let ct = defaultArg ct CancellationToken.None
            let! response = client.StopContainerAsync(StopContainerRequest(Id = id, TimeoutSeconds = timeout), cancellationToken = ct).ResponseAsync
            return response
        }

    member _.DeleteAsync(id: string, ?force: bool, ?ct: CancellationToken) =
        task {
            let f = defaultArg force false
            let ct = defaultArg ct CancellationToken.None
            let! response = client.DeleteContainerAsync(DeleteContainerRequest(Id = id, Force = f), cancellationToken = ct).ResponseAsync
            return response
        }

    member _.InspectAsync(id: string, ?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let! response = client.InspectContainerAsync(InspectContainerRequest(Id = id), cancellationToken = ct).ResponseAsync
            return response
        }

    member _.ListAsync(?all: bool, ?filters: IDictionary<string, string>, ?ct: CancellationToken) =
        task {
            let a = defaultArg all false
            let ct = defaultArg ct CancellationToken.None
            let request = ListContainersRequest(All = a)
            filters |> Option.iter (fun f -> request.Filters.Add(f))
            let! response = client.ListContainersAsync(request, cancellationToken = ct).ResponseAsync
            return response
        }

    member _.GetLogs(id: string, ?follow: bool, ?tail: int, ?since: string, ?ct: CancellationToken) =
        let f = defaultArg follow false
        let t = defaultArg tail 100
        let s = defaultArg since ""
        let ct = defaultArg ct CancellationToken.None
        let request = GetContainerLogsRequest(Id = id, Follow = f, Tail = t, Since = s)
        client.GetContainerLogs(request, cancellationToken = ct).ResponseStream

    member _.Exec(id: string, command: IEnumerable<string>, ?attachStdout: bool, ?attachStderr: bool, ?ct: CancellationToken) =
        let aOut = defaultArg attachStdout true
        let aErr = defaultArg attachStderr true
        let ct = defaultArg ct CancellationToken.None
        let request = ExecInContainerRequest(Id = id, AttachStdout = aOut, AttachStderr = aErr)
        request.Command.AddRange(command)
        client.ExecInContainer(request, cancellationToken = ct).ResponseStream

    member _.PullImageAsync(image: string, ?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let! response = client.PullImageAsync(PullImageRequest(Image = image), cancellationToken = ct).ResponseAsync
            return response
        }

    member _.GetVersionAsync(?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let! response = client.GetVersionAsync(GetVersionRequest(), cancellationToken = ct).ResponseAsync
            return response
        }

    member _.ListNamespacesAsync(?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let! response = client.ListNamespacesAsync(ListNamespacesRequest(), cancellationToken = ct).ResponseAsync
            return response
        }

    member _.RenameContainerAsync(id: string, newName: string, ?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let! response = client.RenameContainerAsync(RenameContainerRequest(Id = id, NewName = newName), cancellationToken = ct).ResponseAsync
            return response
        }

    member _.TopContainerAsync(id: string, ?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let! response = client.TopContainerAsync(TopContainerRequest(Id = id), cancellationToken = ct).ResponseAsync
            return response
        }

    member _.GetContainerStatsAsync(id: string, ?ct: CancellationToken) =
        task {
            let ct = defaultArg ct CancellationToken.None
            let! response = client.GetContainerStatsAsync(GetContainerStatsRequest(Id = id), cancellationToken = ct).ResponseAsync
            return response
        }

    member _.ListImagesAsync(?namespaceName: string, ?ct: CancellationToken) =
        task {
            let ns = defaultArg namespaceName ""
            let ct = defaultArg ct CancellationToken.None
            let request = ListImagesRequest()
            if not (System.String.IsNullOrEmpty(ns)) then
                request.NamespaceName <- ns
            let! response = client.ListImagesAsync(request, cancellationToken = ct).ResponseAsync
            return response
        }

    member _.InspectImageAsync(ref: string, ?namespaceName: string, ?ct: CancellationToken) =
        task {
            let ns = defaultArg namespaceName ""
            let ct = defaultArg ct CancellationToken.None
            let request = InspectImageRequest(Ref = ref)
            if not (System.String.IsNullOrEmpty(ns)) then
                request.NamespaceName <- ns
            let! response = client.InspectImageAsync(request, cancellationToken = ct).ResponseAsync
            return response
        }

    member _.RemoveImageAsync(ref: string, ?namespaceName: string, ?ct: CancellationToken) =
        task {
            let ns = defaultArg namespaceName ""
            let ct = defaultArg ct CancellationToken.None
            let request = RemoveImageRequest(Ref = ref)
            if not (System.String.IsNullOrEmpty(ns)) then
                request.NamespaceName <- ns
            let! response = client.RemoveImageAsync(request, cancellationToken = ct).ResponseAsync
            return response
        }

    member _.TagImageAsync(source: string, target: string, ?namespaceName: string, ?ct: CancellationToken) =
        task {
            let ns = defaultArg namespaceName ""
            let ct = defaultArg ct CancellationToken.None
            let request = TagImageRequest(Source = source, Target = target)
            if not (System.String.IsNullOrEmpty(ns)) then
                request.NamespaceName <- ns
            let! response = client.TagImageAsync(request, cancellationToken = ct).ResponseAsync
            return response
        }

    interface IDisposable with
        member _.Dispose() =
            if ownsChannel then channel.Dispose()
