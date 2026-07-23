module Diplo.Installer.Program

open System
open System.Diagnostics
open System.IO
open System.IO.Compression
open System.Net.Http
open System.Runtime.InteropServices
open System.Security.Cryptography
open System.ServiceProcess
open System.Text.Json
open System.Text.Json.Nodes

// ─── Configuration ───────────────────────────────────────────────────────

let installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Diplo")
let containerdDir = Path.Combine(installDir, "containerd")
let logDir = Path.Combine(installDir, "logs")
let configDir = Path.Combine(installDir, "config")
let cniBinDir = Path.Combine(containerdDir, "cni", "bin")
let cniConfDir = Path.Combine(containerdDir, "cni", "conf")
let containerdRootDir = Path.Combine(containerdDir, "root")
let containerdStateDir = Path.Combine(containerdDir, "state")

let services =
    [|
        "Diplo.Container", "Diplo.Container Service", 5001
        "Diplo.Volume", "Diplo.Volume Service", 5002
        "Diplo.Network", "Diplo.Network Service", 5003
    |]

// ─── Utilitaires ─────────────────────────────────────────────────────────

let runCommand (exe: string) (args: string) : int =
    let psi = ProcessStartInfo(exe)
    psi.UseShellExecute <- false
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.CreateNoWindow <- true
    // Séparer les arguments et utiliser ArgumentList pour éviter l'injection de commande
    let parts = args.Split([| ' ' |], System.StringSplitOptions.RemoveEmptyEntries)
    for part in parts do
        let cleaned = part.Trim('"')
        psi.ArgumentList.Add(cleaned) |> ignore
    use proc = Process.Start(psi)
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    if not (proc.WaitForExit(60_000)) then
        try proc.Kill(true) with _ -> ()
        failwithf "Délai d'attente dépassé pour %s (60s)" exe
    if stdout.Length > 0 then printfn "%s" stdout
    if stderr.Length > 0 then eprintfn "%s" stderr
    proc.ExitCode

let runCommandWithArgs (exe: string) (args: string list) : int =
    let psi = ProcessStartInfo(exe)
    psi.UseShellExecute <- false
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.CreateNoWindow <- true
    for arg in args do
        psi.ArgumentList.Add(arg) |> ignore
    use proc = Process.Start(psi)
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    if not (proc.WaitForExit(60_000)) then
        try proc.Kill(true) with _ -> ()
        failwithf "Délai d'attente dépassé pour %s (60s)" exe
    if stdout.Length > 0 then printfn "%s" stdout
    if stderr.Length > 0 then eprintfn "%s" stderr
    proc.ExitCode

let isWindows () =
    RuntimeInformation.IsOSPlatform(OSPlatform.Windows)

let isAdministrator () =
    if not (isWindows()) then false
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
    if isWs2016 () then "1.6.36" // LTS pour WS2016
    else "1.7.27"

let isLegacyContainerd () =
    let v = getMinContainerdVersion ()
    v.StartsWith("1.6.")

let getSandboxImage () =
    let tag = getWindowsServerVersion ()
    // Le sandbox image utilise Nano Server — léger (~175 Mo) et suffisant pour le namespace HCS
    // La sandbox n'a pas besoin de services Windows ni du planificateur de tâches
    sprintf "mcr.microsoft.com/windows/nanoserver:%s" tag

// ─── Téléchargement ──────────────────────────────────────────────────────

let downloadContainerdVersion = getMinContainerdVersion ()
let cniPluginsVersion = "1.6.2"       // containernetworking/plugins
let winCniVersion = "0.3.1"           // microsoft/windows-container-networking

