namespace DiploWalker.Core.Tests

module DiploWalkerConfigTests =

    open System
    open System.IO
    open Xunit
    open FsUnit.Xunit
    open DiploWalker.Core

    // â”€â”€ configPath â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``configPath sans variable d'environnement pointe vers DiploWalker.json du rÃ©pertoire courant`` () =
        let old = Environment.GetEnvironmentVariable("DIPLO_CONFIG_HOME")
        Environment.SetEnvironmentVariable("DIPLO_CONFIG_HOME", null)

        try
            DiploWalkerConfig.configPath ()
            |> should equal (Path.Combine(Directory.GetCurrentDirectory(), "DiploWalker.json"))
        finally
            Environment.SetEnvironmentVariable("DIPLO_CONFIG_HOME", old)

    [<Fact>]
    let ``configPath honore la variable d'environnement DIPLO_CONFIG_HOME`` () =
        let old = Environment.GetEnvironmentVariable("DIPLO_CONFIG_HOME")
        let custom = Path.Combine(Path.GetTempPath(), "diplo-config-home")
        Environment.SetEnvironmentVariable("DIPLO_CONFIG_HOME", custom)

        try
            DiploWalkerConfig.configPath () |> should equal (Path.Combine(custom, "DiploWalker.json"))
        finally
            Environment.SetEnvironmentVariable("DIPLO_CONFIG_HOME", old)

    // â”€â”€ load â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    let private tempFile (content: string) =
        let dir =
            Path.Combine(Path.GetTempPath(), "diplo-config-test-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory(dir) |> ignore
        let path = Path.Combine(dir, "DiploWalker.json")
        File.WriteAllText(path, content)
        (dir, path)

    [<Fact>]
    let ``load avec fichier valide renvoie les adresses normalisÃ©es`` () =
        let dir, path =
            tempFile
                """{ "container": { "address": "localhost:5001" }, "network": { "address": "http://pipe:/diplo-network" } }"""

        try
            let (c, v, n) = DiploWalkerConfig.load path
            c |> should equal (Some "http://localhost:5001")
            v |> should equal None
            n |> should equal (Some "http://pipe:/diplo-network")
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``load avec fichier absent renvoie des None (repli par dÃ©faut)`` () =
        let path =
            Path.Combine(Path.GetTempPath(), "diplo-config-absente-" + Guid.NewGuid().ToString("N"), "DiploWalker.json")

        let (c, v, n) = DiploWalkerConfig.load path
        c |> should equal None
        v |> should equal None
        n |> should equal None

    [<Fact>]
    let ``load avec fichier malformÃ© renvoie des None (repli par dÃ©faut)`` () =
        let dir, path = tempFile "{ pas du json ]"

        try
            let (c, v, n) = DiploWalkerConfig.load path
            c |> should equal None
            v |> should equal None
            n |> should equal None
        finally
            Directory.Delete(dir, true)

    // â”€â”€ save â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    let private tempDir () =
        let dir =
            Path.Combine(Path.GetTempPath(), "diplo-config-test-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory(dir) |> ignore
        dir

    [<Fact>]
    let ``save Ã©crit un fichier relu par load`` () =
        let dir = tempDir ()

        try
            let path = Path.Combine(dir, "DiploWalker.json")
            DiploWalkerConfig.save path "localhost:5001" "localhost:5002" "http://pipe:/diplo-network"
            let (c, v, n) = DiploWalkerConfig.load path
            c |> should equal (Some "http://localhost:5001")
            v |> should equal (Some "http://localhost:5002")
            n |> should equal (Some "http://pipe:/diplo-network")
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``save prÃ©serve namespace et logLevel existants`` () =
        let dir = tempDir ()

        try
            let path = Path.Combine(dir, "DiploWalker.json")

            File.WriteAllText(
                path,
                """{ "container": { "address": "localhost:5001", "namespace": "prod" }, "volume": { "address": "localhost:5002" }, "network": { "address": "localhost:5003" }, "logLevel": "Debug" }"""
            )

            DiploWalkerConfig.save path "localhost:9001" "localhost:9002" "localhost:9003"
            let content = File.ReadAllText path
            content |> should haveSubstring "\"namespace\": \"prod\""
            content |> should haveSubstring "\"logLevel\": \"Debug\""
            content |> should haveSubstring "\"address\": \"localhost:9001\""
            let (c, _, _) = DiploWalkerConfig.load path
            c |> should equal (Some "http://localhost:9001")
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``save sans fichier existant n'ajoute pas de metadata`` () =
        let dir = tempDir ()

        try
            let path = Path.Combine(dir, "DiploWalker.json")
            DiploWalkerConfig.save path "localhost:5001" "localhost:5002" "localhost:5003"
            let content = File.ReadAllText path
            content |> should not' (haveSubstring "namespace")
            content |> should not' (haveSubstring "logLevel")
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``save crÃ©e le rÃ©pertoire parent manquant`` () =
        let dir = tempDir ()

        try
            let nested = Path.Combine(dir, "sous", "dossier")
            let path = Path.Combine(nested, "DiploWalker.json")
            DiploWalkerConfig.save path "localhost:5001" "localhost:5002" "localhost:5003"
            File.Exists path |> should equal true
        finally
            Directory.Delete(dir, true)

    // â”€â”€ cache / invalidate â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``invalidate vide le cache et relit la configuration`` () =
        let old = Environment.GetEnvironmentVariable("DIPLO_CONFIG_HOME")

        let home =
            Path.Combine(Path.GetTempPath(), "diplo-config-test-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory(home) |> ignore
        Environment.SetEnvironmentVariable("DIPLO_CONFIG_HOME", home)

        try
            let path = Path.Combine(home, "DiploWalker.json")
            DiploWalkerConfig.save path "localhost:5001" "localhost:5002" "localhost:5003"
            DiploWalkerConfig.invalidate ()
            DiploWalkerConfig.containerAddress () |> should equal (Some "http://localhost:5001")
            DiploWalkerConfig.save path "localhost:9001" "localhost:9002" "localhost:9003"
            DiploWalkerConfig.containerAddress () |> should equal (Some "http://localhost:5001")
            DiploWalkerConfig.invalidate ()
            DiploWalkerConfig.containerAddress () |> should equal (Some "http://localhost:9001")
        finally
            Environment.SetEnvironmentVariable("DIPLO_CONFIG_HOME", old)
            DiploWalkerConfig.invalidate ()

            if Directory.Exists home then
                Directory.Delete(home, true)

    // â”€â”€ normalizeAddress â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``normalizeAddress avec host:port prefixe http`` () =
        DiploWalkerConfig.normalizeAddress "localhost:5001"
        |> should equal "http://localhost:5001"

    [<Fact>]
    let ``normalizeAddress avec URL pipe inchangÃ©e`` () =
        DiploWalkerConfig.normalizeAddress "http://pipe:/diplo-container"
        |> should equal "http://pipe:/diplo-container"

    [<Fact>]
    let ``normalizeAddress avec URL http inchangÃ©e`` () =
        DiploWalkerConfig.normalizeAddress "http://localhost:5001"
        |> should equal "http://localhost:5001"

    [<Fact>]
    let ``normalizeAddress avec espaces environnants est nettoyÃ©e`` () =
        DiploWalkerConfig.normalizeAddress " localhost:5001 "
        |> should equal "http://localhost:5001"

    // â”€â”€ parse â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``parse avec toutes les sections renvoie les adresses normalisÃ©es`` () =
        let json =
            """{ "container": { "address": "localhost:5001" }, "volume": { "address": "localhost:5002" }, "network": { "address": "http://pipe:/diplo-network" } }"""

        let (c, v, n) = DiploWalkerConfig.parse json
        c |> should equal (Some "http://localhost:5001")
        v |> should equal (Some "http://localhost:5002")
        n |> should equal (Some "http://pipe:/diplo-network")

    [<Fact>]
    let ``parse avec section absente renvoie None pour cette section`` () =
        let json = """{ "container": { "address": "localhost:5001" } }"""
        let (c, v, n) = DiploWalkerConfig.parse json
        c |> should equal (Some "http://localhost:5001")
        v |> should equal None
        n |> should equal None

    [<Fact>]
    let ``parse avec adresse vide renvoie None`` () =
        let json = """{ "container": { "address": "" } }"""
        let (c, _, _) = DiploWalkerConfig.parse json
        c |> should equal None

    [<Fact>]
    let ``parse avec JSON malformÃ© renvoie des None (repli par dÃ©faut)`` () =
        let (c, v, n) = DiploWalkerConfig.parse "{ pas du json ]"
        c |> should equal None
        v |> should equal None
        n |> should equal None

    [<Fact>]
    let ``parse avec JSON vide renvoie des None`` () =
        let (c, v, n) = DiploWalkerConfig.parse ""
        c |> should equal None
        v |> should equal None
        n |> should equal None

    [<Fact>]
    let ``parse avec adresse non chaÃ®ne renvoie None`` () =
        let json = """{ "container": { "address": 42 } }"""
        let (c, _, _) = DiploWalkerConfig.parse json
        c |> should equal None



