namespace Diplo.Core.Connection

open System
open System.Collections.Concurrent
open System.IO
open System.IO.Pipes
open System.Net.Http
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open System.Runtime.ExceptionServices
open Grpc.Core
open Grpc.Net.Client
open Grpc.Net.Client.Configuration
open Diplo.Abstractions

/// Factory mutualisée pour la création de canaux gRPC.
[<RequireQualifiedAccess>]
module GrpcClientFactory =

    // Ports par défaut selon la configuration de build (Debug 5001-5003,
    // Release 6001-6003) — source unique : DiploPorts.
    [<Literal>]
    let private DefaultContainerPort = DiploPorts.Container

    [<Literal>]
    let private DefaultVolumePort = DiploPorts.Volume

    [<Literal>]
    let private DefaultNetworkPort = DiploPorts.Network

    // H10 : taille maximale des messages gRPC (en octets). 64 Mo couvre les
    // écritures/lectures de fichiers bornées à 50 Mo (M14) ; le serveur est
    // configuré à l'identique (ServerConfig.grpcMaxMessageSize). Sans ce
    // relevement, un WriteFile de plus de 4 Mo échouerait en ResourceExhausted.
    [<Literal>]
    let MaxMessageSize = 64 * 1024 * 1024

    // M1 : les canaux gRPC sont chers (credentials, HttpHandler, politique de
    // retry, pool de connexions). Un seul canal est partagé par endpoint au lieu
    // d'en créer un nouveau à chaque appel (le GUI et le CLI construisent des
    // clients fréquemment). Le canal est évincé du cache dès qu'il passe à
    // l'état Shutdown (disposition explicite, rotation de configuration).
    let private channelCache = ConcurrentDictionary<string, GrpcChannel>()

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
        options.MaxSendMessageSize <- MaxMessageSize
        options.MaxReceiveMessageSize <- MaxMessageSize
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
                            ExceptionDispatchInfo.Capture(ex).Throw()
                            return Unchecked.defaultof<Stream>
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

        // H10 : GrpcChannelOptions n'expose pas « Timeout » ; on borne l'étape
        // de connexion TCP (au-delà, RpcException Unavailable immédiate au lieu
        // d'un verrouillage réseau). Le délai d'un appel individuel reste piloté
        // par le CancellationToken du client.
        let handler = new SocketsHttpHandler()
        handler.ConnectTimeout <- TimeSpan.FromSeconds 5.
        handler.UseProxy <- false
        handler.AllowAutoRedirect <- false
        options.HttpHandler <- handler
        GrpcChannel.ForAddress(address, options)

    let private isUsable (channel: GrpcChannel) =
        try
            channel.CreateCallInvoker() |> ignore
            true
        with :? ObjectDisposedException ->
            false

    let private create (address: string) =
        // INVARIANT DE SÉCURITÉ : validateGrpcAddress restreint l'hôte à
        // localhost/pipe. Les callCredentials partent en clair sur un canal
        // insecure (UnsafeUseInsecureChannelCallCredentials) — tout
        // assouplissement de la validation exposerait le token sur le réseau.
        SecurityValidation.validateGrpcAddress address
        let uri = Uri(address)

        let key =
            if isPipeAddress uri then
                "pipe://" + uri.PathAndQuery.TrimStart('/')
            else
                "tcp://" + uri.Authority

        let fresh =
            if isPipeAddress uri then
                createPipeChannel (uri.PathAndQuery.TrimStart('/'))
            else
                createTcpChannel address

        let mutable cached = Unchecked.defaultof<GrpcChannel>

        if channelCache.TryGetValue(key, &cached) then
            if isUsable cached then
                cached
            else
                let mutable discarded = Unchecked.defaultof<GrpcChannel>
                channelCache.TryRemove(key, &discarded) |> ignore
                channelCache.GetOrAdd(key, fresh)
        else
            channelCache.GetOrAdd(key, fresh)

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
