open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Diplo.Abstractions.Interfaces
open Diplo.Abstractions.ServerConfig
open Diplo.Grpc.Volume
open Diplo.Volume.Drivers
open Diplo.Volume.Services
open ProtoBuf.Grpc.Server

[<EntryPoint>]
let main args =
    runGrpcHost "Diplo.Volume" args
        (fun builder ->
            builder.Services.AddCodeFirstGrpc() |> ignore

            builder.Services.AddSingleton<VolumeDriverRegistry>(fun sp ->
                let config = sp.GetRequiredService<IConfiguration>()
                let getDataRoot (key: string) (defaultSubDir: string) =
                    let value = config.GetValue<string>(key)
                    if System.String.IsNullOrEmpty(value) then
                        System.IO.Path.Combine("C:\\ProgramData\\Diplo\\Volume", defaultSubDir)
                    else value

                let registry = VolumeDriverRegistry()
                registry.Register(StorageDriverType.Local, LocalVolumeDriver(getDataRoot "VolumeDataRoot" "local"))
                registry.Register(StorageDriverType.Nfs, NfsDriver(getDataRoot "NfsDataRoot" "nfs"))
                registry.Register(StorageDriverType.Smb, SmbDriver(getDataRoot "SmbDataRoot" "smb"))
                registry.Register(StorageDriverType.CloudAzure, CloudAzureDriver(getDataRoot "AzureDataRoot" "azure"))
                registry.Register(StorageDriverType.CloudAws, CloudAwsDriver(getDataRoot "AwsDataRoot" "aws"))
                registry.Register(StorageDriverType.CloudGcp, CloudGcpDriver(getDataRoot "GcpDataRoot" "gcp"))
                registry.Register(StorageDriverType.Iso, IsoDriver(getDataRoot "IsoDataRoot" "iso"))
                registry) |> ignore

            builder.Services.AddSingleton<VolumeServiceImpl>() |> ignore)
        (fun app -> app.MapGrpcService<VolumeServiceImpl>() |> ignore)
