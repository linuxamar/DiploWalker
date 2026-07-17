open System.IO
open System.Net
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Serilog
open Serilog.Extensions.Hosting
open Diplo.Abstractions.Interfaces
open Diplo.Container.Clients
open Diplo.Container.Services

[<EntryPoint>]
let main args =
    Log.Logger <- LoggerConfiguration()
        .WriteTo.Console()
        .WriteTo.EventLog(logName = "Diplo.Container", source = "Diplo.Container", manageEventSource = true)
        .WriteTo.File("logs/diplo-container-.log", rollingInterval = RollingInterval.Day)
        .CreateLogger()

    try
        Log.Information("Démarrage du service Diplo.Container")

        let builder = WebApplication.CreateBuilder(args)
        builder.Services.AddWindowsService(fun opts -> opts.ServiceName <- "Diplo.Container") |> ignore
        builder.Services.AddGrpc() |> ignore
        builder.Services.AddSerilog() |> ignore
        builder.Services.AddSingleton<IContainerdClient>(fun sp ->
            let socket = sp.GetRequiredService<IConfiguration>().GetValue<string>("ContainerdSocket")
            ContainerdClient(if System.String.IsNullOrEmpty(socket) then "unix:///run/containerd/containerd.sock" else socket) :> IContainerdClient) |> ignore
        builder.Services.AddSingleton<ContainerServiceImpl>() |> ignore

        builder.WebHost.ConfigureKestrel(fun (ctx: WebHostBuilderContext) (opts: KestrelServerOptions) ->
            let config = ctx.Configuration
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
        ) |> ignore

        let app = builder.Build()
        app.MapGrpcService<ContainerServiceImpl>() |> ignore
        app.Run()
        0
    with ex ->
        Log.Fatal(ex, "Le service Diplo.Container a échoué au démarrage")
        1
