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

[<RequireQualifiedAccess>]
module DiploChannel =

    [<Literal>]
    let private DefaultContainerPort = 5001

    [<Literal>]
    let private DefaultVolumePort = 5002

    [<Literal>]
    let private DefaultNetworkPort = 5003

    /// Indique si l'adresse (URL complète) désigne un canal local par named pipe,
    /// p. ex. "http://pipe:/diplo-container".
    let private isPipeAddress (uri: Uri) =
        String.Equals(uri.Host, "pipe", StringComparison.OrdinalIgnoreCase)

    /// Politique de reprise automatique (grpc-dotnet) : les appels échouant avec
    /// `Unavailable` (connexion refusée, service en cours de redémarrage) sont
    /// relancés avec un backoff exponentiel. Les appels streaming ne sont pas
    /// rejoués après le premier message reçu ni après dépassement du buffer de
    /// reprise — comportement natif de grpc-dotnet.
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
        let callCredentials = TokenInterceptor.createTokenCredentials()
        let channelCredentials = ChannelCredentials.Create(ChannelCredentials.Insecure, callCredentials)
        let options = GrpcChannelOptions()
        options.Credentials <- channelCredentials
        options.UnsafeUseInsecureChannelCallCredentials <- true
        options.ServiceConfig <- buildRetryServiceConfig ()
        (options, callCredentials)

    /// Canal gRPC sur named pipe : grpc-dotnet exige un SocketsHttpHandler dont le
    /// ConnectCallback ouvre un NamedPipeClientStream (le schéma http://pipe: n'est
    /// pas reconnu nativement par le client).
    let private createPipeChannel (pipeName: string) =
        let options, _ = buildCredentials ()
        let connectCallback =
            Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>>(fun _ ct ->
                let t =
                    task {
                        let pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous)
                        do! pipe.ConnectAsync(ct)
                        return pipe :> Stream
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