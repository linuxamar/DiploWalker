open System.Collections.Generic
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Diplo.Abstractions.ServerConfig
open Diplo.Grpc.Network
open Diplo.Network.Plugins
open Diplo.Network.Services
open ProtoBuf.Grpc.Server

[<EntryPoint>]
let main args =
    runGrpcHost "Diplo.Network" args
        (fun builder ->
            builder.Services.AddCodeFirstGrpc() |> ignore
            let drivers = Dictionary<NetworkDriver, INetworkDriver>()
            drivers.[NetworkDriver.Bridge] <- BridgeNetworkDriver() :> INetworkDriver
            drivers.[NetworkDriver.CustomCni] <- CustomCniDriver() :> INetworkDriver
            drivers.[NetworkDriver.``None``] <- NoneDriver() :> INetworkDriver
            drivers.[NetworkDriver.``Pod``] <- PodDriver() :> INetworkDriver
            builder.Services.AddSingleton<IReadOnlyDictionary<NetworkDriver, INetworkDriver>>(drivers :> IReadOnlyDictionary<_, _>) |> ignore
            builder.Services.AddSingleton<NetworkServiceImpl>() |> ignore)
        (fun app -> app.MapGrpcService<NetworkServiceImpl>() |> ignore)
