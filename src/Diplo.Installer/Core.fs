module Diplo.Installer.Core

open System
open System.IO
open System.IO.Compression
open System.Runtime.ExceptionServices
open System.Net.Http
open System.Runtime.InteropServices
open System.Security.Cryptography
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading.Tasks
open Diplo.Abstractions

// ─── Configuration ───────────────────────────────────────────────────────

let installDir =
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Diplo")

let containerdDir = Path.Combine(installDir, "containerd")
let logDir = Path.Combine(installDir, "logs")
let configDir = Path.Combine(installDir, "config")
let cniBinDir = Path.Combine(containerdDir, "cni", "bin")
let cniConfDir = Path.Combine(containerdDir, "cni", "conf")
let containerdRootDir = Path.Combine(containerdDir, "root")
let containerdStateDir = Path.Combine(containerdDir, "state")

let services =
    [| "Diplo.Container", "Diplo.Container Service", DiploPorts.Container
       "Diplo.Volume", "Diplo.Volume Service", DiploPorts.Volume
       "Diplo.Network", "Diplo.Network Service", DiploPorts.Network |]

// ─── Utilitaires ─────────────────────────────────────────────────────────

let runProcess (exe: string) (args: string list) : int =
    let code, stdout, stderr = ProcessExec.runWithResult exe args None None None

    if stdout.Length > 0 then
        printfn "%s" stdout

    if stderr.Length > 0 then
        eprintfn "%s" stderr

    code

let runCommandWithArgs (exe: string) (args: string list) : int = runProcess exe args

let isWindows () =
    RuntimeInformation.IsOSPlatform(OSPlatform.Windows)

let isAdministrator () =
    if not (isWindows ()) then
        false
    else
        use identity = Security.Principal.WindowsIdentity.GetCurrent()
        let principal = Security.Principal.WindowsPrincipal(identity)
        principal.IsInRole(Security.Principal.WindowsBuiltInRole.Administrator)

let ensureDirectory (path: string) =
    if not (Directory.Exists(path)) then
        Directory.CreateDirectory(path) |> ignore
        printfn "  [+] Créé: %s" path

// ─── Détection version Windows Server ────────────────────────────────────

let getWindowsServerVersion () =
    let build = Environment.OSVersion.Version.Build

    if build <= 14393 then "ltsc2016"
    elif build <= 17763 then "ltsc2019"
    elif build <= 20348 then "ltsc2022"
    else "ltsc2025"

let getWindowsServerYear () =
    let tag = getWindowsServerVersion ()

    match tag with
    | "ltsc2016" -> "2016"
    | "ltsc2019" -> "2019"
    | "ltsc2022" -> "2022"
    | "ltsc2025" -> "2025"
    | _ -> "2022"

let isWs2016 () =
    Environment.OSVersion.Version.Build <= 14393

let getMinContainerdVersion () =
    if isWs2016 () then "1.6.36" else "1.7.27"

let isLegacyContainerd () =
    let v = getMinContainerdVersion ()
    v.StartsWith("1.6.")

let getSandboxImage () =
    let tag = getWindowsServerVersion ()
    sprintf "mcr.microsoft.com/windows/nanoserver:%s" tag

// ─── Téléchargement ──────────────────────────────────────────────────────

let downloadContainerdVersion = getMinContainerdVersion ()
let cniPluginsVersion = "1.6.2"
let winCniVersion = "0.3.1"

let containerdChecksums =
    Map.ofList
        [ "1.6.36", "74EEC7B76EBFF2A68DD478413B1ED03D435E03A4DB3244F36E92C8B80AD90C71"
          "1.7.27", "2C51135531ED9EEC3D414CC40E0BF1F0203ABBE48F449BE3CF04BB47F31C7FA5" ]

let cniPluginsChecksum =
    "7D1A7FBB0C8B272801E7E64CC1CAD6939E0E7AD0F52644EE9F8801D61DAD5849"

let winCniChecksum =
    "4F36EE6905ADA238CA2A9E1BFB8A1FB2912C2D88C4B6E5AF4C41A42DB70D7D68"

