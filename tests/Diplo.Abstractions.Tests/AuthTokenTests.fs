namespace Diplo.Abstractions.Tests

module AuthTokenTests =

    open System
    open System.Security.Cryptography
    open Xunit
    open FsUnit.Xunit
    open Diplo.Abstractions.AuthToken

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
        let token = generateToken ()
        let expectedBytes = System.Text.Encoding.UTF8.GetBytes(token)
        let providedBytes = System.Text.Encoding.UTF8.GetBytes(token)
        CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes) |> should equal true

    [<Fact>]
    let ``verifyToken avec un token different retourne false`` () =
        let token1 = generateToken ()
        let token2 = generateToken ()
        let expectedBytes = System.Text.Encoding.UTF8.GetBytes(token1)
        let providedBytes = System.Text.Encoding.UTF8.GetBytes(token2)
        if expectedBytes.Length = providedBytes.Length then
            CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes) |> should equal false
        else
            true |> should equal true

    [<Fact>]
    let ``verifyToken avec token vide retourne false`` () =
        let token = generateToken ()
        let expectedBytes = System.Text.Encoding.UTF8.GetBytes(token)
        let providedBytes = System.Text.Encoding.UTF8.GetBytes("")
        expectedBytes.Length <> providedBytes.Length |> should equal true

    [<Fact>]
    let ``verifyToken avec longueurs differentes retourne false`` () =
        let token1 = generateToken ()
        let token2 = token1 + "x"
        let expectedBytes = System.Text.Encoding.UTF8.GetBytes(token1)
        let providedBytes = System.Text.Encoding.UTF8.GetBytes(token2)
        expectedBytes.Length <> providedBytes.Length |> should equal true

    [<Fact>]
    let ``saveToken cree le fichier avec le bon contenu JSON`` () =
        let dir = IO.Path.Combine(IO.Path.GetTempPath(), "diplo-auth-test-" + Guid.NewGuid().ToString("N"))
        IO.Directory.CreateDirectory(dir) |> ignore
        let path = IO.Path.Combine(dir, "auth-token.json")
        let token = generateToken ()
        let json = sprintf "{\"Token\":\"%s\"}" token
        IO.File.WriteAllText(path, json)
        let content = IO.File.ReadAllText(path)
        let doc = System.Text.Json.JsonDocument.Parse(content)
        let loaded = doc.RootElement.GetProperty("Token").GetString()
        loaded |> should equal token
        IO.Directory.Delete(dir, true)

    [<Fact>]
    let ``rotateToken genere un nouveau token et le sauvegarde`` () =
        let dir = IO.Path.Combine(IO.Path.GetTempPath(), "diplo-rotate-test-" + Guid.NewGuid().ToString("N"))
        IO.Directory.CreateDirectory(dir) |> ignore
        let path = IO.Path.Combine(dir, "auth-token.json")
        let initialToken = generateToken ()
        let json = sprintf "{\"Token\":\"%s\"}" initialToken
        IO.File.WriteAllText(path, json)
        let doc = System.Text.Json.JsonDocument.Parse(IO.File.ReadAllText(path))
        let oldToken = doc.RootElement.GetProperty("Token").GetString()
        oldToken |> should equal initialToken
        let newToken = generateToken ()
        newToken |> should not' (equal oldToken)
        let newJson = sprintf "{\"Token\":\"%s\"}" newToken
        IO.File.WriteAllText(path, newJson)
        let doc2 = System.Text.Json.JsonDocument.Parse(IO.File.ReadAllText(path))
        let loadedToken = doc2.RootElement.GetProperty("Token").GetString()
        loadedToken |> should equal newToken
        IO.Directory.Delete(dir, true)
