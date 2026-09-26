module DiploWalker.Container.Tests.RegistrySearchTests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Threading
open System.Threading.Tasks
open Xunit
open FsUnit.Xunit
open DiploWalker.Container.Services

/// Handler HTTP statique qui répond selon l'URL demandée.
type StubHttpHandler(reply: Uri -> string) =
    inherit HttpMessageHandler()

    override _.SendAsync(request: HttpRequestMessage, _ct: CancellationToken) =
        let response = new HttpResponseMessage(HttpStatusCode.OK)
        response.Content <- new StringContent(reply request.RequestUri, Encoding.UTF8, "application/json")
        Task.FromResult(response)

/// Handler qui renvoie toujours le code d'état demandé (erreurs serveur simulées).
type ErrorHttpHandler(status: HttpStatusCode) =
    inherit HttpMessageHandler()

    override _.SendAsync(_request: HttpRequestMessage, _ct: CancellationToken) =
        Task.FromResult(new HttpResponseMessage(status))

let private run (work: Task<'T>) =
    work |> Async.AwaitTask |> Async.RunSynchronously

// Tests du module RegistrySearch : parsing des API publiques de Docker Hub et de
// Quay, filtre local du catalogue public MCR, registre sans API publique (ghcr.io)
// renvoyant une liste vide.

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
            elif uri.Host = "mcr.microsoft.com" then
                """{ "repositories": [ "azure/nginx", "k8s/pause" ] }"""
            else
                "{}")

    use client = new HttpClient(handler)

    let hits =
        run (RegistrySearch.searchWith client None "nginx" 25 CancellationToken.None)

    hits
    |> List.map (fun h -> h.Registry)
    |> should equal [ "docker.io"; "quay.io"; "mcr.microsoft.com" ]

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
let ``searchWith cible mcr.microsoft.com interroge son catalogue public`` () =
    let handler =
        new StubHttpHandler(fun _ ->
            """{ "repositories": [ "mssql/server", "mssql/bdc/mssql-server-ha", "k8s/pause" ] }""")

    use client = new HttpClient(handler)

    let hits =
        run (RegistrySearch.searchWith client (Some "mcr.microsoft.com") "mssql" 25 CancellationToken.None)

    hits.Length |> should equal 2
    hits.[0].Registry |> should equal "mcr.microsoft.com"
    hits.[0].Repository |> should equal "mssql/server"
    hits.[0].Description |> should equal ""
    hits.[0].Stars |> should equal 0
    hits.[1].Repository |> should equal "mssql/bdc/mssql-server-ha"

[<Fact>]
let ``mcrSearch respecte la limite demandee`` () =
    let handler =
        new StubHttpHandler(fun _ ->
            """{ "repositories": [ "mssql/server", "mssql/server2", "mssql/server3" ] }""")

    use client = new HttpClient(handler)

    let hits =
        run (RegistrySearch.mcrSearch client "mssql" 2 CancellationToken.None)

    hits.Length |> should equal 2
    hits |> List.forall (fun h -> h.Registry = "mcr.microsoft.com") |> should equal true

[<Fact>]
let ``mcrSearch est insensible a la casse`` () =
    let handler =
        new StubHttpHandler(fun _ ->
            """{ "repositories": [ "Mssql/server", "k8s/pause" ] }""")

    use client = new HttpClient(handler)

    let hits =
        run (RegistrySearch.mcrSearch client "MSSQL" 25 CancellationToken.None)

    hits.Length |> should equal 1
    hits.Head.Repository |> should equal "Mssql/server"

[<Fact>]
let ``reponse invalide renvoie une liste vide`` () =
    let handler = new StubHttpHandler(fun _ -> "pas du json")
    use client = new HttpClient(handler)

    let hits =
        run (RegistrySearch.dockerHubSearch client "nginx" 25 CancellationToken.None)

    hits |> should be Empty

[<Fact>]
let ``dockerHubSearch avec reponse 500 renvoie une liste vide`` () =
    use client = new HttpClient(new ErrorHttpHandler(HttpStatusCode.InternalServerError))

    let hits =
        run (RegistrySearch.dockerHubSearch client "nginx" 25 CancellationToken.None)

    hits |> should be Empty

