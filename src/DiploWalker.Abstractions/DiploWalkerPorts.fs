namespace DiploWalker.Abstractions

/// Ports gRPC par dÃ©faut des services Diplo, sÃ©parÃ©s par configuration de
/// build : Debug conserve la plage historique 5001-5003, Release dÃ©cale de
/// +1000 (6001-6003) pour permettre l'exÃ©cution simultanÃ©e d'un service
/// compilÃ© en Debug et d'une installation Release sur la mÃªme machine.
/// Les named pipes ne sont pas concernÃ©s (noms inchangÃ©s).
module DiploWalkerPorts =

#if DEBUG
    /// Port gRPC par dÃ©faut de DiploWalker.Container en Debug.
    [<Literal>]
    let Container = 5001

    /// Port gRPC par dÃ©faut de DiploWalker.Volume en Debug.
    [<Literal>]
    let Volume = 5002

    /// Port gRPC par dÃ©faut de DiploWalker.Network en Debug.
    [<Literal>]
    let Network = 5003
#else
    /// Port gRPC par dÃ©faut de DiploWalker.Container en Release (+1000).
    [<Literal>]
    let Container = 6001

    /// Port gRPC par dÃ©faut de DiploWalker.Volume en Release (+1000).
    [<Literal>]
    let Volume = 6002

    /// Port gRPC par dÃ©faut de DiploWalker.Network en Release (+1000).
    [<Literal>]
    let Network = 6003
#endif


