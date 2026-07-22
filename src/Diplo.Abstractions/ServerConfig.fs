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
open Serilog
open Serilog.Extensions.Hosting
open Diplo.Abstractions.TokenAuthMiddleware

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
        configureServices builder
        builder.WebHost.ConfigureKestrel(fun ctx opts -> configureKestrel ctx.Configuration opts) |> ignore
        builder.WebHost.UseNamedPipes(fun opts -> configureNamedPipeSecurity opts) |> ignore
        let app = builder.Build()
        app.UseMiddleware<TokenAuthMiddleware>() |> ignore
        mapGrpcService app
        app.Run()
        0
    with ex ->
        Log.Fatal(ex, "Le service {ServiceName} a échoué au démarrage", serviceName)
        1
