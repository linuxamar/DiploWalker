namespace DiploWalker.Core.Clients

/// Implémentation par défaut d'IDiploClients : connecte les vrais clients gRPC
/// en fonction de la configuration locale.
type DiploWalkerClients() =

    interface IDiploClients with
        member _.CreateContainerClient() =
            new ContainerClient() :> IContainerClient

        member _.CreateNetworkClient() = new NetworkClient() :> INetworkClient
        member _.CreateVolumeClient() = new VolumeClient() :> IVolumeClient


