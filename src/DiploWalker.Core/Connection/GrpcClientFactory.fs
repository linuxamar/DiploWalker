namespace DiploWalker.Core.Connection

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
open DiploWalker.Abstractions

/// Factory mutualisée pour la création de canaux gRPC.
[<RequireQualifiedAccess>]
module GrpcClientFactory =

    // Ports par défaut selon la configuration de build (Debug 5001-5003,
    // Release 6001-6003) — source unique : DiploWalkerPorts.
    [<Literal>]
    let private DefaultContainerPort = DiploWalkerPorts.Container

    [<Literal>]
    let private DefaultVolumePort = DiploWalkerPorts.Volume

    [<Literal>]
    let private DefaultNetworkPort = DiploWalkerPorts.Network

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

    /// `https://pipe:/…` demande un pipe chiffre ; `http://pipe:/…` le laisse en
    /// clair. Le schéma est donc ce qui sélectionne le transport — sans lui,
    /// `https://` serait accepté puis silencieusement traité comme du HTTP.
    let private isPipeTlsAddress (uri: Uri) =
        String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)

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

    /// Options communes à tous les canaux.
    let private buildBaseOptions () =
        let options = GrpcChannelOptions()
        options.ServiceConfig <- buildRetryServiceConfig ()
        options.MaxSendMessageSize <- MaxMessageSize
        options.MaxReceiveMessageSize <- MaxMessageSize
        options

    let private buildCredentials () =
        let callCredentials = TokenInterceptor.createTokenCredentials ()

        let channelCredentials =
            ChannelCredentials.Create(ChannelCredentials.Insecure, callCredentials)

        let options = buildBaseOptions ()
        options.Credentials <- channelCredentials
        options.UnsafeUseInsecureChannelCallCredentials <- true
        (options, callCredentials)

    /// Injecte l'en-tête Authorization sur chaque requête.
    ///
    /// Cheminement TLS uniquement : GrpcChannel refuse une adresse `https://`
    /// dès lors que `GrpcChannelOptions.Credentials` est un composite insecure
    /// (« Channel is configured with insecure channel credentials and can't use
    /// a HttpClient with a 'https' scheme »). Or c'est ce composite qui portait
    /// les CallCredentials. Le jeton est donc ajouté ici explicitement — ce qui
    /// a l'avantage de l'envoyer par-dessus TLS, ce que le composite insecure
    /// ne pouvait pas garantir.
    ///
    /// `HttpMessageHandler.SendAsync` est `protected internal` : seul un type
    /// dérivé peut l'appeler, d'où `HttpMessageInvoker`, qui expose un
    /// `SendAsync` public.
    type private TokenInjectingHandler(inner: HttpMessageHandler) =
        inherit DelegatingHandler(inner)

        let invoker = new HttpMessageInvoker(inner)

        override _.SendAsync(request: HttpRequestMessage, ct: CancellationToken) =
            task {
                match AuthToken.loadToken () with
                | Some token -> request.Headers.Authorization <- Headers.AuthenticationHeaderValue("Bearer", token)
                | None -> ()

                return! invoker.SendAsync(request, ct)
            }

    let private createPipeChannel (pipeName: string) (useTls: bool) (thumbprintOverride: string option) =
        let options =
            if useTls then
                // Pas de Credentials ici : cf. TokenInjectingHandler.
                buildBaseOptions ()
            else
                let options, _ = buildCredentials ()
                options

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
        // Le flux brut est renvoyé tel quel : SocketsHttpHandler négocie TLS puis
        // ALPN « h2 » par-dessus, exactement comme sur un socket.
        handler.ConnectCallback <- connectCallback
        handler.UseProxy <- false
        handler.AllowAutoRedirect <- false

        let channelAddress =
            if useTls then
                // Aucune validation de nom n'est possible sur un tube : on
                // ancre l'empreinte du certificat serveur.
                handler.SslOptions.TargetHost <- PipeTls.TargetHost

                handler.SslOptions.RemoteCertificateValidationCallback <-
                    thumbprintOverride
                    |> Option.map PipeTls.pinExpectedCertificate
                    |> Option.defaultValue PipeTls.pinExpectedServerCertificate

                "https://" + PipeTls.TargetHost
            else
                "http://localhost"

        options.HttpHandler <-
            if useTls then
                new TokenInjectingHandler(handler) :> HttpMessageHandler
            else
                handler

        GrpcChannel.ForAddress(channelAddress, options)

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

    let private createWithThumbprint (address: string) (thumbprintOverride: string option) =
        // INVARIANT DE SÉCURITÉ : validateGrpcAddress restreint l'hôte à
        // localhost/pipe. Les callCredentials partent en clair sur un canal
        // insecure (UnsafeUseInsecureChannelCallCredentials) — tout
        // assouplissement de la validation exposerait le token sur le réseau.
        //
        // Le token n'est protégé que si le transport l'est aussi : sur un pipe,
        // `https://pipe:/…` chiffre la connexion (cf. PipeTls) alors que
        // `http://pipe:/…` la laisse en clair. Le client et le serveur doivent
        // donc être d'accord sur le schéma.
        SecurityValidation.validateGrpcAddress address
        let uri = Uri(address)

        // Le schéma participe à la clé de cache : un canal en clair ne doit
        // jamais être réinvesti pour une adresse `https://pipe:/…` (ni
        // l'inverse), sans quoi la connexion réutilisée court-circuiterait le
        // TLS et l'épinglage du certificat.
        let pipeTls = isPipeAddress uri && isPipeTlsAddress uri

        let key =
            if isPipeAddress uri then
                let scheme = if pipeTls then "https" else "http"
                // L'empreinte entre aussi dans la clé : deux ancrages distincts
                // ne doivent jamais partager un canal.
                let anchor = thumbprintOverride |> Option.map (fun t -> t + "/") |> Option.defaultValue ""
                sprintf "pipe://%s/%s%s" scheme anchor (uri.PathAndQuery.TrimStart('/'))
            else
                "tcp://" + uri.Authority

        let fresh =
            if isPipeAddress uri then
                createPipeChannel (uri.PathAndQuery.TrimStart('/')) pipeTls thumbprintOverride
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

    let private create (address: string) = createWithThumbprint address None

    let forContainer (port: int) = create $"http://localhost:{port}"
    let forVolume (port: int) = create $"http://localhost:{port}"
    let forNetwork (port: int) = create $"http://localhost:{port}"
    let forAddress (address: string) = create address

    /// Variante d'ancrage explicite, réservée aux tests.
    ///
    /// La CI n'active pas git-crypt sur le job `test` : le PFX de
    /// `certificates/leaf-tls-server/` y est illisible, et le test ne peut donc
    /// pas valider le certificat de production. Il en génère un à l'exécution
    /// et passe son empreinte ici. Le chemin applicatif, lui, reste ancré sur
    /// `PipeTls.ExpectedServerThumbprint`.
    let internal forAddressPinnedTo (address: string) (thumbprint: string) =
        createWithThumbprint address (Some thumbprint)

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