// Checksums SHA256 connus — à mettre à jour lors des changements de version
let containerdChecksums = Map.ofList [
    "1.6.36", "74EEC7B76EBFF2A68DD478413B1ED03D435E03A4DB3244F36E92C8B80AD90C71"
    "1.7.27", "2C51135531ED9EEC3D414CC40E0BF1F0203ABBE48F449BE3CF04BB47F31C7FA5"
]
let cniPluginsChecksum = "7D1A7FBB0C8B272801E7E64CC1CAD6939E0E7AD0F52644EE9F8801D61DAD5849"
let winCniChecksum = "4F36EE6905ADA238CA2A9E1BFB8A1FB2912C2D88C4B6E5AF4C41A42DB70D7D68"

let containerdArchive = sprintf "containerd-%s-windows-amd64.tar.gz" downloadContainerdVersion
let containerdUrl = sprintf "https://github.com/containerd/containerd/releases/download/v%s/%s" downloadContainerdVersion containerdArchive

let downloadFile (url: string) (dest: string) = task {
    use client = new HttpClient()
    client.Timeout <- TimeSpan.FromMinutes(10.0)
    let! response = client.GetAsync(url)
    response.EnsureSuccessStatusCode() |> ignore
    use! stream = response.Content.ReadAsStreamAsync()
    use fileStream = File.Create(dest)
    do! stream.CopyToAsync(fileStream)
    printfn "  [+] Téléchargé: %s" (Path.GetFileName(dest))
}

/// Calcule le hash SHA256 d'un fichier.
let computeSha256 (filePath: string) =
    use sha = SHA256.Create()
    use stream = File.OpenRead(filePath)
    sha.ComputeHash(stream)
    |> Array.map (fun b -> b.ToString("x2"))
    |> String.concat ""

/// Vérifie le hash SHA256 d'un fichier téléchargé. Lève une exception si non concordant ou manquant.
let verifyChecksum (filePath: string) (expectedSha256: string option) =
    match expectedSha256 with
    | None | Some null ->
        failwithf "Aucun checksum fourni pour %s — vérification d'intégrité requise" (Path.GetFileName(filePath))
    | Some expected ->
        if expected.StartsWith("todo", StringComparison.OrdinalIgnoreCase) then
            failwithf "Checksum placeholder non mis à jour pour %s — vérification d'intégrité requise" (Path.GetFileName(filePath))
        let actual = computeSha256 filePath
        if actual <> expected then
            failwithf "Échec de la vérification d'intégrité de %s\n  Attendu: %s\n  Obtenu:  %s" (Path.GetFileName(filePath)) expected actual
        printfn "  [+] SHA256 vérifié: %s" (Path.GetFileName(filePath))

let extractTarGz (archive: string) (destination: string) = task {
    // Vérifier que l'archive n'est pas vide
    if FileInfo(archive).Length = 0L then
        failwithf "Archive vide: %s" archive
    // Extraire dans un sous-dossier temporaire pour validation préalable
    let tempDir = Path.Combine(destination, sprintf "_tmp_extract_%s" (Guid.NewGuid().ToString("N")))
    Directory.CreateDirectory(tempDir) |> ignore
    try
        let exitCode = runCommand "tar" (sprintf "xzf \"%s\" -C \"%s\"" archive tempDir)
        if exitCode <> 0 then
            failwithf "Échec de l'extraction de %s (code %d)" archive exitCode
        // Valider que tous les fichiers restent dans la destination AVANT déplacement
        let tempFull = Path.GetFullPath(tempDir)
        for file in Directory.GetFiles(tempFull, "*", SearchOption.AllDirectories) do
            let fileFull = Path.GetFullPath(file)
            if not (fileFull.StartsWith(tempFull, StringComparison.OrdinalIgnoreCase)) then
                failwithf "Fichier extrait hors de la destination: %s" fileFull
        // Déplacer les fichiers validés vers la destination finale
        for file in Directory.GetFiles(tempFull, "*", SearchOption.AllDirectories) do
            let relPath = file.Substring(tempFull.Length).TrimStart(Path.DirectorySeparatorChar)
            let destFile = Path.Combine(destination, relPath)
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)) |> ignore
            File.Move(file, destFile, overwrite = true)
        for dir in Directory.GetDirectories(tempFull, "*", SearchOption.AllDirectories) |> Array.sortDescending do
            let relPath = dir.Substring(tempFull.Length).TrimStart(Path.DirectorySeparatorChar)
            let destDir = Path.Combine(destination, relPath)
            if Directory.Exists(destDir) && Directory.GetFileSystemEntries(destDir).Length = 0 then
                Directory.Delete(destDir)
        printfn "  [+] Extrait: %s" destination
    finally
        if Directory.Exists(tempDir) then
            Directory.Delete(tempDir, recursive = true)
}

