namespace DiploWalker.Abstractions.Tests

open Xunit
open FsUnit.Xunit
open Grpc.Core
open DiploWalker.Abstractions

type ServiceGuardsTests() =

    [<Fact>]
    let ``requireNonEmpty ne lÃ¨ve pas si valeur non vide`` () =
        ServiceGuards.requireNonEmpty "hello" "Test"

    [<Fact>]
    let ``requireNonEmpty lÃ¨ve si valeur vide`` () =
        (fun () -> ServiceGuards.requireNonEmpty "" "Test")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireNonEmpty lÃ¨ve si valeur null`` () =
        (fun () -> ServiceGuards.requireNonEmpty null "Test")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireNonEmpty lÃ¨ve si whitespace`` () =
        (fun () -> ServiceGuards.requireNonEmpty "   " "Test")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requirePositive ne lÃ¨ve pas si positif`` () = ServiceGuards.requirePositive 5 "Port"

    [<Fact>]
    let ``requirePositive lÃ¨ve si zero`` () =
        (fun () -> ServiceGuards.requirePositive 0 "Port")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requirePositive lÃ¨ve si nÃ©gatif`` () =
        (fun () -> ServiceGuards.requirePositive -1 "Port")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requirePositiveInt64 ne lÃ¨ve pas si positif`` () =
        ServiceGuards.requirePositiveInt64 100L "Taille"

    [<Fact>]
    let ``requirePositiveInt64 lÃ¨ve si zero`` () =
        (fun () -> ServiceGuards.requirePositiveInt64 0L "Taille")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireInRange ne lÃ¨ve pas si dans l'intervalle`` () =
        ServiceGuards.requireInRange 5 1 10 "Port"

    [<Fact>]
    let ``requireInRange lÃ¨ve si trop petit`` () =
        (fun () -> ServiceGuards.requireInRange 0 1 10 "Port")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireInRange lÃ¨ve si trop grand`` () =
        (fun () -> ServiceGuards.requireInRange 11 1 10 "Port")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireId ne lÃ¨ve pas si identifiant valide`` () =
        ServiceGuards.requireId "mon-conteneur_01" "Conteneur"

    [<Fact>]
    let ``requireId lÃ¨ve si identifiant vide`` () =
        (fun () -> ServiceGuards.requireId "" "Conteneur")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireContainerId ne lÃ¨ve pas si identifiant valide`` () = ServiceGuards.requireContainerId "c-1"

    [<Fact>]
    let ``requireContainerId lÃ¨ve si identifiant vide`` () =
        (fun () -> ServiceGuards.requireContainerId "")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireSafeCommand ne lÃ¨ve pas si commande sÃ»re`` () =
        ServiceGuards.requireSafeCommand [| "ls"; "-la" |]

    [<Fact>]
    let ``requireSafeCommand lÃ¨ve si commande vide`` () =
        (fun () -> ServiceGuards.requireSafeCommand [||])
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireSafePath ne lÃ¨ve pas pour un chemin sÃ»r`` () =
        ServiceGuards.requireSafePath @"C:\data\fichier.txt" "Chemin"

    [<Fact>]
    let ``requireSafePath lÃ¨ve si chemin vide`` () =
        (fun () -> ServiceGuards.requireSafePath "" "Chemin")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireSafePath lÃ¨ve si traversÃ©e de rÃ©pertoire`` () =
        (fun () -> ServiceGuards.requireSafePath @"..\..\fichier.txt" "Chemin")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireSafePath lÃ¨ve si caractÃ¨re dangereux`` () =
        (fun () -> ServiceGuards.requireSafePath @"C:\data\a;b" "Chemin")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireLocalAddress accepte localhost`` () =
        ServiceGuards.requireLocalAddress "http://localhost:50051"

    [<Fact>]
    let ``requireLocalAddress accepte 127.0.0.1`` () =
        ServiceGuards.requireLocalAddress "http://127.0.0.1:50051"

    [<Fact>]
    let ``requireLocalAddress accepte un named pipe`` () =
        ServiceGuards.requireLocalAddress "http://pipe:/diplo-container"

    [<Fact>]
    let ``requireLocalAddress rejette hÃ´te distant`` () =
        (fun () -> ServiceGuards.requireLocalAddress "http://evil.example.com:50051")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireLocalAddress rejette adresse vide`` () =
        (fun () -> ServiceGuards.requireLocalAddress "")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireVolumeId ne lÃ¨ve pas si identifiant valide`` () =
        ServiceGuards.requireVolumeId "vol-1"

    [<Fact>]
    let ``requireVolumeId lÃ¨ve si identifiant vide`` () =
        (fun () -> ServiceGuards.requireVolumeId "")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireVolumeFound ne lÃ¨ve pas si trouvÃ©`` () =
        ServiceGuards.requireVolumeFound "vol-1" true

    [<Fact>]
    let ``requireVolumeFound lÃ¨ve NotFound si introuvable`` () =
        (fun () -> ServiceGuards.requireVolumeFound "vol-1" false)
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireTargetPath ne lÃ¨ve pas si chemin valide`` () =
        ServiceGuards.requireTargetPath "/mnt/data"

    [<Fact>]
    let ``requireTargetPath lÃ¨ve si chemin vide`` () =
        (fun () -> ServiceGuards.requireTargetPath "")
        |> should throw typeof<RpcException>

