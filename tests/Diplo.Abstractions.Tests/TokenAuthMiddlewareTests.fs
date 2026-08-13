namespace Diplo.Abstractions.Tests

// Sérialisé avec AuthTokenTests : les deux manipulent le chemin global
// du fichier de token.
[<Xunit.Collection("auth-token")>]
module TokenAuthMiddlewareTests =

    open System
    open System.IO
    open System.Threading.Tasks
    open Microsoft.AspNetCore.Http
    open Microsoft.AspNetCore.Http.Features
    open Microsoft.Extensions.Logging
    open Microsoft.Extensions.Primitives
    open Xunit
    open FsUnit.Xunit
    open Diplo.Abstractions.AuthToken
    open Diplo.Abstractions.TokenAuthMiddleware

    type NoopLogger<'T>() =
        interface ILogger<'T> with
            member _.Log<'TState>(_logLevel: LogLevel, _eventId: EventId, _state: 'TState, _exn: exn, _formatter: Func<'TState, exn, string>) = ()
            member _.IsEnabled(_logLevel) = false
            member _.BeginScope(_state: 'TState) = Unchecked.defaultof<IDisposable>

    type NextHandler() =
        let mutable called = false
        member _.Called = called
        member _.Invoke(_ctx: HttpContext) : Task =
            called <- true
            Task.CompletedTask

    let createHttpContext () =
        let ctx = DefaultHttpContext()
        ctx.Response.Body <- new MemoryStream()
        ctx

    let addAuthHeader (ctx: HttpContext) (token: string) =
        ctx.Request.Headers.Append("Authorization", StringValues("Bearer " + token)) |> ignore

    let withTempToken (token: string) (f: unit -> unit) =
        let path = authTokenPath
        let existed = File.Exists(path)
        let backup =
            if existed then Some (File.ReadAllText(path))
            else None
        try
            saveToken token
            f ()
        finally
            match backup with
            | Some content -> File.WriteAllText(path, content)
            | None ->
                if File.Exists(path) then File.Delete(path)

    [<Fact>]
    let ``middleware sans fichier token retourne 401 (fail-closed)`` () =
        let next = NextHandler()
        let logger = NoopLogger<TokenAuthMiddleware>() :> ILogger<TokenAuthMiddleware>
        let mw = TokenAuthMiddleware(RequestDelegate(next.Invoke), logger)
        let path = authTokenPath
        let existed = File.Exists(path)
        let backup = if existed then Some(File.ReadAllText(path)) else None
        try
            if File.Exists(path) then File.Delete(path)
            let ctx = createHttpContext ()
            mw.Invoke(ctx).Wait()
            next.Called |> should equal false
            ctx.Response.StatusCode |> should equal 401
        finally
            match backup with
            | Some c -> File.WriteAllText(path, c)
            | None -> ()

    [<Fact>]
    let ``middleware avec token correct laisse passer la requete`` () =
        let token = generateToken ()
        let next = NextHandler()
        let logger = NoopLogger<TokenAuthMiddleware>() :> ILogger<TokenAuthMiddleware>
        let mw = TokenAuthMiddleware(RequestDelegate(next.Invoke), logger)
        withTempToken token (fun () ->
            let ctx = createHttpContext ()
            addAuthHeader ctx token
            mw.Invoke(ctx).Wait()
            next.Called |> should equal true
            ctx.Response.StatusCode |> should equal 200
        )

    [<Fact>]
    let ``middleware sans header Authorization retourne 401`` () =
        let token = generateToken ()
        let next = NextHandler()
        let logger = NoopLogger<TokenAuthMiddleware>() :> ILogger<TokenAuthMiddleware>
        let mw = TokenAuthMiddleware(RequestDelegate(next.Invoke), logger)
        withTempToken token (fun () ->
            let ctx = createHttpContext ()
            mw.Invoke(ctx).Wait()
            next.Called |> should equal false
            ctx.Response.StatusCode |> should equal 401
        )

    [<Fact>]
    let ``middleware avec mauvais token retourne 401`` () =
        let token = generateToken ()
        let badToken = generateToken ()
        let next = NextHandler()
        let logger = NoopLogger<TokenAuthMiddleware>() :> ILogger<TokenAuthMiddleware>
        let mw = TokenAuthMiddleware(RequestDelegate(next.Invoke), logger)
        withTempToken token (fun () ->
            let ctx = createHttpContext ()
            addAuthHeader ctx badToken
            mw.Invoke(ctx).Wait()
            next.Called |> should equal false
            ctx.Response.StatusCode |> should equal 401
        )

    [<Fact>]
    let ``middleware avec format Authorization invalide retourne 401`` () =
        let token = generateToken ()
        let next = NextHandler()
        let logger = NoopLogger<TokenAuthMiddleware>() :> ILogger<TokenAuthMiddleware>
        let mw = TokenAuthMiddleware(RequestDelegate(next.Invoke), logger)
        withTempToken token (fun () ->
            let ctx = createHttpContext ()
            ctx.Request.Headers.Append("Authorization", StringValues("Basic " + token)) |> ignore
            mw.Invoke(ctx).Wait()
            next.Called |> should equal false
            ctx.Response.StatusCode |> should equal 401
        )

    [<Fact>]
    let ``middleware avec header vide retourne 401`` () =
        let token = generateToken ()
        let next = NextHandler()
        let logger = NoopLogger<TokenAuthMiddleware>() :> ILogger<TokenAuthMiddleware>
        let mw = TokenAuthMiddleware(RequestDelegate(next.Invoke), logger)
        withTempToken token (fun () ->
            let ctx = createHttpContext ()
            ctx.Request.Headers.Append("Authorization", StringValues("")) |> ignore
            mw.Invoke(ctx).Wait()
            next.Called |> should equal false
            ctx.Response.StatusCode |> should equal 401
        )
