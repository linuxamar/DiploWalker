open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Diplo.Abstractions.Interfaces
open Diplo.Abstractions.ServerConfig
open Diplo.Container.Clients
open Diplo.Container.Services
open Diplo.Disk
open ProtoBuf.Grpc.Server

[<EntryPoint>]
let main args =
    runGrpcHost
        "Diplo.Container"
        args
        (fun builder ->
            builder.Services.AddCodeFirstGrpc() |> ignore
            builder.Services.AddSingleton<IProcessRunner>(ProcessRunner()) |> ignore

            builder.Services.AddSingleton<IContainerdClient>(fun sp ->
                let runner = sp.GetRequiredService<IProcessRunner>()
                ContainerdClient(runner) :> IContainerdClient)
            |> ignore

            builder.Services.AddSingleton<IDiskMounter>(DiskMounter()) |> ignore
            builder.Services.AddSingleton<ContainerServiceImpl>(fun sp ->
                ContainerServiceImpl(
                    sp.GetRequiredService<IContainerdClient>(),
                    sp.GetRequiredService<IDiskMounter>()
                ))
            |> ignore)
        (fun app -> app.MapGrpcService<ContainerServiceImpl>() |> ignore)
