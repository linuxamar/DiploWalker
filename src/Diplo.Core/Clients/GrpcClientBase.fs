namespace Diplo.Core.Clients

open System
open Grpc.Net.Client

[<AbstractClass>]
type GrpcClientBase(channel: GrpcChannel, ownsChannel: bool) =

    new(channel: GrpcChannel) = GrpcClientBase(channel, false)

    interface IDisposable with
        member _.Dispose() =
            if ownsChannel then
                (channel :> IDisposable).Dispose()
