namespace Diplo.Core.Tests

module DiploClientsTests =

    open System
    open Xunit
    open FsUnit.Xunit
    open Diplo.Abstractions
    open Diplo.Core
    open Diplo.Core.Clients
    open Diplo.Core.Connection

    [<Fact>]
    let ``DiploClients cree les trois clients sans legerete`` () =
        let factory = new DiploClients() :> IDiploClients

        let conteneurs = factory.CreateContainerClient()
        let volumes = factory.CreateVolumeClient()
        let reseaux = factory.CreateNetworkClient()

        Assert.NotNull(conteneurs)
        Assert.NotNull(volumes)
        Assert.NotNull(reseaux)

    [<Fact>]
    let ``DiploClients recree le canal si le canal partage a ete dispose`` () =
        let channel = DiploChannel.forContainer DiploPorts.Container
        (channel :> IDisposable).Dispose()

        let factory = new DiploClients() :> IDiploClients
        let conteneurs = factory.CreateContainerClient()

        conteneurs |> should not' (be Null)
        (conteneurs :> IDisposable).Dispose()
