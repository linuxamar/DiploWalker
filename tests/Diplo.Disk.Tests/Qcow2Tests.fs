namespace Diplo.Disk.Tests

module Qcow2Tests =

    open System
    open System.IO
    open Xunit
    open FsUnit.Xunit
    open Diplo.Disk

    let private run (f: string -> string -> unit) =
        let root = TestImage.createTempDir ()
        try
            let img = Path.Combine(root, "test.qcow2")
            f root img
        finally
            TestImage.cleanupDir root

    [<Fact>]
    let ``lecture d'une region non allouee renvoie des zeros`` () =
        run (fun root img ->
            TestImage.createQcow2 img []
            use s = new Qcow2Stream(img, FileAccess.Read)
            let offset = 63L * 1024L * 1024L
            let data = Array.zeroCreate<byte> 4096
            s.Position <- offset
            s.Read(data, 0, data.Length) |> should equal 4096
            data |> Array.forall ((=) 0uy) |> should equal true)

    [<Fact>]
    let ``ecriture puis lecture chevauchant plusieurs clusters`` () =
        run (fun root img ->
            TestImage.createQcow2 img []
            use s = new Qcow2Stream(img, FileAccess.ReadWrite)
            let payload = [| for i in 0 .. 8191 -> byte (i &&& 0xFF) |]
            s.Position <- 3000L
            s.Write(payload, 0, payload.Length)
            s.Flush()
            let readback = Array.zeroCreate<byte> payload.Length
            s.Position <- 3000L
            s.Read(readback, 0, readback.Length) |> should equal payload.Length
            readback |> should equal payload)

    [<Fact>]
    let ``ecriture partielle ne detruit pas le reste du cluster`` () =
        run (fun root img ->
            TestImage.createQcow2 img []
            use s = new Qcow2Stream(img, FileAccess.ReadWrite)
            let full = Array.create 4096 0xA5uy
            s.Position <- 8192L
            s.Write(full, 0, full.Length)
            s.Flush()
            let partial = [| for i in 0 .. 255 -> byte i |]
            s.Position <- 8192L + 2000L
            s.Write(partial, 0, partial.Length)
            s.Flush()
            let readback = Array.zeroCreate<byte> 4096
            s.Position <- 8192L
            s.Read(readback, 0, readback.Length) |> should equal 4096
            readback.[0] |> should equal 0xA5uy
            readback.[1999] |> should equal 0xA5uy
            readback.[2000] |> should equal 0uy
            readback.[2000 + 255] |> should equal 255uy
            readback.[2000 + 256] |> should equal 0xA5uy)

    [<Fact>]
    let ``extract lit une image qcow2 reelle`` () =
        run (fun root img ->
            TestImage.createQcow2 img [ "hello.txt", "Bonjour"; @"dossier\sub.txt", "sous" ]
            let staging = Path.Combine(root, "staging")
            let n = FsImage.extract img staging false
            n |> should equal 2
            File.ReadAllText(Path.Combine(staging, "hello.txt")) |> should equal "Bonjour"
            File.ReadAllText(Path.Combine(staging, "dossier", "sub.txt")) |> should equal "sous")

    [<Fact>]
    let ``writeBack reecrit les fichiers modifies et ajoute les nouveaux dans une image qcow2`` () =
        run (fun root img ->
            TestImage.createQcow2 img [ "hello.txt", "v1" ]
            let staging = Path.Combine(root, "staging")
            FsImage.extract img staging false |> ignore
            File.WriteAllText(Path.Combine(staging, "hello.txt"), "v2")
            File.WriteAllText(Path.Combine(staging, "nouveau.txt"), "nouveau")
            FsImage.writeBack img staging
            let re = Path.Combine(root, "re")
            FsImage.extract img re false |> ignore
            File.ReadAllText(Path.Combine(re, "hello.txt")) |> should equal "v2"
            File.ReadAllText(Path.Combine(re, "nouveau.txt")) |> should equal "nouveau")

    [<Fact>]
    let ``writeBack supprime les fichiers disparus d'une image qcow2`` () =
        run (fun root img ->
            TestImage.createQcow2 img [ "a.txt", "a"; "b.txt", "b" ]
            let staging = Path.Combine(root, "staging")
            FsImage.extract img staging false |> ignore
            File.Delete(Path.Combine(staging, "a.txt"))
            FsImage.writeBack img staging
            let re = Path.Combine(root, "re")
            FsImage.extract img re false |> ignore
            File.Exists(Path.Combine(re, "a.txt")) |> should equal false
            File.Exists(Path.Combine(re, "b.txt")) |> should equal true)

    [<Fact>]
    let ``mount d'une image qcow2 reecrit a la liberation`` () =
        run (fun root img ->
            TestImage.createQcow2 img [ "hello.txt", "v1" ]
            let vol = DiskMounter.mount img "/data" false
            File.ReadAllText(Path.Combine(vol.HostPath, "hello.txt")) |> should equal "v1"
            File.WriteAllText(Path.Combine(vol.HostPath, "hello.txt"), "v2")
            vol.Dispose()
            let re = Path.Combine(root, "re")
            FsImage.extract img re false |> ignore
            File.ReadAllText(Path.Combine(re, "hello.txt")) |> should equal "v2")

    [<Fact>]
    let ``mount d'une image qcow2 en lecture seule ne reecrit pas a la liberation`` () =
        run (fun root img ->
            TestImage.createQcow2 img [ "hello.txt", "v1" ]
            let vol = DiskMounter.mount img "/data" true
            File.WriteAllText(Path.Combine(vol.HostPath, "hello.txt"), "v3")
            vol.Dispose()
            let re = Path.Combine(root, "re")
            FsImage.extract img re false |> ignore
            File.ReadAllText(Path.Combine(re, "hello.txt")) |> should equal "v1")

    [<Fact>]
    let ``l'en-tete version 3 est lu avec les bons offsets`` () =
        run (fun root img ->
            TestImage.createQcow2 img []
            use fs = new FileStream(img, FileMode.Open, FileAccess.ReadWrite)
            let b = Array.zeroCreate<byte> 104
            fs.Read(b, 0, b.Length) |> ignore
            let putBe32 (d: byte[]) o v =
                d.[o] <- byte (v >>> 24)
                d.[o + 1] <- byte (v >>> 16)
                d.[o + 2] <- byte (v >>> 8)
                d.[o + 3] <- byte v
            putBe32 b 4 3
            putBe32 b 72 0
            putBe32 b 96 4
            putBe32 b 100 104
            fs.Position <- 0L
            fs.Write(b, 0, b.Length)
            fs.Flush()
            fs.Position <- 0L
            let h = Qcow2.readHeader fs
            h.Version |> should equal 3
            h.RefcountOrder |> should equal 4)