let containerdArchive =
    sprintf "containerd-%s-windows-amd64.tar.gz" downloadContainerdVersion

let containerdUrl =
    sprintf
        "https://github.com/containerd/containerd/releases/download/v%s/%s"
        downloadContainerdVersion
        containerdArchive

/// HttpClient partagé (M16) : réutilisé entre les téléchargements au lieu d'un
/// client jetable par appel (qui épuise les sockets). Timeout borné.
let private downloader =
    let client = new HttpClient()
    client.Timeout <- TimeSpan.FromMinutes(10.0)
    client

let downloadFile (url: string) (dest: string) =
    task {
        let mutable lastError: exn = null
        let mutable ok = false
        let mutable attempt = 1

        // 3 tentatives au plus, avec backoff croissant : les téléchargements
        // GitHub sont sujets à des coupures réseau transitoires.
        while not ok && attempt <= 3 do
            try
                use! response = downloader.GetAsync(url)
                response.EnsureSuccessStatusCode() |> ignore
                use! stream = response.Content.ReadAsStreamAsync()
                use fileStream = File.Create(dest)
                do! stream.CopyToAsync(fileStream)
                ok <- true
                printfn "  [+] Téléchargé: %s" (Path.GetFileName(dest))
            with ex ->
                lastError <- ex

                if attempt < 3 then
                    let delaySeconds = attempt * 2

                    printfn
                        "  [!] Échec du téléchargement (tentative %d/3), nouvel essai dans %ds : %s"
                        attempt
                        delaySeconds
                        ex.Message

                    do! Task.Delay(TimeSpan.FromSeconds(float delaySeconds))

                attempt <- attempt + 1

        if not ok then
            let message = if isNull lastError then "erreur inconnue" else lastError.Message
            failwithf "Échec du téléchargement de %s après 3 tentatives : %s" url message
    }

let computeSha256 (filePath: string) =
    use sha = SHA256.Create()
    use stream = File.OpenRead(filePath)

    sha.ComputeHash(stream)
    |> Array.map (fun b -> b.ToString("x2"))
    |> String.concat ""

/// Confrontation à temps constant de deux condensats hexadécimaux, sans
/// court-circuit selon la position de la première différence.
let private fixedTimeEqualsHex (a: string) (b: string) =
    if a.Length = 0 || a.Length <> b.Length || (a.Length % 2 <> 0) then
        // Longueur (publique) différente : traiter comme non équivalents.
        false
    else
        let mutable diff = 0
        let mutable i = 0

        while i < a.Length do
            diff <- diff ||| (int (Char.ToLowerInvariant a.[i]) ^^^ int (Char.ToLowerInvariant b.[i]))
            i <- i + 1

        diff = 0

let verifyChecksum (filePath: string) (expectedSha256: string option) =
    match expectedSha256 with
    | None
    | Some null ->
        failwithf "Aucun checksum fourni pour %s — vérification d'intégrité requise" (Path.GetFileName(filePath))
    | Some expected ->
        if expected.StartsWith("todo", StringComparison.OrdinalIgnoreCase) then
            failwithf
                "Checksum placeholder non mis à jour pour %s — vérification d'intégrité requise"
                (Path.GetFileName(filePath))

        let actual = computeSha256 filePath

        if not (fixedTimeEqualsHex actual expected) then
            failwithf
                "Échec de la vérification d'intégrité de %s\n  Attendu: %s\n  Obtenu:  %s"
                (Path.GetFileName(filePath))
                expected
                actual

        printfn "  [+] SHA256 vérifié: %s" (Path.GetFileName(filePath))

