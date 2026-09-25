namespace DiploWalker.Abstractions

open System
open System.Threading

/// Cache gÃ©nÃ©rique avec invalidation manuelle.
/// Le chargement paresseux est confiÃ© Ã  Lazy<'T> : le verrou n'est tenu que
/// pour Ã©changer l'instance, jamais pendant l'exÃ©cution du loader. Le mode
/// ExecutionAndPublication garantit une exÃ©cution unique du loader sous
/// concurrence, et dÃ©tecte un chargement rÃ©entrant (le loader appelant Value
/// sur la mÃªme instance) par une InvalidOperationException plutÃ´t que par une
/// rÃ©cursion infinie.
type CachedConfig<'T>(loader: unit -> 'T) =
    let cacheLock = obj ()
    let mutable cacheValue: Lazy<'T> option = None

    let createLazy () =
        Lazy<'T>(Func<'T>(loader), LazyThreadSafetyMode.ExecutionAndPublication)

    /// Lit la valeur en cache ; charge via `loader` si absent (une seule fois).
    member _.Value: 'T =
        let lazyValue =
            lock cacheLock (fun () ->
                match cacheValue with
                | Some lazyValue -> lazyValue
                | None ->
                    let lazyValue = createLazy ()
                    cacheValue <- Some lazyValue
                    lazyValue)

        lazyValue.Value

    /// Vide le cache forÃ§ant un rechargement au prochain accÃ¨s.
    member _.Invalidate() =
        lock cacheLock (fun () -> cacheValue <- None)

    /// Charge ou recharge la valeur explicitement.
    member _.Load() =
        let lazyValue =
            lock cacheLock (fun () ->
                let lazyValue = createLazy ()
                cacheValue <- Some lazyValue
                lazyValue)

        lazyValue.Value

    /// Indique si une valeur est actuellement en cache.
    member _.IsCached = lock cacheLock (fun () -> cacheValue.IsSome)
