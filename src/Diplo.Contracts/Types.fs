namespace Diplo.Contracts

/// Types partagés entre les services Diplo.
/// Ce module contient les types communs utilisés par tous les services.
module Types =

    /// Identifiant unique d'un conteneur
    type ContainerId = ContainerId of string

    /// État d'un conteneur
    type ContainerState =
        | Created
        | Running
        | Paused
        | Stopped
        | Failed

    /// Informations sur un conteneur
    type ContainerInfo = {
        Id: ContainerId
        Name: string
        Image: string
        State: ContainerState
        CreatedAt: System.DateTime
    }
