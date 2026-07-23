namespace Diplo.Network.Tests

module BridgeDriverTests =

    open Xunit
    open FsUnit.Xunit
    open Diplo.Network.Plugins
    open Diplo.Grpc.Network

    let createDriver () = BridgeNetworkDriver() :> INetworkDriver

    [<Fact>]
    let ``DriverType retourne Bridge`` () =
        let driver = createDriver ()
        driver.DriverType |> should equal NetworkDriver.Bridge

    [<Fact>]
    let ``Inspect retourne Error pour un id inexistant`` () =
        let driver = createDriver ()
        match driver.Inspect("nonexistent") with
        | Error _ -> ()
        | Ok _ -> failwith "Inspect devrait retourner Error pour un id inexistant"

    [<Fact>]
    let ``Remove retourne Error pour un id inexistant`` () =
        let driver = createDriver ()
        match driver.Remove("nonexistent", false) with
        | Error msg -> msg |> should haveSubstring "introuvable"
        | Ok _ -> failwith "Remove devrait retourner Error pour un id inexistant"

    [<Fact>]
    let ``List retourne vide initialement`` () =
        let driver = createDriver ()
        match driver.List() with
        | Ok networks -> networks |> should be Empty
        | Error msg -> failwithf "List a echoue: %s" msg

    [<Fact>]
    let ``Connect retourne Error pour un reseau inexistant`` () =
        let driver = createDriver ()
        match driver.Connect("nonexistent", "container123", "", None, Map.empty) with
        | Error _ -> ()
        | Ok _ -> failwith "Connect devrait retourner Error pour un reseau inexistant"

    [<Fact>]
    let ``Disconnect retourne Error pour un reseau inexistant`` () =
        let driver = createDriver ()
        match driver.Disconnect("nonexistent", "container123", "endpoint456", false) with
        | Error _ -> ()
        | Ok _ -> failwith "Disconnect devrait retourner Error pour un reseau inexistant"

    [<Fact>]
    let ``Create avec nom vide retourne Error`` () =
        let driver = createDriver ()
        match driver.Create("", "10.0.0.0/24", "10.0.0.1", "", Map.empty, Map.empty) with
        | Error _ -> ()
        | Ok _ -> failwith "Create devrait retourner Error avec un nom vide"

    [<Fact>]
    let ``Create avec caracteres dangereux dans le nom retourne Error`` () =
        let driver = createDriver ()
        match driver.Create("test;rm -rf /", "", "", "", Map.empty, Map.empty) with
        | Error _ -> ()
        | Ok _ -> failwith "Create devrait retourner Error avec un nom dangereux"

    [<Fact>]
    let ``Create avec CIDR invalide retourne Error`` () =
        let driver = createDriver ()
        match driver.Create("test-net", "999.999.999.999/99", "", "", Map.empty, Map.empty) with
        | Error _ -> ()
        | Ok _ -> failwith "Create devrait retourner Error avec un CIDR invalide"

    [<Fact>]
    let ``Create avec gateway invalide retourne Error`` () =
        let driver = createDriver ()
        match driver.Create("test-net", "10.0.0.0/24", "999.999.999.999", "", Map.empty, Map.empty) with
        | Error _ -> ()
        | Ok _ -> failwith "Create devrait retourner Error avec une gateway invalide"
