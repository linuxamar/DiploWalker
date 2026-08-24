namespace Diplo.Abstractions

/// Cache générique avec invalidation manuelle, protégé par un verrou.
/// Fournit un chargement paresseux (lazy) avec relecture à la demande.
type CachedConfig<'T>(loader: unit -> 'T) =
    let cacheLock = obj()
    let mutable cacheValue: 'T option = None

    /// Lit la valeur cache ; charge via `loader` si absent.
    member _.Value: 'T =
        lock cacheLock (fun () ->
            match cacheValue with
            | Some value -> value
            | None ->
                let value = loader ()
                cacheValue <- Some value
                value)

    /// Vide le cache forçant un rechargement au prochain accès.
    member _.Invalidate() =
        lock cacheLock (fun () -> cacheValue <- None)

    /// Charge ou recharge la valeur explicitement.
    member _.Load() =
        lock cacheLock (fun () ->
            let value = loader ()
            cacheValue <- Some value
            value)

    /// Indique si une valeur est actuellement en cache.
    member _.IsCached =
        lock cacheLock (fun () -> cacheValue.IsSome)
