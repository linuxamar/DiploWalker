namespace DiploWalker.Core.Connection

open DiploWalker.Core

/// Canal gRPC â€” dÃ©lÃ¨gue la construction au GrpcClientFactory mutualisÃ©.
[<RequireQualifiedAccess>]
module DiploWalkerChannel =

    let forContainer (port: int) = GrpcClientFactory.forContainer port
    let forVolume (port: int) = GrpcClientFactory.forVolume port
    let forNetwork (port: int) = GrpcClientFactory.forNetwork port
    let forAddress (address: string) = GrpcClientFactory.forAddress address

