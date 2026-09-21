open System.Net.Http
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open DiploWalker.Abstractions.Interfaces
open DiploWalker.Abstractions.ServerConfig
open DiploWalker.Container.Clients
open DiploWalker.Container.Services
open DiploWalker.Disk
open ProtoBuf.Grpc.Server

[<EntryPoint>]
let main args =
    runGrpcHost
        "DiploWalker.Container"
        args
        (fun builder ->
            builder.Services.AddCodeFirstGrpc() |> ignore
            builder.Services.AddSingleton<IProcessRunner>(ProcessRunner()) |> ignore

            builder.Services.AddSingleton<IContainerdClient>(fun sp ->
                let runner = sp.GetRequiredService<IProcessRunner>()
                ContainerdClient(runner) :> IContainerdClient)
            |> ignore

            builder.Services.AddSingleton<IDiskMounter>(DiskMounter()) |> ignore
            builder.Services.AddSingleton<HttpClient>(RegistrySearch.sharedClient) |> ignore
            builder.Services.AddSingleton<ContainerServiceImpl>(fun sp ->
                ContainerServiceImpl(
                    sp.GetRequiredService<IContainerdClient>(),
                    sp.GetRequiredService<IDiskMounter>(),
                    sp.GetRequiredService<HttpClient>()
                ))
            |> ignore)
        (fun app -> app.MapGrpcService<ContainerServiceImpl>() |> ignore)


