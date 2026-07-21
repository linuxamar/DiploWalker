module Diplo.Abstractions.AuthToken

open System
open System.IO
open System.Security.Cryptography
open System.Text.Json

let authTokenDir = @"C:\ProgramData\Diplo"

let authTokenPath = Path.Combine(authTokenDir, "auth-token.json")

let private jsonOptions = JsonSerializerOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)

type AuthTokenFile = { Token: string }

let generateToken () : string =
    let bytes = RandomNumberGenerator.GetBytes(32)
    Convert.ToBase64String(bytes)

let saveToken (token: string) =
    if not (Directory.Exists(authTokenDir)) then
        Directory.CreateDirectory(authTokenDir) |> ignore
    let json = JsonSerializer.Serialize({ Token = token }, jsonOptions)
    File.WriteAllText(authTokenPath, json)

let loadToken () : string option =
    try
        if File.Exists(authTokenPath) then
            let json = File.ReadAllText(authTokenPath)
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement
            Some(root.GetProperty("Token").GetString())
        else
            None
    with _ -> None

let verifyToken (provided: string) : bool =
    match loadToken() with
    | None -> false
    | Some expected ->
        let expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected)
        let providedBytes = System.Text.Encoding.UTF8.GetBytes(provided)
        if expectedBytes.Length <> providedBytes.Length then false
        else CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes)

/// Génère un nouveau token et l'enregistre, remplaçant l'ancien.
/// Retourne le nouveau token.
let rotateToken () : string =
    let newToken = generateToken ()
    saveToken newToken
    newToken
