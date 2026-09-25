namespace DiploWalker.Abstractions.Tests

open System.Text.Json
open Xunit
open FsUnit.Xunit
open DiploWalker.Abstractions

type DiploWalkerJsonTests() =

    [<Fact>]
    let ``defaultOptions produit du JSON indentÃ©`` () =
        let opts = DiploWalkerJson.defaultOptions
        opts.WriteIndented |> should equal true

    [<Fact>]
    let ``snakeCaseOptions utilise SnakeCaseLower`` () =
        let opts = DiploWalkerJson.snakeCaseOptions
        opts.PropertyNamingPolicy |> should equal JsonNamingPolicy.SnakeCaseLower

    [<Fact>]
    let ``caseInsensitiveOptions est insensible Ã  la casse`` () =
        let opts = DiploWalkerJson.caseInsensitiveOptions
        opts.PropertyNameCaseInsensitive |> should equal true

    [<Fact>]
    let ``documentOptions a MaxDepth de 64`` () =
        DiploWalkerJson.documentOptions.MaxDepth |> should equal 64

    [<Fact>]
    let ``withMaxDepthDoc retourne le bon MaxDepth`` () =
        DiploWalkerJson.withMaxDepthDoc 32 |> fun o -> o.MaxDepth |> should equal 32


