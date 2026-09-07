namespace Diplo.Disk.Tests

module DmgVdiFsTests =

    open System
    open System.IO
    open Xunit
    open FsUnit.Xunit
    open Diplo.Disk

    let private nonexistentPath () =
        Path.Combine(Path.GetTempPath(), "diplo-disk-tests", Guid.NewGuid().ToString("N") + ".img")

    [<Fact>]
    let ``DmgFs.tryExtract renvoie None si le fichier source n'existe pas`` () =
        DmgFs.tryExtract (nonexistentPath ()) (Path.GetTempPath()) |> should equal None

    [<Fact>]
    let ``VdiFs.tryExtract renvoie None si le fichier source n'existe pas`` () =
        VdiFs.tryExtract (nonexistentPath ()) (Path.GetTempPath()) |> should equal None

    [<Fact>]
    let ``VdiFs.tryWriteBack renvoie false si le fichier source n'existe pas`` () =
        VdiFs.tryWriteBack (nonexistentPath ()) (Path.GetTempPath()) |> should equal false
