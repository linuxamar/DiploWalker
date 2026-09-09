module Diplo.Container.Tests.RegistrySearchTests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Threading
open System.Threading.Tasks
open Xunit
open FsUnit.Xunit
open Diplo.Container.Services

/// Handler HTTP statique qui répond selon l'URL demandée.
type StubHttpHandler(reply: Uri -> string) =
    inherit HttpMessageHandler()

    override _.SendAsync(request: HttpRequestMessage, _ct: CancellationToken) =
        let response = new HttpResponseMessage(HttpStatusCode.OK)
        response.Content <- new StringContent(reply request.RequestUri, Encoding.UTF8, "application/json")
        Task.FromResult(response)

let private run (work: Task<'T>) =
    work |> Async.AwaitTask |> Async.RunSynchronously

// Tests du module RegistrySearch : parsing des API publiques de Docker Hub et de
// Quay, registres sans API publique (ghcr.io, MCR) renvoyant une liste vide.

[<Fact>]
let ``dockerHubSearch parse repo_name, description et star_count`` () =
    let handler =
        new StubHttpHandler(fun _ ->
            """{ "count": 2, "results": [
                    { "repo_name": "nginx", "short_description": "Serveur web", "star_count": 1200 },
                    { "repo_name": "library/redis", "short_description": "", "star_count": 0 } ] }""")

    use client = new HttpClient(handler)

    let hits =
        run (RegistrySearch.dockerHubSearch client "nginx" 25 CancellationToken.None)

    hits.Length |> should equal 2
    hits.[0].Registry |> should equal "docker.io"
    hits.[0].Repository |> should equal "nginx"
    hits.[0].Description |> should equal "Serveur web"
    hits.[0].Stars |> should equal 1200
    hits.[1].Repository |> should equal "library/redis"

[<Fact>]
let ``quaySearch parse name et description`` () =
    let handler =
        new StubHttpHandler(fun _ ->
            """{ "results": [
                    { "name": "mattermost/mattermost", "description": "Messagerie", "repository_kind": 0 } ] }""")

    use client = new HttpClient(handler)

    let hits =
        run (RegistrySearch.quaySearch client "mattermost" 25 CancellationToken.None)

    hits.Length |> should equal 1
    hits.[0].Registry |> should equal "quay.io"
    hits.[0].Repository |> should equal "mattermost/mattermost"
    hits.[0].Description |> should equal "Messagerie"
    hits.[0].Stars |> should equal 0

[<Fact>]
let ``searchWith sans registre interroge tous les fournisseurs autorises`` () =
    let handler =
        new StubHttpHandler(fun uri ->
            if uri.Host = "hub.docker.com" then
                """{ "results": [ { "repo_name": "nginx", "short_description": "web", "star_count": 1 } ] }"""
            elif uri.Host = "quay.io" then
                """{ "results": [ { "name": "team/app", "description": "app" } ] }"""
            else
                "{}")

    use client = new HttpClient(handler)

    let hits =
        run (RegistrySearch.searchWith client None "nginx" 25 CancellationToken.None)

    hits |> List.map (fun h -> h.Registry) |> should equal [ "docker.io"; "quay.io" ]

[<Fact>]
let ``searchWith cible docker.io uniquement`` () =
    let handler =
        new StubHttpHandler(fun uri ->
            if uri.Host = "hub.docker.com" then
                """{ "results": [ { "repo_name": "nginx", "short_description": "web", "star_count": 1 } ] }"""
            else
                "{}")

    use client = new HttpClient(handler)

    let hits =
        run (RegistrySearch.searchWith client (Some "docker.io") "nginx" 25 CancellationToken.None)

    hits.Length |> should equal 1
    hits.Head.Registry |> should equal "docker.io"

[<Fact>]
let ``searchWith sur ghcr.io ne renvoie rien`` () =
    use client = new HttpClient(new StubHttpHandler(fun _ -> "{}"))

    let hits =
        run (RegistrySearch.searchWith client (Some "ghcr.io") "nginx" 25 CancellationToken.None)

    hits |> should be Empty

[<Fact>]
let ``searchWith sur mcr.microsoft.com ne renvoie rien`` () =
    use client = new HttpClient(new StubHttpHandler(fun _ -> "{}"))

    let hits =
        run (RegistrySearch.searchWith client (Some "mcr.microsoft.com") "nginx" 25 CancellationToken.None)

    hits |> should be Empty

[<Fact>]
let ``reponse invalide renvoie une liste vide`` () =
    let handler = new StubHttpHandler(fun _ -> "pas du json")
    use client = new HttpClient(handler)

    let hits =
        run (RegistrySearch.dockerHubSearch client "nginx" 25 CancellationToken.None)

    hits |> should be Empty

