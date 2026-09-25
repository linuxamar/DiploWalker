namespace DiploWalker.Grpc

open DiploWalker.Grpc.Volume
open DiploWalker.Grpc.Network

/// Mapping centralisÃ© entre les types enum de drivers et leurs reprÃ©sentations texte.
[<RequireQualifiedAccess>]
module DriverMappings =

    // â”€â”€ Volume drivers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    let volumeDriverToString (dt: StorageDriverType) =
        match dt with
        | StorageDriverType.Local -> "local"
        | StorageDriverType.Nfs -> "nfs"
        | StorageDriverType.Smb -> "smb"
        | StorageDriverType.CloudAzure -> "azure"
        | StorageDriverType.CloudAws -> "aws"
        | StorageDriverType.CloudGcp -> "gcp"
        | StorageDriverType.Iso -> "iso"
        | _ -> "local"

    let parseVolumeDriver (s: string) =
        match s.ToLowerInvariant() with
        | "local" -> StorageDriverType.Local
        | "nfs" -> StorageDriverType.Nfs
        | "smb" -> StorageDriverType.Smb
        | "azure" -> StorageDriverType.CloudAzure
        | "aws" -> StorageDriverType.CloudAws
        | "gcp" -> StorageDriverType.CloudGcp
        | "iso" -> StorageDriverType.Iso
        | _ -> StorageDriverType.Local

    let isValidVolumeDriver (s: string) =
        let lower = s.ToLowerInvariant()

        lower = "local"
        || lower = "nfs"
        || lower = "smb"
        || lower = "azure"
        || lower = "aws"
        || lower = "gcp"
        || lower = "iso"

    let allVolumeDriverNames = [ "local"; "nfs"; "smb"; "azure"; "aws"; "gcp"; "iso" ]

    // â”€â”€ Network drivers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    let networkDriverToString (dt: NetworkDriver) =
        match dt with
        | NetworkDriver.Bridge -> "bridge"
        | NetworkDriver.None -> "none"
        | NetworkDriver.CustomCni -> "custom_cni"
        | NetworkDriver.Pod -> "pod"
        | _ -> "bridge"

    let parseNetworkDriver (s: string) =
        match s.ToLowerInvariant() with
        | "bridge" -> NetworkDriver.Bridge
        | "none" -> NetworkDriver.None
        | "custom_cni"
        | "cni" -> NetworkDriver.CustomCni
        | "pod" -> NetworkDriver.Pod
        | _ -> NetworkDriver.Bridge

    let isValidNetworkDriver (s: string) =
        let lower = s.ToLowerInvariant()

        lower = "bridge"
        || lower = "none"
        || lower = "custom_cni"
        || lower = "cni"
        || lower = "pod"

    let allNetworkDriverNames = [ "bridge"; "none"; "custom_cni"; "pod" ]

