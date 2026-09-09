namespace Diplo.Abstractions.Tests

open System
open Xunit
open FsUnit.Xunit
open Diplo.Abstractions

module RegistryProvidersTests =

    [<Fact>]
    let ``hosts liste les quatre fournisseurs autorises`` () =
        RegistryProviders.hosts
        |> should equal [ "ghcr.io"; "docker.io"; "quay.io"; "mcr.microsoft.com" ]

        RegistryProviders.label.Contains("ghcr.io") |> should be True
        RegistryProviders.label.Contains("docker.io") |> should be True
        RegistryProviders.label.Contains("quay.io") |> should be True
        RegistryProviders.label.Contains("mcr.microsoft.com") |> should be True

    [<Fact>]
    let ``tryResolve reconnait les hotes canoniques`` () =
        RegistryProviders.tryResolve "ghcr.io" |> should equal (Some "ghcr.io")
        RegistryProviders.tryResolve "docker.io" |> should equal (Some "docker.io")
        RegistryProviders.tryResolve "quay.io" |> should equal (Some "quay.io")
        RegistryProviders.tryResolve "mcr.microsoft.com" |> should equal (Some "mcr.microsoft.com")

    [<Fact>]
    let ``tryResolve mappe les alias vers le host canonique`` () =
        RegistryProviders.tryResolve "ghcr" |> should equal (Some "ghcr.io")
        RegistryProviders.tryResolve "dockerhub" |> should equal (Some "docker.io")
        RegistryProviders.tryResolve "docker" |> should equal (Some "docker.io")
        RegistryProviders.tryResolve "hub" |> should equal (Some "docker.io")
        RegistryProviders.tryResolve "registry-1.docker.io" |> should equal (Some "docker.io")
        RegistryProviders.tryResolve "quay" |> should equal (Some "quay.io")
        RegistryProviders.tryResolve "mcr" |> should equal (Some "mcr.microsoft.com")

    [<Theory>]
    [<InlineData("https://GHCR.io/v2/")>]
    [<InlineData(" dockerhub ")>]
    [<InlineData("DockerHub")>]
    let ``tryResolve tolere le schema, le chemin et la casse``(input: string) =
        RegistryProviders.tryResolve input |> should not' (equal None)

    [<Fact>]
    let ``tryResolve rejette un registre hors liste blanche`` () =
        RegistryProviders.tryResolve "myregistry.azurecr.io" |> should equal None
        RegistryProviders.tryResolve "reg.example.com" |> should equal None
        RegistryProviders.tryResolve "localhost:5000" |> should equal None
        RegistryProviders.tryResolve "10.0.0.1:5000" |> should equal None

    [<Fact>]
    let ``tryResolve rejette une entree vide`` () =
        RegistryProviders.tryResolve "" |> should equal None
        RegistryProviders.tryResolve "   " |> should equal None

    [<Theory>]
    [<InlineData("nginx:latest", "docker.io")>]
    [<InlineData("library/nginx:latest", "docker.io")>]
    [<InlineData("ghcr.io/org/app:v1", "ghcr.io")>]
    [<InlineData("quay.io/team/x", "quay.io")>]
    [<InlineData("mcr.microsoft.com/dotnet/runtime:10.0", "mcr.microsoft.com")>]
    [<InlineData("registry-1.docker.io/library/nginx", "registry-1.docker.io")>]
    let ``hostOfImage determine le registre d'une image``(image: string, expected: string) =
        RegistryProviders.hostOfImage image |> should equal expected

    [<Fact>]
    let ``tryResolveImage resout un fournisseur autorise`` () =
        RegistryProviders.tryResolveImage "ghcr.io/org/app" |> should equal (Some "ghcr.io")
        RegistryProviders.tryResolveImage "nginx:latest" |> should equal (Some "docker.io")
        RegistryProviders.tryResolveImage "mcr.microsoft.com/x/y:tag"
        |> should equal (Some "mcr.microsoft.com")

    [<Fact>]
    let ``tryResolveImage rejette un registre non autorise`` () =
        RegistryProviders.tryResolveImage "myregistry.azurecr.io/team/app:latest" |> should equal None
        RegistryProviders.tryResolveImage "localhost:5000/app" |> should equal None
        RegistryProviders.tryResolveImage "" |> should equal None