let extractZip (archive: string) (destination: string) =
    // Zip Slip: vérifier chaque entrée avant extraction
    let destFull = Path.GetFullPath(destination)
    use archiveStream = File.OpenRead(archive)
    use zipArchive = new System.IO.Compression.ZipArchive(archiveStream, System.IO.Compression.ZipArchiveMode.Read)
    for entry in zipArchive.Entries do
        let entryPath = Path.GetFullPath(Path.Combine(destFull, entry.FullName))
        if not (entryPath.StartsWith(destFull, StringComparison.OrdinalIgnoreCase)) then
            failwithf "Zip Slip détecté — chemin non autorisé: %s" entry.FullName
    // Extraction après validation
    ZipFile.ExtractToDirectory(archive, destination)
    printfn "  [+] Extrait: %s" destination

// ─── Installation containerd + CNI ───────────────────────────────────────

let archiveSuffix = Guid.NewGuid().ToString("N")

let installContainerd () = task {
    printfn "=== Installation de containerd %s ===" downloadContainerdVersion
    ensureDirectory containerdDir
    ensureDirectory containerdRootDir
    ensureDirectory containerdStateDir

    let archivePath = Path.Combine(Path.GetTempPath(), sprintf "%s_%s" archiveSuffix containerdArchive)

    printfn "  [*] Téléchargement depuis GitHub..."
    do! downloadFile containerdUrl archivePath
    let expectedChecksum = containerdChecksums |> Map.tryFind downloadContainerdVersion
    verifyChecksum archivePath expectedChecksum

    printfn "  [*] Extraction..."
    do! extractTarGz archivePath containerdDir

    File.Delete(archivePath)

    printfn "  [✓] containerd installé dans %s" containerdDir
}

let downloadCniPlugins () = task {
    printfn "=== Installation des plugins CNI ==="
    ensureDirectory cniBinDir
    ensureDirectory cniConfDir

    // Plugins Microsoft Windows Container Networking (nat, overlay, l2bridge, etc.)
    printfn "  [*] Téléchargement des plugins Microsoft CNI v%s..." winCniVersion
    let winCniArchive = sprintf "windows-container-networking-cni-amd64-v%s.zip" winCniVersion
    let winCniUrl = sprintf "https://github.com/microsoft/windows-container-networking/releases/download/v%s/%s" winCniVersion winCniArchive
    let winCniTemp = Path.Combine(Path.GetTempPath(), sprintf "%s_%s" archiveSuffix winCniArchive)
    do! downloadFile winCniUrl winCniTemp
    verifyChecksum winCniTemp (Some winCniChecksum)
    extractZip winCniTemp cniBinDir
    File.Delete(winCniTemp)

    // Plugins CNI standards (bridge, host-local, portmap, etc.)
    printfn "  [*] Téléchargement des plugins CNI standards v%s..." cniPluginsVersion
    let cniArchive = sprintf "cni-plugins-windows-amd64-%s.tgz" cniPluginsVersion
    let cniUrl = sprintf "https://github.com/containernetworking/plugins/releases/download/v%s/%s" cniPluginsVersion cniArchive
    let cniTemp = Path.Combine(Path.GetTempPath(), sprintf "%s_%s" archiveSuffix cniArchive)
    do! downloadFile cniUrl cniTemp
    verifyChecksum cniTemp (Some cniPluginsChecksum)
    do! extractTarGz cniTemp cniBinDir

    File.Delete(cniTemp)

    printfn "  [✓] Plugins CNI installés dans %s" cniBinDir
}

