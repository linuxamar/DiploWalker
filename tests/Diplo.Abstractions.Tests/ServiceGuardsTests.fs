namespace Diplo.Abstractions.Tests

open Xunit
open FsUnit.Xunit
open Grpc.Core
open Diplo.Abstractions

type ServiceGuardsTests() =

    [<Fact>]
    let ``requireNonEmpty ne lève pas si valeur non vide`` () =
        ServiceGuards.requireNonEmpty "hello" "Test"

    [<Fact>]
    let ``requireNonEmpty lève si valeur vide`` () =
        (fun () -> ServiceGuards.requireNonEmpty "" "Test")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireNonEmpty lève si valeur null`` () =
        (fun () -> ServiceGuards.requireNonEmpty null "Test")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireNonEmpty lève si whitespace`` () =
        (fun () -> ServiceGuards.requireNonEmpty "   " "Test")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requirePositive ne lève pas si positif`` () = ServiceGuards.requirePositive 5 "Port"

    [<Fact>]
    let ``requirePositive lève si zero`` () =
        (fun () -> ServiceGuards.requirePositive 0 "Port")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requirePositive lève si négatif`` () =
        (fun () -> ServiceGuards.requirePositive -1 "Port")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requirePositiveInt64 ne lève pas si positif`` () =
        ServiceGuards.requirePositiveInt64 100L "Taille"

    [<Fact>]
    let ``requirePositiveInt64 lève si zero`` () =
        (fun () -> ServiceGuards.requirePositiveInt64 0L "Taille")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireInRange ne lève pas si dans l'intervalle`` () =
        ServiceGuards.requireInRange 5 1 10 "Port"

    [<Fact>]
    let ``requireInRange lève si trop petit`` () =
        (fun () -> ServiceGuards.requireInRange 0 1 10 "Port")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireInRange lève si trop grand`` () =
        (fun () -> ServiceGuards.requireInRange 11 1 10 "Port")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireId ne lève pas si identifiant valide`` () =
        ServiceGuards.requireId "mon-conteneur_01" "Conteneur"

    [<Fact>]
    let ``requireId lève si identifiant vide`` () =
        (fun () -> ServiceGuards.requireId "" "Conteneur")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireContainerId ne lève pas si identifiant valide`` () = ServiceGuards.requireContainerId "c-1"

    [<Fact>]
    let ``requireContainerId lève si identifiant vide`` () =
        (fun () -> ServiceGuards.requireContainerId "")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireSafeCommand ne lève pas si commande sûre`` () =
        ServiceGuards.requireSafeCommand [| "ls"; "-la" |]

    [<Fact>]
    let ``requireSafeCommand lève si commande vide`` () =
        (fun () -> ServiceGuards.requireSafeCommand [||])
        |> should throw typeof<RpcException>
