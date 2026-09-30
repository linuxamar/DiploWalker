namespace DiploWalker.Abstractions

/// Ports gRPC par défaut des services Diplo, séparés par configuration de
/// build : Debug conserve la plage historique 5001-5003, Release décale de
/// +1000 (6001-6003) pour permettre l'exécution simultanée d'un service
/// compilé en Debug et d'une installation Release sur la même machine.
/// Les named pipes sont suffixés par "-debug" en Debug pour la même raison.
module DiploWalkerPorts =

#if DEBUG
    /// Port gRPC par défaut de DiploWalker.Container en Debug.
    [<Literal>]
    let Container = 5001

    /// Port gRPC par défaut de DiploWalker.Volume en Debug.
    [<Literal>]
    let Volume = 5002

    /// Port gRPC par défaut de DiploWalker.Network en Debug.
    [<Literal>]
    let Network = 5003

    /// Nom de pipe par défaut pour DiploWalker.Container en Debug.
    [<Literal>]
    let ContainerPipe = "diplowalker-container-debug"

    /// Nom de pipe par défaut pour DiploWalker.Volume en Debug.
    [<Literal>]
    let VolumePipe = "diplowalker-volume-debug"

    /// Nom de pipe par défaut pour DiploWalker.Network en Debug.
    [<Literal>]
    let NetworkPipe = "diplowalker-network-debug"
#else
    /// Port gRPC par défaut de DiploWalker.Container en Release (+1000).
    [<Literal>]
    let Container = 6001

    /// Port gRPC par défaut de DiploWalker.Volume en Release (+1000).
    [<Literal>]
    let Volume = 6002

    /// Port gRPC par défaut de DiploWalker.Network en Release (+1000).
    [<Literal>]
    let Network = 6003

    /// Nom de pipe par défaut pour DiploWalker.Container en Release.
    [<Literal>]
    let ContainerPipe = "diplowalker-container"

    /// Nom de pipe par défaut pour DiploWalker.Volume en Release.
    [<Literal>]
    let VolumePipe = "diplowalker-volume"

    /// Nom de pipe par défaut pour DiploWalker.Network en Release.
    [<Literal>]
    let NetworkPipe = "diplowalker-network"
#endif