// ─── Installation des services Windows ───────────────────────────────────
// Note: sc.exe create/start/stop fonctionne sur Server Core et Nano Server.
// Le planificateur de tâches (schtasks) n'est PAS disponible sur Nano Server —
// si des automatisations via le planificateur sont nécessaires, utiliser Server Core.

let serviceDllPath (serviceName: string) =
    Path.Combine(installDir, serviceName, sprintf "%s.exe" serviceName)

let installWindowsService (serviceName: string, displayName: string, port: int) = task {
    printfn "=== Installation du service %s ===" serviceName

    let exePath = serviceDllPath serviceName
    if not (File.Exists(exePath)) then
        printfn "  [!] EXE non trouvé: %s" exePath
        printfn "  [!] Assurez-vous que le build a copié l'exécutable dans %s" (Path.GetDirectoryName(exePath))
    else
        let scArgs = [ "create"; serviceName; sprintf "binPath= %s" exePath; "start= auto"; sprintf "DisplayName= %s" displayName ]
        let exitCode = runCommandWithArgs "sc.exe" scArgs
        if exitCode = 0 then
            printfn "  [✓] Service %s créé" serviceName
        else
            printfn "  [✗] Échec de la création du service %s (code %d)" serviceName exitCode
}

let removeWindowsService (serviceName: string) = task {
    printfn "=== Suppression du service %s ===" serviceName
    let exitCode = runCommandWithArgs "sc.exe" [ "stop"; serviceName ]
    if exitCode = 0 then
        printfn "  [+] Service %s arrêté" serviceName
    let exitCode = runCommandWithArgs "sc.exe" [ "delete"; serviceName ]
    if exitCode = 0 then
        printfn "  [✓] Service %s supprimé" serviceName
    else
        printfn "  [!] Code retour %d (service peut-être déjà supprimé)" exitCode
}

// ─── Configuration containerd ─────────────────────────────────────────────

