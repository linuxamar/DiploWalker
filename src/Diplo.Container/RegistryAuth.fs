namespace Diplo.Container

open System
open System.IO
open System.Security.Cryptography
open System.Text.Json
open Diplo.Abstractions

/// Persistance des identifiants de registres de conteneurs (login/logout).
/// Le mot de passe est chiffré avec DPAPI (portée utilisateur courant) sous
/// Windows ; ailleurs, un repli base64 est utilisé (sans chiffrement).
module RegistryAuth =

    /// Identifiant d'un registre tel que persisté dans le fichier d'état.
    type RegistryEntry =
        { Registry: string
          Username: string
          EncryptedPassword: string }

    let private stateFileName = "registry-auth.json"

    let private defaultStateFile () =
        let baseDir =
            let programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            Path.Combine(programData, "Diplo")
        Path.Combine(baseDir, stateFileName)

    /// Chemin du fichier d'état (par défaut : %ProgramData%\Diplo\registry-auth.json).
    let private stateFileRef = ref (defaultStateFile ())

    /// Remplace le chemin du fichier d'état (utile pour les tests).
    let setStateFile (path: string) =
        lock stateFileRef (fun () -> stateFileRef := path)

    /// Chemin courant du fichier d'état des identifiants de registres.
    let stateFile () = !stateFileRef

    let private protect (password: string) =
        if OperatingSystem.IsWindows() then
            let bytes = System.Text.Encoding.UTF8.GetBytes(password)
            Convert.ToBase64String(ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser))
        else
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password))

    let private unprotect (encoded: string) =
        try
            if OperatingSystem.IsWindows() then
                System.Text.Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(Convert.FromBase64String(encoded), null, DataProtectionScope.CurrentUser))
            else
                System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded))
        with _ -> ""

    /// Charge les identifiants persistés (Map registre -> identifiant).
    /// Retourne un état vide si le fichier est absent ou illisible.
    let load (path: string) : Map<string, RegistryEntry> =
        try
            if not (File.Exists path) then Map.empty
            else
                let json = File.ReadAllText path
                if String.IsNullOrWhiteSpace(json) || json = "null" then Map.empty
                else
                    let entries = JsonSerializer.Deserialize<RegistryEntry list>(json)
                    if isNull (box entries) then Map.empty
                    else entries |> Seq.map (fun e -> e.Registry, e) |> Map.ofSeq
        with
        | :? JsonException -> Map.empty
        | _ -> Map.empty

    /// Enregistre les identifiants (écriture atomique : fichier temporaire puis remplacement).
    let save (path: string) (entries: seq<RegistryEntry>) =
        let json = JsonSerializer.Serialize(entries |> Seq.toList, JsonSerializerOptions(WriteIndented = true))
        AtomicFile.write path json

    /// Ajoute ou met à jour l'identifiant d'un registre (mot de passe chiffré).
    let add (path: string) (registry: string) (username: string) (password: string) =
        let current = load path
        let updated =
            Map.add registry
                { Registry = registry
                  Username = username
                  EncryptedPassword = protect password }
                current
        save path (updated |> Map.toSeq |> Seq.map snd)

    /// Retire l'identifiant d'un registre.
    let remove (path: string) (registry: string) =
        let current = load path
        let updated = Map.remove registry current
        save path (updated |> Map.toSeq |> Seq.map snd)

    /// Retourne l'identifiant d'un registre sous forme "user:password" pour ctr --user.
    let tryGetUserArg (path: string) (registry: string) =
        load path
        |> Map.tryFind registry
        |> Option.map (fun e -> sprintf "%s:%s" e.Username (unprotect e.EncryptedPassword))