[<Fact>]
let ``quaySearch avec reponse 404 renvoie une liste vide`` () =
    use client = new HttpClient(new ErrorHttpHandler(HttpStatusCode.NotFound))

    let hits =
        run (RegistrySearch.quaySearch client "nginx" 25 CancellationToken.None)

    hits |> should be Empty

[<Fact>]
let ``searchWith ignore un fournisseur en erreur et garde les autres resultats`` () =
    let handler =
        { new HttpMessageHandler() with
            override _.SendAsync(request: HttpRequestMessage, _ct: CancellationToken) =
                if request.RequestUri.Host = "hub.docker.com" then
                    Task.FromException<HttpResponseMessage>(HttpRequestException("erreur réseau"))
                else
                    let response = new HttpResponseMessage(HttpStatusCode.OK)
                    response.Content <-
                        new StringContent(
                            """{ "results": [ { "name": "team/app", "description": "app" } ] }""",
                            Encoding.UTF8,
                            "application/json"
                        )

                    Task.FromResult(response) }

    use client = new HttpClient(handler)

    let hits =
        run (RegistrySearch.searchWith client None "nginx" 25 CancellationToken.None)

    hits.Length |> should equal 1
    hits.Head.Registry |> should equal "quay.io"

[<Fact>]
let ``searchWith borne le total des resultats fusionnes a limit`` () =
    let handler =
        new StubHttpHandler(fun uri ->
            if uri.Host = "hub.docker.com" then
                """{ "results": [ { "repo_name": "app1" }, { "repo_name": "app2" }, { "repo_name": "app3" } ] }"""
            elif uri.Host = "quay.io" then
                """{ "results": [ { "name": "team/app" } ] }"""
            elif uri.Host = "mcr.microsoft.com" then
                """{ "repositories": [ "microsoft/app" ] }"""
            else
                "{}")

    use client = new HttpClient(handler)

    let hits =
        run (RegistrySearch.searchWith client None "app" 2 CancellationToken.None)

    hits.Length |> should equal 2
    hits |> List.forall (fun h -> h.Registry = "docker.io") |> should equal true
    hits.[0].Repository |> should equal "app1"

[<Fact>]
let ``searchWith interroge les fournisseurs en parallele`` () =
    let handler =
        { new HttpMessageHandler() with
            member _.SendAsync(request: HttpRequestMessage, _ct: CancellationToken) =
                task {
                    do! Task.Delay 250

                    let body =
                        match request.RequestUri.Host with
                        | "hub.docker.com" -> """{ "results": [ { "repo_name": "nginx" } ] }"""
                        | "quay.io" -> """{ "results": [ { "name": "team/app" } ] }"""
                        | "mcr.microsoft.com" -> """{ "repositories": [ "azure/nginx" ] }"""
                        | _ -> "{}"

                    let response = new HttpResponseMessage(HttpStatusCode.OK)
                    response.Content <- new StringContent(body, Encoding.UTF8, "application/json")
                    return response
                } }

    use client = new HttpClient(handler)
    let sw = System.Diagnostics.Stopwatch.StartNew()

    let hits =
        run (RegistrySearch.searchWith client None "nginx" 25 CancellationToken.None)

    sw.Stop()

    // Trois fournisseurs à 250 ms chacun : en parallèle la durée reste proche
    // de 250 ms, très en dessous de la somme séquentielle (~750 ms).
    hits.Length |> should equal 3
    sw.ElapsedMilliseconds < 600L |> should equal true

[<Fact>]
let ``mcrSearch borne la requete catalogue a n=limit`` () =
    let urls = ResizeArray<string>()

    let handler =
        new StubHttpHandler(fun uri ->
            lock urls (fun () -> urls.Add(uri.ToString()))
            """{ "repositories": [ "mssql/server" ] }""")

    use client = new HttpClient(handler)

    let hits =
        run (RegistrySearch.mcrSearch client "mssql" 2 CancellationToken.None)

    hits.Length |> should equal 1
    urls |> Seq.exists (fun u -> u.Contains "n=2") |> should equal true

[<Fact>]
let ``searchWith avec annulation renvoie une liste vide`` () =
    use client = new HttpClient(new StubHttpHandler(fun _ -> """{ "results": [ { "repo_name": "nginx" } ] }"""))
    use cts = new CancellationTokenSource()
    cts.Cancel()

    let hits =
        run (RegistrySearch.searchWith client None "nginx" 25 cts.Token)

    hits |> should be Empty