let extractTarGz (archive: string) (destination: string) =
    task {
        if FileInfo(archive).Length = 0L then
            failwithf "Archive vide: %s" archive

        let tempDir =
            Path.Combine(destination, sprintf "_tmp_extract_%s" (Guid.NewGuid().ToString("N")))

        Directory.CreateDirectory(tempDir) |> ignore

        try
            let exitCode = runCommandWithArgs "tar" [ "xzf"; archive; "-C"; tempDir ]

            if exitCode <> 0 then
                failwithf "Échec de l'extraction de %s (code %d)" archive exitCode

            let tempFull = Path.GetFullPath(tempDir)
            let destFull = Path.GetFullPath(destination)

            // Contenance stricte (M16) : la cible doit être STRICTEMENT sous la
            // racine — un simple StartsWith accepterait un voisin « _tmp_extract_X2 »
            // ou un chemin « sous » la racine par coïncidence de préfixe.
            let isWithin (root: string) (candidate: string) =
                let rootWithSep =
                    Path.TrimEndingDirectorySeparator(root) + string Path.DirectorySeparatorChar

                candidate.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase)

            // Revalidation post-extraction : chaque entrée (fichier OU répertoire)
            // doit rester sous le répertoire d'extraction temporaire.
            for file in Directory.GetFiles(tempFull, "*", SearchOption.AllDirectories) do
                let fileFull = Path.GetFullPath(file)

                if not (isWithin tempFull fileFull) then
                    failwithf "Fichier extrait hors de la destination: %s" fileFull

            for dir in Directory.GetDirectories(tempFull, "*", SearchOption.AllDirectories) do
                let dirFull = Path.GetFullPath(dir)

                if not (isWithin tempFull dirFull) then
                    failwithf "Répertoire extrait hors de la destination: %s" dirFull

            // Déplacement avec revalidation de la CIBLE : un relPath qui serait
            // résolu hors de `destination` est rejeté avant tout accès fichier.
            for file in Directory.GetFiles(tempFull, "*", SearchOption.AllDirectories) do
                let fileFull = Path.GetFullPath(file)
                let relPath = fileFull.Substring(tempFull.Length).TrimStart(Path.DirectorySeparatorChar)
                let destFile = Path.GetFullPath(Path.Combine(destination, relPath))

                if not (isWithin destFull destFile) then
                    failwithf "Cible d'extraction hors de la destination: %s" destFile

                Directory.CreateDirectory(Path.GetDirectoryName(destFile)) |> ignore
                File.Move(fileFull, destFile, overwrite = true)

            for dir in
                Directory.GetDirectories(tempFull, "*", SearchOption.AllDirectories)
                |> Array.sortDescending do
                let dirFull = Path.GetFullPath(dir)
                let relPath = dirFull.Substring(tempFull.Length).TrimStart(Path.DirectorySeparatorChar)
                let destDir = Path.GetFullPath(Path.Combine(destination, relPath))

                if not (isWithin destFull destDir) then
                    failwithf "Cible d'extraction hors de la destination: %s" destDir

                if Directory.Exists(destDir) && Directory.GetFileSystemEntries(destDir).Length = 0 then
                    Directory.Delete(destDir)

            printfn "  [+] Extrait: %s" destination
        finally
            if Directory.Exists(tempDir) then
                Directory.Delete(tempDir, recursive = true)
    }

let extractZip (archive: string) (destination: string) =
    let destFull = Path.GetFullPath(destination)
    use archiveStream = File.OpenRead(archive)
    use zipArchive = new ZipArchive(archiveStream, ZipArchiveMode.Read)

    for entry in zipArchive.Entries do
        let entryPath = Path.GetFullPath(Path.Combine(destFull, entry.FullName))

        if not (entryPath.StartsWith(destFull, StringComparison.OrdinalIgnoreCase)) then
            failwithf "Zip Slip détecté — chemin non autorisé: %s" entry.FullName

    ZipFile.ExtractToDirectory(archive, destination)
    printfn "  [+] Extrait: %s" destination

// ─── Installation containerd + CNI ───────────────────────────────────────

let archiveSuffix = Guid.NewGuid().ToString("N")

