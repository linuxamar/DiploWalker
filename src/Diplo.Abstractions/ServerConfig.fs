module Diplo.Abstractions.ServerConfig

open System.Net
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.Extensions.Configuration
open Serilog

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
