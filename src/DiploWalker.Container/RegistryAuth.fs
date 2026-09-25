namespace DiploWalker.Container

open System
open System.IO
open System.Security.AccessControl
open System.Security.Cryptography
open System.Security.Principal
open System.Text.Json
open Serilog
open DiploWalker.Abstractions

/// Persistance des identifiants de registres de conteneurs (login/logout).
/// Le mot de passe est chiffrÃ© avec DPAPI (portÃ©e utilisateur courant) sous
/// Windows ; ailleurs, il est scellÃ© en AES-GCM avec une clÃ© par utilisateur
/// (fichier Ã  droits restreints, voir SECURITY.md).
module RegistryAuth =

    /// Identifiant d'un registre tel que persistÃ© dans le fichier d'Ã©tat.
    type RegistryEntry =
        { Registry: string
          Username: string
          EncryptedPassword: string }

    let private stateFileName = "registry-auth.json"

    let private defaultStateFile () = Path.Combine(AppPaths.dataRoot (), stateFileName)

    /// Chemin du fichier d'Ã©tat (par dÃ©faut : %ProgramData%\Diplo\registry-auth.json).
    let private stateFileRef = ref (defaultStateFile ())

    /// Remplace le chemin du fichier d'Ã©tat (utile pour les tests).
    let setStateFile (path: string) =
        lock stateFileRef (fun () -> stateFileRef := path)

    /// Chemin courant du fichier d'Ã©tat des identifiants de registres.
    let stateFile () = !stateFileRef

    /// Fichier de clÃ© AES-GCM (hors Windows), sous la racine par utilisateur
    /// (`AppPaths.userRoot`) : `%LocalAppData%\Diplo\registry-key.bin` sous
    /// Windows, `$XDG_DATA_HOME/Diplo/registry-key.bin` ailleurs.
    let keyFile () = Path.Combine(AppPaths.userRoot (), "registry-key.bin")

    /// Charge ou crÃ©e la clÃ© par utilisateur. Sur les plateformes POSIX le fichier
    /// est crÃ©Ã© avec des droits 0600 ; sous Windows il hÃ©rite du profil utilisateur.
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
            invalidArg (nameof password) "Le mot de passe ne peut pas Ãªtre null"

        if OperatingSystem.IsWindows() then
            let bytes = System.Text.Encoding.UTF8.GetBytes(password)
            Convert.ToBase64String(ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser))
        else
            // M7 : hors Windows, PAS de repli base64 en clair â€” chiffrement
            // AES-GCM scellÃ© par une clÃ© par utilisateur (fichier Ã  droits
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
                // Repli de compatibilitÃ© : anciens Ã©tats hors Windows Ã©crits en
                // base64 clair. Lecture tolÃ©rÃ©e (le stockage, lui, n'est plus en
                // clair) ; une nouvelle connexion rÃ©Ã©crit l'entrÃ©e en AES-GCM.
                try
                    System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded))
                with _ ->
                    failwith "Impossible de dÃ©chiffrer le mot de passe (donnÃ©es corrompues)"

    /// Charge les identifiants persistÃ©s (Map registre -> identifiant).
    /// Retourne un Ã©tat vide si le fichier est absent ou illisible.
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

    /// Enregistre les identifiants (Ã©criture atomique : fichier temporaire puis remplacement).
    let save (path: string) (entries: seq<RegistryEntry>) =
        let json =
            JsonSerializer.Serialize(entries |> Seq.toList, JsonSerializerOptions(WriteIndented = true))

        AtomicFile.write path json

    /// Verrou global : les handlers gRPC s'exÃ©cutent en parallÃ¨le et add/remove
    /// font une lecture-modification-Ã©criture â€” sans verrou, deux mutations
    /// concurrentes s'Ã©crasent mutuellement (perte silencieuse d'identifiants).
    let private stateLock = obj ()

    /// Ajoute ou met Ã  jour l'identifiant d'un registre (mot de passe chiffrÃ©).
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

    // â”€â”€â”€ Helper de credentials containerd â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    //
    // `ctr image pull --user user:pass` expose le mot de passe dans argv,
    // lisible par tout processus local (WMI Win32_Process, journaux d'audit).
    // containerd supporte le protocole docker-credential-helper : un hosts.toml
    // dÃ©clare Â« auth = <programme> Â», et le programme reÃ§oit l'URL du serveur
    // sur stdin puis rÃ©pond {"Username":â€¦,"Secret":â€¦}. Le secret ne transite
    // plus par argv â€” il reste en mÃ©moire du helper uniquement.

    /// RÃ©pertoire du helper : chemin SANS espace (C:\ProgramData\Diplo\...),
    /// requis car containerd exÃ©cute la valeur Â« auth Â» telle quelle.
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

    /// Nom du lanceur rÃ©fÃ©rencÃ© dans hosts.toml.
    let private launcherName =
        if OperatingSystem.IsWindows() then
            "diplo-cred-helper.cmd"
        else
            "diplo-cred-helper"

    /// Ã‰crit (idempotent) dans `dir` une copie du script PowerShell et un
    /// lanceur de la plateforme, et retourne le chemin du lanceur.
    ///
    /// containerd exÃ©cute la valeur Â« auth Â» de hosts.toml telle quelle, sans
    /// argument : le lanceur est donc indispensable. Il se contente de
    /// transmettre les chemins du fichier d'Ã©tat et de la clÃ© de chiffrement,
    /// puis d'exÃ©cuter le script. Sous Unix il doit Ãªtre exÃ©cutable (droits
    /// 0755) et PowerShell 7 (`pwsh`) doit Ãªtre installÃ© sur l'hÃ´te.
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

    /// Helper partagÃ©, installÃ© dans le rÃ©pertoire de donnÃ©es : le lanceur
    /// retournÃ© est celui Ã  rÃ©fÃ©rencer depuis hosts.toml. Le script retrouve
    /// l'Ã©tat et la clÃ© par rapport Ã  lui-mÃªme (`<racine>/cred-helper/..`), sans
    /// chemin figÃ© dans le lanceur.
    let ensureHelper () : string = writeHelperTo (helperDir ()) "" ""

    /// Normalise une chaÃ®ne de registre en URL de serveur utilisable comme clÃ©
    /// hosts.toml. docker.io est mappÃ© sur son endpoint canonique. Retourne ""
    /// si la chaÃ®ne n'est pas un hÃ´te exploitable (ex. bibliothÃ¨que locale).
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

    /// PrÃ©pare un rÃ©pertoire hosts-dir temporaire dÃ©lÃ©guant l'authentification
    /// au helper. Structure attendue par ctr : <racine>/<hÃ´te>/hosts.toml.
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

            // ChaÃ®ne littÃ©rale TOML (apostrophes) : les backslashes Windows
            // restent tels quels pour containerd.
            let toml =
                sprintf "[host.\"%s\"]\ncapabilities = [\"pull\", \"resolve\"]\nauth = '%s'\n" serverUrl (ensureHelper ())

            File.WriteAllText(Path.Combine(hostDir, "hosts.toml"), toml)
            Some root

    /// PrÃ©pare un hosts-dir temporaire pour des identifiants EXPLICITES
    /// (option --user, au format Â« utilisateur:secret Â») : H6 â€” les secrets ne
    /// transitent plus par argv. Les identifiants sont persistÃ©s dans un Ã©tat
    /// jetable (chiffrÃ© comme `RegistryAuth.protect`) lu par un helper crÃ©Ã©
    /// dans le mÃªme dossier.
    /// Retourne None si l'argument n'est pas au format attendu.
    let prepareHostsDirForCredentials (registry: string) (authArg: string) : string option =
        match normalizeRegistryHost registry with
        | "" ->
            None
        | serverUrl ->
            // Â« user[:password] Â» : seul le dernier Â« : Â» sÃ©pare les deux.
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

                // Helper jetable : copie du script et lanceur pointant sur
                // l'etat de ce pull (le lanceur exporte les chemins, le script
                // n'a donc pas besoin d'etre reecrit).
                let shim = writeHelperTo root stateFile (keyFile ())

                let toml =
                    sprintf "[host.\"%s\"]\ncapabilities = [\"pull\", \"resolve\"]\nauth = '%s'\n" serverUrl shim

                File.WriteAllText(Path.Combine(hostDir, "hosts.toml"), toml)
                Some root

