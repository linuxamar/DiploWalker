namespace DiploWalker.Abstractions.Tests

open System.Text.Json
open Xunit
open FsUnit.Xunit
open DiploWalker.Abstractions

type JsonHelpersTests() =

    let parseJson (json: string) = JsonDocument.Parse(json).RootElement

    [<Fact>]
    let ``tryGetString retourne la valeur string`` () =
        let el = parseJson """{"name": "test"}"""
        JsonHelpers.tryGetString el "name" |> should equal "test"

    [<Fact>]
    let ``tryGetString retourne chaîne vide si propriété absente`` () =
        let el = parseJson """{"other": "value"}"""
        JsonHelpers.tryGetString el "name" |> should equal ""

    [<Fact>]
    let ``tryGetString retourne chaîne vide si pas une string`` () =
        let el = parseJson """{"count": 42}"""
        JsonHelpers.tryGetString el "count" |> should equal ""

    [<Fact>]
    let ``tryGetInt64 retourne la valeur numérique`` () =
        let el = parseJson """{"count": 42}"""
        JsonHelpers.tryGetInt64 el "count" |> should equal 42L

    [<Fact>]
    let ``tryGetInt64 retourne 0 si propriété absente`` () =
        let el = parseJson """{"other": 1}"""
        JsonHelpers.tryGetInt64 el "count" |> should equal 0L

    [<Fact>]
    let ``tryGetDouble retourne la valeur décimale`` () =
        let el = parseJson """{"ratio": 3.14}"""
        JsonHelpers.tryGetDouble el "ratio" |> should equal 3.14

    [<Fact>]
    let ``tryGetDouble retourne 0.0 si propriété absente`` () =
        let el = parseJson """{"other": 1.0}"""
        JsonHelpers.tryGetDouble el "ratio" |> should equal 0.0

    [<Fact>]
    let ``tryGetBool retourne true`` () =
        let el = parseJson """{"enabled": true}"""
        JsonHelpers.tryGetBool el "enabled" |> should equal true

    [<Fact>]
    let ``tryGetBool retourne false`` () =
        let el = parseJson """{"enabled": false}"""
        JsonHelpers.tryGetBool el "enabled" |> should equal false

    [<Fact>]
    let ``tryGetBool retourne false si propriété absente`` () =
        let el = parseJson """{"other": true}"""
        JsonHelpers.tryGetBool el "enabled" |> should equal false

    [<Fact>]
    let ``tryGetElement retourne Some si propriété existe`` () =
        let el = parseJson """{"data": {"nested": true}}"""
        let result = JsonHelpers.tryGetElement el "data"
        result.IsSome |> should equal true

    [<Fact>]
    let ``tryGetElement retourne None si propriété absente`` () =
        let el = parseJson """{"other": 1}"""
        JsonHelpers.tryGetElement el "data" |> should equal None

    [<Fact>]
    let ``tryGetStringValue retourne Some si string`` () =
        let el = parseJson """{"name": "hello"}"""
        JsonHelpers.tryGetStringValue el "name" |> should equal (Some "hello")

    [<Fact>]
    let ``tryGetStringValue retourne None si pas string`` () =
        let el = parseJson """{"count": 42}"""
        JsonHelpers.tryGetStringValue el "count" |> should equal None

    [<Fact>]
    let ``tryGetStringValue retourne None si propriété absente`` () =
        let el = parseJson """{"other": "hi"}"""
        JsonHelpers.tryGetStringValue el "name" |> should equal None