let installContainerd () =
    task {
        printfn "=== Installation de containerd %s ===" downloadContainerdVersion
        ensureDirectory containerdDir
        ensureDirectory containerdRootDir
        ensureDirectory containerdStateDir

        let archivePath =
            Path.Combine(Path.GetTempPath(), sprintf "%s_%s" archiveSuffix containerdArchive)

        printfn "  [*] Téléchargement depuis GitHub..."
        do! downloadFile containerdUrl archivePath
        let expectedChecksum = containerdChecksums |> Map.tryFind downloadContainerdVersion

        try
            verifyChecksum archivePath expectedChecksum
        with ex ->
            printfn "  [!] Échec de vérification SHA256: %s" ex.Message
            File.Delete(archivePath)
            ExceptionDispatchInfo.Capture(ex).Throw()

        printfn "  [*] Extraction..."
        do! extractTarGz archivePath containerdDir

        File.Delete(archivePath)

        printfn "  [✓] containerd installé dans %s" containerdDir
    }

let downloadCniPlugins () =
    task {
        printfn "=== Installation des plugins CNI ==="
        ensureDirectory cniBinDir
        ensureDirectory cniConfDir

        printfn "  [*] Téléchargement des plugins Microsoft CNI v%s..." winCniVersion

        let winCniArchive =
            sprintf "windows-container-networking-cni-amd64-v%s.zip" winCniVersion

        let winCniUrl =
            sprintf
                "https://github.com/microsoft/windows-container-networking/releases/download/v%s/%s"
                winCniVersion
                winCniArchive

        let winCniTemp =
            Path.Combine(Path.GetTempPath(), sprintf "%s_%s" archiveSuffix winCniArchive)

        do! downloadFile winCniUrl winCniTemp

        try
            verifyChecksum winCniTemp (Some winCniChecksum)
        with ex ->
            printfn "  [!] Échec de vérification SHA256: %s" ex.Message
            File.Delete(winCniTemp)
            ExceptionDispatchInfo.Capture(ex).Throw()

        extractZip winCniTemp cniBinDir
        File.Delete(winCniTemp)

        printfn "  [*] Téléchargement des plugins CNI standards v%s..." cniPluginsVersion
        let cniArchive = sprintf "cni-plugins-windows-amd64-%s.tgz" cniPluginsVersion

        let cniUrl =
            sprintf
                "https://github.com/containernetworking/plugins/releases/download/v%s/%s"
                cniPluginsVersion
                cniArchive

        let cniTemp =
            Path.Combine(Path.GetTempPath(), sprintf "%s_%s" archiveSuffix cniArchive)

        do! downloadFile cniUrl cniTemp

        try
            verifyChecksum cniTemp (Some cniPluginsChecksum)
        with ex ->
            printfn "  [!] Échec de vérification SHA256: %s" ex.Message
            File.Delete(cniTemp)
            ExceptionDispatchInfo.Capture(ex).Throw()

        do! extractTarGz cniTemp cniBinDir

        File.Delete(cniTemp)

        printfn "  [✓] Plugins CNI installés dans %s" cniBinDir
    }

// ─── Installation des services Windows ───────────────────────────────────

let serviceDllPath (serviceName: string) =
    Path.Combine(installDir, serviceName, sprintf "%s.exe" serviceName)

let installWindowsService (serviceName: string, displayName: string, port: int) =
    task {
        printfn "=== Installation du service %s ===" serviceName

        let exePath = serviceDllPath serviceName

        if not (File.Exists(exePath)) then
            printfn "  [!] EXE non trouvé: %s" exePath
            printfn "  [!] Assurez-vous que le build a copié l'exécutable dans %s" (Path.GetDirectoryName(exePath))

            // Un service absent n'est pas un succès : l'appelant doit pouvoir
            // distinguer une installation partielle d'une réussite.
            return false
        else
            // Guillemets INTERNES obligatoires : sans eux, SCM tente de lancer
            // « C:\Program » pour un chemin contenant des espaces.
            let quotedExe = sprintf "\"%s\"" exePath

            let scArgs =
                [ "create"
                  serviceName
                  sprintf "binPath= %s" quotedExe
                  "start= auto"
                  sprintf "DisplayName= %s" displayName ]

            let exitCode = runCommandWithArgs "sc.exe" scArgs

            if exitCode = 0 then
                printfn "  [✓] Service %s créé" serviceName
                return true
            else
                printfn "  [✗] Échec de la création du service %s (code %d)" serviceName exitCode
                return false
    }

