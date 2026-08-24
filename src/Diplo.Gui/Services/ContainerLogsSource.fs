namespace Diplo.Gui.Services

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Diplo.Core.Clients
open Diplo.Grpc.Container

/// Lecture abstraite des journaux d'un conteneur : permet de tester le
/// ViewModel sans serveur gRPC (le flux est simulé par un faux).
type IContainerLogsSource =
    inherit IDisposable

    abstract GetStream:
        id: string * follow: bool * tail: int * since: string * ct: CancellationToken ->
            IAsyncEnumerable<ContainerLogEntry>

    abstract GetSnapshot: id: string * tail: int * since: string * ct: CancellationToken -> Task<seq<ContainerLogEntry>>

/// Implémentation par défaut reposant sur le client gRPC réel.
type GrpcContainerLogsSource() =
    let client = new ContainerClient()
    let mutable disposed = false

    interface IContainerLogsSource with
        member _.GetStream(id, follow, tail, since, ct) =
            client.GetLogsStream(id, follow = follow, tail = tail, since = since, ct = ct)

        member _.GetSnapshot(id, tail, since, ct) =
            task { return! client.GetLogs(id, tail = tail, since = since, ct = ct) }

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                (client :> IDisposable).Dispose()