let buildContainerdConfigToml () =
    let legacy = isLegacyContainerd ()
    let sb = System.Text.StringBuilder()
    let line (s: string) = sb.AppendLine(s) |> ignore
    line "# configuration containerd Diplo"
    line "# Genere par Diplo.Installer"
    if legacy then
        line "# containerd 1.6.x (LTS — Windows Server 2016)"
    else
        line "# containerd 1.7.x (Windows Server 2019+)"
    line ""
    line "# --- Repertoires de travail ---"
    line (sprintf "root = \"%s\"" containerdRootDir)
    line (sprintf "state = \"%s\"" containerdStateDir)
    line ""
    line "# --- GRPC ---"
    line "[grpc]"
    line "  address = \"npipe:////./pipe/containerd-containerd\""
    line "  uid = 0"
    line "  gid = 0"
    line ""
    line "# --- Metriques ---"
    line "[metrics]"
    line "  address = \"127.0.0.1:1338\""
    if not legacy then
        line "  grpc_histogram = false"
    line ""
    if not legacy then
        line "# --- Evenements ---"
        line "[events]"
        line "  address = \"127.0.0.1:1339\""
        line "  brokers = []"
        line ""
    line "# --- Plugins ---"
    line ""
    line "# Content store"
    line "[plugins.\"io.containerd.content.v1.content\"]"
    line "  max_concurrent_downloads = 3"
    line ""
    line "# Garbage collector"
    line "[plugins.\"io.containerd.gc.v1.scheduler\"]"
    line "  pause_threshold = 0.02"
    line "  deletion_threshold = 0"
    line "  mutation_threshold = 100"
    line "  schedule_delay = \"0s\""
    line "  startup_delay = \"100ms\""
    line "  cleanup_interval = \"10s\""
    line ""
    line "# Metadata store - BoltDB"
    line "[plugins.\"io.containerd.metadata.v1 bolt\"]"
    line "  content_sharing_policy = \"shared\""
    line ""
    line "# Snapshotter Windows"
    line "[plugins.\"io.containerd.snapshotter.v1.windows\"]"
    line "  root_path = \"\""
    line ""
    line "# Runtime v2"
    line "[plugins.\"io.containerd.runtime.v2.task\"]"
    line "  platforms = [\"windows/amd64\"]"
    line "  scheduler = \"io.containerd.gc.v1.scheduler\""
    line ""
    line "# Diff"
    line "[plugins.\"io.containerd.differ.v1.walking\"]"
    line "  no_pigz = false"
    line "  uncompressed_layers = false"
    line ""
    if not legacy then
        line "# Transfer service"
        line "[plugins.\"io.containerd.transfer.v1.local\"]"
        line "  max_concurrent_layer_downloads = 3"
        line ""
    line "# --- CRI (Container Runtime Interface) ---"
    line ""
    line "[plugins.\"io.containerd.grpc.v1.cri\"]"
    line (sprintf "  sandbox_image = \"%s\"" (getSandboxImage ()))
    line "  max_concurrent_downloads = 3"
    line ""
    line "  [plugins.\"io.containerd.grpc.v1.cri\".containerd]"
    line "    default_runtime_name = \"io.containerd.runhcs.v1\""
    line "    disable_snapshot_annotations = false"
    line "    disable_tcp_service = true"
    line "    snapshotter = \"windows\""
    line "    netns_mounts_under_state_dir = false"
    line ""
    line "    [plugins.\"io.containerd.grpc.v1.cri\".containerd.runtimes]"
    line ""
    line "      [plugins.\"io.containerd.grpc.v1.cri\".containerd.runtimes.\"io.containerd.runhcs.v1\"]"
    line "        runtime_type = \"io.containerd.runhcs.v1\""
    line "        [plugins.\"io.containerd.grpc.v1.cri\".containerd.runtimes.\"io.containerd.runhcs.v1\".options]"
    line "          PlatformSupported = false"
    line "          IsolationType = \"process\""
    line ""
    line "      [plugins.\"io.containerd.grpc.v1.cri\".containerd.runtimes.\"runc\"]"
    line "        runtime_type = \"io.containerd.runc.v2\""
    line ""
    line "  [plugins.\"io.containerd.grpc.v1.cri\".cni]"
    line (sprintf "    bin_dir = \"%s\"" cniBinDir)
    line (sprintf "    conf_dir = \"%s\"" cniConfDir)
    line ""
    line "  [plugins.\"io.containerd.grpc.v1.cri\".registry]"
    line "    [plugins.\"io.containerd.grpc.v1.cri\".registry.mirrors]"
    line "      [plugins.\"io.containerd.grpc.v1.cri\".registry.mirrors.\"docker.io\"]"
    line "        endpoint = [\"https://registry-1.docker.io\"]"
    line "      [plugins.\"io.containerd.grpc.v1.cri\".registry.mirrors.\"mcr.microsoft.com\"]"
    line "        endpoint = [\"https://mcr.microsoft.com\"]"
    line "    [plugins.\"io.containerd.grpc.v1.cri\".registry.configs]"
    line "      [plugins.\"io.containerd.grpc.v1.cri\".registry.configs.\"registry-1.docker.io\".tls]"
    line "        insecure_skip_verify = false"
    line ""
    line "  [plugins.\"io.containerd.grpc.v1.cri\".image_decryption]"
    line "    key_model = \"\""
    line ""
    line "  [plugins.\"io.containerd.grpc.v1.cri\".streaming]"
    line "    disable_http2 = false"
    line "    stream_idle_timeout = \"0s\""
    sb.ToString()

// ─── Configuration CNI ──────────────────────────────────────────────────

let createCniConfig () =
    ensureDirectory cniConfDir
    let configPath = Path.Combine(cniConfDir, "0-containerd-nat.conf")
    if not (File.Exists(configPath)) then
        let (_, subnet, gateway) = Diplo.Abstractions.NetworkConfig.resolveAll None
        let config = Diplo.Abstractions.NetworkConfig.generateCniConflistJson
                        Diplo.Abstractions.NetworkConfig.defaultConfig subnet gateway
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

    let options = JsonSerializerOptions(WriteIndented = true)
    root.ToJsonString(options)

