namespace Diplo.Installer.Tests

module AppSettingsJsonTests =

    open System.Text.Json.Nodes
    open Xunit
    open FsUnit.Xunit
    open Diplo.Installer.Core

    let private parseJson (json: string) : JsonNode = JsonNode.Parse(json)

    [<Fact>]
    let ``buildAppSettingsJson returns valid JSON with all required sections`` () =
        let json = buildAppSettingsJson 5000 "TestPipe" (Some "Docker")
        let doc = parseJson json
        doc.ToString().Length |> should be (greaterThan 0)

    [<Fact>]
    let ``buildAppSettingsJson contains ServiceSettings with GrpcPort`` () =
        let json = buildAppSettingsJson 5000 "TestPipe" (Some "Docker")
        let doc = parseJson json :?> JsonObject
        let serviceSettings = doc["ServiceSettings"] :?> JsonObject
        serviceSettings["GrpcPort"].GetValue<int>() |> should equal 5000

    [<Fact>]
    let ``buildAppSettingsJson contains ServiceSettings with NamedPipeName`` () =
        let json = buildAppSettingsJson 5000 "TestPipe" (Some "Docker")
        let doc = parseJson json :?> JsonObject
        let serviceSettings = doc["ServiceSettings"] :?> JsonObject
        serviceSettings["NamedPipeName"].GetValue<string>() |> should equal "TestPipe"

    [<Fact>]
    let ``buildAppSettingsJson contains ServiceSettings with UseTcp and UseNamedPipes`` () =
        let json = buildAppSettingsJson 5000 "TestPipe" (Some "Docker")
        let doc = parseJson json :?> JsonObject
        let serviceSettings = doc["ServiceSettings"] :?> JsonObject
        serviceSettings["UseTcp"].GetValue<bool>() |> should equal true
        serviceSettings["UseNamedPipes"].GetValue<bool>() |> should equal true

    [<Fact>]
    let ``buildAppSettingsJson with Some isolationType contains IsolationType`` () =
        let json = buildAppSettingsJson 5000 "TestPipe" (Some "Docker")
        let doc = parseJson json :?> JsonObject
        doc["IsolationType"].GetValue<string>() |> should equal "Docker"

    [<Fact>]
    let ``buildAppSettingsJson with None isolationType omits IsolationType`` () =
        let json = buildAppSettingsJson 5000 "TestPipe" None
        let doc = parseJson json :?> JsonObject
        doc.ContainsKey("IsolationType") |> should equal false

    [<Fact>]
    let ``buildAppSettingsJson contains Logging with LogLevel section`` () =
        let json = buildAppSettingsJson 5000 "TestPipe" (Some "Docker")
        let doc = parseJson json :?> JsonObject
        let logging = doc["Logging"] :?> JsonObject
        let logLevel = logging["LogLevel"] :?> JsonObject
        logLevel["Default"].GetValue<string>() |> should equal "Information"

    [<Fact>]
    let ``buildAppSettingsJson contains Logging with Microsoft.Hosting.Lifetime`` () =
        let json = buildAppSettingsJson 5000 "TestPipe" (Some "Docker")
        let doc = parseJson json :?> JsonObject
        let logging = doc["Logging"] :?> JsonObject
        let logLevel = logging["LogLevel"] :?> JsonObject

        logLevel["Microsoft.Hosting.Lifetime"].GetValue<string>()
        |> should equal "Information"

    [<Fact>]
    let ``buildAppSettingsJson uses different ports correctly`` () =
        let json = buildAppSettingsJson 9999 "MyPipe" None
        let doc = parseJson json :?> JsonObject
        let serviceSettings = doc["ServiceSettings"] :?> JsonObject
        serviceSettings["GrpcPort"].GetValue<int>() |> should equal 9999
        serviceSettings["NamedPipeName"].GetValue<string>() |> should equal "MyPipe"

    [<Fact>]
    let ``buildAppSettingsJson produces indented output`` () =
        let json = buildAppSettingsJson 5000 "TestPipe" None
        json.Contains("\n") |> should equal true