let removeWindowsService (serviceName: string) =
    task {
        printfn "=== Suppression du service %s ===" serviceName

        let stopResult = runCommandWithArgs "sc.exe" [ "stop"; serviceName ]

        if stopResult = 0 then
            printfn "  [+] Arrêt du service %s demandé" serviceName

            // Attendre l'état STOPPED : un `sc delete` immédiat échoue en 1072
            // tant que le service est RUNNING/STOP_PENDING.
            try
                use svc =
                    new System.ServiceProcess.ServiceController(serviceName)

                let mutable waited = System.TimeSpan.Zero

                while svc.Status <> System.ServiceProcess.ServiceControllerStatus.Stopped
                      && waited < System.TimeSpan.FromSeconds(30.0) do
                    do! System.Threading.Tasks.Task.Delay(500)
                    svc.Refresh()
                    waited <- waited + System.TimeSpan.FromMilliseconds(500.0)
            with ex ->
                printfn "  [!] Attente de l'arrêt du service impossible : %s" ex.Message

        let exitCode = runCommandWithArgs "sc.exe" [ "delete"; serviceName ]

        if exitCode = 0 then
            printfn "  [✓] Service %s supprimé" serviceName
            return true
        else
            printfn "  [!] Code retour %d (service peut-être déjà supprimé)" exitCode

            return (exitCode = 1072 || exitCode = 1060) // déjà supprimé / inexistant : OK
    }

// ─── Configuration containerd ─────────────────────────────────────────────

let buildContainerdConfigToml () =
    let legacy = isLegacyContainerd ()

    String.concat
        "\n"
        [ "# configuration containerd Diplo"
          "# Genere par Diplo.Installer"
          if legacy then
              "# containerd 1.6.x (LTS — Windows Server 2016)"
          else
              "# containerd 1.7.x (Windows Server 2019+)"
          ""
          "# --- Repertoires de travail ---"
          sprintf "root = \"%s\"" containerdRootDir
          sprintf "state = \"%s\"" containerdStateDir
          ""
          "# --- GRPC ---"
          "[grpc]"
          "  address = \"npipe:////./pipe/containerd-containerd\""
          "  uid = 0"
          "  gid = 0"
          ""
          "# --- Metriques ---"
          "[metrics]"
          "  address = \"127.0.0.1:1338\""
          if not legacy then
              "  grpc_histogram = false"
          ""
          if not legacy then
              "# --- Evenements ---"
              "[events]"
              "  address = \"127.0.0.1:1339\""
              "  brokers = []"
              ""
          "# --- Plugins ---"
          ""
          "# Content store"
          "[plugins.\"io.containerd.content.v1.content\"]"
          "  max_concurrent_downloads = 3"
          ""
          "# Garbage collector"
          "[plugins.\"io.containerd.gc.v1.scheduler\"]"
          "  pause_threshold = 0.02"
          "  deletion_threshold = 0"
          "  mutation_threshold = 100"
          "  schedule_delay = \"0s\""
          "  startup_delay = \"100ms\""
          "  cleanup_interval = \"10s\""
          ""
          "# Metadata store - BoltDB"
          "[plugins.\"io.containerd.metadata.v1 bolt\"]"
          "  content_sharing_policy = \"shared\""
          ""
          "# Snapshotter Windows"
          "[plugins.\"io.containerd.snapshotter.v1.windows\"]"
          "  root_path = \"\""
          ""
          "# Runtime v2"
          "[plugins.\"io.containerd.runtime.v2.task\"]"
          "  platforms = [\"windows/amd64\"]"
          "  scheduler = \"io.containerd.gc.v1.scheduler\""
          ""
          "# Diff"
          "[plugins.\"io.containerd.differ.v1.walking\"]"
          "  no_pigz = false"
          "  uncompressed_layers = false"
          ""
          if not legacy then
              "# Transfer service"
              "[plugins.\"io.containerd.transfer.v1.local\"]"
              "  max_concurrent_layer_downloads = 3"
              ""
          "# --- CRI (Container Runtime Interface) ---"
          ""
          "[plugins.\"io.containerd.grpc.v1.cri\"]"
          sprintf "  sandbox_image = \"%s\"" (getSandboxImage ())
          "  max_concurrent_downloads = 3"
          ""
          "  [plugins.\"io.containerd.grpc.v1.cri\".containerd]"
          "    default_runtime_name = \"io.containerd.runhcs.v1\""
          "    disable_snapshot_annotations = false"
          "    disable_tcp_service = true"
          "    snapshotter = \"windows\""
          "    netns_mounts_under_state_dir = false"
          ""
          "    [plugins.\"io.containerd.grpc.v1.cri\".containerd.runtimes]"
          ""
          "      [plugins.\"io.containerd.grpc.v1.cri\".containerd.runtimes.\"io.containerd.runhcs.v1\"]"
          "        runtime_type = \"io.containerd.runhcs.v1\""
          "        [plugins.\"io.containerd.grpc.v1.cri\".containerd.runtimes.\"io.containerd.runhcs.v1\".options]"
          "          PlatformSupported = false"
          "          IsolationType = \"process\""
          ""
          "      [plugins.\"io.containerd.grpc.v1.cri\".containerd.runtimes.\"runc\"]"
          "        runtime_type = \"io.containerd.runc.v2\""
          ""
          "  [plugins.\"io.containerd.grpc.v1.cri\".cni]"
          sprintf "    bin_dir = \"%s\"" cniBinDir
          sprintf "    conf_dir = \"%s\"" cniConfDir
          ""
          "  [plugins.\"io.containerd.grpc.v1.cri\".registry]"
          "    [plugins.\"io.containerd.grpc.v1.cri\".registry.mirrors]"
          "      [plugins.\"io.containerd.grpc.v1.cri\".registry.mirrors.\"docker.io\"]"
          "        endpoint = [\"https://registry-1.docker.io\"]"
          "      [plugins.\"io.containerd.grpc.v1.cri\".registry.mirrors.\"mcr.microsoft.com\"]"
          "        endpoint = [\"https://mcr.microsoft.com\"]"
          "    [plugins.\"io.containerd.grpc.v1.cri\".registry.configs]"
          "      [plugins.\"io.containerd.grpc.v1.cri\".registry.configs.\"registry-1.docker.io\".tls]"
          "        insecure_skip_verify = false"
          ""
          "  [plugins.\"io.containerd.grpc.v1.cri\".image_decryption]"
          "    key_model = \"\""
          ""
          "  [plugins.\"io.containerd.grpc.v1.cri\".streaming]"
          "    disable_http2 = false"
          "    stream_idle_timeout = \"0s\"" ]

