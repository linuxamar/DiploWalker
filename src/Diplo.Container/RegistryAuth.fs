namespace Diplo.Container

open System
open System.IO
open System.Security.Cryptography
open System.Text.Json
open Serilog
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
            let programData =
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)

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
        if isNull password then
            invalidArg (nameof password) "Le mot de passe ne peut pas être null"

        if OperatingSystem.IsWindows() then
            let bytes = System.Text.Encoding.UTF8.GetBytes(password)
            Convert.ToBase64String(ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser))
        else
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password))

    let private unprotect (encoded: string) =
        try
            if OperatingSystem.IsWindows() then
                System.Text.Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(Convert.FromBase64String(encoded), null, DataProtectionScope.CurrentUser)
                )
            else
                System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded))
        with ex ->
            Log.Warning(ex, "Impossible de déchiffrer le mot de passe chiffré ( données potentiellement corrompues)")
            ""

    /// Charge les identifiants persistés (Map registre -> identifiant).
    /// Retourne un état vide si le fichier est absent ou illisible.
    let load (path: string) : Map<string, RegistryEntry> =
        try
            if not (File.Exists path) then
                Map.empty
            else
                let json = File.ReadAllText path

                if String.IsNullOrWhiteSpace(json) || json = "null" then
                    Map.empty
                else
                    let entries = JsonSerializer.Deserialize<RegistryEntry list>(json)

                    if isNull (box entries) then
                        Map.empty
                    else
                        entries |> Seq.map (fun e -> e.Registry, e) |> Map.ofSeq
        with
        | :? JsonException as ex ->
            Log.Warning(ex, "Fichier d'authentification registre corrompu: {Path}", path)
            Map.empty
        | ex ->
            Log.Warning(ex, "Erreur lors de la lecture du fichier d'authentification registre: {Path}", path)
            Map.empty

    /// Enregistre les identifiants (écriture atomique : fichier temporaire puis remplacement).
    let save (path: string) (entries: seq<RegistryEntry>) =
        let json =
            JsonSerializer.Serialize(entries |> Seq.toList, JsonSerializerOptions(WriteIndented = true))

        AtomicFile.write path json

    /// Verrou global : les handlers gRPC s'exécutent en parallèle et add/remove
    /// font une lecture-modification-écriture — sans verrou, deux mutations
    /// concurrentes s'écrasent mutuellement (perte silencieuse d'identifiants).
    let private stateLock = obj ()

    /// Ajoute ou met à jour l'identifiant d'un registre (mot de passe chiffré).
    let add (path: string) (registry: string) (username: string) (password: string) =
        lock stateLock (fun () ->
            let current = load path

            let updated =
                Map.add
                    registry
                    { Registry = registry
                      Username = username
                      EncryptedPassword = protect password }
                    current

            save path (updated |> Map.toSeq |> Seq.map snd))

    /// Retire l'identifiant d'un registre.
    let remove (path: string) (registry: string) =
        lock stateLock (fun () ->
            let current = load path
            let updated = Map.remove registry current
            save path (updated |> Map.toSeq |> Seq.map snd))

    /// Retourne l'identifiant d'un registre sous forme "user:password" pour ctr --user.
    let tryGetUserArg (path: string) (registry: string) =
        load path
        |> Map.tryFind registry
        |> Option.map (fun e -> sprintf "%s:%s" e.Username (unprotect e.EncryptedPassword))

    // ─── Helper de credentials containerd ─────────────────────────────
    //
    // `ctr image pull --user user:pass` expose le mot de passe dans argv,
    // lisible par tout processus local (WMI Win32_Process, journaux d'audit).
    // containerd supporte le protocole docker-credential-helper : un hosts.toml
    // déclare « auth = <programme> », et le programme reçoit l'URL du serveur
    // sur stdin puis répond {"Username":…,"Secret":…}. Le secret ne transite
    // plus par argv — il reste en mémoire du helper uniquement.

    /// Répertoire du helper : chemin SANS espace (C:\ProgramData\Diplo\...),
    /// requis car containerd exécute la valeur « auth » telle quelle.
    let helperDir () =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Diplo",
            "cred-helper"
        )

    let private helperScript =
        """$ErrorActionPreference = 'Stop'
try {
  Add-Type -AssemblyName System.Security | Out-Null
  $serverUrl = [Console]::In.ReadLine()
  if (-not $serverUrl) { exit 1 }
  $target = ([Uri]$serverUrl).Authority.ToLowerInvariant()
  $statePath = Join-Path $env:ProgramData 'Diplo\registry-auth.json'
  if (-not (Test-Path $statePath)) { exit 1 }
  $list = Get-Content $statePath -Raw | ConvertFrom-Json
  foreach ($e in @($list)) {
    $r = ('' + $e.registry).ToLowerInvariant() -replace '^[a-z]+://', '' -replace '/.*$', ''
    if ($r -eq $target -or $target.EndsWith($r)) {
      $bytes = [Convert]::FromBase64String($e.encryptedPassword)
      $plain = [Text.Encoding]::UTF8.GetString(
                 [Security.Cryptography.ProtectedData]::Unprotect(
                   $bytes, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser))
      $escU = ('' + $e.username).Replace('\', '\\').Replace('"', '\"')
      $escP = $plain.Replace('\', '\\').Replace('"', '\"')
      [Console]::Out.Write('{"Username":"' + $escU + '","Secret":"' + $escP + '"}')
      exit 0
    }
  }
  exit 1
} catch { exit 1 }"""

    /// Texte du script PowerShell du helper. Exposé en lecture pour les tests :
    /// ceux-ci en instancient une copie avec le chemin du fichier d'état
    /// redirigé vers un répertoire temporaire, sans toucher au vrai
    /// %ProgramData%\Diplo\registry-auth.json.
    let helperScriptText () : string = helperScript

    /// Écrit (idempotent) le shim .cmd + le script PowerShell du helper et
    /// retourne le chemin du shim à référencer depuis hosts.toml.
    let ensureHelper () : string =
        let dir = helperDir ()
        Directory.CreateDirectory dir |> ignore

        let ps1Path = Path.Combine(dir, "diplo-cred-helper.ps1")

        if not (File.Exists ps1Path) || (File.ReadAllText ps1Path) <> helperScript then
            File.WriteAllText(ps1Path, helperScript)

        let cmdPath = Path.Combine(dir, "diplo-cred-helper.cmd")

        let shim =
            "@echo off\r\npowershell -NoProfile -ExecutionPolicy Bypass -File \"%~dp0diplo-cred-helper.ps1\" %*\r\n"

        if not (File.Exists cmdPath) || (File.ReadAllText cmdPath) <> shim then
            File.WriteAllText(cmdPath, shim)

        cmdPath

    /// Normalise une chaîne de registre en URL de serveur utilisable comme clé
    /// hosts.toml. docker.io est mappé sur son endpoint canonique. Retourne ""
    /// si la chaîne n'est pas un hôte exploitable (ex. bibliothèque locale).
    let normalizeRegistryHost (registry: string) : string =
        if String.IsNullOrWhiteSpace registry then
            ""
        else
            let stripped = registry.Trim().TrimEnd('/')

            let noScheme =
                if stripped.StartsWith("http://", StringComparison.OrdinalIgnoreCase) then
                    stripped.Substring(7)
                elif stripped.StartsWith("https://", StringComparison.OrdinalIgnoreCase) then
                    stripped.Substring(8)
                else
                    stripped

            let hostOnly = noScheme.Split('/').[0]

            if hostOnly.Equals("docker.io", StringComparison.OrdinalIgnoreCase) then
                "https://registry-1.docker.io"
            elif hostOnly.Contains('.') || hostOnly.Contains(':') then
                "https://" + hostOnly
            else
                ""

    /// Prépare un répertoire hosts-dir temporaire déléguant l'authentification
    /// au helper. Structure attendue par ctr : <racine>/<hôte>/hosts.toml.
    let prepareHostsDir (registry: string) : string option =
        match normalizeRegistryHost registry with
        | "" ->
            None
        | serverUrl ->
            let root =
                Path.Combine(Path.GetTempPath(), "diplo-hosts-" + Guid.NewGuid().ToString("N"))

            let hostName = serverUrl.Substring("https://".Length)
            let hostDir = Path.Combine(root, hostName)
            Directory.CreateDirectory(hostDir) |> ignore

            // Chaîne littérale TOML (apostrophes) : les backslashes Windows
            // restent tels quels pour containerd.
            let toml =
                sprintf "[host.\"%s\"]\ncapabilities = [\"pull\", \"resolve\"]\nauth = '%s'\n" serverUrl (ensureHelper ())

            File.WriteAllText(Path.Combine(hostDir, "hosts.toml"), toml)
            Some root
