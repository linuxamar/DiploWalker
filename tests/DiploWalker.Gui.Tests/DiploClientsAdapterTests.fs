namespace DiploWalker.Gui.Tests

open Xunit
open FsUnit.Xunit
open DiploWalker.Gui.Adapters

/// Tests pour le DiploClientsAdapter.
type DiploClientsAdapterTests() =

    [<Fact>]
    member _.``IsConnected par défaut est false``() =
        let adapter = new DiploClientsAdapter()
        adapter.IsConnected |> should equal false

    [<Fact>]
    member _.``LastError par défaut est None``() =
        let adapter = new DiploClientsAdapter()
        adapter.LastError |> should equal None

    [<Fact>]
    member _.``Connect retourne false sans services``() =
        let adapter = new DiploClientsAdapter()
        // Sans services DiploWalker en cours d'exécution, la connexion échoue.
        let result = adapter.Connect().GetAwaiter().GetResult()
        result |> should equal false
        adapter.IsConnected |> should equal false

    [<Fact>]
    member _.``Disconnect ne lève pas d'exception``() =
        let adapter = new DiploClientsAdapter()
        (fun () -> adapter.Disconnect() |> ignore) |> should not' (throw typeof<System.Exception>)
