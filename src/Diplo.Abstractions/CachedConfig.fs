namespace Diplo.Abstractions

open System
open System.Threading

/// Cache générique avec invalidation manuelle.
/// Le chargement paresseux est confié à Lazy<'T> : le verrou n'est tenu que
/// pour échanger l'instance, jamais pendant l'exécution du loader. Le mode
/// ExecutionAndPublication garantit une exécution unique du loader sous
/// concurrence, et détecte un chargement réentrant (le loader appelant Value
/// sur la même instance) par une InvalidOperationException plutôt que par une
/// récursion infinie.
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

    /// Vide le cache forçant un rechargement au prochain accès.
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