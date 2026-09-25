namespace DiploWalker.Contracts

/// Types partagÃ©s entre les services DiploWalker.
module Types =

    /// Identifiant unique d'un conteneur
    type ContainerId = ContainerId of string

    /// Ã‰tat d'un conteneur
    type ContainerState =
        | Created
        | Running
        | Paused
        | Stopped
        | Failed

    /// Informations sur un conteneur
    type ContainerInfo =
        { Id: ContainerId
          Name: string
          Image: string
          State: ContainerState
          CreatedAt: System.DateTime }


