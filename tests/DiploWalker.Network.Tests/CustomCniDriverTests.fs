namespace DiploWalker.Network.Tests

module CustomCniDriverTests =

    open System
    open Xunit
    open FsUnit.Xunit
    open DiploWalker.Network.Plugins
    open DiploWalker.Grpc.Network

    let createDriver () = CustomCniDriver() :> INetworkDriver

    [<Fact>]
    let ``DriverType retourne CustomCni`` () =
        let driver = createDriver ()
        driver.DriverType |> should equal NetworkDriver.CustomCni

    [<Fact>]
    let ``Create avec subnet explicit le utilise`` () =
        let driver = createDriver ()

        match driver.Create("cni-net", "10.244.0.0/16", "10.244.0.1", "", Map.empty, Map.empty) with
        | Ok info ->
            info.Name |> should equal "cni-net"
            info.Subnet |> should equal "10.244.0.0/16"
            info.Gateway |> should equal "10.244.0.1"
            info.Driver |> should equal NetworkDriver.CustomCni
        | Error msg -> failwithf "Create a echoue: %s" msg

    [<Fact>]
    let ``Create sans gateway genere un gateway par defaut`` () =
        let driver = createDriver ()

        match driver.Create("auto-net", "172.30.0.0/16", "", "", Map.empty, Map.empty) with
        | Ok info ->
            info.Subnet |> should equal "172.30.0.0/16"
            info.Gateway |> should equal "172.30.0.1"
        | Error msg -> failwithf "Create a echoue: %s" msg

    [<Fact>]
    let ``Inspect retourne Ok pour un reseau existant`` () =
        let driver = createDriver ()

        match driver.Create("inspect-cni", "10.244.0.0/16", "", "", Map.empty, Map.empty) with
        | Ok created ->
            match driver.Inspect(created.Id) with
            | Ok info -> info.Name |> should equal "inspect-cni"
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

        match driver.Create("remove-cni", "10.244.0.0/16", "", "", Map.empty, Map.empty) with
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

        driver.Create("cni-list-1", "10.244.0.0/16", "", "", Map.empty, Map.empty)
        |> ignore

        driver.Create("cni-list-2", "10.245.0.0/16", "", "", Map.empty, Map.empty)
        |> ignore

        match driver.List() with
        | Ok networks -> networks.Length |> should equal 2
        | Error msg -> failwithf "List a echoue: %s" msg

    [<Fact>]
    let ``Connect sans plugin retourne Ok`` () =
        let driver = createDriver ()

        match driver.Create("conn-cni", "10.244.0.0/16", "", "", Map.empty, Map.empty) with
        | Ok created ->
            match driver.Connect(created.Id, "container-abc", "", None, Map.empty) with
            | Ok ep ->
                String.IsNullOrEmpty(ep.EndpointId) |> should equal false
                ep.Message.Contains("conn-cni") |> should equal true
            | Error msg -> failwithf "Connect a echoue: %s" msg
        | Error msg -> failwithf "Create a echoue: %s" msg

    [<Fact>]
    let ``Disconnect sans plugin retourne Ok`` () =
        let driver = createDriver ()

        match driver.Create("disc-cni", "10.244.0.0/16", "", "", Map.empty, Map.empty) with
        | Ok created ->
            match driver.Disconnect(created.Id, "container-abc", "endpoint-xyz", false) with
            | Ok _ -> ()
            | Error msg -> failwithf "Disconnect a echoue: %s" msg
        | Error msg -> failwithf "Create a echoue: %s" msg

    [<Fact>]
    let ``Prune supprime tous les reseaux CNI`` () =
        let driver = createDriver ()

        match driver.Create("prune-cni-1", "10.244.0.0/16", "", "", Map.empty, Map.empty) with
        | Ok n1 ->
            driver.Create("prune-cni-2", "10.245.0.0/16", "", "", Map.empty, Map.empty)
            |> ignore

            match driver.Prune() with
            | Ok removed ->
                removed.Length |> should equal 2
                removed |> List.contains n1.Id |> should equal true

                match driver.List() with
                | Ok nets -> nets.Length |> should equal 0
                | Error msg -> failwithf "List a echoue: %s" msg
            | Error msg -> failwithf "Prune a echoue: %s" msg
        | Error msg -> failwithf "Create a echoue: %s" msg

    [<Fact>]
    let ``GetAvailableSubnet retourne un sous-reseau non utilise`` () =
        let driver = CustomCniDriver()
        let subnet = driver.GetAvailableSubnet()
        subnet |> should not' (equal "")

    [<Fact>]
    let ``GetAvailableSubnet evite les sous-reseaux deja utilises`` () =
        let driver = createDriver ()

        match driver.Create("sub-cni", "10.244.0.0/16", "", "", Map.empty, Map.empty) with
        | Ok created ->
            match driver.Inspect(created.Id) with
            | Ok info -> info.Subnet |> should equal "10.244.0.0/16"
            | Error msg -> failwithf "Inspect a echoue: %s" msg
        | Error msg -> failwithf "Create a echoue: %s" msg

        let next = (CustomCniDriver()).GetAvailableSubnet()
        next |> should not' (equal "10.244.0.0/16")

