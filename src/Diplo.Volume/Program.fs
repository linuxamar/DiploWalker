open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Diplo.Abstractions.Interfaces
open Diplo.Abstractions.ServerConfig
open Diplo.Volume.Drivers
open Diplo.Volume.Services

[<EntryPoint>]
let main args =
    runGrpcHost "Diplo.Volume" args
        (fun builder ->
            builder.Services.AddGrpc() |> ignore
            builder.Services.AddSingleton<IVolumeDriver>(fun sp ->
                let value = sp.GetRequiredService<IConfiguration>().GetValue<string>("VolumeDataRoot")
                let dataRoot = if System.String.IsNullOrEmpty(value) then "C:\\ProgramData\\Diplo\\Volume" else value
                LocalVolumeDriver(dataRoot) :> IVolumeDriver) |> ignore
            builder.Services.AddSingleton<VolumeServiceImpl>() |> ignore)
        (fun app -> app.MapGrpcService<VolumeServiceImpl>() |> ignore)
