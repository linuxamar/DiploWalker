namespace DiploWalker.Container.Services

open System
open System.Net.Http
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open DiploWalker.Abstractions

/// RÃ©sultat d'une recherche dans le catalogue en ligne d'un registre autorisÃ©.
type RegistryImageHit =
    { Registry: string
      Repository: string
      Description: string
      Stars: int }

/// Recherche d'images dans les catalogues en ligne des registres autorisÃ©s.
/// docker.io et quay.io exposent une API publique de recherche ; le catalogue
/// de mcr.microsoft.com est public mais sans requÃªte serveur (filtrage local) ;
/// ghcr.io n'expose aucune recherche : ses rÃ©sultats sont vides.
[<RequireQualifiedAccess>]
module RegistrySearch =

    /// Client HTTP partagÃ© du service, exposÃ© pour Ãªtre enregistrÃ© en singleton
    /// DI (Program.fs) et injectÃ© dans ContainerServiceImpl : une seule instance
    /// qu'utilisent aussi bien la libÃ©ration que la recherche, testÃ©e Ã  part.
    /// La taille maximale des rÃ©ponses borne la mÃ©moire en cas de catalogue
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
                            // Borne de sÃ©curitÃ© : une valeur dÃ©mesurÃ©e (ou incohÃ©rente)
                            // ne doit pas faire dÃ©border la conversion int64 â†’ int.
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
                Serilog.Log.Warning(ex, "La recherche dans {Registry} a Ã©chouÃ© (rÃ©sultats ignorÃ©s)", "docker.io")
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
                Serilog.Log.Warning(ex, "La recherche dans {Registry} a Ã©chouÃ© (rÃ©sultats ignorÃ©s)", "quay.io")
                return []
        }

    /// Recherche mcr.microsoft.com : le catalogue Â« _catalog Â» est public mais
    /// ne prend aucune requÃªte serveur â€” il est tÃ©lÃ©chargÃ© puis filtrÃ© en local
    /// (correspondance insensible Ã  la casse sur le nom du rÃ©fÃ©rentiel).
    let mcrSearch
        (client: HttpClient)
        (query: string)
        (limit: int)
        (ct: CancellationToken)
        : Task<RegistryImageHit list> =
        task {
            try
                // Le catalogue est paginÃ© cÃ´tÃ© serveur : demander une page de la
                // taille demandÃ©e borne la rÃ©ponse au lieu de tÃ©lÃ©charger la totalitÃ©
                // du catalogue (des centaines de Mo sur le rÃ©seau).
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
                Serilog.Log.Warning(ex, "La recherche dans {Registry} a Ã©chouÃ© (rÃ©sultats ignorÃ©s)", "mcr.microsoft.com")
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

    /// Recherche sur un client HTTP injectÃ© (testable). Sans registre ciblÃ©,
    /// interroge tous les fournisseurs autorisÃ©s en parallÃ¨le ; les rÃ©sultats
    /// sont fusionnÃ©s dans l'ordre de la liste blanche puis bornÃ©s Ã  limit.
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

            // ExÃ©cution parallÃ¨le des fournisseurs autorisÃ©s : le client HTTP est
            // sÃ»r en multi-thread, donc les dÃ©lais de chaque registre ne
            // s'additionnent plus. La fusion conserve l'ordre de la liste blanche
            // et le total est bornÃ© Ã  la limite demandÃ©e.
            let tasks = active |> List.map (fun (_, search) -> search client query limit ct)

            let! completed = Task.WhenAll tasks

            let merged =
                active
                |> List.mapi (fun i _ -> completed.[i])
                |> List.concat

            return List.truncate limit merged
        }

    /// Recherche avec le client HTTP partagÃ© du service.
    let search (registry: string option) (query: string) (limit: int) (ct: CancellationToken) : Task<RegistryImageHit list> =
        searchWith sharedClient registry query limit ct
