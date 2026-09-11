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

    /// Fichier de clé AES-GCM (hors Windows) : %LocalAppData%\Diplo\registry-key.bin.
    let private keyFile () =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Diplo",
            "registry-key.bin"
        )

    /// Charge ou crée la clé par utilisateur. Sur les plateformes POSIX le fichier
    /// est créé avec des droits 0600 ; sous Windows il hérite du profil utilisateur.
    let private loadOrCreateKey () : byte array =
        let path = keyFile ()
        Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore

        if File.Exists path then
            File.ReadAllBytes path
        else
            let key = RandomNumberGenerator.GetBytes(32)
            File.WriteAllBytes(path, key)

            if not (OperatingSystem.IsWindows()) then
                try
                    File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
                with _ ->
                    ()

            key

    let private protect (password: string) =
        if isNull password then
            invalidArg (nameof password) "Le mot de passe ne peut pas être null"

        if OperatingSystem.IsWindows() then
            let bytes = System.Text.Encoding.UTF8.GetBytes(password)
            Convert.ToBase64String(ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser))
        else
            // M7 : hors Windows, PAS de repli base64 en clair — chiffrement
            // AES-GCM scellé par une clé par utilisateur (fichier à droits
            // restreints, voir loadOrCreateKey).
            let plain = System.Text.Encoding.UTF8.GetBytes(password)
            let key = loadOrCreateKey ()
            let nonce = RandomNumberGenerator.GetBytes(12)
            let cipher = Array.zeroCreate<byte> plain.Length
            let tag = Array.zeroCreate<byte> 16

            use aes = new AesGcm(key, 16)
            aes.Encrypt(nonce, plain, cipher, tag)

            // format : base64(nonce | cipher | tag)
            let blob = Array.append (Array.append nonce cipher) tag
            Convert.ToBase64String blob

    let private unprotect (encoded: string) =
        if OperatingSystem.IsWindows() then
            try
                System.Text.Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(Convert.FromBase64String(encoded), null, DataProtectionScope.CurrentUser)
                )
            with ex ->
                Log.Warning(ex, "Impossible de déchiffrer le mot de passe (données potentiellement corrompues)")
                ""
        else
            try
                let blob = Convert.FromBase64String(encoded)
                let key = loadOrCreateKey ()

                use aes = new AesGcm(key, 16)
                let nonce = blob[.. 11]
                let tag = blob[blob.Length - 16 ..]
                let cipher = blob[12 .. blob.Length - 17]
                let plain = Array.zeroCreate<byte> cipher.Length
                aes.Decrypt(nonce, cipher, tag, plain)
                System.Text.Encoding.UTF8.GetString(plain)
            with ex ->
                // Repli de compatibilité : anciens états hors Windows écrits en
                // base64 clair. Lecture tolérée (le stockage, lui, n'est plus en
                // clair) ; une nouvelle connexion réécrit l'entrée en AES-GCM.
                try
                    System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded))
                with _ ->
                    Log.Warning(ex, "Impossible de déchiffrer le mot de passe (données potentiellement corrompues)")
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

    /// Prépare un hosts-dir temporaire pour des identifiants EXPLICITES
    /// (option --user, au format « utilisateur:secret ») : H6 — les secrets ne
    /// transitent plus par argv. Les identifiants sont persistés dans un état
    /// jetable (DPAPI sous Windows) lu par un helper créé dans le même dossier.
    /// Retourne None si l'argument n'est pas au format attendu.
    let prepareHostsDirForCredentials (registry: string) (authArg: string) : string option =
        match normalizeRegistryHost registry with
        | "" ->
            None
        | serverUrl ->
            // « user[:password] » : seul le dernier « : » sépare les deux.
            match authArg.LastIndexOf(':') with
            | sep when sep <= 0 || sep = authArg.Length - 1 ->
                None
            | sep ->
                let username = authArg.Substring(0, sep)
                let password = authArg.Substring(sep + 1)
                let root = Path.Combine(Path.GetTempPath(), "diplo-hosts-" + Guid.NewGuid().ToString("N"))

                let hostName = serverUrl.Substring("https://".Length)
                let hostDir = Path.Combine(root, hostName)
                Directory.CreateDirectory(hostDir) |> ignore

                let stateFile = Path.Combine(root, "registry-auth.json")
                save stateFile [ { Registry = registry; Username = username; EncryptedPassword = protect password } ]

                // Copie du script helper partagé avec le fichier d'état pointé
                // vers l'état jetable (chaîne littérale PS entre apostrophes :
                // les backslashes n'y sont pas échappés).
                let helperPs1 = Path.Combine(root, "diplo-cred-helper.ps1")

                let script =
                    helperScript.Replace("'Diplo\\registry-auth.json'", sprintf "'%s'" stateFile)

                File.WriteAllText(helperPs1, script)

                let shim = Path.Combine(root, "diplo-cred-helper.cmd")

                File.WriteAllText(
                    shim,
                    "@echo off\r\npowershell -NoProfile -ExecutionPolicy Bypass -File \"%~dp0diplo-cred-helper.ps1\" %*\r\n"
                )

                let toml =
                    sprintf "[host.\"%s\"]\ncapabilities = [\"pull\", \"resolve\"]\nauth = '%s'\n" serverUrl shim

                File.WriteAllText(Path.Combine(hostDir, "hosts.toml"), toml)
                Some root
