namespace Diplo.Disk.Tests

module DiskFormatTests =

    open System
    open System.IO
    open System.Text
    open Xunit
    open FsUnit.Xunit
    open Diplo.Disk

    let private tempFile () =
        Path.Combine(Path.GetTempPath(), "diplo-format-" + Guid.NewGuid().ToString("N"))

    let private writeBytes (path: string) (data: byte[]) =
        use fs = File.Create(path)
        fs.Write(data, 0, data.Length)

    [<Fact>]
    let ``detect reconnait une image qcow2`` () =
        let p = tempFile ()

        try
            writeBytes p [| 0x51uy; 0x46uy; 0x49uy; 0xFBuy |]
            DiskFormat.detect p |> should equal DiskFormat.Qcow2
        finally
            File.Delete p

    [<Fact>]
    let ``detect reconnait une image qcow v1`` () =
        let p = tempFile ()

        try
            writeBytes p [| 0x51uy; 0x46uy; 0x49uy; 0xFEuy |]
            DiskFormat.detect p |> should equal DiskFormat.Qcow1
        finally
            File.Delete p

    [<Fact>]
    let ``detect reconnait une image vhdx`` () =
        let p = tempFile ()

        try
            writeBytes p (Encoding.ASCII.GetBytes "vhdxfile")
            DiskFormat.detect p |> should equal DiskFormat.Vhdx
        finally
            File.Delete p

    [<Fact>]
    let ``detect reconnait une image vmdk`` () =
        let p = tempFile ()

        try
            writeBytes p (Encoding.ASCII.GetBytes "KDMV")
            DiskFormat.detect p |> should equal DiskFormat.Vmdk
        finally
            File.Delete p

    [<Fact>]
    let ``detect reconnait une image vhd par son footer`` () =
        let p = tempFile ()

        try
            let footer = Array.zeroCreate<byte> 512
            Encoding.ASCII.GetBytes("conectix").CopyTo(footer, 0)
            writeBytes p footer
            DiskFormat.detect p |> should equal DiskFormat.Vhd
        finally
            File.Delete p

    [<Fact>]
    let ``detect retombe sur raw faute de signature`` () =
        let p = tempFile ()

        try
            writeBytes p [| 0x01uy; 0x02uy; 0x03uy; 0x04uy; 0x05uy; 0x06uy; 0x07uy; 0x08uy |]
            DiskFormat.detect p |> should equal DiskFormat.Raw
        finally
            File.Delete p

    [<Fact>]
    let ``detect traite un fichier trop court comme raw`` () =
        let p = tempFile ()

        try
            writeBytes p [| 0x01uy; 0x02uy |]
            DiskFormat.detect p |> should equal DiskFormat.Raw
        finally
            File.Delete p

    [<Fact>]
    let ``detect reconnait une image vdi`` () =
        let p = tempFile ()

        try
            let header = Array.zeroCreate<byte> 72
            [| 0x7Euy; 0x10uy; 0x10uy; 0x10uy; 0x4Duy; 0x61uy; 0x63uy; 0x20uy |].CopyTo(header, 0)
            writeBytes p header
            DiskFormat.detect p |> should equal DiskFormat.Vdi
        finally
            File.Delete p

    [<Fact>]
    let ``detect reconnait une image dmg`` () =
        let p = tempFile ()

        try
            writeBytes p [| 0x78uy; 0x6Buy; 0x6Fuy; 0x6Cuy |]
            DiskFormat.detect p |> should equal DiskFormat.Dmg
        finally
            File.Delete p

    [<Fact>]
    let ``detect reconnait une image parallels`` () =
        let p = tempFile ()

        try
            let header = [| 0x70uy; 0x61uy; 0x72uy; 0x61uy; 0x0Duy; 0x0Auy; 0x1Auy; 0x0Auy |]
            writeBytes p header
            DiskFormat.detect p |> should equal DiskFormat.Parallels
        finally
            File.Delete p

    [<Theory>]
    [<InlineData("Qcow2")>]
    [<InlineData("Qcow1")>]
    [<InlineData("Vhd")>]
    [<InlineData("Vhdx")>]
    [<InlineData("Vmdk")>]
    [<InlineData("Vdi")>]
    [<InlineData("Dmg")>]
    [<InlineData("Parallels")>]
    [<InlineData("Raw")>]
    let ``isDiskImage accepte les formats montables`` (name: string) =
        let format =
            match name with
            | "Qcow2" -> DiskFormat.Qcow2
            | "Qcow1" -> DiskFormat.Qcow1
            | "Vhd" -> DiskFormat.Vhd
            | "Vhdx" -> DiskFormat.Vhdx
            | "Vmdk" -> DiskFormat.Vmdk
            | "Vdi" -> DiskFormat.Vdi
            | "Dmg" -> DiskFormat.Dmg
            | "Parallels" -> DiskFormat.Parallels
            | _ -> DiskFormat.Raw

        DiskFormat.isDiskImage format |> should equal true

    [<Theory>]
    [<InlineData("Unknown")>]
    let ``isDiskImage refuse les formats non montables`` (name: string) =
        let format =
            match name with
            | _ -> DiskFormat.Unknown

        DiskFormat.isDiskImage format |> should equal false
