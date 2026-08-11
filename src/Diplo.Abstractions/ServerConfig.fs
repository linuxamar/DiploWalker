module Diplo.Abstractions.ServerConfig

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
open Diplo.Abstractions.TokenAuthMiddleware

/// Taille maximale des messages gRPC en octets (4 Mo).
let private grpcMaxMessageSize = 4 * 1024 * 1024

let configureKestrel (config: IConfiguration) (opts: KestrelServerOptions) =
    let grpcPort = config.GetValue<int>("ServiceSettings:GrpcPort")
    let pipeName = config.GetValue<string>("ServiceSettings:NamedPipeName")
    let useTcp = config.GetValue<bool>("ServiceSettings:UseTcp")
    let usePipes = config.GetValue<bool>("ServiceSettings:UseNamedPipes")

    if useTcp then
        Log.Information("Écoute TCP sur localhost:{Port}", grpcPort)
        opts.Listen(IPAddress.Loopback, grpcPort, fun listenOpts ->
            listenOpts.Protocols <- HttpProtocols.Http2) |> ignore
    if usePipes then
        Log.Information("Écoute Named Pipe: {PipeName}", pipeName)
        opts.ListenNamedPipe(pipeName, fun listenOpts ->
            listenOpts.Protocols <- HttpProtocols.Http2) |> ignore

let configureNamedPipeSecurity (opts: NamedPipeTransportOptions) =
    // Kestrel active CurrentUserOnly par défaut ; il faut le désactiver pour
    // fournir une PipeSecurity explicite (utilisateur courant + refus Everyone).
    opts.CurrentUserOnly <- false
    let pipeSecurity = PipeSecurity()
    let currentUser = WindowsIdentity.GetCurrent()
    let allowRule = PipeAccessRule(
        currentUser.User,
        PipeAccessRights.ReadWrite,
        AccessControlType.Allow)
    pipeSecurity.AddAccessRule(allowRule)
    let everyone = SecurityIdentifier(WellKnownSidType.WorldSid, null)
    let denyRule = PipeAccessRule(
        everyone,
        PipeAccessRights.FullControl,
        AccessControlType.Deny)
    pipeSecurity.AddAccessRule(denyRule)
    opts.PipeSecurity <- pipeSecurity

let runGrpcHost (serviceName: string) (args: string[]) (configureServices: WebApplicationBuilder -> unit) (mapGrpcService: WebApplication -> unit) =
    Log.Logger <-
        let baseConfig =
            LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File(sprintf "logs/diplo-%s-.log" (serviceName.ToLowerInvariant()), rollingInterval = RollingInterval.Day)
        let loggerConfig =
            if OperatingSystem.IsWindows() then
                baseConfig.WriteTo.EventLog(logName = serviceName, source = serviceName, manageEventSource = true)
            else
                baseConfig
        loggerConfig.CreateLogger()

    try
        Log.Information("Démarrage du service {ServiceName}", serviceName)
        let builder = WebApplication.CreateBuilder(args)
        builder.Services.AddWindowsService(fun opts -> opts.ServiceName <- serviceName) |> ignore
        builder.Services.AddSerilog() |> ignore
        builder.Services.AddGrpcHealthChecks() |> ignore
        builder.Services.AddHealthChecks() |> ignore
        configureServices builder
        builder.Services.Configure<Grpc.AspNetCore.Server.GrpcServiceOptions>(fun (opts: Grpc.AspNetCore.Server.GrpcServiceOptions) ->
            opts.MaxReceiveMessageSize <- Nullable(grpcMaxMessageSize)
            opts.MaxSendMessageSize <- Nullable(grpcMaxMessageSize)) |> ignore
        builder.WebHost.ConfigureKestrel(fun ctx opts -> configureKestrel ctx.Configuration opts) |> ignore
        builder.WebHost.UseNamedPipes(fun opts -> configureNamedPipeSecurity opts) |> ignore
        let app = builder.Build()
        app.UseMiddleware<TokenAuthMiddleware>() |> ignore
        mapGrpcService app
        app.MapGrpcHealthChecksService() |> ignore
        app.MapHealthChecks("/healthz") |> ignore

        let lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>()
        lifetime.ApplicationStarted.Register(fun () -> Log.Information("Service {ServiceName} démarré", serviceName)) |> ignore
        lifetime.ApplicationStopping.Register(fun () -> Log.Information("Service {ServiceName} arrêt en cours...", serviceName)) |> ignore
        lifetime.ApplicationStopped.Register(fun () -> Log.Information("Service {ServiceName} arrêté", serviceName)) |> ignore

        app.Run()
        0
    with ex ->
        Log.Fatal(ex, "Le service {ServiceName} a échoué au démarrage", serviceName)
        1
