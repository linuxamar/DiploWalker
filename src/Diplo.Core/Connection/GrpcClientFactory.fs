namespace Diplo.Core.Connection

open System
open System.IO
open System.IO.Pipes
open System.Net.Http
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open Grpc.Core
open Grpc.Net.Client
open Grpc.Net.Client.Configuration
open Diplo.Abstractions

/// Factory mutualisée pour la création de canaux gRPC.
[<RequireQualifiedAccess>]
module GrpcClientFactory =

    [<Literal>]
    let private DefaultContainerPort = 5001

    [<Literal>]
    let private DefaultVolumePort = 5002

    [<Literal>]
    let private DefaultNetworkPort = 5003

    let private isPipeAddress (uri: Uri) =
        String.Equals(uri.Host, "pipe", StringComparison.OrdinalIgnoreCase)

    let private buildRetryServiceConfig () =
        let retryPolicy = RetryPolicy()
        retryPolicy.MaxAttempts <- 5
        retryPolicy.InitialBackoff <- TimeSpan.FromMilliseconds 200.
        retryPolicy.MaxBackoff <- TimeSpan.FromSeconds 5.
        retryPolicy.BackoffMultiplier <- 2.
        retryPolicy.RetryableStatusCodes.Add(StatusCode.Unavailable) |> ignore
        let methodConfig = MethodConfig()
        methodConfig.Names.Add(MethodName.Default)
        methodConfig.RetryPolicy <- retryPolicy
        let serviceConfig = ServiceConfig()
        serviceConfig.MethodConfigs.Add(methodConfig)
        serviceConfig

    let private buildCredentials () =
        let callCredentials = TokenInterceptor.createTokenCredentials ()

        let channelCredentials =
            ChannelCredentials.Create(ChannelCredentials.Insecure, callCredentials)

        let options = GrpcChannelOptions()
        options.Credentials <- channelCredentials
        options.UnsafeUseInsecureChannelCallCredentials <- true
        options.ServiceConfig <- buildRetryServiceConfig ()
        (options, callCredentials)

    let private createPipeChannel (pipeName: string) =
        let options, _ = buildCredentials ()

        let connectCallback =
            Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>>(fun _ ct ->
                let t =
                    task {
                        let pipe =
                            new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous)

                        try
                            do! pipe.ConnectAsync(ct)
                            return pipe :> Stream
                        with ex ->
                            pipe.Dispose()
                            return raise ex
                    }

                ValueTask<Stream>(t))

        let handler = new SocketsHttpHandler()
        handler.ConnectCallback <- connectCallback
        handler.UseProxy <- false
        handler.AllowAutoRedirect <- false
        options.HttpHandler <- handler
        GrpcChannel.ForAddress("http://localhost", options)

    let private createTcpChannel (address: string) =
        let options, _ = buildCredentials ()
        GrpcChannel.ForAddress(address, options)

    let private create (address: string) =
        SecurityValidation.validateGrpcAddress address
        let uri = Uri(address)

        if isPipeAddress uri then
            createPipeChannel (uri.PathAndQuery.TrimStart('/'))
        else
            createTcpChannel address

    let forContainer (port: int) = create $"http://localhost:{port}"
    let forVolume (port: int) = create $"http://localhost:{port}"
    let forNetwork (port: int) = create $"http://localhost:{port}"
    let forAddress (address: string) = create address

    let resolveAddress (configAddress: string option) (defaultPort: int) =
        match configAddress with
        | Some address -> forAddress address
        | None ->
            if defaultPort = DefaultVolumePort then
                forVolume defaultPort
            elif defaultPort = DefaultNetworkPort then
                forNetwork defaultPort
            else
                forContainer defaultPort