// ─── Configuration CNI ──────────────────────────────────────────────────

let createCniConfig () =
    ensureDirectory cniConfDir
    let configPath = Path.Combine(cniConfDir, "0-containerd-nat.conf")

    if not (File.Exists(configPath)) then
        let (_, subnet, gateway) = Diplo.Abstractions.NetworkConfig.resolveAll None |> Result.defaultWith failwith

        let config =
            Diplo.Abstractions.NetworkConfig.generateCniConflistJson
                Diplo.Abstractions.NetworkConfig.defaultConfig
                subnet
                gateway

        File.WriteAllText(configPath, config)
        printfn "  [+] config CNI: %s (subnet: %s, gateway: %s)" (Path.GetFileName(configPath)) subnet gateway
    else
        printfn "  [=] config CNI existe déjà, ignoré"

// ─── Configuration services ───────────────────────────────────────────────

let buildAppSettingsJson (grpcPort: int) (pipeName: string) (isolationType: string option) =
    let serviceSettings = JsonObject()
    serviceSettings.["GrpcPort"] <- JsonValue.Create(grpcPort)
    serviceSettings.["NamedPipeName"] <- JsonValue.Create(pipeName)
    serviceSettings.["UseTcp"] <- JsonValue.Create(true)
    serviceSettings.["UseNamedPipes"] <- JsonValue.Create(true)

    let logLevel = JsonObject()
    logLevel.["Default"] <- JsonValue.Create("Information")
    logLevel.["Microsoft.Hosting.Lifetime"] <- JsonValue.Create("Information")

    let logging = JsonObject()
    logging.["LogLevel"] <- logLevel

    let root = JsonObject()
    root.["ServiceSettings"] <- serviceSettings

    match isolationType with
    | Some iso -> root.["IsolationType"] <- JsonValue.Create(iso)
    | None -> ()

    root.["Logging"] <- logging

    let options = DiploJson.defaultOptions
    root.ToJsonString(options)