let createConfigFiles () =
    printfn "=== Création de la configuration ==="
    ensureDirectory configDir

    // config.toml containerd — plugins complets + isolation process
    let containerdConfigPath = Path.Combine(containerdDir, "config.toml")
    if not (File.Exists(containerdConfigPath)) then
        File.WriteAllText(containerdConfigPath, buildContainerdConfigToml ())
        printfn "  [+] config.toml (tous les plugins containerd configurés)"
    else
        printfn "  [=] config.toml existe déjà, ignoré"

    // config CNI par défaut
    createCniConfig ()

    // token d'authentification gRPC
    let tokenPath = Diplo.Abstractions.AuthToken.authTokenPath
    if not (File.Exists(tokenPath)) then
        let token = Diplo.Abstractions.AuthToken.generateToken ()
        Diplo.Abstractions.AuthToken.saveToken token
        printfn "  [+] auth-token.json (token gRPC généré)"
    else
        printfn "  [=] auth-token.json existe déjà, ignoré"

    // appsettings pour chaque service
    for (serviceName, _, port) in services do
        let pipeName = serviceName.ToLowerInvariant().Replace(".", "-")
        let isolationType = if serviceName = "Diplo.Container" then Some "process" else None
        let settings = buildAppSettingsJson port pipeName isolationType

        let settingsPath = Path.Combine(configDir, sprintf "%s.appsettings.json" serviceName)
        if not (File.Exists(settingsPath)) then
            File.WriteAllText(settingsPath, settings)
            printfn "  [+] %s" (Path.GetFileName(settingsPath))
        else
            printfn "  [=] %s existe déjà, ignoré" (Path.GetFileName(settingsPath))

    // Ajouter le socket containerd au service Container
    let containerSettingsPath = Path.Combine(configDir, "Diplo.Container.appsettings.json")
    let content = File.ReadAllText(containerSettingsPath)
    let doc = JsonNode.Parse(content) :?> JsonObject
    doc.["ContainerdSocket"] <- JsonValue.Create("npipe:////./pipe/containerd-containerd")
    let options = JsonSerializerOptions(WriteIndented = true)
    File.WriteAllText(containerSettingsPath, doc.ToJsonString(options))

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

// ─── Commandes CLI ───────────────────────────────────────────────────────

let installAll () = task {
    if not (isWindows()) then
        printfn "[ERREUR] L'installation ne fonctionne que sous Windows."
    elif not (isAdministrator()) then
        printfn "[ERREUR] L'installation nécessite les droits administrateur."
        printfn "  Exécutez: Diplo.Installer.exe install --elevated"
    else
        let osVersion = getWindowsServerVersion ()
        let osYear = getWindowsServerYear ()
        printfn "=== Détection du système ==="
        printfn "  Windows Server %s (build %d)" osYear Environment.OSVersion.Version.Build
        if isWs2016 () then
            printfn ""
            printfn "  [!] ATTENTION: Windows Server 2016 détecté"
            printfn "      - Containerd 1.6.x (LTS) sera installé"
            printfn "      - Isolation process uniquement (pas de Hyper-V requis)"
            printfn "      - Certaines fonctionnalités 1.7.x ne seront pas disponibles"
            printfn ""
        createDirectories ()
        do! installContainerd ()
        do! downloadCniPlugins ()
        createConfigFiles ()
        for service in services do
            do! installWindowsService service
        printfn ""
        printfn "=== Installation terminée ==="
        printfn "  Système: Windows Server %s (build %d)" osYear Environment.OSVersion.Version.Build
        printfn "  Répertoire: %s" installDir
        printfn "  Services: %d installés" services.Length
        printfn "  Containerd: %s (isolation process)" downloadContainerdVersion
        printfn "  Sandbox image: %s" (getSandboxImage ())
        printfn "  Plugins CNI: Microsoft v%s + Standards v%s" winCniVersion cniPluginsVersion
        printfn "  Logs: %s" logDir
        printfn "  Config: %s" configDir
        printfn ""
        printfn "  Pour démarrer les services:"
        for (name, _, _) in services do
            printfn "    sc.exe start \"%s\"" name
}

