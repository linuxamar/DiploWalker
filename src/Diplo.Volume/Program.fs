open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Serilog
open Serilog.Extensions.Hosting
open Diplo.Abstractions.ServerConfig
open Diplo.Abstractions.AuthToken
open Diplo.Abstractions.TokenAuthMiddleware
open Diplo.Abstractions.Interfaces
open Diplo.Volume.Drivers
open Diplo.Volume.Services

[<EntryPoint>]
let main args =
    Log.Logger <-
        let baseConfig =
            LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File("logs/diplo-volume-.log", rollingInterval = RollingInterval.Day)
        let loggerConfig =
            if OperatingSystem.IsWindows() then
                baseConfig.WriteTo.EventLog(logName = "Diplo.Volume", source = "Diplo.Volume", manageEventSource = true)
            else
                baseConfig
        loggerConfig.CreateLogger()

    try
        Log.Information("Démarrage du service Diplo.Volume")

        let builder = WebApplication.CreateBuilder(args)
        builder.Services.AddWindowsService(fun opts -> opts.ServiceName <- "Diplo.Volume") |> ignore
        builder.Services.AddGrpc() |> ignore
        builder.Services.AddSerilog() |> ignore
        builder.Services.AddSingleton<IVolumeDriver>(fun sp ->
            let value = sp.GetRequiredService<IConfiguration>().GetValue<string>("VolumeDataRoot")
            let dataRoot = if System.String.IsNullOrEmpty(value) then "C:\\ProgramData\\Diplo\\Volume" else value
            LocalVolumeDriver(dataRoot) :> IVolumeDriver) |> ignore
        builder.Services.AddSingleton<VolumeServiceImpl>() |> ignore

        builder.WebHost.ConfigureKestrel(fun ctx opts ->
            configureKestrel ctx.Configuration opts
        ) |> ignore

        builder.WebHost.UseNamedPipes(fun opts ->
            configureNamedPipeSecurity opts
        ) |> ignore

        let app = builder.Build()
        app.UseMiddleware<TokenAuthMiddleware>() |> ignore
        app.MapGrpcService<VolumeServiceImpl>() |> ignore
        app.Run()
        0
    with ex ->
        Log.Fatal(ex, "Le service Diplo.Volume a échoué au démarrage")
        1
