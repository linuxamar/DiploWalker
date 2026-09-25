module DiploWalker.Abstractions.ServerConfig

open System
open System.Net
open System.IO.Pipes
open System.Security.AccessControl
open System.Security.Principal
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Grpc.AspNetCore.HealthChecks
open Microsoft.Extensions.Diagnostics.HealthChecks
open Serilog
open Serilog.Extensions.Hosting
open DiploWalker.Abstractions.TokenAuthMiddleware

/// Taille maximale des messages gRPC en octets (64 Mo).
///
/// Doit rester alignÃ©e sur le client (GrpcClientFactory.MaxMessageSize) : les
/// lectures/Ã©critures de fichiers sont bornÃ©es Ã  50 Mo cÃ´tÃ© API (M14), un
/// plafond serveur de 4 Mo les ferait Ã©chouer en ResourceExhausted.
let private grpcMaxMessageSize = 64 * 1024 * 1024

let configureKestrel (config: IConfiguration) (opts: KestrelServerOptions) =
    let grpcPort = config.GetValue<int>("ServiceSettings:GrpcPort")
    let pipeName = config.GetValue<string>("ServiceSettings:NamedPipeName")
    let useTcp = config.GetValue<bool>("ServiceSettings:UseTcp")
    let usePipes = config.GetValue<bool>("ServiceSettings:UseNamedPipes")

    if not useTcp && not usePipes then
        failwith "Configuration invalide : ni ServiceSettings:UseTcp ni ServiceSettings:UseNamedPipes sont activÃ©s"

    if useTcp then
        Log.Information("Ã‰coute TCP sur localhost:{Port}", grpcPort)

        opts.Listen(IPAddress.Loopback, grpcPort, fun listenOpts -> listenOpts.Protocols <- HttpProtocols.Http2)
        |> ignore

    if usePipes then
        Log.Information("Ã‰coute Named Pipe: {PipeName}", pipeName)

        opts.ListenNamedPipe(pipeName, fun listenOpts -> listenOpts.Protocols <- HttpProtocols.Http2)
        |> ignore

let configureNamedPipeSecurity (opts: NamedPipeTransportOptions) =
    // Kestrel active CurrentUserOnly par dÃ©faut ; il faut le dÃ©sactiver pour
    // fournir une PipeSecurity explicite (utilisateur courant uniquement).
    // NB : une rÃ¨gle "Deny Everyone" bloquerait aussi l'utilisateur courant
    // (sur Windows, les rÃ¨gles Deny priment sur les rÃ¨gles Allow).
    opts.CurrentUserOnly <- false
    let pipeSecurity = PipeSecurity()

    // WindowsIdentity dÃ©tient un handle de token : Ã  disposer.
    do
        use currentUser = WindowsIdentity.GetCurrent()

        let allowRule =
            PipeAccessRule(currentUser.User, PipeAccessRights.FullControl, AccessControlType.Allow)

        pipeSecurity.AddAccessRule(allowRule)

    opts.PipeSecurity <- pipeSecurity

let runGrpcHost
    (serviceName: string)
    (args: string[])
    (configureServices: WebApplicationBuilder -> unit)
    (mapGrpcService: WebApplication -> unit)
    =
    Log.Logger <-
        let baseConfig =
            LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File(
                    sprintf "logs/diplo-%s-.log" (serviceName.ToLowerInvariant()),
                    rollingInterval = RollingInterval.Day
                )

        let loggerConfig =
            if OperatingSystem.IsWindows() then
                baseConfig.WriteTo.EventLog(logName = serviceName, source = serviceName, manageEventSource = true)
            else
                baseConfig

        loggerConfig.CreateLogger()

    try
        Log.Information("DÃ©marrage du service {ServiceName}", serviceName)
        let builder = WebApplication.CreateBuilder(args)

        // Environnement par dÃ©faut alignÃ© sur la configuration de build :
        // sans lui, `dotnet run` dÃ©marre en Production et chargerait
        // appsettings.json (ports Release) mÃªme en Debug â€” incohÃ©rent avec
        // les clients compilÃ©s en Debug (5001-5003).
#if DEBUG
        if isNull (Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"))
           && isNull (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")) then
            builder.Environment.EnvironmentName <- "Development"
#else
        if isNull (Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"))
           && isNull (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")) then
            builder.Environment.EnvironmentName <- "Production"
#endif

        builder.Services.AddWindowsService(fun opts -> opts.ServiceName <- serviceName)
        |> ignore

        builder.Services.AddSerilog() |> ignore
        builder.Services.AddGrpcHealthChecks() |> ignore
        builder.Services.AddHealthChecks() |> ignore
        configureServices builder

        builder.Services.Configure<Grpc.AspNetCore.Server.GrpcServiceOptions>
            (fun (opts: Grpc.AspNetCore.Server.GrpcServiceOptions) ->
                opts.MaxReceiveMessageSize <- Nullable(grpcMaxMessageSize)
                opts.MaxSendMessageSize <- Nullable(grpcMaxMessageSize))
        |> ignore

        builder.WebHost.ConfigureKestrel(fun ctx opts -> configureKestrel ctx.Configuration opts)
        |> ignore

        builder.WebHost.UseNamedPipes(fun opts -> configureNamedPipeSecurity opts)
        |> ignore

        let app = builder.Build()
        app.UseMiddleware<TokenAuthMiddleware>() |> ignore
        mapGrpcService app
        app.MapGrpcHealthChecksService() |> ignore
        app.MapHealthChecks("/healthz") |> ignore

        let lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>()

        lifetime.ApplicationStarted.Register(fun () -> Log.Information("Service {ServiceName} dÃ©marrÃ©", serviceName))
        |> ignore

        lifetime.ApplicationStopping.Register(fun () ->
            // M9 : lÃ¢cher le Timer de purge du rate limiter (au lieu de laisser
            // un Timer racine actif aprÃ¨s l'arrÃªt).
            TokenAuthMiddleware.disposePurge ()
            Log.Information("Service {ServiceName} arrÃªt en cours...", serviceName))
        |> ignore

        lifetime.ApplicationStopped.Register(fun () -> Log.Information("Service {ServiceName} arrÃªtÃ©", serviceName))
        |> ignore

        app.Run()
        0
    with ex ->
        Log.Fatal(ex, "Le service {ServiceName} a Ã©chouÃ© au dÃ©marrage", serviceName)
        1

