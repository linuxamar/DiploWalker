namespace Diplo.Contracts.Tests

module TypesTests =

    open System
    open Xunit
    open FsUnit.Xunit
    open Diplo.Contracts

    [<Fact>]
    let ``ContainerId extrait la valeur correctement`` () =
        let id = Types.ContainerId "abc123"
        match id with
        | Types.ContainerId value -> value |> should equal "abc123"

    [<Fact>]
    let ``ContainerId egalite fonctionne`` () =
        let id1 = Types.ContainerId "abc123"
        let id2 = Types.ContainerId "abc123"
        id1 |> should equal id2

    [<Fact>]
    let ``ContainerId inequalite fonctionne`` () =
        let id1 = Types.ContainerId "abc123"
        let id2 = Types.ContainerId "def456"
        id1 |> should not' (equal id2)

    [<Fact>]
    let ``ContainerState Created est valide`` () =
        let state = Types.ContainerState.Created
        state |> should equal Types.ContainerState.Created

    [<Fact>]
    let ``ContainerState Running est valide`` () =
        let state = Types.ContainerState.Running
        state |> should equal Types.ContainerState.Running

    [<Fact>]
    let ``ContainerState Paused est valide`` () =
        let state = Types.ContainerState.Paused
        state |> should equal Types.ContainerState.Paused

    [<Fact>]
    let ``ContainerState Stopped est valide`` () =
        let state = Types.ContainerState.Stopped
        state |> should equal Types.ContainerState.Stopped

    [<Fact>]
    let ``ContainerState Failed est valide`` () =
        let state = Types.ContainerState.Failed
        state |> should equal Types.ContainerState.Failed

    [<Fact>]
    let ``ContainerInfo enregistrement toutes les proprietes`` () =
        let info : Types.ContainerInfo = {
            Id = Types.ContainerId "abc123"
            Name = "mon-conteneur"
            Image = "nginx:latest"
            State = Types.ContainerState.Running
            CreatedAt = DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc)
        }
        match info.Id with
        | Types.ContainerId id -> id |> should equal "abc123"
        info.Name |> should equal "mon-conteneur"
        info.Image |> should equal "nginx:latest"
        info.State |> should equal Types.ContainerState.Running

    [<Fact>]
    let ``ContainerInfo egalite structurelle`` () =
        let now = DateTime.UtcNow
        let info1 : Types.ContainerInfo = {
            Id = Types.ContainerId "abc123"
            Name = "test"
            Image = "nginx"
            State = Types.ContainerState.Running
            CreatedAt = now
        }
        let info2 : Types.ContainerInfo = {
            Id = Types.ContainerId "abc123"
            Name = "test"
            Image = "nginx"
            State = Types.ContainerState.Running
            CreatedAt = now
        }
        info1 |> should equal info2

    [<Fact>]
    let ``ContainerInfo avec etats differents n'est pas egal`` () =
        let now = DateTime.UtcNow
        let info1 : Types.ContainerInfo = { Id = Types.ContainerId "abc123"; Name = "test"; Image = "nginx"; State = Types.ContainerState.Running; CreatedAt = now }
        let info2 : Types.ContainerInfo = { Id = Types.ContainerId "abc123"; Name = "test"; Image = "nginx"; State = Types.ContainerState.Stopped; CreatedAt = now }
        info1 |> should not' (equal info2)
