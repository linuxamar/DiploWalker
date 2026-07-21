module Diplo.Abstractions.AuthToken

open System
open System.IO
open System.Security.AccessControl
open System.Security.Cryptography
open System.Security.Principal
open System.Text.Json
open Serilog

let authTokenDir = @"C:\ProgramData\Diplo"

let authTokenPath = Path.Combine(authTokenDir, "auth-token.json")

let private jsonOptions = JsonSerializerOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)

type AuthTokenFile =
    { Token: string
      [<System.Text.Json.Serialization.JsonPropertyName("expiresAt")>]
      ExpiresAt: System.DateTime }

let generateToken () : string =
    let bytes = RandomNumberGenerator.GetBytes(32)
    Convert.ToBase64String(bytes)

let saveToken (token: string) =
    if not (Directory.Exists(authTokenDir)) then
        Directory.CreateDirectory(authTokenDir) |> ignore
    let expiresAt = DateTime.UtcNow.AddHours(24.0)
    let json = JsonSerializer.Serialize({ Token = token; ExpiresAt = expiresAt }, jsonOptions)
    File.WriteAllText(authTokenPath, json)
    try
        let fileInfo = new FileInfo(authTokenPath)
        let acl = fileInfo.GetAccessControl()
        acl.SetAccessRuleProtection(true, false)
        let currentUser = WindowsIdentity.GetCurrent()
        let rule = FileSystemAccessRule(
            currentUser.User,
            FileSystemRights.FullControl,
            AccessControlType.Allow
        )
        acl.AddAccessRule(rule)
        fileInfo.SetAccessControl(acl)
    with ex ->
        Log.Warning(ex, "Impossible de définir les ACL NTFS sur {Path}" , authTokenPath)

let loadToken () : string option =
    try
        if File.Exists(authTokenPath) then
            let json = File.ReadAllText(authTokenPath)
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement
            let token = root.GetProperty("Token").GetString()
            let mutable expiresElement = Unchecked.defaultof<JsonElement>
            if root.TryGetProperty("expiresAt", &expiresElement) then
                let expiresAt = expiresElement.GetDateTime()
                if DateTime.UtcNow > expiresAt then
                    Log.Warning("Token expiré le {ExpiresAt}", expiresAt)
                    None
                else
                    Some token
            else
                Some token
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
