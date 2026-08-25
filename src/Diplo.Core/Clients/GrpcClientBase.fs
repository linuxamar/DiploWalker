namespace Diplo.Core.Clients

open System
open System.Threading.Tasks
open Grpc.Net.Client

[<AbstractClass>]
type GrpcClientBase(channel: GrpcChannel, ownsChannel: bool) =

    interface IDisposable with
        member _.Dispose() =
            if ownsChannel then
                (channel :> IDisposable).Dispose()

    interface IAsyncDisposable with
        member _.DisposeAsync() =
            if ownsChannel then
                (channel :> IDisposable).Dispose()
            ValueTask()
