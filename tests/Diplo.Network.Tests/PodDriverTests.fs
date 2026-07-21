namespace Diplo.Network.Tests

module PodDriverTests =

    open System
    open Xunit
    open FsUnit.Xunit
    open Diplo.Network.Plugins
    open Diplo.Grpc.Network

    type MockHnsProvider() =
        interface IHnsProvider with
            member _.CreateNetwork(_name, _subnet) = ()
            member _.RemoveNetwork(_name) = ()
            member _.CreateNat(_natName, _subnet) = ()
            member _.RemoveNat(_natName) = ()

    let createDriver () = PodDriver(MockHnsProvider() :> IHnsProvider) :> INetworkDriver

    [<Fact>]
    let ``DriverType retourne Pod`` () =
        let driver = createDriver ()
        driver.DriverType |> should equal NetworkDriver.``Pod``

    [<Fact>]
    let ``Create retourne Ok avec les informations du pod`` () =
        let driver = createDriver ()
        match driver.Create("test-pod", "10.244.0.0/24", "10.244.0.1", "", Map.empty, Map.empty) with
        | Ok info ->
            info.Name |> should equal "test-pod"
            info.Driver |> should equal NetworkDriver.``Pod``
            info.Subnet |> should equal "10.244.0.0/24"
            info.Gateway |> should equal "10.244.0.1"
            String.IsNullOrEmpty(info.Id) |> should equal false
        | Error msg -> failwithf "Create a échoué: %s" msg

    [<Fact>]
    let ``Create avec option max_containers personnalisée`` () =
        let driver = createDriver ()
        let options = Map.ofList [("max_containers", "64")]
        match driver.Create("pod-limit", "10.244.1.0/24", "10.244.1.1", "", options, Map.empty) with
        | Ok info ->
            info.Name |> should equal "pod-limit"
            info.Driver |> should equal NetworkDriver.``Pod``
        | Error msg -> failwithf "Create a échoué: %s" msg

    [<Fact>]
    let ``Inspect retourne Ok pour un pod existant`` () =
        let driver = createDriver ()
        match driver.Create("inspect-pod", "10.244.2.0/24", "10.244.2.1", "", Map.empty, Map.empty) with
        | Ok created ->
            match driver.Inspect(created.Id) with
            | Ok info -> info.Name |> should equal "inspect-pod"
            | Error msg -> failwithf "Inspect a échoué: %s" msg
        | Error msg -> failwithf "Create a échoué: %s" msg

    [<Fact>]
    let ``Inspect retourne Error pour un id inexistant`` () =
        let driver = createDriver ()
        match driver.Inspect("nonexistent") with
        | Error _ -> ()
        | Ok _ -> failwith "Inspect devrait retourner Error"

    [<Fact>]
    let ``Remove retourne Ok pour un pod existant`` () =
        let driver = createDriver ()
        match driver.Create("remove-pod", "10.244.3.0/24", "10.244.3.1", "", Map.empty, Map.empty) with
        | Ok created ->
            match driver.Remove(created.Id, false) with
            | Ok _ -> ()
            | Error msg -> failwithf "Remove a échoué: %s" msg
        | Error msg -> failwithf "Create a échoué: %s" msg

    [<Fact>]
    let ``Remove retourne Error pour un id inexistant`` () =
        let driver = createDriver ()
        match driver.Remove("nonexistent", false) with
        | Error _ -> ()
        | Ok _ -> failwith "Remove devrait retourner Error"

    [<Fact>]
    let ``List retourne tous les pods créés`` () =
        let driver = createDriver ()
        driver.Create("list-pod-1", "10.244.4.0/24", "10.244.4.1", "", Map.empty, Map.empty) |> ignore
        driver.Create("list-pod-2", "10.244.5.0/24", "10.244.5.1", "", Map.empty, Map.empty) |> ignore
        match driver.List() with
        | Ok pods ->
            pods.Length |> should equal 2
            pods |> List.exists (fun p -> p.Name = "list-pod-1") |> should equal true
            pods |> List.exists (fun p -> p.Name = "list-pod-2") |> should equal true
        | Error msg -> failwithf "List a échoué: %s" msg

    [<Fact>]
    let ``Connect retourne Ok avec un endpoint pour un conteneur`` () =
        let driver = createDriver ()
        match driver.Create("conn-pod", "10.244.6.0/24", "10.244.6.1", "", Map.empty, Map.empty) with
        | Ok created ->
            match driver.Connect(created.Id, "container-abc123", "", None, Map.empty) with
            | Ok ep ->
                String.IsNullOrEmpty(ep.EndpointId) |> should equal false
                ep.Message.Contains("conn-pod") |> should equal true
                ep.Message.Contains("1/32") |> should equal true
            | Error msg -> failwithf "Connect a échoué: %s" msg
        | Error msg -> failwithf "Create a échoué: %s" msg

    [<Fact>]
    let ``Disconnect retourne Ok`` () =
        let driver = createDriver ()
        match driver.Create("disc-pod", "10.244.7.0/24", "10.244.7.1", "", Map.empty, Map.empty) with
        | Ok created ->
            match driver.Disconnect(created.Id, "container-123", "endpoint-456", false) with
            | Ok _ -> ()
            | Error msg -> failwithf "Disconnect a échoué: %s" msg
        | Error msg -> failwithf "Create a échoué: %s" msg

    [<Fact>]
    let ``Remove puis List retourne liste vide`` () =
        let driver = createDriver ()
        match driver.Create("temp-pod", "10.244.8.0/24", "10.244.8.1", "", Map.empty, Map.empty) with
        | Ok created ->
            driver.Remove(created.Id, false) |> ignore
            match driver.List() with
            | Ok pods -> pods.Length |> should equal 0
            | Error msg -> failwithf "List a échoué: %s" msg
        | Error msg -> failwithf "Create a échoué: %s" msg

    [<Fact>]
    let ``Create échoue avec nom invalide`` () =
        let driver = createDriver ()
        match driver.Create("invalid name with spaces", "10.244.0.0/24", "10.244.0.1", "", Map.empty, Map.empty) with
        | Error _ -> ()
        | Ok _ -> failwith "Create devrait échouer avec un nom invalide"

    [<Fact>]
    let ``Connect avec adresse IP statique`` () =
        let driver = createDriver ()
        match driver.Create("ip-pod", "10.244.9.0/24", "10.244.9.1", "", Map.empty, Map.empty) with
        | Ok created ->
            match driver.Connect(created.Id, "container-ip", "", Some "10.244.9.10", Map.empty) with
            | Ok ep ->
                ep.Ipv4Address |> should equal "10.244.9.10"
            | Error msg -> failwithf "Connect a échoué: %s" msg
        | Error msg -> failwithf "Create a échoué: %s" msg
