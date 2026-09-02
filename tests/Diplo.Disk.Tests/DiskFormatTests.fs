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
            // Signature réelle VirtualBox : 7F 10 DA BE (LE) à l'offset 0x40.
            let header = Array.zeroCreate<byte> 72
            [| 0x7Fuy; 0x10uy; 0xDAuy; 0xBEuy |].CopyTo(header, 0x40)
            writeBytes p header
            DiskFormat.detect p |> should equal DiskFormat.Vdi
        finally
            File.Delete p

    [<Fact>]
    let ``detect reconnait une image dmg par son trailer koly`` () =
        let p = tempFile ()

        try
            // Magic UDIF « koly » dans le TRAILER de 512 octets.
            let foot = Array.zeroCreate<byte> 512
            Encoding.ASCII.GetBytes("koly").CopyTo(foot, 0)
            writeBytes p foot
            DiskFormat.detect p |> should equal DiskFormat.Dmg
        finally
            File.Delete p

    [<Fact>]
    let ``detect reconnait une image parallels`` () =
        let p = tempFile ()

        try
            // Magic aligné sur Parallels.readHeader (LE 0x30617261 → « ara0 »).
            let header = [| 0x61uy; 0x72uy; 0x61uy; 0x30uy |]
            writeBytes p header
            DiskFormat.detect p |> should equal DiskFormat.Parallels
        finally
            File.Delete p

    [<Fact>]
    let ``detect reconnait une image iso par la signature CD001`` () =
        let p = tempFile ()

        try
            // Signature ISO9660 « CD001 » à l'offset 0x8001 (bloc 16 : 2048*16+1).
            let data = Array.zeroCreate<byte> 40000
            Encoding.ASCII.GetBytes("CD001").CopyTo(data, 0x8001)
            writeBytes p data
            DiskFormat.detect p |> should equal DiskFormat.Iso
        finally
            File.Delete p

    [<Fact>]
    let ``detect retombe sur raw pour un fichier trop court pour l'iso`` () =
        let p = tempFile ()

        try
            writeBytes p [| 0x01uy; 0x02uy; 0x03uy |]
            DiskFormat.detect p |> should equal DiskFormat.Raw
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
    [<InlineData("Iso")>]
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
            | "Iso" -> DiskFormat.Iso
            | _ -> DiskFormat.Raw

        DiskFormat.isDiskImage format |> should equal true

    [<Theory>]
    [<InlineData("Unknown")>]
    let ``isDiskImage refuse les formats non montables`` (name: string) =
        let format =
            match name with
            | _ -> DiskFormat.Unknown

        DiskFormat.isDiskImage format |> should equal false

    // ── toString ───────────────────────────────────────────────────────

    [<Fact>]
    let ``toString retourne qcow2`` () =
        DiskFormat.toString DiskFormat.Qcow2 |> should equal "qcow2"

    [<Fact>]
    let ``toString retourne qcow (v1)`` () =
        DiskFormat.toString DiskFormat.Qcow1 |> should equal "qcow (v1)"

    [<Fact>]
    let ``toString retourne vhd`` () =
        DiskFormat.toString DiskFormat.Vhd |> should equal "vhd"

    [<Fact>]
    let ``toString retourne vhdx`` () =
        DiskFormat.toString DiskFormat.Vhdx |> should equal "vhdx"

    [<Fact>]
    let ``toString retourne vmdk`` () =
        DiskFormat.toString DiskFormat.Vmdk |> should equal "vmdk"

    [<Fact>]
    let ``toString retourne vdi`` () =
        DiskFormat.toString DiskFormat.Vdi |> should equal "vdi"

    [<Fact>]
    let ``toString retourne dmg`` () =
        DiskFormat.toString DiskFormat.Dmg |> should equal "dmg"

    [<Fact>]
    let ``toString retourne parallels`` () =
        DiskFormat.toString DiskFormat.Parallels |> should equal "parallels"

    [<Fact>]
    let ``toString retourne raw`` () =
        DiskFormat.toString DiskFormat.Raw |> should equal "raw"

    [<Fact>]
    let ``toString retourne iso`` () =
        DiskFormat.toString DiskFormat.Iso |> should equal "iso"

    [<Fact>]
    let ``toString retourne inconnu`` () =
        DiskFormat.toString DiskFormat.Unknown |> should equal "inconnu"