let createConfigFiles () =
    printfn "=== Création de la configuration ==="
    ensureDirectory configDir

    let containerdConfigPath = Path.Combine(containerdDir, "config.toml")

    if not (File.Exists(containerdConfigPath)) then
        File.WriteAllText(containerdConfigPath, buildContainerdConfigToml ())
        printfn "  [+] config.toml (tous les plugins containerd configurés)"
    else
        printfn "  [=] config.toml existe déjà, ignoré"

    createCniConfig ()

    let tokenPath = Diplo.Abstractions.AuthToken.tokenPath ()

    if not (File.Exists(tokenPath)) then
        let token = Diplo.Abstractions.AuthToken.generateToken ()
        Diplo.Abstractions.AuthToken.saveToken token

        // saveToken n'accorde FullControl qu'à l'utilisateur COURANT (l'admin
        // qui lance l'installeur) : or les services tournent en LocalSystem.
        // Sans cette ouverture, les lectures du token échouent en AccessDenied
        // juste après l'installation.
        try
            let fileInfo = FileInfo(tokenPath)
            let acl = fileInfo.GetAccessControl()

            let grantRead (account: string) =
                try
                    acl.AddAccessRule(
                        System.Security.AccessControl.FileSystemAccessRule(
                            account,
                            System.Security.AccessControl.FileSystemRights.Read,
                            System.Security.AccessControl.AccessControlType.Allow
                        )
                    )
                with ex ->
                    printfn "  [!] Octroi de lecture à '%s' impossible : %s" account ex.Message

            grantRead "SYSTEM"
            grantRead "Administrators"
            fileInfo.SetAccessControl(acl)
        with ex ->
            printfn "  [!] Ajustement ACL du token impossible : %s" ex.Message

        printfn "  [+] auth-token.json (token gRPC généré)"
    else
        printfn "  [=] auth-token.json existe déjà, ignoré"

    for (serviceName, _, port) in services do
        let pipeName = serviceName.ToLowerInvariant().Replace(".", "-")

        let isolationType =
            if serviceName = "Diplo.Container" then
                Some "process"
            else
                None

        let settings = buildAppSettingsJson port pipeName isolationType

        let settingsPath =
            Path.Combine(configDir, sprintf "%s.appsettings.json" serviceName)

        if not (File.Exists(settingsPath)) then
            File.WriteAllText(settingsPath, settings)
            printfn "  [+] %s" (Path.GetFileName(settingsPath))
        else
            printfn "  [=] %s existe déjà, ignoré" (Path.GetFileName(settingsPath))

    let containerSettingsPath =
        Path.Combine(configDir, "Diplo.Container.appsettings.json")

    try
        let content = File.ReadAllText(containerSettingsPath)
        let doc = JsonNode.Parse(content) :?> JsonObject
        doc.["ContainerdSocket"] <- JsonValue.Create("npipe:////./pipe/containerd-containerd")
        let options = DiploJson.defaultOptions
        File.WriteAllText(containerSettingsPath, doc.ToJsonString(options))
    with ex ->
        printfn "  [!] Erreur lors de la mise à jour de %s: %s" (Path.GetFileName(containerSettingsPath)) ex.Message

    printfn "  [✓] Configuration créée dans %s" configDir

let createDirectories () =
    printfn "=== Création des répertoires ==="
    ensureDirectory installDir
    ensureDirectory containerdDir
    ensureDirectory containerdRootDir
    ensureDirectory containerdStateDir
    ensureDirectory logDir
    ensureDirectory configDir
    ensureDirectory cniBinDir
    ensureDirectory cniConfDir

    for (serviceName, _, _) in services do
        ensureDirectory (Path.Combine(installDir, serviceName))
