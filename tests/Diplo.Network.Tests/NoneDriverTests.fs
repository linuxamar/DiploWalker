namespace Diplo.Network.Tests

module NoneDriverTests =

    open System
    open Xunit
    open FsUnit.Xunit
    open Diplo.Network.Plugins
    open Diplo.Grpc.Network

    let createDriver () = NoneDriver() :> INetworkDriver

    [<Fact>]
    let ``DriverType retourne None`` () =
        let driver = createDriver ()
        driver.DriverType |> should equal NetworkDriver.``None``

    [<Fact>]
    let ``Create retourne Ok avec les informations du reseau`` () =
        let driver = createDriver ()

        match driver.Create("test-net", "", "", "", Map.empty, Map.empty) with
        | Ok info ->
            info.Name |> should equal "test-net"
            info.Driver |> should equal NetworkDriver.``None``
            String.IsNullOrEmpty(info.Id) |> should equal false
        | Error msg -> failwithf "Create a echoue: %s" msg

    [<Fact>]
    let ``Inspect retourne Ok pour un reseau existant`` () =
        let driver = createDriver ()

        match driver.Create("inspect-net", "", "", "", Map.empty, Map.empty) with
        | Ok created ->
            match driver.Inspect(created.Id) with
            | Ok info -> info.Name |> should equal "inspect-net"
            | Error msg -> failwithf "Inspect a echoue: %s" msg
        | Error msg -> failwithf "Create a echoue: %s" msg

    [<Fact>]
    let ``Inspect retourne Error pour un id inexistant`` () =
        let driver = createDriver ()

        match driver.Inspect("nonexistent") with
        | Error _ -> ()
        | Ok _ -> failwith "Inspect devrait retourner Error"

    [<Fact>]
    let ``Remove retourne Ok pour un reseau existant`` () =
        let driver = createDriver ()

        match driver.Create("remove-net", "", "", "", Map.empty, Map.empty) with
        | Ok created ->
            match driver.Remove(created.Id, false) with
            | Ok _ -> ()
            | Error msg -> failwithf "Remove a echoue: %s" msg
        | Error msg -> failwithf "Create a echoue: %s" msg

    [<Fact>]
    let ``Remove retourne Error pour un id inexistant`` () =
        let driver = createDriver ()

        match driver.Remove("nonexistent", false) with
        | Error _ -> ()
        | Ok _ -> failwith "Remove devrait retourner Error"

    [<Fact>]
    let ``List retourne tous les reseaux crees`` () =
        let driver = createDriver ()
        driver.Create("list-net-1", "", "", "", Map.empty, Map.empty) |> ignore
        driver.Create("list-net-2", "", "", "", Map.empty, Map.empty) |> ignore

        match driver.List() with
        | Ok networks ->
            networks.Length |> should equal 2
            networks |> List.exists (fun n -> n.Name = "list-net-1") |> should equal true
            networks |> List.exists (fun n -> n.Name = "list-net-2") |> should equal true
        | Error msg -> failwithf "List a echoue: %s" msg

    [<Fact>]
    let ``Connect retourne Ok avec un endpoint pour un container`` () =
        let driver = createDriver ()

        match driver.Create("conn-net", "", "", "", Map.empty, Map.empty) with
        | Ok created ->
            match driver.Connect(created.Id, "container-123", "", None, Map.empty) with
            | Ok ep ->
                String.IsNullOrEmpty(ep.EndpointId) |> should equal false
                ep.Message.Contains("container-123") |> should equal true
            | Error msg -> failwithf "Connect a echoue: %s" msg
        | Error msg -> failwithf "Create a echoue: %s" msg

    [<Fact>]
    let ``Disconnect retourne Ok`` () =
        let driver = createDriver ()

        match driver.Create("disc-net", "", "", "", Map.empty, Map.empty) with
        | Ok created ->
            match driver.Disconnect(created.Id, "container-123", "endpoint-456", false) with
            | Ok _ -> ()
            | Error msg -> failwithf "Disconnect a echoue: %s" msg
        | Error msg -> failwithf "Create a echoue: %s" msg

    [<Fact>]
    let ``Remove puis List retourne liste vide`` () =
        let driver = createDriver ()

        match driver.Create("temp-net", "", "", "", Map.empty, Map.empty) with
        | Ok created ->
            driver.Remove(created.Id, false) |> ignore

            match driver.List() with
            | Ok nets -> nets.Length |> should equal 0
            | Error msg -> failwithf "List a echoue: %s" msg
        | Error msg -> failwithf "Create a echoue: %s" msg

    [<Fact>]
    let ``Prune supprime tous les reseaux None`` () =
        let driver = createDriver ()
        driver.Create("prune-none-1", "", "", "", Map.empty, Map.empty) |> ignore
        driver.Create("prune-none-2", "", "", "", Map.empty, Map.empty) |> ignore

        match driver.Prune() with
        | Ok removed ->
            removed.Length |> should equal 2

            match driver.List() with
            | Ok nets -> nets.Length |> should equal 0
            | Error msg -> failwithf "List a echoue: %s" msg
        | Error msg -> failwithf "Prune a echoue: %s" msg
