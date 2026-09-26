namespace DiploWalker.Abstractions.Tests

open Xunit
open FsUnit.Xunit
open Grpc.Core
open DiploWalker.Abstractions

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

    [<Fact>]
    let ``requireSafePath ne lève pas pour un chemin sûr`` () =
        ServiceGuards.requireSafePath @"C:\data\fichier.txt" "Chemin"

    [<Fact>]
    let ``requireSafePath lève si chemin vide`` () =
        (fun () -> ServiceGuards.requireSafePath "" "Chemin")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireSafePath lève si traversée de répertoire`` () =
        (fun () -> ServiceGuards.requireSafePath @"..\..\fichier.txt" "Chemin")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireSafePath lève si caractère dangereux`` () =
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
    let ``requireLocalAddress rejette hôte distant`` () =
        (fun () -> ServiceGuards.requireLocalAddress "http://evil.example.com:50051")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireLocalAddress rejette adresse vide`` () =
        (fun () -> ServiceGuards.requireLocalAddress "")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireVolumeId ne lève pas si identifiant valide`` () =
        ServiceGuards.requireVolumeId "vol-1"

    [<Fact>]
    let ``requireVolumeId lève si identifiant vide`` () =
        (fun () -> ServiceGuards.requireVolumeId "")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireVolumeFound ne lève pas si trouvé`` () =
        ServiceGuards.requireVolumeFound "vol-1" true

    [<Fact>]
    let ``requireVolumeFound lève NotFound si introuvable`` () =
        (fun () -> ServiceGuards.requireVolumeFound "vol-1" false)
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``requireTargetPath ne lève pas si chemin valide`` () =
        ServiceGuards.requireTargetPath "/mnt/data"

    [<Fact>]
    let ``requireTargetPath lève si chemin vide`` () =
        (fun () -> ServiceGuards.requireTargetPath "")
        |> should throw typeof<RpcException>

