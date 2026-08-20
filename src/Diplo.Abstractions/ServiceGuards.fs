namespace Diplo.Abstractions

open Grpc.Core

/// Guards de validation d'entrée réutilisables par tous les services gRPC.
/// Lèvent `RpcException(InvalidArgument)` en cas de valeur invalide.
[<RequireQualifiedAccess>]
module ServiceGuards =

    /// Vérifie que la chaîne n'est pas null ou vide.
    let requireNonEmpty (value: string) (label: string) =
        if System.String.IsNullOrWhiteSpace(value) then
            raise (RpcException(Status(StatusCode.InvalidArgument, sprintf "%s ne peut pas être vide" label)))

    /// Vérifie que l'identifiant est valide (alphanumériques, tirets, underscores, max 128).
    let requireId (id: string) (label: string) =
        SecurityValidation.validateId id label

    /// Vérifie que l'identifiant de conteneur est valide.
    let requireContainerId (id: string) =
        SecurityValidation.validateContainerId id

    /// Vérifie que la valeur est un entier positif.
    let requirePositive (value: int) (label: string) =
        if value <= 0 then
            raise (RpcException(Status(StatusCode.InvalidArgument, sprintf "%s doit être positif (reçu : %d)" label value)))

    /// Vérifie que la valeur est un entier positif (int64).
    let requirePositiveInt64 (value: int64) (label: string) =
        if value <= 0L then
            raise (RpcException(Status(StatusCode.InvalidArgument, sprintf "%s doit être positif (reçu : %d)" label value)))

    /// Vérifie que la valeur est dans l'intervalle [min, max].
    let requireInRange (value: int) (min: int) (max: int) (label: string) =
        if value < min || value > max then
            raise (RpcException(Status(StatusCode.InvalidArgument, sprintf "%s doit être entre %d et %d (reçu : %d)" label min max value)))

    /// Vérifie que le chemin est sûr (anti-traversal, pas de caractères dangereux).
    let requireSafePath (path: string) (label: string) =
        SecurityValidation.validateContainerPath path label

    /// Vérifie que l'adresse gRPC est locale (localhost, 127.0.0.1, ou named pipe).
    let requireLocalAddress (address: string) =
        SecurityValidation.validateGrpcAddress address

    /// Vérifie que la commande ne contient pas d'injection.
    let requireSafeCommand (command: string array) =
        SecurityValidation.validateCommand command

    /// Lève NotFound si l'identifiant de volume est manquant.
    let requireVolumeId (id: string) =
        if System.String.IsNullOrWhiteSpace(id) then
            raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du volume est requis")))

    /// Lève NotFound si le volume est introuvable.
    let requireVolumeFound (id: string) (found: bool) =
        if not found then
            raise (RpcException(Status(StatusCode.NotFound, sprintf "Volume '%s' introuvable" id)))

    /// Lève NotFound si le chemin cible est manquant.
    let requireTargetPath (path: string) =
        if System.String.IsNullOrWhiteSpace(path) then
            raise (RpcException(Status(StatusCode.InvalidArgument, "Le chemin cible est requis")))
