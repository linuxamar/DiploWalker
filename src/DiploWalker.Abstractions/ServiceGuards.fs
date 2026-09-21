namespace DiploWalker.Abstractions

open Grpc.Core

/// Guards de validation d'entrÃ©e rÃ©utilisables par tous les services gRPC.
/// LÃ¨vent `RpcException(InvalidArgument)` en cas de valeur invalide.
[<RequireQualifiedAccess>]
module ServiceGuards =

    /// Message d'erreur standard pour l'identifiant de conteneur requis.
    [<Literal>]
    let ContainerIdRequired = "L'identifiant du conteneur est requis"

    /// Taille maximale d'un transfert de fichier unique (WriteFile/ReadFile) â€” 50 Mo.
    /// BornÃ©e des deux cÃ´tÃ©s (client et serveur) : au-delÃ , l'encodage base64
    /// (+33 %) et la mÃ©moire des messages gRPC deviendraient excessifs.
    [<Literal>]
    let MaxFileTransferBytes = 50 * 1024 * 1024

    /// Nombre maximal d'Ã©lÃ©ments retournÃ©s par les opÃ©rations de liste (serveur) :
    /// garde-fou anti-pagination saturÃ©e d'un contenant/registre local volumineux
    /// (M14). La pagination par curseur n'existe pas ici, on borne donc la rÃ©ponse.
    [<Literal>]
    let MaxListItems = 10_000

    /// VÃ©rifie que les donnÃ©es ne dÃ©passent pas la limite de transfert de fichier unique.
    let requireFileTransferWithinLimit (data: byte[]) (label: string) =
        if not (isNull data) && data.LongLength > int64 MaxFileTransferBytes then
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        sprintf "%s dÃ©passe la limite de %.0f Mo" label (float MaxFileTransferBytes / (1024. * 1024.))
                    )
                )
            )

    /// VÃ©rifie que la chaÃ®ne n'est pas null ou vide.
    let requireNonEmpty (value: string) (label: string) =
        if System.String.IsNullOrWhiteSpace(value) then
            raise (RpcException(Status(StatusCode.InvalidArgument, sprintf "%s ne peut pas Ãªtre vide" label)))

    /// VÃ©rifie que l'identifiant est valide (alphanumÃ©riques, tirets, underscores, max 128).
    let requireId (id: string) (label: string) = SecurityValidation.validateId id label

    /// VÃ©rifie que l'identifiant de conteneur est valide.
    let requireContainerId (id: string) =
        SecurityValidation.validateContainerId id

    /// VÃ©rifie que la valeur est un entier positif.
    let requirePositive (value: int) (label: string) =
        if value <= 0 then
            raise (
                RpcException(Status(StatusCode.InvalidArgument, sprintf "%s doit Ãªtre positif (reÃ§u : %d)" label value))
            )

    /// VÃ©rifie que la valeur est un entier positif (int64).
    let requirePositiveInt64 (value: int64) (label: string) =
        if value <= 0L then
            raise (
                RpcException(Status(StatusCode.InvalidArgument, sprintf "%s doit Ãªtre positif (reÃ§u : %d)" label value))
            )

    /// VÃ©rifie que la valeur est dans l'intervalle [min, max].
    let requireInRange (value: int) (min: int) (max: int) (label: string) =
        if value < min || value > max then
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        sprintf "%s doit Ãªtre entre %d et %d (reÃ§u : %d)" label min max value
                    )
                )
            )

    /// VÃ©rifie que le chemin est sÃ»r (anti-traversal, pas de caractÃ¨res dangereux).
    let requireSafePath (path: string) (label: string) =
        SecurityValidation.validateContainerPath path label

    /// VÃ©rifie que l'adresse gRPC est locale (localhost, 127.0.0.1, ou named pipe).
    let requireLocalAddress (address: string) =
        SecurityValidation.validateGrpcAddress address

    /// VÃ©rifie que la commande ne contient pas d'injection.
    let requireSafeCommand (command: string array) =
        SecurityValidation.validateCommand command

    /// LÃ¨ve InvalidArgument si l'identifiant de volume est manquant.
    let requireVolumeId (id: string) =
        if System.String.IsNullOrWhiteSpace(id) then
            raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du volume est requis")))

    /// LÃ¨ve NotFound si le volume est introuvable.
    let requireVolumeFound (id: string) (found: bool) =
        if not found then
            raise (RpcException(Status(StatusCode.NotFound, sprintf "Volume '%s' introuvable" id)))

    /// LÃ¨ve InvalidArgument si le chemin cible est manquant.
    let requireTargetPath (path: string) =
        if System.String.IsNullOrWhiteSpace(path) then
            raise (RpcException(Status(StatusCode.InvalidArgument, "Le chemin cible est requis")))

