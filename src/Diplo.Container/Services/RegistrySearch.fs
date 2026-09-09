namespace Diplo.Container.Services

open System
open System.Net.Http
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Diplo.Abstractions

/// Résultat d'une recherche dans le catalogue en ligne d'un registre autorisé.
type RegistryImageHit =
    { Registry: string
      Repository: string
      Description: string
      Stars: int }

/// Recherche d'images dans les catalogues en ligne des registres autorisés.
/// docker.io et quay.io exposent une API publique de recherche ; ghcr.io et
/// mcr.microsoft.com n'en exposent aucune : leurs résultats sont vides.
[<RequireQualifiedAccess>]
module RegistrySearch =

    let private http = new HttpClient(Timeout = TimeSpan.FromSeconds(30.0))

    /// Recherche docker.io (Docker Hub).
    let dockerHubSearch
        (client: HttpClient)
        (query: string)
        (limit: int)
        (ct: CancellationToken)
        : Task<RegistryImageHit list> =
        task {
            let url =
                sprintf
                    "https://hub.docker.com/v2/search/repositories/?query=%s&page_size=%d"
                    (Uri.EscapeDataString query)
                    limit

            try
                let! json = client.GetStringAsync(url, ct)

                use doc = JsonDocument.Parse json

                match doc.RootElement.TryGetProperty("results") with
                | true, results ->
                    let mutable hits: RegistryImageHit list = []

                    for el in results.EnumerateArray() do
                        let repo = JsonHelpers.tryGetString el "repo_name"

                        if not (String.IsNullOrWhiteSpace repo) then
                            let description = JsonHelpers.tryGetString el "short_description"
                            let stars = int (JsonHelpers.tryGetInt64 el "star_count")

                            hits <-
                                { Registry = "docker.io"
                                  Repository = repo
                                  Description = description
                                  Stars = stars }
                                :: hits

                    return List.rev hits
                | _ -> return []
            with _ ->
                return []
        }

    /// Recherche quay.io.
    let quaySearch (client: HttpClient) (query: string) (limit: int) (ct: CancellationToken) : Task<RegistryImageHit list> =
        task {
            let url =
                sprintf
                    "https://quay.io/api/v1/find/repositories?query=%s&page_size=%d"
                    (Uri.EscapeDataString query)
                    limit

            try
                let! json = client.GetStringAsync(url, ct)

                use doc = JsonDocument.Parse json

                match doc.RootElement.TryGetProperty("results") with
                | true, results ->
                    let mutable hits: RegistryImageHit list = []

                    for el in results.EnumerateArray() do
                        let repo = JsonHelpers.tryGetString el "name"

                        if not (String.IsNullOrWhiteSpace repo) then
                            let description = JsonHelpers.tryGetString el "description"

                            hits <-
                                { Registry = "quay.io"
                                  Repository = repo
                                  Description = description
                                  Stars = 0 }
                                :: hits

                    return List.rev hits
                | _ -> return []
            with _ ->
                return []
        }

    /// Registres sans API publique de recherche : aucune correspondance.
    let private unsupported
        (_client: HttpClient)
        (_query: string)
        (_limit: int)
        (_ct: CancellationToken)
        : Task<RegistryImageHit list> =
        Task.FromResult([])

    let private providers: (string * (HttpClient -> string -> int -> CancellationToken -> Task<RegistryImageHit list>)) list =
        [ "docker.io", dockerHubSearch
          "quay.io", quaySearch
          "ghcr.io", unsupported
          "mcr.microsoft.com", unsupported ]

    /// Recherche sur un client HTTP injecté (testable). Sans registre ciblé,
    /// interroge tous les fournisseurs autorisés dans l'ordre de la liste
    /// blanche.
    let searchWith
        (client: HttpClient)
        (registry: string option)
        (query: string)
        (limit: int)
        (ct: CancellationToken)
        : Task<RegistryImageHit list> =
        task {
            let active =
                providers
                |> List.filter (fun (host, _) ->
                    match registry with
                    | None -> true
                    | Some r -> host = r)

            let mutable all: RegistryImageHit list = []

            for (_, search) in active do
                let! hits = search client query limit ct
                all <- all @ hits

            return all
        }

    /// Recherche avec le client HTTP partagé du service.
    let search (registry: string option) (query: string) (limit: int) (ct: CancellationToken) : Task<RegistryImageHit list> =
        searchWith http registry query limit ct