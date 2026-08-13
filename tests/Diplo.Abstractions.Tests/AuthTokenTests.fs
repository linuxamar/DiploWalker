namespace Diplo.Abstractions.Tests

module AuthTokenTests =

    open System
    open System.IO
    open System.Text.Json
    open Xunit
    open FsUnit.Xunit
    open Diplo.Abstractions.AuthToken

    // Sérialisé avec TokenAuthMiddlewareTests : les deux manipulent le
    // chemin global du fichier de token.
    [<Xunit.Collection("auth-token")>]
    module Tests =

        let withTempPath (f: unit -> unit) =
            let dir = Path.Combine(Path.GetTempPath(), "diplo-auth-" + Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(dir) |> ignore
            let previous = authTokenPath
            authTokenPath <- Path.Combine(dir, "auth-token.json")
            try
                f ()
            finally
                authTokenPath <- previous
                Directory.Delete(dir, true)

        [<Fact>]
        let ``generateToken retourne une chaine non vide`` () =
            let token = generateToken ()
            token |> should not' (be NullOrEmptyString)

        [<Fact>]
        let ``generateToken retourne un token Base64 de 32 octets`` () =
            let token = generateToken ()
            let bytes = Convert.FromBase64String(token)
            bytes.Length |> should equal 32

        [<Fact>]
        let ``generateToken produit des tokens differents`` () =
            let t1 = generateToken ()
            let t2 = generateToken ()
            t1 |> should not' (equal t2)

        [<Fact>]
        let ``verifyToken avec le meme token retourne true`` () =
            withTempPath (fun () ->
                let token = generateToken ()
                saveToken token
                verifyToken token |> should equal true)

        [<Fact>]
        let ``verifyToken avec un token different retourne false`` () =
            withTempPath (fun () ->
                let token = generateToken ()
                saveToken token
                verifyToken (generateToken ()) |> should equal false)

        [<Fact>]
        let ``verifyToken sans fichier token retourne false`` () =
            withTempPath (fun () ->
                verifyToken (generateToken ()) |> should equal false)

        [<Fact>]
        let ``verifyToken avec token vide retourne false`` () =
            withTempPath (fun () ->
                let token = generateToken ()
                saveToken token
                verifyToken "" |> should equal false)

        [<Fact>]
        let ``verifyToken echoue si le token est expire`` () =
            withTempPath (fun () ->
                let token = generateToken ()
                let expired = { Token = token; ExpiresAt = DateTime.UtcNow.AddHours(-1.0) }
                File.WriteAllText(authTokenPath, JsonSerializer.Serialize(expired))
                verifyToken token |> should equal false)

        [<Fact>]
        let ``saveToken ecrit un fichier dont verifyToken accepte le token`` () =
            withTempPath (fun () ->
                let token = generateToken ()
                saveToken token
                File.Exists(authTokenPath) |> should equal true
                verifyToken token |> should equal true)

        [<Fact>]
        let ``rotateToken genere un nouveau token et invalide l'ancien`` () =
            withTempPath (fun () ->
                let token = generateToken ()
                saveToken token
                let newToken = rotateToken ()
                newToken |> should not' (equal token)
                verifyToken newToken |> should equal true
                verifyToken token |> should equal false)
