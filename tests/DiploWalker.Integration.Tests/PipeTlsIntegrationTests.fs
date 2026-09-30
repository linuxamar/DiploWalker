namespace DiploWalker.Integration.Tests

/// Tests de bout en bout du transport TLS sur named pipe.
///
/// Ces tests génèrent leur propre certificat : le PFX de production
/// (`certificates/leaf-tls-server/`) est chiffré par git-crypt et illisible
/// sur le job `test` de la CI. L'ancrage est donc transmis explicitement via
/// `GrpcClientFactory.forAddressPinnedTo`, qui reproduit exactement le
/// chemin de production (`PipeTls.pinExpectedServerCertificate`) avec une
/// autre valeur d'empreinte.
module PipeTlsIntegrationTests =

    open System
    open System.Collections.Generic
    open System.IO
    open System.Net
    open System.Security.Cryptography
    open System.Security.Cryptography.X509Certificates
    open System.Threading
    open System.Threading.Tasks
    open Microsoft.AspNetCore.Builder
    open Microsoft.AspNetCore.Hosting
    open Microsoft.AspNetCore.Http
    open Microsoft.AspNetCore.Server.Kestrel.Core
    open Microsoft.Extensions.DependencyInjection
    open Xunit
    open FsUnit.Xunit
    open ProtoBuf.Grpc.Client
    open ProtoBuf.Grpc.Server
    open DiploWalker.Grpc
    open DiploWalker.Grpc.Volume
    open DiploWalker.Core.Connection
    open DiploWalker.Volume.Drivers
    open DiploWalker.Volume.Services
    open DiploWalker.Abstractions
    open DiploWalker.Abstractions.SecurityValidation
    open DiploWalker.TestHelpers

    do addAllowedVolumeDir (Path.GetTempPath())

    /// Certificat serveur auto-signé, équivalent à `leaf-tls-server` : EKU
    /// serverAuth, SAN localhost. RSA 2048 suffit ici — la PKI du dépôt est en
    /// 8192 par convention, mais la taille n'a aucune incidence sur le test du
    /// transport et 2048 garde l'exécution rapide.
    let private createServerCertificate () =
        use rsa = RSA.Create(2048)

        let request =
            CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)

        let san = SubjectAlternativeNameBuilder()
        san.AddDnsName("localhost")
        san.AddIpAddress(IPAddress.Loopback)
        san.AddIpAddress(IPAddress.IPv6Loopback)
        request.CertificateExtensions.Add(san.Build())

        let eku = OidCollection()
        eku.Add(Oid("1.3.6.1.5.5.7.3.1")) |> ignore // serverAuth
        request.CertificateExtensions.Add(X509EnhancedKeyUsageExtension(eku, false))

        let generated =
            request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1))

        // Retourne la clé privée avec le certificat : UseHttps a besoin des deux.
        X509CertificateLoader.LoadPkcs12(
            generated.Export(X509ContentType.Pfx),
            "",
            X509KeyStorageFlags.Exportable
        )

    /// Démarre un service de volume sur un named pipe, en TLS si
    /// `certificate` est fourni, sinon en clair. `onRequest` est appelé pour
    /// chaque requête HTTP reçue, avant le dispatch gRPC : c'est là qu'on
    /// observe les en-têtes réellement arrivant sur le tube.
    let private startPipeApp (pipeName: string) (certificate: X509Certificate2 option) (onRequest: HttpContext -> unit) =
        let dataRoot = TestHelpers.createTempDir "pipetls"
        let builder = WebApplication.CreateBuilder()
        builder.Services.AddCodeFirstGrpc() |> ignore

        builder.Services.AddSingleton<VolumeDriverRegistry>(fun _ ->
            let reg = VolumeDriverRegistry()
            reg.Register(StorageDriverType.Local, LocalVolumeDriver(dataRoot) :> Interfaces.IVolumeDriver)
            reg)
        |> ignore

        builder.Services.AddSingleton<VolumeServiceImpl>() |> ignore

        builder.WebHost.ConfigureKestrel(fun opts ->
            match certificate with
            | Some cert ->
                opts.ListenNamedPipe(
                    pipeName,
                    fun lo ->
                        lo.Protocols <- HttpProtocols.Http2
                        lo.UseHttps(cert) |> ignore
                )
                |> ignore
            | None ->
                opts.ListenNamedPipe(pipeName, fun lo -> lo.Protocols <- HttpProtocols.Http2)
                |> ignore)
        |> ignore

        let app = builder.Build()

        // Surcharge explicitée : `Func<HttpContext, Func<Task>, Task>` est la
        // seule qui accepte un corps en deux instructions. `next` est un
        // `Func<Task>` déjà lié au contexte, d'où `Invoke()` sans argument.
        app.Use(
            Func<HttpContext, Func<Task>, Task>(fun ctx next ->
                onRequest ctx
                next.Invoke())
        )
        |> ignore
        app.MapGrpcService<VolumeServiceImpl>() |> ignore
        app.StartAsync().GetAwaiter().GetResult()
        app, dataRoot

    let private stopApp (app: WebApplication) (dataRoot: string) =
        app.StopAsync().GetAwaiter().GetResult()
        (app :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()
        TestHelpers.cleanupDir dataRoot

    let private uniquePipe () = "diplo-pipetls-test-" + Guid.NewGuid().ToString("N")

    /// Observations d'en-tête inutiles : garde la signature de `startPipeApp`
    /// stable pour les tests qui ne se intéressent pas au transport HTTP.
    let private noRequest (_: HttpContext) = ()

    let private newVolumeRequest name =
        { Name = name
          Driver = StorageDriverType.Local
          DriverOpts = Dictionary()
          Labels = Dictionary() }

    /// `.GetAwaiter().GetResult()` et non `.Result` : ce dernier emballe toute
    /// exception dans une AggregateException, ce qui masquerait le type réel
    /// (RpcException) que ces tests veulent vérifier.
    let private createVolume (client: IVolumeService) (name: string) =
        client.CreateVolume(newVolumeRequest name, CancellationToken.None).GetAwaiter().GetResult()

    [<Fact>]
    [<Trait("Platform", "Windows")>]
    /// Preuve que le chiffrement fonctionne réellement de bout en bout : TLS,
    /// ALPN « h2 », épinglage de l'empreinte et gRPC sur un même named pipe.
    let ``CreateVolume via named pipe chiffre fonctionne de bout en bout`` () =
        let pipeName = uniquePipe()
        use certificate = createServerCertificate ()
        let app, dataRoot = startPipeApp pipeName (Some certificate) noRequest

        try
            let address = sprintf "https://pipe:/%s" pipeName
            use channel = GrpcClientFactory.forAddressPinnedTo address certificate.Thumbprint
            let client = channel.CreateGrpcService<IVolumeService>()
            let result = createVolume client "tls-volume"
            result.Name |> should equal "tls-volume"
        finally
            stopApp app dataRoot

    [<Fact>]
    [<Trait("Platform", "Windows")>]
    /// L'ancrage protège : un service porteur d'un autre certificat (typiquement
    /// un pipe usurpé par un processus du même utilisateur) doit être refusé.
    let ``Un certificat different de l'empreinte attendue est refuse`` () =
        let pipeName = uniquePipe()
        use imposteur = createServerCertificate ()
        use attendu = createServerCertificate ()
        let app, dataRoot = startPipeApp pipeName (Some imposteur) noRequest

        try
            let address = sprintf "https://pipe:/%s" pipeName
            use channel = GrpcClientFactory.forAddressPinnedTo address attendu.Thumbprint
            let client = channel.CreateGrpcService<IVolumeService>()

            // `|> ignore` : sans lui la lambda renvoie une valeur et FsUnit
            // compare la fonction au lieu de l'invoquer.
            let call () = createVolume client "refuse" |> ignore
            call |> should throw typeof<Grpc.Core.RpcException>
        finally
            stopApp app dataRoot

    [<Fact>]
    [<Trait("Platform", "Windows")>]
    /// `https://pipe:/…` ne doit pas pouvoir se rabattre silencieusement sur un
    /// service en clair : le client exige le TLS.
    let ``Un service en clair refuse une adresse https`` () =
        let pipeName = uniquePipe()
        use certificate = createServerCertificate ()
        let app, dataRoot = startPipeApp pipeName None noRequest

        try
            let address = sprintf "https://pipe:/%s" pipeName
            use channel = GrpcClientFactory.forAddressPinnedTo address certificate.Thumbprint
            let client = channel.CreateGrpcService<IVolumeService>()

            let call () = createVolume client "refuse" |> ignore
            call |> should throw typeof<Grpc.Core.RpcException>
        finally
            stopApp app dataRoot

    [<Fact>]
    [<Trait("Platform", "Windows")>]
    /// Non-régression : `http://pipe:/…` continue de fonctionner en clair.
    let ``Le pipe en clair reste supporte`` () =
        let pipeName = uniquePipe()
        let app, dataRoot = startPipeApp pipeName None noRequest

        try
            let address = sprintf "http://pipe:/%s" pipeName
            use channel = DiploWalkerChannel.forAddress address
            let client = channel.CreateGrpcService<IVolumeService>()
            let result = createVolume client "plain-volume"
            result.Name |> should equal "plain-volume"
        finally
            stopApp app dataRoot

    [<Fact>]
    [<Trait("Platform", "Windows")>]
    /// Le chemin TLS ne porte pas de `ChannelCredentials` (GrpcChannel les refuse
    /// sur une adresse `https`) : c'est `TokenInjectingHandler` qui ajoute
    /// l'en-tête. Sans ce test, une régression supprimant le jeton ne serait
    /// visible par aucun autre test de ce fichier, tous non authentifiés.
    let ``Le jeton d'authentification est transmis par-dessus TLS`` () =
        let pipeName = uniquePipe()
        use certificate = createServerCertificate ()

        let observed = ResizeArray<string>()

        let observe (ctx: HttpContext) =
            match ctx.Request.Headers.TryGetValue("Authorization") with
            | true, value -> observed.Add(value.ToString())
            | _ -> observed.Add "<absent>"

        let app, dataRoot = startPipeApp pipeName (Some certificate) observe

        // `tokenPath` est un état global : on le redirige le temps du test puis
        // on le restaure, comme le fait DiploWalker.Abstractions.Tests.
        let previousTokenPath = AuthToken.tokenPath ()
        let tokenDir = TestHelpers.createTempDir "pipetls-token"
        let token = AuthToken.generateToken ()
        AuthToken.setTokenPath (Path.Combine(tokenDir, "auth-token.json"))
        AuthToken.saveToken token

        try
            let address = sprintf "https://pipe:/%s" pipeName
            use channel = GrpcClientFactory.forAddressPinnedTo address certificate.Thumbprint
            let client = channel.CreateGrpcService<IVolumeService>()
            let result = createVolume client "auth-volume"
            result.Name |> should equal "auth-volume"

            observed.Count |> should be (greaterThan 0)
            observed |> Seq.forall (fun h -> h = sprintf "Bearer %s" token) |> should equal true
        finally
            AuthToken.setTokenPath previousTokenPath
            stopApp app dataRoot
            TestHelpers.cleanupDir tokenDir
