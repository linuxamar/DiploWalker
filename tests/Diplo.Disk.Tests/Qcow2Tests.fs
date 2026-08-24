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

    // ── helpers binaires ────────────────────────────────────────────────────

    let private putBe16 (d: byte[]) (o: int) (v: int) =
        d.[o] <- byte (v >>> 8)
        d.[o + 1] <- byte v

    let private putBe32 (d: byte[]) (o: int) (v: int) =
        d.[o] <- byte (v >>> 24)
        d.[o + 1] <- byte (v >>> 16)
        d.[o + 2] <- byte (v >>> 8)
        d.[o + 3] <- byte v

    let private putBe64 (d: byte[]) (o: int) (v: int64) =
        d.[o] <- byte (v >>> 56)
        d.[o + 1] <- byte (v >>> 48)
        d.[o + 2] <- byte (v >>> 40)
        d.[o + 3] <- byte (v >>> 32)
        d.[o + 4] <- byte (v >>> 24)
        d.[o + 5] <- byte (v >>> 16)
        d.[o + 6] <- byte (v >>> 8)
        d.[o + 7] <- byte v

    let private readBe64 (d: byte[]) (o: int) : int64 =
        let mutable v = 0L

        for i in 0..7 do
            v <- (v <<< 8) ||| int64 d.[o + i]

        v

    let private patchHeader (path: string) (f: byte[] -> unit) =
        use fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite)
        let b = Array.zeroCreate<byte> 4096
        fs.Read(b, 0, b.Length) |> ignore
        f b
        fs.Position <- 0L
        fs.Write(b, 0, b.Length)
        fs.Flush()

    /// Transforme l'en-tête en version 3 (incompatible/refcount_order
    /// conformes) : version à 4, header_length à 100, refcount_order à 96.
    let private makeV3 (b: byte[]) =
        putBe32 b 4 3
        putBe32 b 72 0
        putBe32 b 96 4
        putBe32 b 100 104

    let private readUInt64AtFile (path: string) (offset: int64) : int64 =
        use fs = new FileStream(path, FileMode.Open, FileAccess.Read)
        let b = Array.zeroCreate<byte> 8
        fs.Position <- offset
        let mutable total = 0

        while total < 8 do
            let r = fs.Read(b, total, 8 - total)

            if r <= 0 then
                failwith "Fichier tronqué"

            total <- total + r

        readBe64 b 0

    let private writeUInt64AtFile (path: string) (offset: int64) (value: int64) =
        use fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite)
        let b = Array.zeroCreate<byte> 8
        putBe64 b 0 value
        fs.Position <- offset
        fs.Write(b, 0, 8)
        fs.Flush()

    let private captureException (f: unit -> unit) : Exception =
        try
            f ()
            failwith "Aucune exception levée"
        with e ->
            e

    // ── comportement nominal ────────────────────────────────────────────────

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
            let payload = [| for i in 0..8191 -> byte (i &&& 0xFF) |]
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
            let partial = [| for i in 0..255 -> byte i |]
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
    let ``ecriture et relecture d'un volume multi-clusters`` () =
        run (fun root img ->
            TestImage.createQcow2 img []
            use s = new Qcow2Stream(img, FileAccess.ReadWrite)
            let offset = 32L * 1024L * 1024L
            let length = 4 * 1024 * 1024
            let payload = [| for i in 0 .. length - 1 -> byte (i &&& 0xFF) |]
            s.Position <- offset
            s.Write(payload, 0, payload.Length)
            s.Flush()
            // Réécriture partielle au milieu (lecture-modification-écriture).
            let partial = Array.create 4096 0x5Auy
            s.Position <- offset + int64 (2 * 1024 * 1024)
            s.Write(partial, 0, partial.Length)
            s.Flush()
            let readback = Array.zeroCreate<byte> length
            s.Position <- offset
            s.Read(readback, 0, readback.Length) |> should equal length
            readback.[0] |> should equal 0uy
            readback.[2 * 1024 * 1024] |> should equal 0x5Auy
            readback.[2 * 1024 * 1024 + 4095] |> should equal 0x5Auy
            readback.[2 * 1024 * 1024 + 4096] |> should equal 0uy
            readback.[length - 1] |> should equal 255uy
            let mid = Array.zeroCreate<byte> 4096
            s.Position <- offset + int64 (2 * 1024 * 1024)
            s.Read(mid, 0, mid.Length) |> should equal 4096
            mid |> Array.forall ((=) 0x5Auy) |> should equal true)

    [<Fact>]
    let ``extract lit une image qcow2 reelle`` () =
        run (fun root img ->
            TestImage.createQcow2 img [ "hello.txt", "Bonjour"; @"dossier\sub.txt", "sous" ]
            let staging = Path.Combine(root, "staging")
            let n = FsImage.extract img staging false
            n |> should equal 2
            File.ReadAllText(Path.Combine(staging, "hello.txt")) |> should equal "Bonjour"

            File.ReadAllText(Path.Combine(staging, "dossier", "sub.txt"))
            |> should equal "sous")

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
            patchHeader img makeV3
            use fs = new FileStream(img, FileMode.Open, FileAccess.Read)
            let h = Qcow2.readHeader fs
            h.Version |> should equal 3
            h.RefcountOrder |> should equal 4)

    [<Fact>]
    let ``une image qcow2 v3 est lisible et reecrite de bout en bout`` () =
        run (fun root img ->
            TestImage.createQcow2 img [ "hello.txt", "v1" ]
            patchHeader img makeV3
            let staging = Path.Combine(root, "staging")
            FsImage.extract img staging false |> ignore
            File.ReadAllText(Path.Combine(staging, "hello.txt")) |> should equal "v1"
            File.WriteAllText(Path.Combine(staging, "hello.txt"), "v2")
            FsImage.writeBack img staging
            let re = Path.Combine(root, "re")
            FsImage.extract img re false |> ignore
            File.ReadAllText(Path.Combine(re, "hello.txt")) |> should equal "v2")

    // ── refus et limites ────────────────────────────────────────────────────

    [<Fact>]
    let ``une image qcow2 avec fichier de sauvegarde est refusee`` () =
        run (fun root img ->
            TestImage.createQcow2 img []
            patchHeader img (fun b -> putBe64 b 8 4096L)
            let ex = captureException (fun () -> DiskMounter.mount img "/data" false |> ignore)
            ex.Message |> should haveSubstring "fichier de sauvegarde")

    [<Fact>]
    let ``une image qcow2 chiffree est refusee`` () =
        run (fun root img ->
            TestImage.createQcow2 img []
            patchHeader img (fun b -> putBe32 b 32 1)
            let ex = captureException (fun () -> DiskMounter.mount img "/data" false |> ignore)
            ex.Message |> should haveSubstring "chiffrées")

    [<Fact>]
    let ``une image qcow2 v3 avec fichier de donnees externe est refusee`` () =
        run (fun root img ->
            TestImage.createQcow2 img []

            patchHeader img (fun b ->
                makeV3 b
                putBe32 b 72 4)

            let ex = captureException (fun () -> DiskMounter.mount img "/data" false |> ignore)
            ex.Message |> should haveSubstring "fichier de données externe")

    [<Fact>]
    let ``un cluster compresse est refuse a la lecture`` () =
        run (fun root img ->
            TestImage.createQcow2 img []
            // L1[0] pointe vers la table L2 ; descripteur du cluster virtuel 0.
            let l1 = readUInt64AtFile img 4096L
            let l2Table = l1 &&& 0x00FFFFFFFFFFFFFE00L
            let desc = readUInt64AtFile img l2Table
            // Le cluster virtuel 0 doit être alloué (le formatage y écrit le MBR).
            desc |> should not' (equal 0L)
            // Marque le cluster comme compressé (bit 62).
            writeUInt64AtFile img l2Table (desc ||| 0x4000000000000000L)
            use s = new Qcow2Stream(img, FileAccess.Read)
            let b = Array.zeroCreate<byte> 512
            let ex = captureException (fun () -> s.Read(b, 0, 512) |> ignore)
            ex.Message |> should haveSubstring "compression qcow2")

    [<Fact>]
    let ``l'allocation echoue si le bloc de refcounts est manquant`` () =
        run (fun root img ->
            TestImage.createQcow2 img []
            // Vide la table de refcounts : plus aucun bloc n'est référencé.
            do
                use fs =
                    new FileStream(img, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)

                let zero = Array.zeroCreate<byte> 4096
                fs.Position <- 8192L
                fs.Write(zero, 0, zero.Length)
                fs.Flush()

            use s = new Qcow2Stream(img, FileAccess.ReadWrite)
            let b = Array.create 4096 0x01uy
            // Écriture dans une région non allouée : l'allocation d'un cluster
            // (et de sa table L2) exige un bloc de refcounts absent.
            s.Position <- 16L * 1024L * 1024L
            let ex = captureException (fun () -> s.Write(b, 0, b.Length) |> ignore)
            ex.Message |> should haveSubstring "saturée")

    // ── redimensionnement ───────────────────────────────────────────────────

    [<Fact>]
    let ``grow etend la taille virtuelle et permet d'ecrire au-dela`` () =
        run (fun root img ->
            TestImage.createQcow2 img [ "a.txt", "a" ]

            do
                use fs =
                    new FileStream(img, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)

                let h = Qcow2.resize fs (72L * 1024L * 1024L)
                h.VirtualSize |> should equal (72L * 1024L * 1024L)

            use s = new Qcow2Stream(img, FileAccess.ReadWrite)
            s.Length |> should equal (72L * 1024L * 1024L)
            // la région au-delà de 64 Mo est nulle
            s.Position <- 65L * 1024L * 1024L
            let zeros = Array.zeroCreate<byte> 4096
            let buf = Array.zeroCreate<byte> 4096
            s.Read(buf, 0, buf.Length) |> ignore
            buf |> should equal zeros
            // écriture puis relecture dans la région étendue
            let payload = Array.create 4096 0x5Auy
            s.Position <- 70L * 1024L * 1024L
            s.Write(payload, 0, payload.Length)
            s.Position <- 70L * 1024L * 1024L
            let back = Array.zeroCreate<byte> 4096
            s.Read(back, 0, back.Length) |> ignore
            back |> should equal payload)

    [<Fact>]
    let ``grow avec expansion de la table L1 relocalise et conserve les donnees`` () =
        run (fun root img ->
            // image vide de 1 Go : la table L1 occupe un cluster (512 entrées)
            TestImage.createEmptyQcow2 img 1024L
            let l1Before = readUInt64AtFile img 40L

            do
                use fs =
                    new FileStream(img, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)

                let h = Qcow2.resize fs (1152L * 1024L * 1024L)
                h.VirtualSize |> should equal (1152L * 1024L * 1024L)
                h.L1Size |> should equal 576

            let l1After = readUInt64AtFile img 40L
            l1After |> should not' (equal l1Before)
            use s = new Qcow2Stream(img, FileAccess.ReadWrite)
            s.Length |> should equal (1152L * 1024L * 1024L)
            // écriture puis relecture dans la partie étendue
            let payload = Array.create 4096 0x3Cuy
            s.Position <- 1024L * 1024L * 1024L
            s.Write(payload, 0, payload.Length)
            s.Position <- 1024L * 1024L * 1024L
            let back = Array.zeroCreate<byte> 4096
            s.Read(back, 0, back.Length) |> ignore
            back |> should equal payload)

    [<Fact>]
    let ``shrink reduit la taille, libere les clusters et tronque le fichier`` () =
        run (fun root img ->
            TestImage.createQcow2 img [ "a.txt", "a" ]
            // occupe des clusters élevés (50 Mo)
            do
                use s = new Qcow2Stream(img, FileAccess.ReadWrite)
                let payload = Array.create 4096 0x21uy
                s.Position <- 50L * 1024L * 1024L
                s.Write(payload, 0, payload.Length)

            let sizeBefore = FileInfo(img).Length

            do
                use fs =
                    new FileStream(img, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)

                let h = Qcow2.resize fs (32L * 1024L * 1024L)
                h.VirtualSize |> should equal (32L * 1024L * 1024L)
                h.L1Size |> should equal 16

            use s = new Qcow2Stream(img, FileAccess.Read)
            s.Length |> should equal (32L * 1024L * 1024L)
            // au-delà de la nouvelle taille, la lecture est vide
            let b = Array.zeroCreate<byte> 4096
            s.Position <- 40L * 1024L * 1024L
            s.Read(b, 0, b.Length) |> should equal 0
            // le fichier est tronqué à la dernière position utile
            FileInfo(img).Length |> should be (lessThan sizeBefore)
            s.Dispose()
            // après un nouvel agrandissement, la zone libérée est réutilisable (zéros)
            do
                use fs =
                    new FileStream(img, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)

                Qcow2.resize fs (64L * 1024L * 1024L) |> ignore

            use s2 = new Qcow2Stream(img, FileAccess.Read)
            s2.Position <- 50L * 1024L * 1024L
            let back = Array.zeroCreate<byte> 4096
            s2.Read(back, 0, back.Length) |> ignore
            back |> Array.forall ((=) 0uy) |> should equal true)

    [<Fact>]
    let ``Qcow2Stream.SetLength redimensionne l'image`` () =
        run (fun root img ->
            TestImage.createQcow2 img [ "a.txt", "a" ]
            use s = new Qcow2Stream(img, FileAccess.ReadWrite)
            s.Length |> should equal (64L * 1024L * 1024L)
            s.SetLength(80L * 1024L * 1024L)
            s.Length |> should equal (80L * 1024L * 1024L)
            s.SetLength(16L * 1024L * 1024L)
            s.Length |> should equal (16L * 1024L * 1024L))
