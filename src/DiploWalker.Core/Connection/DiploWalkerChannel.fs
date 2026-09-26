namespace DiploWalker.Core.Connection

open DiploWalker.Core

/// Canal gRPC — délègue la construction au GrpcClientFactory mutualisé.
[<RequireQualifiedAccess>]
module DiploWalkerChannel =

    let forContainer (port: int) = GrpcClientFactory.forContainer port
    let forVolume (port: int) = GrpcClientFactory.forVolume port
    let forNetwork (port: int) = GrpcClientFactory.forNetwork port
    let forAddress (address: string) = GrpcClientFactory.forAddress address

