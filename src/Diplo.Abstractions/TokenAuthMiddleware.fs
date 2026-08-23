module Diplo.Abstractions.TokenAuthMiddleware

open System
open System.Collections.Concurrent
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open Diplo.Abstractions.AuthToken

/// Fenêtre glissante de rate limiting par adresse IP.
type private RateLimiter(maxRequests: int, windowSeconds: int) =
    let hits = ConcurrentDictionary<string, ResizeArray<DateTime>>()

    member _.IsAllowed(ip: string) =
        let now = DateTime.UtcNow
        let windowStart = now.AddSeconds(-float windowSeconds)
        let timestamps =
            hits.GetOrAdd(ip, fun _ -> ResizeArray<DateTime>())
        lock (box timestamps) (fun () ->
            // Nettoyer les entrées hors fenêtre
            timestamps.RemoveAll(fun t -> t < windowStart) |> ignore
            if timestamps.Count >= maxRequests then false
            else
                timestamps.Add(now)
                true)

    member _.TryReset(ip: string) =
        hits.TryRemove(ip) |> ignore

    /// Nettoie les entrées expirées de toutes les IP (appelé périodiquement).
    member _.PurgeExpired() =
        let now = DateTime.UtcNow
        let windowStart = now.AddSeconds(-float windowSeconds)
        for kvp in hits do
            lock (box kvp.Value) (fun () ->
                kvp.Value.RemoveAll(fun t -> t < windowStart) |> ignore)
            if kvp.Value.Count = 0 then
                hits.TryRemove(kvp.Key) |> ignore

let private rateLimiter = RateLimiter(maxRequests = 30, windowSeconds = 60)

/// Timer de nettoyage : purge les entrées expirées toutes les 2 minutes.
let private purgeTimer =
    new System.Threading.Timer(
        (fun _ -> rateLimiter.PurgeExpired()),
        null,
        System.TimeSpan.FromMinutes(2.0),
        System.TimeSpan.FromMinutes(2.0))

type TokenAuthMiddleware(next: RequestDelegate, logger: ILogger<TokenAuthMiddleware>) =

    /// Chemins exclus de l'authentification (health checks, probes).
    let isExcludedPath (path: string) =
        path.StartsWith("/healthz", StringComparison.OrdinalIgnoreCase)

    member _.Invoke(context: HttpContext) : Task =
        let path = context.Request.Path.Value

        if isExcludedPath path then
            next.Invoke(context)
        else
            let clientIp = context.Connection.RemoteIpAddress |> Option.ofObj |> function Some ip -> ip.ToString() | None -> "unknown"

            if not (rateLimiter.IsAllowed(clientIp)) then
                logger.LogWarning("Rate limit dépassé pour {ClientIp}", clientIp)
                context.Response.StatusCode <- 429
                context.Response.WriteAsync("Trop de requêtes — réessayez plus tard")
            else
                match loadToken() with
                | None ->
                    logger.LogWarning("Fichier auth-token.json introuvable — accès refusé (fail-closed)")
                    context.Response.StatusCode <- 401
                    context.Response.WriteAsync("Fichier auth-token.json introuvable")
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
                                context.Response.StatusCode <- 401
                                context.Response.WriteAsync("Token invalide")
                        else
                            logger.LogWarning("Format Authorization invalide depuis {ClientIp}: {Header}", clientIp, header)
                            context.Response.StatusCode <- 401
                            context.Response.WriteAsync("Format Authorization invalide")
                    | _ ->
                        logger.LogWarning("En-tête Authorization manquant depuis {ClientIp}", clientIp)
                        context.Response.StatusCode <- 401
                        context.Response.WriteAsync("En-tête Authorization manquant")
