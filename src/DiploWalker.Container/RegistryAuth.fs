namespace DiploWalker.Container

open System
open System.IO
open System.Security.AccessControl
open System.Security.Cryptography
open System.Security.Principal
open System.Text.Json
open Grpc.Core
open Serilog
open DiploWalker.Abstractions

/// Persistance des identifiants de registres de conteneurs (login/logout).
/// Le mot de passe est chiffré avec DPAPI (portée utilisateur courant) sous
/// Windows ; ailleurs, il est scellé en AES-GCM avec une clé par utilisateur
/// (fichier à droits restreints, voir SECURITY.md).
module RegistryAuth =

    /// Identifiant d'un registre tel que persisté dans le fichier d'état.
    type RegistryEntry =
        { Registry: string
          Username: string
          EncryptedPassword: string }

    let private stateFileName = "registry-auth.json"

    let private defaultStateFile () = Path.Combine(AppPaths.dataRoot (), stateFileName)

    /// Chemin du fichier d'état (par défaut : %ProgramData%\Diplo\registry-auth.json).
    let private stateFileRef = ref (defaultStateFile ())

    /// Remplace le chemin du fichier d'état (utile pour les tests).
    let setStateFile (path: string) =
        lock stateFileRef (fun () -> stateFileRef := path)

    /// Chemin courant du fichier d'état des identifiants de registres.
    let stateFile () = !stateFileRef

    /// Fichier de clé AES-GCM (hors Windows), sous la racine par utilisateur
    /// (`AppPaths.userRoot`) : `%LocalAppData%\Diplo\registry-key.bin` sous
    /// Windows, `$XDG_DATA_HOME/Diplo/registry-key.bin` ailleurs.
    let keyFile () = Path.Combine(AppPaths.userRoot (), "registry-key.bin")

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

            if OperatingSystem.IsWindows() then
                try
                    let fs = FileSecurity()
                    fs.SetAccessRuleProtection(true, false)
                    let sid = WindowsIdentity.GetCurrent().User
                    fs.AddAccessRule(FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow))
                    let fi = new FileInfo(path)
                    fi.SetAccessControl(fs)
                with _ ->
                    ()
            else
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
            System.Text.Encoding.UTF8.GetString(
                ProtectedData.Unprotect(Convert.FromBase64String(encoded), null, DataProtectionScope.CurrentUser)
            )
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
            with _ ->
                // Repli de compatibilité : anciens états hors Windows écrits en
                // base64 clair. Lecture tolérée (le stockage, lui, n'est plus en
                // clair) ; une nouvelle connexion réécrit l'entrée en AES-GCM.
                try
                    System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded))
                with _ ->
                    failwith "Impossible de déchiffrer le mot de passe (données corrompues)"

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
    let helperDir () = AppPaths.dataDir "cred-helper"

    /// Script PowerShell du helper de credentials. Portable : le dechiffrement
    /// utilise DPAPI sous Windows et l AES-GCM (meme format que `protect`) ailleurs.
    /// Les chemins du fichier d'etat et de la cle sont fournis par le lanceur via
    /// l'environnement ; a defaut ils sont deduits de l'emplacement du script.
    let private helperScript =
        """$ErrorActionPreference = 'Stop'
try {
  $serverUrl = [Console]::In.ReadLine()
  if (-not $serverUrl) { exit 1 }
  $target = ([Uri]$serverUrl).Authority.ToLowerInvariant()

  # Repertoire du helper : un niveau au-dessus pour un helper partage,
  # le script lui-meme pour un helper jetable.
  $base = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
  $statePath = $env:DIPLO_REGISTRY_AUTH_STATE
  if (-not $statePath) { $statePath = [IO.Path]::GetFullPath([IO.Path]::Combine($base, '..', 'registry-auth.json')) }
  if (-not [IO.File]::Exists($statePath)) { exit 1 }

  $list = Get-Content $statePath -Raw | ConvertFrom-Json
  foreach ($e in @($list)) {
    $r = ('' + $e.registry).ToLowerInvariant() -replace '^[a-z]+://', '' -replace '/.*$', ''
    if ($r -eq $target -or $target.EndsWith($r)) {
      $blob = [Convert]::FromBase64String($e.encryptedPassword)
      $plain = $null

      # PlatformID existe sur .NET Framework (PowerShell 5.1) comme sur .NET
      # Core/5+ (PowerShell 7) : seule alternative portable a $IsWindows.
      if ([Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT) {
        Add-Type -AssemblyName System.Security | Out-Null
        $plain = [Text.Encoding]::UTF8.GetString(
                   [Security.Cryptography.ProtectedData]::Unprotect(
                     $blob, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser))
      } else {
        # AES-GCM, format de `protect` : base64(nonce | cipher | tag)
        # avec nonce 12 octets et tag 16 octets.
        $keyPath = $env:DIPLO_REGISTRY_KEY_FILE
        if (-not $keyPath) { $keyPath = [IO.Path]::GetFullPath([IO.Path]::Combine($base, '..', 'registry-key.bin')) }
        $key = [IO.File]::ReadAllBytes($keyPath)
        $nonce = [byte[]]($blob[0..11])
        $tag = [byte[]]($blob[($blob.Length - 16)..($blob.Length - 1)])
        $cipher = [byte[]]($blob[12..($blob.Length - 17)])
        $out = [byte[]]::new($cipher.Length)
        $aes = [Security.Cryptography.AesGcm]::new($key, 16)
        $aes.Decrypt($nonce, $cipher, $tag, $out)
        $plain = [Text.Encoding]::UTF8.GetString($out)
      }

      $obj = @{ Username = ('' + $e.username); Secret = $plain }
      [Console]::Out.Write(($obj | ConvertTo-Json -Compress))
      exit 0
    }
  }
  exit 1
} catch { exit 1 }"""

    /// Nom du lanceur référencé dans hosts.toml.
    let private launcherName =
        if OperatingSystem.IsWindows() then
            "diplo-cred-helper.cmd"
        else
            "diplo-cred-helper"

    /// Écrit (idempotent) dans `dir` une copie du script PowerShell et un
    /// lanceur de la plateforme, et retourne le chemin du lanceur.
    ///
    /// containerd exécute la valeur « auth » de hosts.toml telle quelle, sans
    /// argument : le lanceur est donc indispensable. Il se contente de
    /// transmettre les chemins du fichier d'état et de la clé de chiffrement,
    /// puis d'exécuter le script. Sous Unix il doit être exécutable (droits
    /// 0755) et PowerShell 7 (`pwsh`) doit être installé sur l'hôte.
    let writeHelperTo (dir: string) (statePath: string) (keyPath: string) : string =
        Directory.CreateDirectory dir |> ignore

        let ps1Path = Path.Combine(dir, "diplo-cred-helper.ps1")

        if not (File.Exists ps1Path) || (File.ReadAllText ps1Path) <> helperScript then
            File.WriteAllText(ps1Path, helperScript)

        // Chemin vide : le script retrouve le fichier par rapport a son propre
        // emplacement (helper partage, pose dans <racine>/cred-helper).
        let export (name: string) (value: string) =
            if String.IsNullOrEmpty value then
                ""
            elif OperatingSystem.IsWindows() then
                // `set "VAR=..."` : le chemin peut contenir des espaces.
                sprintf "set \"%s=%s\"\r\n" name value
            else
                // Apostrophes : le chemin n'est pas interprete par le shell.
                sprintf "export %s='%s'\n" name value

        let exports =
            export "DIPLO_REGISTRY_AUTH_STATE" statePath + export "DIPLO_REGISTRY_KEY_FILE" keyPath

        let launcher =
            if OperatingSystem.IsWindows() then
                sprintf
                    "@echo off\r\n%spowershell -NoProfile -ExecutionPolicy Bypass -File \"%%~dp0diplo-cred-helper.ps1\" %%*\r\n"
                    exports
            else
                sprintf
                    "#!/bin/sh\n%sexec pwsh -NoProfile -File \"$(dirname \"$0\")/diplo-cred-helper.ps1\" \"$@\"\n"
                    exports

        let launcherPath = Path.Combine(dir, launcherName)

        if not (File.Exists launcherPath) || (File.ReadAllText launcherPath) <> launcher then
            File.WriteAllText(launcherPath, launcher)

        if not (OperatingSystem.IsWindows()) then
            File.SetUnixFileMode(
                launcherPath,
                UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute
            )

        launcherPath

    /// Helper partagé, installé dans le répertoire de données : le lanceur
    /// retourné est celui à référencer depuis hosts.toml. Le script retrouve
    /// l'état et la clé par rapport à lui-même (`<racine>/cred-helper/..`), sans
    /// chemin figé dans le lanceur.
    let ensureHelper () : string = writeHelperTo (helperDir ()) "" ""

    /// Recherche un exécutable dans le PATH sans le lancer. Sous Unix, un
    /// fichier sans bit d'exécution est ignoré : le shell refuserait de
    /// l'invoquer, et PATH peut contenir des répertoires sans accès.
    let tryFindOnPath (name: string) : string option =
        let isUsable (path: string) =
            if not (File.Exists path) then
                false
            elif OperatingSystem.IsWindows() then
                true
            else
                try
                    // Le shell exige le bit d'exécution pour au moins un
                    // porteur : on suit cette règle plutöt que root uniquement.
                    let anyExecute =
                        UnixFileMode.UserExecute
                        ||| UnixFileMode.GroupExecute
                        ||| UnixFileMode.OtherExecute

                    (File.GetUnixFileMode path &&& anyExecute) <> UnixFileMode.None
                with _ ->
                    false

        match Environment.GetEnvironmentVariable "PATH" with
        | null
        | "" ->
            None
        | path ->
            path.Split(Path.PathSeparator)
            |> Seq.filter (fun dir -> not (String.IsNullOrWhiteSpace dir))
            |> Seq.map (fun dir -> Path.Combine(dir, name))
            |> Seq.tryFind isUsable

    /// Le helper de credentials est un script PowerShell : `powershell` sous
    /// Windows (toujours présent), `pwsh` ailleurs. Sans cet interpréteur, le
    /// lanceur installé par [`writeHelperTo`](writeHelperTo) echoue et
    /// containerd ne remonte qu'un refus d'authentification opaque : on vérifie
    /// donc la présence de l'exécutable avant de construire le hosts-dir.
    let ensureHelperRuntime () =
        if not (OperatingSystem.IsWindows()) then
            match tryFindOnPath "pwsh" with
            | Some _ ->
                ()
            | None ->
                raise (
                    RpcException(
                        Status(
                            StatusCode.FailedPrecondition,
                            "Les registres authentifiés exigent PowerShell 7 (pwsh) sur cette plateforme : le helper de credentials est un script PowerShell exécuté par containerd. Installez-le (dnf install powershell, apt install powershell ou snap install powershell --classic) puis réessayez."
                        )
                    )
                )

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
            ensureHelperRuntime ()

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
    /// jetable (chiffré comme `RegistryAuth.protect`) lu par un helper créé
    /// dans le même dossier.
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
                ensureHelperRuntime ()

                let username = authArg.Substring(0, sep)
                let password = authArg.Substring(sep + 1)
                let root = Path.Combine(Path.GetTempPath(), "diplo-hosts-" + Guid.NewGuid().ToString("N"))

                let hostName = serverUrl.Substring("https://".Length)
                let hostDir = Path.Combine(root, hostName)
                Directory.CreateDirectory(hostDir) |> ignore

                let stateFile = Path.Combine(root, "registry-auth.json")
                save stateFile [ { Registry = registry; Username = username; EncryptedPassword = protect password } ]

                // Helper jetable : copie du script et lanceur pointant sur
                // l'etat de ce pull (le lanceur exporte les chemins, le script
                // n'a donc pas besoin d'etre reecrit).
                let shim = writeHelperTo root stateFile (keyFile ())

                let toml =
                    sprintf "[host.\"%s\"]\ncapabilities = [\"pull\", \"resolve\"]\nauth = '%s'\n" serverUrl shim

                File.WriteAllText(Path.Combine(hostDir, "hosts.toml"), toml)
                Some root

