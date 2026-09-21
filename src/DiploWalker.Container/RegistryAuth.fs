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

    let private defaultStateFile () =
        let baseDir =
            let programData =
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)

            Path.Combine(programData, "Diplo")

        Path.Combine(baseDir, stateFileName)

    /// Chemin du fichier d'Ã©tat (par dÃ©faut : %ProgramData%\Diplo\registry-auth.json).
    let private stateFileRef = ref (defaultStateFile ())

    /// Remplace le chemin du fichier d'Ã©tat (utile pour les tests).
    let setStateFile (path: string) =
        lock stateFileRef (fun () -> stateFileRef := path)

    /// Chemin courant du fichier d'Ã©tat des identifiants de registres.
    let stateFile () = !stateFileRef

    /// Fichier de clÃ© AES-GCM (hors Windows) : %LocalAppData%\Diplo\registry-key.bin.
    let private keyFile () =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Diplo",
            "registry-key.bin"
        )

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
      $obj = @{ Username = ('' + $e.username); Secret = $plain }
      [Console]::Out.Write(($obj | ConvertTo-Json -Compress))
      exit 0
    }
  }
  exit 1
} catch { exit 1 }"""

    /// Texte du script PowerShell du helper. ExposÃ© en lecture pour les tests :
    /// ceux-ci en instancient une copie avec le chemin du fichier d'Ã©tat
    /// redirigÃ© vers un rÃ©pertoire temporaire, sans toucher au vrai
    /// %ProgramData%\Diplo\registry-auth.json.
    let helperScriptText () : string = helperScript

    /// Ã‰crit (idempotent) le shim .cmd + le script PowerShell du helper et
    /// retourne le chemin du shim Ã  rÃ©fÃ©rencer depuis hosts.toml.
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
    /// jetable (DPAPI sous Windows) lu par un helper crÃ©Ã© dans le mÃªme dossier.
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

                // Copie du script helper partagÃ© avec le fichier d'Ã©tat pointÃ©
                // vers l'Ã©tat jetable (chaÃ®ne littÃ©rale PS entre apostrophes :
                // les backslashes n'y sont pas Ã©chappÃ©s).
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