let uninstallAll () = task {
    if not (isWindows()) then
        printfn "[ERREUR] La désinstallation ne fonctionne que sous Windows."
    elif not (isAdministrator()) then
        printfn "[ERREUR] La désinstallation nécessite les droits administrateur."
    else
        for (serviceName, _, _) in services do
            do! removeWindowsService serviceName
        // Supprimer le fichier d'authentification token
        let tokenPath = Diplo.Abstractions.AuthToken.authTokenPath
        if File.Exists(tokenPath) then
            try File.Delete(tokenPath) with _ -> ()
            printfn "  [+] auth-token.json supprimé"
        let tokenDir = Diplo.Abstractions.AuthToken.authTokenDir
        if Directory.Exists(tokenDir) then
            try
                if Directory.GetFiles(tokenDir).Length = 0 && Directory.GetDirectories(tokenDir).Length = 0 then
                    Directory.Delete(tokenDir)
                    printfn "  [+] Répertoire %s supprimé" tokenDir
            with _ -> ()
        printfn ""
        printfn "=== Désinstallation des services terminée ==="
        printfn "  Les fichiers dans %s n'ont pas été supprimés." installDir
        printfn "  Supprimez manuellement si nécessaire."
}

let statusAll () =
    printfn "=== Statut des services Diplo ==="
    for (serviceName, displayName, port) in services do
        try
            use svc = new ServiceController(serviceName)
            let status = svc.Status
            printfn "  %-20s [%-12s] port %d" serviceName (status.ToString()) port
        with
        | _ -> printfn "  %-20s [INCONNU]      port %d" serviceName port

// ─── Point d'entrée ─────────────────────────────────────────────────────

[<EntryPoint>]
let main argv =
    if not (isWindows()) then
        printfn "Diplo.Installer est uniquement disponible sur Windows."
        1
    else
        match argv with
        | [| "install" |] ->
            installAll () |> Async.AwaitTask |> Async.RunSynchronously
            0
        | [| "uninstall" |] ->
            uninstallAll () |> Async.AwaitTask |> Async.RunSynchronously
            0
        | [| "status" |] ->
            statusAll ()
            0
        | _ ->
            printfn "Diplo.Installer — Installation des services Windows Diplo"
            printfn ""
            printfn "Usage:"
            printfn "  Diplo.Installer.exe install      Installer containerd + plugins CNI + services"
            printfn "  Diplo.Installer.exe uninstall    Supprimer les services"
            printfn "  Diplo.Installer.exe status       Afficher l'état des services"
            printfn ""
            printfn "Prérequis:"
            printfn "  - Windows Server (2016 ou plus récent)"
            printfn "  - .NET 10 Runtime"
            printfn "  - Droits administrateur"
            printfn "  - PAS de virtualisation requise (isolation process uniquement)"
            printfn ""
            printfn "Fonctionnalités:"
            printfn "  - Télécharge et installe containerd %s" downloadContainerdVersion
            printfn "  - Installe les plugins CNI Microsoft v%s (nat, overlay, l2bridge)" winCniVersion
            printfn "  - Installe les plugins CNI standards v%s (bridge, host-local, portmap)" cniPluginsVersion
            printfn "  - Configure containerd avec tous les plugins (isolation process, pas de Hyper-V)"
            printfn "  - Crée la config CNI par défaut (réseau nat 172.20.0.0/16)"
            printfn "  - Crée les services Windows Diplo.Container, Diplo.Volume, Diplo.Network"
            printfn "  - Configure les logs, la config et les répertoires"
            0
