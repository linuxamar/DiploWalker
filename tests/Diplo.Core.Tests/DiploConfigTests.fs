namespace Diplo.Core.Tests

module DiploConfigTests =

    open Xunit
    open FsUnit.Xunit
    open Diplo.Core

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
