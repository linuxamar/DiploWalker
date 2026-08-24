namespace Diplo.Container.Tests

open Xunit
open FsUnit.Xunit
open Diplo.Grpc
open Diplo.Grpc.Volume
open Diplo.Grpc.Network

type DriverMappingsTests() =

    // ── Volume drivers ─────────────────────────────────────────

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
    let ``parseVolumeDriver retourne Local pour local`` () =
        DriverMappings.parseVolumeDriver "local" |> should equal StorageDriverType.Local

    [<Fact>]
    let ``parseVolumeDriver est insensible à la casse`` () =
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
    let ``isValidVolumeDriver retourne false pour unknown`` () =
        DriverMappings.isValidVolumeDriver "unknown" |> should equal false

    [<Fact>]
    let ``allVolumeDriverNames contient 7 éléments`` () =
        DriverMappings.allVolumeDriverNames.Length |> should equal 7

    // ── Network drivers ────────────────────────────────────────

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
    let ``parseNetworkDriver retourne Bridge pour bridge`` () =
        DriverMappings.parseNetworkDriver "bridge" |> should equal NetworkDriver.Bridge

    [<Fact>]
    let ``parseNetworkDriver retourne CustomCni pour cni`` () =
        DriverMappings.parseNetworkDriver "cni" |> should equal NetworkDriver.CustomCni

    [<Fact>]
    let ``parseNetworkDriver est insensible à la casse`` () =
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
    let ``isValidNetworkDriver retourne false pour unknown`` () =
        DriverMappings.isValidNetworkDriver "unknown" |> should equal false

    [<Fact>]
    let ``allNetworkDriverNames contient 4 éléments`` () =
        DriverMappings.allNetworkDriverNames.Length |> should equal 4
