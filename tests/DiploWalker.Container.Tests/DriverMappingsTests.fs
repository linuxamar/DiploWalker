namespace DiploWalker.Container.Tests

open Xunit
open FsUnit.Xunit
open DiploWalker.Grpc
open DiploWalker.Grpc.Volume
open DiploWalker.Grpc.Network

type DriverMappingsTests() =

    // â”€â”€ Volume drivers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``volumeDriverToString retourne local pour Local`` () =
        DriverMappings.volumeDriverToString StorageDriverType.Local
        |> should equal "local"

    [<Fact>]
    let ``volumeDriverToString retourne nfs pour Nfs`` () =
        DriverMappings.volumeDriverToString StorageDriverType.Nfs |> should equal "nfs"

    [<Fact>]
    let ``volumeDriverToString retourne smb pour Smb`` () =
        DriverMappings.volumeDriverToString StorageDriverType.Smb |> should equal "smb"

    [<Fact>]
    let ``volumeDriverToString retourne azure pour CloudAzure`` () =
        DriverMappings.volumeDriverToString StorageDriverType.CloudAzure
        |> should equal "azure"

    [<Fact>]
    let ``volumeDriverToString retourne aws pour CloudAws`` () =
        DriverMappings.volumeDriverToString StorageDriverType.CloudAws
        |> should equal "aws"

    [<Fact>]
    let ``volumeDriverToString retourne gcp pour CloudGcp`` () =
        DriverMappings.volumeDriverToString StorageDriverType.CloudGcp
        |> should equal "gcp"

    [<Fact>]
    let ``volumeDriverToString retourne iso pour Iso`` () =
        DriverMappings.volumeDriverToString StorageDriverType.Iso |> should equal "iso"

    [<Fact>]
    let ``volumeDriverToString retombe sur local pour une valeur inconnue`` () =
        let unknown = LanguagePrimitives.EnumOfValue<int, StorageDriverType>(99)
        DriverMappings.volumeDriverToString unknown |> should equal "local"

    [<Fact>]
    let ``parseVolumeDriver retourne Local pour local`` () =
        DriverMappings.parseVolumeDriver "local" |> should equal StorageDriverType.Local

    [<Fact>]
    let ``parseVolumeDriver retourne Smb pour smb`` () =
        DriverMappings.parseVolumeDriver "smb" |> should equal StorageDriverType.Smb

    [<Fact>]
    let ``parseVolumeDriver retourne CloudAzure pour azure`` () =
        DriverMappings.parseVolumeDriver "azure" |> should equal StorageDriverType.CloudAzure

    [<Fact>]
    let ``parseVolumeDriver retourne CloudAws pour aws`` () =
        DriverMappings.parseVolumeDriver "aws" |> should equal StorageDriverType.CloudAws

    [<Fact>]
    let ``parseVolumeDriver retourne CloudGcp pour gcp`` () =
        DriverMappings.parseVolumeDriver "gcp" |> should equal StorageDriverType.CloudGcp

    [<Fact>]
    let ``parseVolumeDriver retourne Iso pour iso`` () =
        DriverMappings.parseVolumeDriver "iso" |> should equal StorageDriverType.Iso

    [<Fact>]
    let ``parseVolumeDriver est insensible Ã  la casse`` () =
        DriverMappings.parseVolumeDriver "NFS" |> should equal StorageDriverType.Nfs

    [<Fact>]
    let ``parseVolumeDriver retourne Local pour valeur inconnue`` () =
        DriverMappings.parseVolumeDriver "unknown"
        |> should equal StorageDriverType.Local

    [<Fact>]
    let ``isValidVolumeDriver retourne true pour nfs`` () =
        DriverMappings.isValidVolumeDriver "nfs" |> should equal true

    [<Fact>]
    let ``isValidVolumeDriver retourne true pour iso`` () =
        DriverMappings.isValidVolumeDriver "iso" |> should equal true

    [<Fact>]
    let ``isValidVolumeDriver retourne true pour local`` () =
        DriverMappings.isValidVolumeDriver "local" |> should equal true

    [<Fact>]
    let ``isValidVolumeDriver retourne true pour smb`` () =
        DriverMappings.isValidVolumeDriver "smb" |> should equal true

    [<Fact>]
    let ``isValidVolumeDriver retourne true pour azure`` () =
        DriverMappings.isValidVolumeDriver "azure" |> should equal true

    [<Fact>]
    let ``isValidVolumeDriver retourne true pour aws`` () =
        DriverMappings.isValidVolumeDriver "aws" |> should equal true

    [<Fact>]
    let ``isValidVolumeDriver retourne true pour gcp`` () =
        DriverMappings.isValidVolumeDriver "gcp" |> should equal true

    [<Fact>]
    let ``isValidVolumeDriver est insensible Ã  la casse`` () =
        DriverMappings.isValidVolumeDriver "ISO" |> should equal true

    [<Fact>]
    let ``isValidVolumeDriver retourne false pour unknown`` () =
        DriverMappings.isValidVolumeDriver "unknown" |> should equal false

    [<Fact>]
    let ``allVolumeDriverNames contient 7 Ã©lÃ©ments`` () =
        DriverMappings.allVolumeDriverNames.Length |> should equal 7

    [<Fact>]
    let ``allVolumeDriverNames contient les bons noms`` () =
        DriverMappings.allVolumeDriverNames
        |> should equal [ "local"; "nfs"; "smb"; "azure"; "aws"; "gcp"; "iso" ]

    // â”€â”€ Network drivers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``networkDriverToString retourne bridge pour Bridge`` () =
        DriverMappings.networkDriverToString NetworkDriver.Bridge
        |> should equal "bridge"

    [<Fact>]
    let ``networkDriverToString retourne none pour None`` () =
        DriverMappings.networkDriverToString NetworkDriver.None |> should equal "none"

    [<Fact>]
    let ``networkDriverToString retourne custom_cni pour CustomCni`` () =
        DriverMappings.networkDriverToString NetworkDriver.CustomCni
        |> should equal "custom_cni"

    [<Fact>]
    let ``networkDriverToString retourne pod pour Pod`` () =
        DriverMappings.networkDriverToString NetworkDriver.Pod |> should equal "pod"

    [<Fact>]
    let ``networkDriverToString retombe sur bridge pour une valeur inconnue`` () =
        let unknown = LanguagePrimitives.EnumOfValue<int, NetworkDriver>(99)
        DriverMappings.networkDriverToString unknown |> should equal "bridge"

    [<Fact>]
    let ``parseNetworkDriver retourne Bridge pour bridge`` () =
        DriverMappings.parseNetworkDriver "bridge" |> should equal NetworkDriver.Bridge

    [<Fact>]
    let ``parseNetworkDriver retourne None pour none`` () =
        DriverMappings.parseNetworkDriver "none" |> should equal NetworkDriver.None

    [<Fact>]
    let ``parseNetworkDriver retourne CustomCni pour custom_cni`` () =
        DriverMappings.parseNetworkDriver "custom_cni"
        |> should equal NetworkDriver.CustomCni

    [<Fact>]
    let ``parseNetworkDriver retourne CustomCni pour cni`` () =
        DriverMappings.parseNetworkDriver "cni" |> should equal NetworkDriver.CustomCni

    [<Fact>]
    let ``parseNetworkDriver retourne Pod pour pod`` () =
        DriverMappings.parseNetworkDriver "pod" |> should equal NetworkDriver.Pod

    [<Fact>]
    let ``parseNetworkDriver est insensible Ã  la casse`` () =
        DriverMappings.parseNetworkDriver "POD" |> should equal NetworkDriver.Pod

    [<Fact>]
    let ``parseNetworkDriver retourne Bridge pour valeur inconnue`` () =
        DriverMappings.parseNetworkDriver "unknown" |> should equal NetworkDriver.Bridge

    [<Fact>]
    let ``isValidNetworkDriver retourne true pour bridge`` () =
        DriverMappings.isValidNetworkDriver "bridge" |> should equal true

    [<Fact>]
    let ``isValidNetworkDriver retourne true pour cni`` () =
        DriverMappings.isValidNetworkDriver "cni" |> should equal true

    [<Fact>]
    let ``isValidNetworkDriver retourne true pour none`` () =
        DriverMappings.isValidNetworkDriver "none" |> should equal true

    [<Fact>]
    let ``isValidNetworkDriver retourne true pour custom_cni`` () =
        DriverMappings.isValidNetworkDriver "custom_cni" |> should equal true

    [<Fact>]
    let ``isValidNetworkDriver retourne true pour pod`` () =
        DriverMappings.isValidNetworkDriver "pod" |> should equal true

    [<Fact>]
    let ``isValidNetworkDriver est insensible Ã  la casse`` () =
        DriverMappings.isValidNetworkDriver "BRIDGE" |> should equal true

    [<Fact>]
    let ``isValidNetworkDriver retourne false pour unknown`` () =
        DriverMappings.isValidNetworkDriver "unknown" |> should equal false

    [<Fact>]
    let ``allNetworkDriverNames contient 4 Ã©lÃ©ments`` () =
        DriverMappings.allNetworkDriverNames.Length |> should equal 4

    [<Fact>]
    let ``allNetworkDriverNames contient les bons noms`` () =
        DriverMappings.allNetworkDriverNames
        |> should equal [ "bridge"; "none"; "custom_cni"; "pod" ]

