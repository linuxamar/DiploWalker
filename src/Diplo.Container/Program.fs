open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Diplo.Abstractions.Interfaces
open Diplo.Abstractions.ServerConfig
open Diplo.Container.Clients
open Diplo.Container.Services

[<EntryPoint>]
let main args =
    runGrpcHost "Diplo.Container" args
        (fun builder ->
            builder.Services.AddGrpc() |> ignore
            builder.Services.AddSingleton<IProcessRunner>(ProcessRunner()) |> ignore
            builder.Services.AddSingleton<IContainerdClient>(fun sp ->
                let runner = sp.GetRequiredService<IProcessRunner>()
                ContainerdClient(runner) :> IContainerdClient) |> ignore
            builder.Services.AddSingleton<ContainerServiceImpl>() |> ignore)
        (fun app -> app.MapGrpcService<ContainerServiceImpl>() |> ignore)
