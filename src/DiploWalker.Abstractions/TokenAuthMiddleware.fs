module DiploWalker.Abstractions.TokenAuthMiddleware

open System
open System.Collections.Concurrent
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open DiploWalker.Abstractions.AuthToken

/// FenÃªtre glissante de rate limiting par adresse IP.
type private RateLimiter(maxRequests: int, windowSeconds: int) =
    let hits = ConcurrentDictionary<string, ResizeArray<DateTime>>()

    member _.IsAllowed(ip: string) =
        let now = DateTime.UtcNow
        let windowStart = now.AddSeconds(-float windowSeconds)
        let timestamps = hits.GetOrAdd(ip, fun _ -> ResizeArray<DateTime>())

        lock (box timestamps) (fun () ->
            // Nettoyer les entrÃ©es hors fenÃªtre
            timestamps.RemoveAll(fun t -> t < windowStart) |> ignore

            if timestamps.Count >= maxRequests then
                false
            else
                timestamps.Add(now)
                true)

    member _.TryReset(ip: string) = hits.TryRemove(ip) |> ignore

    /// Nettoie les entrÃ©es expirÃ©es de toutes les IP (appelÃ© pÃ©riodiquement).
    member _.PurgeExpired() =
        let now = DateTime.UtcNow
        let windowStart = now.AddSeconds(-float windowSeconds)

        for kvp in hits do
            lock (box kvp.Value) (fun () ->
                kvp.Value.RemoveAll(fun t -> t < windowStart) |> ignore

                if kvp.Value.Count = 0 then
                    hits.TryRemove(kvp.Key) |> ignore)

let private rateLimiter = RateLimiter(maxRequests = 30, windowSeconds = 60)

/// Timer de nettoyage : purge les entrÃ©es expirÃ©es toutes les 2 minutes.
/// DÃ©tenu au niveau du module pour la persistance du rate limiter entre les
/// requÃªtes ; dispose sur l'arrÃªt du service (voir disposePurge).
let private purgeTimer =
    new System.Threading.Timer(
        (fun _ -> rateLimiter.PurgeExpired()),
        null,
        System.TimeSpan.FromMinutes(2.0),
        System.TimeSpan.FromMinutes(2.0)
    )

/// Dispose le timer de purge (appelÃ© Ã  l'arrÃªt du service â€” M9) : Ã©vite de
/// laisser un Timer racine actif indÃ©finiment.
let disposePurge () =
    try
        purgeTimer.Dispose()
    with _ ->
        ()

type TokenAuthMiddleware(next: RequestDelegate, logger: ILogger<TokenAuthMiddleware>) =

    /// Chemins exclus de l'authentification (health checks, probes).
    let isExcludedPath (path: string) =
        path.StartsWith("/healthz", StringComparison.OrdinalIgnoreCase)

    member _.Invoke(context: HttpContext) : Task =
        // Erreurs serveur : aucune mise en cache, et le corps de la rÃ©ponse Ã©crit
        // est bien retournÃ© comme tÃ¢che (M9 â€” sinon la rÃ©ponse peut Ãªtre tronquÃ©e).
        let writeError (status: int) (message: string) =
            context.Response.Headers.CacheControl <- "no-store"
            context.Response.StatusCode <- status
            context.Response.WriteAsync(message)

        let path = context.Request.Path.Value

        if isExcludedPath path then
            next.Invoke(context)
        else
            let clientIp =
                context.Connection.RemoteIpAddress
                |> Option.ofObj
                |> function
                    | Some ip -> ip.ToString()
                    | None -> "unknown"

            if not (rateLimiter.IsAllowed(clientIp)) then
                logger.LogWarning("Rate limit dÃ©passÃ© pour {ClientIp}", clientIp)
                writeError 429 "Trop de requÃªtes â€” rÃ©essayez plus tard"
            else
                match loadToken () with
                | None ->
                    logger.LogWarning("Fichier auth-token.json introuvable â€” accÃ¨s refusÃ© (fail-closed)")
                    writeError 401 "Fichier auth-token.json introuvable"
                | Some _ ->
                    match context.Request.Headers.TryGetValue("authorization") with
                    | true, values when values.Count > 0 ->
                        let header = values.[0]

                        if header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) then
                            let token = header.Substring(7)

                            if verifyToken token then
                                next.Invoke(context)
                            else
                                logger.LogWarning("Token invalide depuis {ClientIp}", clientIp)
                                writeError 401 "Token invalide"
                        else
                            // Ne jamais journaliser la valeur brute de l'en-tÃªte
                            // (elle peut contenir un secret) : schÃ©ma seul + longueur.
                            let schemeEnd = header.IndexOf(' ')

                            let scheme =
                                if schemeEnd > 0 && schemeEnd <= 32 then
                                    header.Substring(0, schemeEnd)
                                else
                                    "<sans espace>"

                            logger.LogWarning(
                                "Format Authorization invalide depuis {ClientIp} (schÃ©ma: {Scheme}, longueur: {Length})",
                                clientIp,
                                scheme,
                                header.Length
                            )

                            writeError 401 "Format Authorization invalide"
                    | _ ->
                        logger.LogWarning("En-tÃªte Authorization manquant depuis {ClientIp}", clientIp)
                        writeError 401 "En-tÃªte Authorization manquant"

