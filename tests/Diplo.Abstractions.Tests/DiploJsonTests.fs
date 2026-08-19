namespace Diplo.Abstractions.Tests

open System.Text.Json
open Xunit
open FsUnit.Xunit
open Diplo.Abstractions

type DiploJsonTests() =

    [<Fact>]
    let ``defaultOptions produit du JSON indenté`` () =
        let opts = DiploJson.defaultOptions
        opts.WriteIndented |> should equal true

    [<Fact>]
    let ``snakeCaseOptions utilise SnakeCaseLower`` () =
        let opts = DiploJson.snakeCaseOptions
        opts.PropertyNamingPolicy |> should equal JsonNamingPolicy.SnakeCaseLower

    [<Fact>]
    let ``caseInsensitiveOptions est insensible à la casse`` () =
        let opts = DiploJson.caseInsensitiveOptions
        opts.PropertyNameCaseInsensitive |> should equal true

    [<Fact>]
    let ``documentOptions a MaxDepth de 64`` () =
        DiploJson.documentOptions.MaxDepth |> should equal 64

    [<Fact>]
    let ``withMaxDepthDoc retourne le bon MaxDepth`` () =
        DiploJson.withMaxDepthDoc 32 |> fun o -> o.MaxDepth |> should equal 32
