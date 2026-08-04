namespace Diplo.Volume.Tests

module IsoDriverTests =

    open System
    open System.IO
    open System.Text
    open Xunit
    open FsUnit.Xunit
    open Diplo.Volume.Drivers
    open Diplo.Abstractions.SecurityValidation

    do addAllowedVolumeDir(Path.GetTempPath())

    let createTempDir () =
        let dir = Path.Combine(Path.GetTempPath(), "diplo-vol-test-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(dir) |> ignore
        dir

    let cleanupDir (dir: string) =
        try if Directory.Exists(dir) then Directory.Delete(dir, true) with _ -> ()

    let writeBothEndian (iso: byte[]) offset (value: int) =
        let v = uint32 value
        iso.[offset] <- byte (v &&& 0xFFu)
        iso.[offset + 1] <- byte ((v >>> 8) &&& 0xFFu)
        iso.[offset + 2] <- byte ((v >>> 16) &&& 0xFFu)
        iso.[offset + 3] <- byte ((v >>> 24) &&& 0xFFu)
        iso.[offset + 4] <- byte ((v >>> 24) &&& 0xFFu)
        iso.[offset + 5] <- byte ((v >>> 16) &&& 0xFFu)
        iso.[offset + 6] <- byte ((v >>> 8) &&& 0xFFu)
        iso.[offset + 7] <- byte (v &&& 0xFFu)

    let writeUInt16BothEndian (iso: byte[]) offset (value: int) =
        let v = uint16 value
        iso.[offset] <- byte (v &&& 0xFFus)
        iso.[offset + 1] <- byte ((v >>> 8) &&& 0xFFus)
        iso.[offset + 2] <- byte ((v >>> 8) &&& 0xFFus)
        iso.[offset + 3] <- byte (v &&& 0xFFus)

    let writeRecord (iso: byte[]) offset extent dataLength flags (name: byte[]) =
        let recLen = 33 + name.Length
        let padded = if recLen % 2 = 1 then recLen + 1 else recLen
        iso.[offset] <- byte padded
        iso.[offset + 1] <- 0uy
        writeBothEndian iso (offset + 2) extent
        writeBothEndian iso (offset + 10) dataLength
        iso.[offset + 25] <- flags
        writeUInt16BothEndian iso (offset + 28) 1
        iso.[offset + 32] <- byte name.Length
        Array.Copy(name, 0, iso, offset + 33, name.Length)
        padded

    let buildIso () =
        let iso = Array.zeroCreate<byte> (22 * 2048)
        let pvd = 16 * 2048
        iso.[pvd] <- 1uy
        Array.Copy(Encoding.ASCII.GetBytes("CD001"), 0, iso, pvd + 1, 5)
        iso.[pvd + 6] <- 1uy
        writeBothEndian iso (pvd + 80) 22
        writeUInt16BothEndian iso (pvd + 128) 2048
        writeRecord iso (pvd + 156) 20 2048 0x02uy [| 0x00uy |] |> ignore
        let rootOffset = 20 * 2048
        let dot = writeRecord iso rootOffset 20 2048 0x02uy [| 0x00uy |]
        let dotdot = writeRecord iso (rootOffset + dot) 20 2048 0x02uy [| 0x01uy |]
        let content = Encoding.UTF8.GetBytes("Bonjour ISO!\n")
        let name = Encoding.ASCII.GetBytes("HELLO.TXT;1")
        writeRecord iso (rootOffset + dot + dotdot) 21 content.Length 0x00uy name |> ignore
        Array.Copy(content, 0, iso, 21 * 2048, content.Length)
        iso

    let writeUInt16LE (iso: byte[]) offset (value: int) =
        let v = uint16 value
        iso.[offset] <- byte (v &&& 0xFFus)
        iso.[offset + 1] <- byte ((v >>> 8) &&& 0xFFus)

    let writeUInt32LE (iso: byte[]) offset (value: int) =
        let v = uint32 value
        iso.[offset] <- byte (v &&& 0xFFu)
        iso.[offset + 1] <- byte ((v >>> 8) &&& 0xFFu)
        iso.[offset + 2] <- byte ((v >>> 16) &&& 0xFFu)
        iso.[offset + 3] <- byte ((v >>> 24) &&& 0xFFu)

    let buildUdfDvd () =
        let iso = Array.zeroCreate<byte> (23 * 2048)
        // Bloc 16 : séquence de reconnaissance de volume (VRS), descripteur NSR03.
        let vrs = 16 * 2048
        iso.[vrs] <- 0uy
        Array.Copy(Encoding.ASCII.GetBytes("NSR03"), 0, iso, vrs + 1, 5)
        iso.[vrs + 6] <- 1uy
        writeUInt32LE iso (vrs + 16) 17
        // Bloc 17 : descripteur de partition (partition 0, démarre au bloc 0).
        let pd = 17 * 2048
        writeUInt16LE iso pd 0x0005
        writeUInt16LE iso (pd + 4) 1
        writeUInt32LE iso (pd + 12) 17
        writeUInt32LE iso (pd + 185) 0
        // Bloc 18 : descripteur de volume logique (bloc logique 2048, FSD au bloc 19).
        let lvd = 18 * 2048
        writeUInt16LE iso lvd 0x0006
        writeUInt16LE iso (lvd + 4) 1
        writeUInt32LE iso (lvd + 12) 18
        writeUInt32LE iso (lvd + 210) 2048
        writeUInt32LE iso (lvd + 246) 2048
        writeUInt32LE iso (lvd + 250) 19
        // Bloc 19 : descripteur de jeu de fichiers (ICB racine au bloc 20).
        let fsd = 19 * 2048
        writeUInt16LE iso fsd 0x0100
        writeUInt16LE iso (fsd + 4) 1
        writeUInt32LE iso (fsd + 12) 19
        writeUInt32LE iso (fsd + 458) 2048
        writeUInt32LE iso (fsd + 462) 20
        // Bloc 20 : entrée de fichier du répertoire racine avec « HELLO.TXT » et « .. ».
        let rootIcb = 20 * 2048
        writeUInt16LE iso rootIcb 0x0201
        writeUInt16LE iso (rootIcb + 4) 1
        writeUInt32LE iso (rootIcb + 12) 20
        iso.[rootIcb + 27] <- 2uy
        writeUInt32LE iso (rootIcb + 56) 88
        writeUInt32LE iso (rootIcb + 172) 8
        // FID « HELLO.TXT » (fichier, ICB au bloc 22).
        let fid1 = rootIcb + 36
        writeUInt16LE iso fid1 0x0101
        writeUInt16LE iso (fid1 + 4) 1
        writeUInt32LE iso (fid1 + 12) 20
        writeUInt16LE iso (fid1 + 16) 1
        iso.[fid1 + 18] <- 0uy
        iso.[fid1 + 19] <- 9uy
        writeUInt32LE iso (fid1 + 20) 2048
        writeUInt32LE iso (fid1 + 24) 21
        writeUInt16LE iso (fid1 + 36) 0
        Array.Copy(Encoding.ASCII.GetBytes("HELLO.TXT"), 0, iso, fid1 + 38, 9)
        // FID « .. » (répertoire parent).
        let fid2 = fid1 + 48
        writeUInt16LE iso fid2 0x0101
        writeUInt16LE iso (fid2 + 4) 1
        writeUInt32LE iso (fid2 + 12) 20
        writeUInt16LE iso (fid2 + 16) 1
        iso.[fid2 + 18] <- 0x40uy
        iso.[fid2 + 19] <- 1uy
        writeUInt32LE iso (fid2 + 20) 2048
        writeUInt32LE iso (fid2 + 24) 20
        writeUInt16LE iso (fid2 + 36) 0
        iso.[fid2 + 38] <- 0x01uy
        // Descripteur d'allocation du répertoire racine (short_ad → bloc 21).
        writeUInt32LE iso (rootIcb + 176) 88
        writeUInt32LE iso (rootIcb + 180) 21
        // Bloc 21 : entrée de fichier de HELLO.TXT (short_ad → bloc 22).
        let fileIcb = 21 * 2048
        writeUInt16LE iso fileIcb 0x0201
        writeUInt16LE iso (fileIcb + 4) 1
        writeUInt32LE iso (fileIcb + 12) 21
        iso.[fileIcb + 27] <- 1uy
        let content = Encoding.UTF8.GetBytes("Bonjour DVD!\n")
        writeUInt32LE iso (fileIcb + 56) content.Length
        writeUInt32LE iso (fileIcb + 172) 8
        writeUInt32LE iso (fileIcb + 176) content.Length
        writeUInt32LE iso (fileIcb + 180) 22
        // Bloc 22 : contenu du fichier.
        Array.Copy(content, 0, iso, 22 * 2048, content.Length)
        iso

    [<Fact>]
    let ``CreateVolume cree un volume ISO et retourne id et chemin`` () =
        let tempRoot = createTempDir ()
        try
            let isoFile = Path.Combine(tempRoot, "test.iso")
            File.WriteAllBytes(isoFile, buildIso ())
            let driver = IsoDriver(Path.Combine(tempRoot, "data"))
            let (id, mountpoint) = driver.CreateVolume("test-iso", Map.ofList ["iso", isoFile], Map.empty)
            String.IsNullOrEmpty(id) |> should equal false
            mountpoint |> should equal isoFile
        finally cleanupDir tempRoot

    [<Fact>]
    let ``CreateVolume sans option iso leve une exception`` () =
        let tempRoot = createTempDir ()
        try
            let driver = IsoDriver(Path.Combine(tempRoot, "data"))
            (fun () -> driver.CreateVolume("bad", Map.empty, Map.empty) |> ignore)
            |> should throw typeof<System.Exception>
        finally cleanupDir tempRoot

    [<Fact>]
    let ``CreateVolume avec fichier ISO inexistant leve une exception`` () =
        let tempRoot = createTempDir ()
        try
            let driver = IsoDriver(Path.Combine(tempRoot, "data"))
            (fun () -> driver.CreateVolume("bad", Map.ofList ["iso", "Z:\\inexistant.iso"], Map.empty) |> ignore)
            |> should throw typeof<System.Exception>
        finally cleanupDir tempRoot

    [<Fact>]
    let ``MountVolume extrait les fichiers de l ISO`` () =
        let tempRoot = createTempDir ()
        try
            let isoFile = Path.Combine(tempRoot, "test.iso")
            File.WriteAllBytes(isoFile, buildIso ())
            let driver = IsoDriver(Path.Combine(tempRoot, "data"))
            let (id, _) = driver.CreateVolume("mount-iso", Map.ofList ["iso", isoFile], Map.empty)
            let target = Path.Combine(tempRoot, "mnt")
            Directory.CreateDirectory(target) |> ignore
            let (success, mountDir) = driver.MountVolume(id, target, "")
            success |> should equal true
            mountDir |> should equal target
            File.ReadAllText(Path.Combine(mountDir, "HELLO.TXT")) |> should equal "Bonjour ISO!\n"
        finally cleanupDir tempRoot

    [<Fact>]
    let ``UnmountVolume supprime le repertoire de montage`` () =
        let tempRoot = createTempDir ()
        try
            let isoFile = Path.Combine(tempRoot, "test.iso")
            File.WriteAllBytes(isoFile, buildIso ())
            let driver = IsoDriver(Path.Combine(tempRoot, "data"))
            let (id, _) = driver.CreateVolume("unmount-iso", Map.ofList ["iso", isoFile], Map.empty)
            let target = Path.Combine(tempRoot, "mnt")
            Directory.CreateDirectory(target) |> ignore
            driver.MountVolume(id, target, "") |> ignore
            Directory.Exists(target) |> should equal true
            let (success, msg) = driver.UnmountVolume(id, target)
            success |> should equal true
            msg |> should equal "Démonté"
            Directory.Exists(target) |> should equal false
        finally cleanupDir tempRoot

    [<Fact>]
    let ``InspectVolume retourne Some pour un volume ISO existant`` () =
        let tempRoot = createTempDir ()
        try
            let isoFile = Path.Combine(tempRoot, "test.iso")
            File.WriteAllBytes(isoFile, buildIso ())
            let driver = IsoDriver(Path.Combine(tempRoot, "data"))
            let (id, _) = driver.CreateVolume("inspect-iso", Map.ofList ["iso", isoFile], Map.empty)
            let result = driver.InspectVolume(id)
            result.IsSome |> should equal true
            result.Value.GetProperty("name").GetString() |> should equal "inspect-iso"
            result.Value.GetProperty("driver").GetString() |> should equal "iso"
            result.Value.GetProperty("remotePath").GetString() |> should equal isoFile
        finally cleanupDir tempRoot

    [<Fact>]
    let ``RemoveVolume supprime le volume et retourne true`` () =
        let tempRoot = createTempDir ()
        try
            let isoFile = Path.Combine(tempRoot, "test.iso")
            File.WriteAllBytes(isoFile, buildIso ())
            let driver = IsoDriver(Path.Combine(tempRoot, "data"))
            let (id, _) = driver.CreateVolume("delete-iso", Map.ofList ["iso", isoFile], Map.empty)
            driver.RemoveVolume(id, false) |> should equal true
            driver.InspectVolume(id).IsNone |> should equal true
        finally cleanupDir tempRoot

    [<Fact>]
    let ``MountVolume extrait les fichiers d une image DVD UDF`` () =
        let tempRoot = createTempDir ()
        try
            let isoFile = Path.Combine(tempRoot, "test.iso")
            File.WriteAllBytes(isoFile, buildUdfDvd ())
            let driver = IsoDriver(Path.Combine(tempRoot, "data"))
            let (id, _) = driver.CreateVolume("mount-udf", Map.ofList ["iso", isoFile], Map.empty)
            let target = Path.Combine(tempRoot, "mnt")
            Directory.CreateDirectory(target) |> ignore
            let (success, mountDir) = driver.MountVolume(id, target, "")
            success |> should equal true
            mountDir |> should equal target
            File.ReadAllText(Path.Combine(mountDir, "HELLO.TXT")) |> should equal "Bonjour DVD!\n"
        finally cleanupDir tempRoot
