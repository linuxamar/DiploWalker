namespace Diplo.Core.Connection

open Grpc.Core
open Grpc.Net.Client

[<RequireQualifiedAccess>]
module DiploChannel =

    [<Literal>]
    let private DefaultContainerPort = 5001

    [<Literal>]
    let private DefaultVolumePort = 5002

    [<Literal>]
    let private DefaultNetworkPort = 5003

    let private create (address: string) =
        let callCredentials = Diplo.Abstractions.TokenInterceptor.createTokenCredentials()
        let channelCredentials = ChannelCredentials.Create(ChannelCredentials.Insecure, callCredentials)
        let options = GrpcChannelOptions()
        options.Credentials <- channelCredentials
        GrpcChannel.ForAddress(address, options)

    let forContainer (port: int) = create $"http://localhost:{port}"
    let forVolume (port: int) = create $"http://localhost:{port}"
    let forNetwork (port: int) = create $"http://localhost:{port}"
    let forAddress (address: string) = create address
