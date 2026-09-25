namespace DiploWalker.Installer.Tests

module CoreTests =

    open System
    open System.IO
    open System.IO.Compression
    open System.Text.RegularExpressions
    open Xunit
    open FsUnit.Xunit
    open DiploWalker.Installer.Core

    let private tempDir () =
        let dir = Path.Combine(Path.GetTempPath(), "diplo-installer-tests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(dir) |> ignore
        dir

    // â”€â”€â”€ Checksum â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``computeSha256 retourne le condensat hexadÃ©cimal minuscule`` () =
        let dir = tempDir ()

        try
            let path = Path.Combine(dir, "f.txt")
            File.WriteAllText(path, "hello")
            let hash = computeSha256 path
            hash |> should equal "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824"
            Regex.IsMatch(hash, "^[0-9a-f]{64}$") |> should equal true
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``verifyChecksum ne lÃ¨ve pas si le condensat correspond`` () =
        let dir = tempDir ()

        try
            let path = Path.Combine(dir, "f.txt")
            File.WriteAllText(path, "hello")
            let expected = computeSha256 path
            verifyChecksum path (Some expected)
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``verifyChecksum lÃ¨ve si le condensat ne correspond pas`` () =
        let dir = tempDir ()

        try
            let path = Path.Combine(dir, "f.txt")
            File.WriteAllText(path, "hello")
            (fun () -> verifyChecksum path (Some "0")) |> should throw typeof<Exception>
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``verifyChecksum lÃ¨ve si aucun checksum fourni`` () =
        let dir = tempDir ()

        try
            let path = Path.Combine(dir, "f.txt")
            File.WriteAllText(path, "hello")
            (fun () -> verifyChecksum path None) |> should throw typeof<Exception>
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``verifyChecksum lÃ¨ve si checksum placeholder todo`` () =
        let dir = tempDir ()

        try
            let path = Path.Combine(dir, "f.txt")
            File.WriteAllText(path, "hello")
            (fun () -> verifyChecksum path (Some "todo_Ã _mettre_Ã _jour")) |> should throw typeof<Exception>
        finally
            Directory.Delete(dir, true)

    // â”€â”€â”€ Extraction Zip â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``extractZip extrait les fichiers vers la destination`` () =
        let dir = tempDir ()
        let archive = Path.Combine(dir, "a.zip")
        let outDir = Path.Combine(dir, "out")

        try
            Directory.CreateDirectory(outDir) |> ignore

            let writeZip () =
                use fs = File.Create(archive)
                use za = new ZipArchive(fs, ZipArchiveMode.Create)
                let entry = za.CreateEntry("fichier.txt")
                use es = entry.Open()
                use writer = new StreamWriter(es)
                writer.Write("contenu")

            writeZip ()

            extractZip archive outDir

            File.ReadAllText(Path.Combine(outDir, "fichier.txt")) |> should equal "contenu"
        finally
            Directory.Delete(dir, true)

    // â”€â”€â”€ Config containerd â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``buildContainerdConfigToml contient les rÃ©pertoires de travail`` () =
        let toml = buildContainerdConfigToml ()
        toml.Contains(sprintf "root = \"%s\"" containerdRootDir) |> should equal true
        toml.Contains(sprintf "state = \"%s\"" containerdStateDir) |> should equal true

    [<Fact>]
    let ``buildContainerdConfigToml configure l'adresse gRPC par named pipe`` () =
        let toml = buildContainerdConfigToml ()
        toml.Contains("address = \"npipe:////./pipe/containerd-containerd\"") |> should equal true

    [<Fact>]
    let ``buildContainerdConfigToml rÃ©fÃ©rence l'image sandbox du serveur`` () =
        let toml = buildContainerdConfigToml ()
        let sandbox = getSandboxImage ()
        toml.Contains(sprintf "sandbox_image = \"%s\"" sandbox) |> should equal true

    [<Fact>]
    let ``buildContainerdConfigToml configure les rÃ©pertoires CNI`` () =
        let toml = buildContainerdConfigToml ()
        toml.Contains(sprintf "bin_dir = \"%s\"" cniBinDir) |> should equal true
        toml.Contains(sprintf "conf_dir = \"%s\"" cniConfDir) |> should equal true

    // â”€â”€â”€ CohÃ©rence version Windows â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``getSandboxImage suit le tag de la version serveur`` () =
        let image = getSandboxImage ()
        image.StartsWith("mcr.microsoft.com/windows/nanoserver:") |> should equal true
        image.EndsWith(getWindowsServerVersion ()) |> should equal true

    [<Fact>]
    let ``getMinContainerdVersion est 1.6 pour 2016 sinon 1.7`` () =
        if isWs2016 () then
            getMinContainerdVersion () |> should equal "1.6.36"
            isLegacyContainerd () |> should equal true
        else
            getMinContainerdVersion () |> should equal "1.7.27"
            isLegacyContainerd () |> should equal false

