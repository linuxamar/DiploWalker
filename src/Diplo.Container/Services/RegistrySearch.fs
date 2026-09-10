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
/// docker.io et quay.io exposent une API publique de recherche ; le catalogue
/// de mcr.microsoft.com est public mais sans requête serveur (filtrage local) ;
/// ghcr.io n'expose aucune recherche : ses résultats sont vides.
[<RequireQualifiedAccess>]
module RegistrySearch =

    /// Client HTTP partagé du service, exposé pour être enregistré en singleton
    /// DI (Program.fs) et injecté dans ContainerServiceImpl : une seule instance
    /// qu'utilisent aussi bien la libération que la recherche, testée à part.
    /// La taille maximale des réponses borne la mémoire en cas de catalogue
    /// volumineux (mcr.microsoft.com).
    let sharedClient = new HttpClient(Timeout = TimeSpan.FromSeconds(30.0), MaxResponseContentBufferSize = 8L * 1024L * 1024L)

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
                            // Borne de sécurité : une valeur démesurée (ou incohérente)
                            // ne doit pas faire déborder la conversion int64 → int.
                            let stars = int (min (JsonHelpers.tryGetInt64 el "star_count") (int64 Int32.MaxValue))

                            hits <-
                                { Registry = "docker.io"
                                  Repository = repo
                                  Description = description
                                  Stars = stars }
                                :: hits

                    return List.rev hits
                | _ -> return []
            with
            | :? OperationCanceledException -> return []
            | ex ->
                Serilog.Log.Warning(ex, "La recherche dans {Registry} a échoué (résultats ignorés)", "docker.io")
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
            with
            | :? OperationCanceledException -> return []
            | ex ->
                Serilog.Log.Warning(ex, "La recherche dans {Registry} a échoué (résultats ignorés)", "quay.io")
                return []
        }

    /// Recherche mcr.microsoft.com : le catalogue « _catalog » est public mais
    /// ne prend aucune requête serveur — il est téléchargé puis filtré en local
    /// (correspondance insensible à la casse sur le nom du référentiel).
    let mcrSearch
        (client: HttpClient)
        (query: string)
        (limit: int)
        (ct: CancellationToken)
        : Task<RegistryImageHit list> =
        task {
            try
                // Le catalogue est paginé côté serveur : demander une page de la
                // taille demandée borne la réponse au lieu de télécharger la totalité
                // du catalogue (des centaines de Mo sur le réseau).
                let url = sprintf "https://mcr.microsoft.com/v2/_catalog?n=%d" limit
                let! json = client.GetStringAsync(url, ct)

                use doc = JsonDocument.Parse json

                match doc.RootElement.TryGetProperty("repositories") with
                | true, repos ->
                    let mutable hits: RegistryImageHit list = []
                    let mutable count = 0

                    for el in repos.EnumerateArray() do
                        let repo = el.GetString()

                        if
                            count < limit
                            && not (String.IsNullOrWhiteSpace repo)
                            && repo.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                        then
                            hits <-
                                { Registry = "mcr.microsoft.com"
                                  Repository = repo
                                  Description = ""
                                  Stars = 0 }
                                :: hits

                            count <- count + 1

                    return List.rev hits
                | _ -> return []
            with
            | :? OperationCanceledException -> return []
            | ex ->
                Serilog.Log.Warning(ex, "La recherche dans {Registry} a échoué (résultats ignorés)", "mcr.microsoft.com")
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
          "mcr.microsoft.com", mcrSearch ]

    /// Recherche sur un client HTTP injecté (testable). Sans registre ciblé,
    /// interroge tous les fournisseurs autorisés en parallèle ; les résultats
    /// sont fusionnés dans l'ordre de la liste blanche puis bornés à limit.
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

            // Exécution parallèle des fournisseurs autorisés : le client HTTP est
            // sûr en multi-thread, donc les délais de chaque registre ne
            // s'additionnent plus. La fusion conserve l'ordre de la liste blanche
            // et le total est borné à la limite demandée.
            let tasks = active |> List.map (fun (_, search) -> search client query limit ct)

            let! completed = Task.WhenAll tasks

            let merged =
                active
                |> List.mapi (fun i _ -> completed.[i])
                |> List.concat

            return List.truncate limit merged
        }

    /// Recherche avec le client HTTP partagé du service.
    let search (registry: string option) (query: string) (limit: int) (ct: CancellationToken) : Task<RegistryImageHit list> =
        searchWith sharedClient registry query limit ct