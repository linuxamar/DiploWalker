open System
open System.Collections.Generic
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Serilog
open Serilog.Extensions.Hosting
open Diplo.Abstractions.ServerConfig
open Diplo.Grpc.Network
open Diplo.Network.Plugins
open Diplo.Network.Services

[<EntryPoint>]
let main args =
    Log.Logger <-
        let baseConfig =
            LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File("logs/diplo-network-.log", rollingInterval = RollingInterval.Day)
        let loggerConfig =
            if OperatingSystem.IsWindows() then
                baseConfig.WriteTo.EventLog(logName = "Diplo.Network", source = "Diplo.Network", manageEventSource = true)
            else
                baseConfig
        loggerConfig.CreateLogger()

    try
        Log.Information("Démarrage du service Diplo.Network")

        let builder = WebApplication.CreateBuilder(args)
        builder.Services.AddWindowsService(fun opts -> opts.ServiceName <- "Diplo.Network") |> ignore
        builder.Services.AddGrpc() |> ignore
        builder.Services.AddSerilog() |> ignore

        let drivers = Dictionary<NetworkDriver, INetworkDriver>()
        drivers.[NetworkDriver.Bridge] <- BridgeNetworkDriver() :> INetworkDriver
        drivers.[NetworkDriver.CustomCni] <- CustomCniDriver() :> INetworkDriver
        drivers.[NetworkDriver.``None``] <- NoneDriver() :> INetworkDriver
        builder.Services.AddSingleton<IReadOnlyDictionary<NetworkDriver, INetworkDriver>>(drivers :> IReadOnlyDictionary<_, _>) |> ignore
        builder.Services.AddSingleton<NetworkServiceImpl>() |> ignore

        builder.WebHost.ConfigureKestrel(fun ctx opts ->
            configureKestrel ctx.Configuration opts
        ) |> ignore

        builder.WebHost.UseNamedPipes(fun opts ->
            configureNamedPipeSecurity opts
        ) |> ignore

        let app = builder.Build()
        app.MapGrpcService<NetworkServiceImpl>() |> ignore
        app.Run()
        0
    with ex ->
        Log.Fatal(ex, "Le service Diplo.Network a échoué au démarrage")
        1
