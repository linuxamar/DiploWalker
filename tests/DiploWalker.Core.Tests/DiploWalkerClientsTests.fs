namespace DiploWalker.Core.Tests

module DiploWalkerClientsTests =

    open System
    open Xunit
    open FsUnit.Xunit
    open DiploWalker.Abstractions
    open DiploWalker.Core
    open DiploWalker.Core.Clients
    open DiploWalker.Core.Connection

    [<Fact>]
    let ``DiploWalkerClients cree les trois clients sans legerete`` () =
        let factory = new DiploWalkerClients() :> IDiploClients

        let conteneurs = factory.CreateContainerClient()
        let volumes = factory.CreateVolumeClient()
        let reseaux = factory.CreateNetworkClient()

        Assert.NotNull(conteneurs)
        Assert.NotNull(volumes)
        Assert.NotNull(reseaux)

    [<Fact>]
    let ``DiploWalkerClients recree le canal si le canal partage a ete dispose`` () =
        let channel = DiploWalkerChannel.forContainer DiploWalkerPorts.Container
        (channel :> IDisposable).Dispose()

        let factory = new DiploWalkerClients() :> IDiploClients
        let conteneurs = factory.CreateContainerClient()

        conteneurs |> should not' (be Null)
        (conteneurs :> IDisposable).Dispose()


