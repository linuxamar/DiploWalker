namespace Diplo.Core.Connection

open Grpc.Core
open Grpc.Net.Client
open Diplo.Abstractions

[<RequireQualifiedAccess>]
module DiploChannel =

    [<Literal>]
    let private DefaultContainerPort = 5001

    [<Literal>]
    let private DefaultVolumePort = 5002

    [<Literal>]
    let private DefaultNetworkPort = 5003

    let private create (address: string) =
        SecurityValidation.validateGrpcAddress address
        let callCredentials = TokenInterceptor.createTokenCredentials()
        let channelCredentials = ChannelCredentials.Create(ChannelCredentials.Insecure, callCredentials)
        let options = GrpcChannelOptions()
        options.Credentials <- channelCredentials
        options.UnsafeUseInsecureChannelCallCredentials <- true
        GrpcChannel.ForAddress(address, options)

    let forContainer (port: int) = create $"http://localhost:{port}"
    let forVolume (port: int) = create $"http://localhost:{port}"
    let forNetwork (port: int) = create $"http://localhost:{port}"
    let forAddress (address: string) = create address
