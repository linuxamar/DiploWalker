namespace Diplo.Core.Tests

module DiploClientsTests =

    open Xunit
    open FsUnit.Xunit
    open Diplo.Core.Clients

    [<Fact>]
    let ``DiploClients cree les trois clients sans legerete`` () =
        let factory = new DiploClients() :> IDiploClients

        use conteneurs = factory.CreateContainerClient()
        use volumes = factory.CreateVolumeClient()
        use reseaux = factory.CreateNetworkClient()

        conteneurs |> should not' (be null)
        volumes |> should not' (be null)
        reseaux |> should not' (be null)
        conteneurs |> should be instanceOfType<ContainerClient>
        volumes |> should be instanceOfType<VolumeClient>
        reseaux |> should be instanceOfType<NetworkClient>