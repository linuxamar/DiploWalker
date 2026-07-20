open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Serilog
open Serilog.Extensions.Hosting
open Diplo.Abstractions.ServerConfig
open Diplo.Abstractions.Interfaces
open Diplo.Container.Clients
open Diplo.Container.Services

[<EntryPoint>]
let main args =
    Log.Logger <-
        let baseConfig =
            LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File("logs/diplo-container-.log", rollingInterval = RollingInterval.Day)
        let loggerConfig =
            if OperatingSystem.IsWindows() then
                baseConfig.WriteTo.EventLog(logName = "Diplo.Container", source = "Diplo.Container", manageEventSource = true)
            else
                baseConfig
        loggerConfig.CreateLogger()

    try
        Log.Information("Démarrage du service Diplo.Container")

        let builder = WebApplication.CreateBuilder(args)
        builder.Services.AddWindowsService(fun opts -> opts.ServiceName <- "Diplo.Container") |> ignore
        builder.Services.AddGrpc() |> ignore
        builder.Services.AddSerilog() |> ignore
        builder.Services.AddSingleton<IProcessRunner>(ProcessRunner()) |> ignore
        builder.Services.AddSingleton<IContainerdClient>(fun sp ->
            let runner = sp.GetRequiredService<IProcessRunner>()
            ContainerdClient(runner) :> IContainerdClient) |> ignore
        builder.Services.AddSingleton<ContainerServiceImpl>() |> ignore

        builder.WebHost.ConfigureKestrel(fun ctx opts ->
            configureKestrel ctx.Configuration opts
        ) |> ignore

        builder.WebHost.UseNamedPipes(fun opts ->
            configureNamedPipeSecurity opts
        ) |> ignore

        let app = builder.Build()
        app.MapGrpcService<ContainerServiceImpl>() |> ignore
        app.Run()
        0
    with ex ->
        Log.Fatal(ex, "Le service Diplo.Container a échoué au démarrage")
        1
