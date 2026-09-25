namespace DiploWalker.Abstractions.Tests

open System
open System.Threading.Tasks
open Xunit
open FsUnit.Xunit
open DiploWalker.Abstractions

type CachedConfigTests() =

    [<Fact>]
    let ``Value charge via le loader une premiÃ¨re fois`` () =
        let mutable callCount = 0

        let loader =
            fun () ->
                callCount <- callCount + 1
                42

        let cache = CachedConfig<int>(loader)
        cache.Value |> should equal 42
        callCount |> should equal 1

    [<Fact>]
    let ``Value retourne la valeur cache sans recharger`` () =
        let mutable callCount = 0

        let loader =
            fun () ->
                callCount <- callCount + 1
                42

        let cache = CachedConfig<int>(loader)
        cache.Value |> ignore
        cache.Value |> ignore
        callCount |> should equal 1

    [<Fact>]
    let ``Invalidate force le rechargement`` () =
        let mutable callCount = 0

        let loader =
            fun () ->
                callCount <- callCount + 1
                callCount

        let cache = CachedConfig<int>(loader)
        cache.Value |> should equal 1
        cache.Invalidate()
        cache.Value |> should equal 2

    [<Fact>]
    let ``Load recharge explicitement`` () =
        let mutable callCount = 0

        let loader =
            fun () ->
                callCount <- callCount + 1
                callCount

        let cache = CachedConfig<int>(loader)
        cache.Load() |> should equal 1
        cache.Load() |> should equal 2

    [<Fact>]
    let ``IsCached est false avant le premier accÃ¨s`` () =
        let cache = CachedConfig<int>(fun () -> 42)
        cache.IsCached |> should equal false

    [<Fact>]
    let ``IsCached est true aprÃ¨s un accÃ¨s`` () =
        let cache = CachedConfig<int>(fun () -> 42)
        cache.Value |> ignore
        cache.IsCached |> should equal true

    [<Fact>]
    let ``IsCached est false aprÃ¨s invalidation`` () =
        let cache = CachedConfig<int>(fun () -> 42)
        cache.Value |> ignore
        cache.Invalidate()
        cache.IsCached |> should equal false

    [<Fact>]
    let ``Cache avec string fonctionne`` () =
        let cache = CachedConfig<string>(fun () -> "hello")
        cache.Value |> should equal "hello"
        cache.Invalidate()
        cache.Value |> should equal "hello"

    [<Fact>]
    let ``Un chargement rÃ©entrant lÃ¨ve une exception claire`` () =
        let mutable reentered = false
        let mutable cache = Unchecked.defaultof<CachedConfig<bool>>
        cache <- CachedConfig<bool>(fun () -> reentered <- cache.Value; reentered)
        Assert.Throws<InvalidOperationException>(fun () -> cache.Value |> ignore) |> ignore

    [<Fact>]
    let ``AccÃ¨s concurrents : le loader n'est exÃ©cutÃ© qu'une seule fois`` () =
        let mutable callCount = 0
        let cache = CachedConfig<int>(fun () -> callCount <- callCount + 1; 42)

        let read () = cache.Value |> ignore

        let tasks = [ for _ in 1 .. 8 -> Task.Run(Action(read)) ]
        Task.WaitAll(tasks |> Array.ofList)
        callCount |> should equal 1

