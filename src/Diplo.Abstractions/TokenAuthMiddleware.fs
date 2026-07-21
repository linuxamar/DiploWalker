module Diplo.Abstractions.TokenAuthMiddleware

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open Diplo.Abstractions.AuthToken

type TokenAuthMiddleware(next: RequestDelegate, logger: ILogger<TokenAuthMiddleware>) =

    member _.Invoke(context: HttpContext) : Task =
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
                        context.Response.StatusCode <- 401
                        context.Response.WriteAsync("Token invalide")
                else
                    context.Response.StatusCode <- 401
                    context.Response.WriteAsync("Format Authorization invalide")
            | _ ->
                context.Response.StatusCode <- 401
                context.Response.WriteAsync("En-tête Authorization manquant")
