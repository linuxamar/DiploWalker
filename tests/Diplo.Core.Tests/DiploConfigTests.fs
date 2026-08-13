namespace Diplo.Core.Tests

module DiploConfigTests =

    open System
    open System.IO
    open Xunit
    open FsUnit.Xunit
    open Diplo.Core

    // ── configPath ─────────────────────────────────────────────────

    [<Fact>]
    let ``configPath sans variable d'environnement pointe vers diplo.json du répertoire courant`` () =
        let old = Environment.GetEnvironmentVariable("DIPLO_CONFIG_HOME")
        Environment.SetEnvironmentVariable("DIPLO_CONFIG_HOME", null)
        try
            DiploConfig.configPath()
            |> should equal (Path.Combine(Directory.GetCurrentDirectory(), "diplo.json"))
        finally
            Environment.SetEnvironmentVariable("DIPLO_CONFIG_HOME", old)

    [<Fact>]
    let ``configPath honore la variable d'environnement DIPLO_CONFIG_HOME`` () =
        let old = Environment.GetEnvironmentVariable("DIPLO_CONFIG_HOME")
        let custom = Path.Combine(Path.GetTempPath(), "diplo-config-home")
        Environment.SetEnvironmentVariable("DIPLO_CONFIG_HOME", custom)
        try
            DiploConfig.configPath()
            |> should equal (Path.Combine(custom, "diplo.json"))
        finally
            Environment.SetEnvironmentVariable("DIPLO_CONFIG_HOME", old)

    // ── load ───────────────────────────────────────────────────────

    let private tempFile (content: string) =
        let dir = Path.Combine(Path.GetTempPath(), "diplo-config-test-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(dir) |> ignore
        let path = Path.Combine(dir, "diplo.json")
        File.WriteAllText(path, content)
        (dir, path)

    [<Fact>]
    let ``load avec fichier valide renvoie les adresses normalisées`` () =
        let dir, path = tempFile """{ "container": { "address": "localhost:5001" }, "network": { "address": "http://pipe:/diplo-network" } }"""
        try
            let (c, v, n) = DiploConfig.load path
            c |> should equal (Some "http://localhost:5001")
            v |> should equal None
            n |> should equal (Some "http://pipe:/diplo-network")
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``load avec fichier absent renvoie des None (repli par défaut)`` () =
        let path = Path.Combine(Path.GetTempPath(), "diplo-config-absente-" + Guid.NewGuid().ToString("N"), "diplo.json")
        let (c, v, n) = DiploConfig.load path
        c |> should equal None
        v |> should equal None
        n |> should equal None

    [<Fact>]
    let ``load avec fichier malformé renvoie des None (repli par défaut)`` () =
        let dir, path = tempFile "{ pas du json ]"
        try
            let (c, v, n) = DiploConfig.load path
            c |> should equal None
            v |> should equal None
            n |> should equal None
        finally
            Directory.Delete(dir, true)

    // ── normalizeAddress ────────────────────────────────────────────

    [<Fact>]
    let ``normalizeAddress avec host:port prefixe http`` () =
        DiploConfig.normalizeAddress "localhost:5001"
        |> should equal "http://localhost:5001"

    [<Fact>]
    let ``normalizeAddress avec URL pipe inchangée`` () =
        DiploConfig.normalizeAddress "http://pipe:/diplo-container"
        |> should equal "http://pipe:/diplo-container"

    [<Fact>]
    let ``normalizeAddress avec URL http inchangée`` () =
        DiploConfig.normalizeAddress "http://localhost:5001"
        |> should equal "http://localhost:5001"

    [<Fact>]
    let ``normalizeAddress avec espaces environnants est nettoyée`` () =
        DiploConfig.normalizeAddress " localhost:5001 "
        |> should equal "http://localhost:5001"

    // ── parse ───────────────────────────────────────────────────────

    [<Fact>]
    let ``parse avec toutes les sections renvoie les adresses normalisées`` () =
        let json = """{ "container": { "address": "localhost:5001" }, "volume": { "address": "localhost:5002" }, "network": { "address": "http://pipe:/diplo-network" } }"""
        let (c, v, n) = DiploConfig.parse json
        c |> should equal (Some "http://localhost:5001")
        v |> should equal (Some "http://localhost:5002")
        n |> should equal (Some "http://pipe:/diplo-network")

    [<Fact>]
    let ``parse avec section absente renvoie None pour cette section`` () =
        let json = """{ "container": { "address": "localhost:5001" } }"""
        let (c, v, n) = DiploConfig.parse json
        c |> should equal (Some "http://localhost:5001")
        v |> should equal None
        n |> should equal None

    [<Fact>]
    let ``parse avec adresse vide renvoie None`` () =
        let json = """{ "container": { "address": "" } }"""
        let (c, _, _) = DiploConfig.parse json
        c |> should equal None

    [<Fact>]
    let ``parse avec JSON malformé renvoie des None (repli par défaut)`` () =
        let (c, v, n) = DiploConfig.parse "{ pas du json ]"
        c |> should equal None
        v |> should equal None
        n |> should equal None

    [<Fact>]
    let ``parse avec JSON vide renvoie des None`` () =
        let (c, v, n) = DiploConfig.parse ""
        c |> should equal None
        v |> should equal None
        n |> should equal None

    [<Fact>]
    let ``parse avec adresse non chaîne renvoie None`` () =
        let json = """{ "container": { "address": 42 } }"""
        let (c, _, _) = DiploConfig.parse json
        c |> should equal None
