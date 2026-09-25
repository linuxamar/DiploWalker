module DiploWalker.Installer.Core

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
open DiploWalker.Abstractions

// â”€â”€â”€ Configuration â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
    [| "DiploWalker.Container", "DiploWalker.Container Service", DiploWalkerPorts.Container
       "DiploWalker.Volume", "DiploWalker.Volume Service", DiploWalkerPorts.Volume
       "DiploWalker.Network", "DiploWalker.Network Service", DiploWalkerPorts.Network |]

// â”€â”€â”€ Utilitaires â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
        printfn "  [+] CrÃ©Ã©: %s" path

// â”€â”€â”€ DÃ©tection version Windows Server â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

// â”€â”€â”€ TÃ©lÃ©chargement â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

let downloadContainerdVersion = getMinContainerdVersion ()
let cniPluginsVersion = "1.6.2"
let winCniVersion = "0.3.1"

/// Manifeste des checksums signÃ© (RSA-4096/SHA-384) â€” vÃ©rifiÃ© une seule fois au
/// dÃ©marrage. LÃ¨ve dÃ¨s le chargement si la signature est invalide (fail-closed).
let artifactChecksums = ArtifactSigning.loadVerifiedManifest ()

let containerdArchive =
    sprintf "containerd-%s-windows-amd64.tar.gz" downloadContainerdVersion

let containerdUrl =
    sprintf
        "https://github.com/containerd/containerd/releases/download/v%s/%s"
        downloadContainerdVersion
        containerdArchive

/// HttpClient partagÃ© (M16) : rÃ©utilisÃ© entre les tÃ©lÃ©chargements au lieu d'un
/// client jetable par appel (qui Ã©puise les sockets). Timeout bornÃ©.
let private downloader =
    let client = new HttpClient()
    client.Timeout <- TimeSpan.FromMinutes(10.0)
    client

let downloadFile (url: string) (dest: string) =
    task {
        let mutable lastError: exn = null
        let mutable ok = false
        let mutable attempt = 1

        // 3 tentatives au plus, avec backoff croissant : les tÃ©lÃ©chargements
        // GitHub sont sujets Ã  des coupures rÃ©seau transitoires.
        while not ok && attempt <= 3 do
            try
                use! response = downloader.GetAsync(url)
                response.EnsureSuccessStatusCode() |> ignore
                use! stream = response.Content.ReadAsStreamAsync()
                use fileStream = File.Create(dest)
                do! stream.CopyToAsync(fileStream)
                ok <- true
                printfn "  [+] TÃ©lÃ©chargÃ©: %s" (Path.GetFileName(dest))
            with ex ->
                lastError <- ex

                if attempt < 3 then
                    let delaySeconds = attempt * 2

                    printfn
                        "  [!] Ã‰chec du tÃ©lÃ©chargement (tentative %d/3), nouvel essai dans %ds : %s"
                        attempt
                        delaySeconds
                        ex.Message

                    do! Task.Delay(TimeSpan.FromSeconds(float delaySeconds))

                attempt <- attempt + 1

        if not ok then
            let message = if isNull lastError then "erreur inconnue" else lastError.Message
            failwithf "Ã‰chec du tÃ©lÃ©chargement de %s aprÃ¨s 3 tentatives : %s" url message
    }

let computeSha256 (filePath: string) =
    use sha = SHA256.Create()
    use stream = File.OpenRead(filePath)

    sha.ComputeHash(stream)
    |> Array.map (fun b -> b.ToString("x2"))
    |> String.concat ""

/// Confrontation Ã  temps constant de deux condensats hexadÃ©cimaux, sans
/// court-circuit selon la position de la premiÃ¨re diffÃ©rence.
let private fixedTimeEqualsHex (a: string) (b: string) =
    if a.Length = 0 || a.Length <> b.Length || (a.Length % 2 <> 0) then
        // Longueur (publique) diffÃ©rente : traiter comme non Ã©quivalents.
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
        failwithf "Aucun checksum fourni pour %s â€” vÃ©rification d'intÃ©gritÃ© requise" (Path.GetFileName(filePath))
    | Some expected ->
        if expected.StartsWith("todo", StringComparison.OrdinalIgnoreCase) then
            failwithf
                "Checksum placeholder non mis Ã  jour pour %s â€” vÃ©rification d'intÃ©gritÃ© requise"
                (Path.GetFileName(filePath))

        let actual = computeSha256 filePath

        if not (fixedTimeEqualsHex actual expected) then
            failwithf
                "Ã‰chec de la vÃ©rification d'intÃ©gritÃ© de %s\n  Attendu: %s\n  Obtenu:  %s"
                (Path.GetFileName(filePath))
                expected
                actual

        printfn "  [+] SHA256 vÃ©rifiÃ©: %s" (Path.GetFileName(filePath))

let extractTarGz (archive: string) (destination: string) =
    task {
        if FileInfo(archive).Length = 0L then
            failwithf "Archive vide: %s" archive

        let tempDir =
            Path.Combine(destination, sprintf "_tmp_extract_%s" (Guid.NewGuid().ToString("N")))

        Directory.CreateDirectory(tempDir) |> ignore

        let tempFull = Path.GetFullPath(tempDir)

        // Contenance stricte (M16) : la cible doit Ãªtre STRICTEMENT sous la
        // racine â€” un simple StartsWith accepterait un voisin Â« _tmp_extract_X2 Â»
        // ou un chemin Â« sous Â» la racine par coÃ¯ncidence de prÃ©fixe.
        let isWithin (root: string) (candidate: string) =
            let rootWithSep =
                Path.TrimEndingDirectorySeparator(root) + string Path.DirectorySeparatorChar

            candidate.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase)

        try
            // PrÃ©-Ã©numÃ©ration (M11) : on liste le contenu de l'archive AVANT
            // toute extraction. Chaque entrÃ©e doit rester sous le rÃ©pertoire de
            // staging â€” un Â« .. Â», un chemin enracinÃ© ou une lettre de lecteur
            // est rejetÃ© sans rien extraire. L'entrÃ©e racine (Â« . Â») est admise.
            let listCode, listOut, _ = ProcessExec.runWithResult "tar" [ "tzf"; archive ] None None None

            if listCode <> 0 then
                failwithf "Ã‰chec de la lecture de %s (code %d)" archive listCode

            for entry in
                listOut.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries) do
                let entryFull = Path.GetFullPath(Path.Combine(tempFull, entry))

                if entryFull <> tempFull && not (isWithin tempFull entryFull) then
                    failwithf "EntrÃ©e d'archive hors de la destination: %s" entry

            let exitCode = runCommandWithArgs "tar" [ "xzf"; archive; "-C"; tempDir ]

            if exitCode <> 0 then
                failwithf "Ã‰chec de l'extraction de %s (code %d)" archive exitCode

            let destFull = Path.GetFullPath(destination)

            // Revalidation post-extraction : chaque entrÃ©e (fichier OU rÃ©pertoire)
            // doit rester sous le rÃ©pertoire d'extraction temporaire.
            for file in Directory.GetFiles(tempFull, "*", SearchOption.AllDirectories) do
                let fileFull = Path.GetFullPath(file)

                if not (isWithin tempFull fileFull) then
                    failwithf "Fichier extrait hors de la destination: %s" fileFull

            for dir in Directory.GetDirectories(tempFull, "*", SearchOption.AllDirectories) do
                let dirFull = Path.GetFullPath(dir)

                if not (isWithin tempFull dirFull) then
                    failwithf "RÃ©pertoire extrait hors de la destination: %s" dirFull

            // DÃ©placement avec revalidation de la CIBLE : un relPath qui serait
            // rÃ©solu hors de `destination` est rejetÃ© avant tout accÃ¨s fichier.
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
    let destFullWithSep =
        if destFull.EndsWith(Path.DirectorySeparatorChar) then destFull
        else destFull + string Path.DirectorySeparatorChar
    use archiveStream = File.OpenRead(archive)
    use zipArchive = new ZipArchive(archiveStream, ZipArchiveMode.Read)

    for entry in zipArchive.Entries do
        let entryPath = Path.GetFullPath(Path.Combine(destFull, entry.FullName))

        if entryPath <> destFull && not (entryPath.StartsWith(destFullWithSep, StringComparison.OrdinalIgnoreCase)) then
            failwithf "Zip Slip dÃ©tectÃ© â€” chemin non autorisÃ©: %s" entry.FullName

    ZipFile.ExtractToDirectory(archive, destination)
    printfn "  [+] Extrait: %s" destination

// â”€â”€â”€ Installation containerd + CNI â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

let archiveSuffix = Guid.NewGuid().ToString("N")

let installContainerd () =
    task {
        printfn "=== Installation de containerd %s ===" downloadContainerdVersion
        ensureDirectory containerdDir
        ensureDirectory containerdRootDir
        ensureDirectory containerdStateDir

        let archivePath =
            Path.Combine(Path.GetTempPath(), sprintf "%s_%s" archiveSuffix containerdArchive)

        printfn "  [*] TÃ©lÃ©chargement depuis GitHub..."
        do! downloadFile containerdUrl archivePath
        let expectedChecksum = ArtifactSigning.lookupChecksum artifactChecksums containerdArchive

        try
            verifyChecksum archivePath expectedChecksum
        with ex ->
            printfn "  [!] Ã‰chec de vÃ©rification SHA256: %s" ex.Message
            File.Delete(archivePath)
            ExceptionDispatchInfo.Capture(ex).Throw()

        printfn "  [*] Extraction..."
        do! extractTarGz archivePath containerdDir

        File.Delete(archivePath)

        printfn "  [âœ“] containerd installÃ© dans %s" containerdDir
    }

let downloadCniPlugins () =
    task {
        printfn "=== Installation des plugins CNI ==="
        ensureDirectory cniBinDir
        ensureDirectory cniConfDir

        printfn "  [*] TÃ©lÃ©chargement des plugins Microsoft CNI v%s..." winCniVersion

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
            verifyChecksum winCniTemp (ArtifactSigning.lookupChecksum artifactChecksums winCniArchive)
        with ex ->
            printfn "  [!] Ã‰chec de vÃ©rification SHA256: %s" ex.Message
            File.Delete(winCniTemp)
            ExceptionDispatchInfo.Capture(ex).Throw()

        extractZip winCniTemp cniBinDir
        File.Delete(winCniTemp)

        printfn "  [*] TÃ©lÃ©chargement des plugins CNI standards v%s..." cniPluginsVersion
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
            verifyChecksum cniTemp (ArtifactSigning.lookupChecksum artifactChecksums cniArchive)
        with ex ->
            printfn "  [!] Ã‰chec de vÃ©rification SHA256: %s" ex.Message
            File.Delete(cniTemp)
            ExceptionDispatchInfo.Capture(ex).Throw()

        do! extractTarGz cniTemp cniBinDir

        File.Delete(cniTemp)

        printfn "  [âœ“] Plugins CNI installÃ©s dans %s" cniBinDir
    }

// â”€â”€â”€ Installation des services Windows â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

let serviceDllPath (serviceName: string) =
    Path.Combine(installDir, serviceName, sprintf "%s.exe" serviceName)

let installWindowsService (serviceName: string, displayName: string, port: int) =
    task {
        printfn "=== Installation du service %s ===" serviceName

        let exePath = serviceDllPath serviceName

        if not (File.Exists(exePath)) then
            printfn "  [!] EXE non trouvÃ©: %s" exePath
            printfn "  [!] Assurez-vous que le build a copiÃ© l'exÃ©cutable dans %s" (Path.GetDirectoryName(exePath))

            // Un service absent n'est pas un succÃ¨s : l'appelant doit pouvoir
            // distinguer une installation partielle d'une rÃ©ussite.
            return false
        else
            // Guillemets INTERNES obligatoires : sans eux, SCM tente de lancer
            // Â« C:\Program Â» pour un chemin contenant des espaces.
            let quotedExe = sprintf "\"%s\"" exePath

            let scArgs =
                [ "create"
                  serviceName
                  sprintf "binPath= %s" quotedExe
                  "start= auto"
                  sprintf "DisplayName= %s" displayName ]

            let exitCode = runCommandWithArgs "sc.exe" scArgs

            if exitCode = 0 then
                printfn "  [âœ“] Service %s crÃ©Ã©" serviceName
                return true
            else
                printfn "  [âœ—] Ã‰chec de la crÃ©ation du service %s (code %d)" serviceName exitCode
                return false
    }

let removeWindowsService (serviceName: string) =
    task {
        printfn "=== Suppression du service %s ===" serviceName

        let stopResult = runCommandWithArgs "sc.exe" [ "stop"; serviceName ]

        if stopResult = 0 then
            printfn "  [+] ArrÃªt du service %s demandÃ©" serviceName

            // Attendre l'Ã©tat STOPPED : un `sc delete` immÃ©diat Ã©choue en 1072
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
                printfn "  [!] Attente de l'arrÃªt du service impossible : %s" ex.Message

        let exitCode = runCommandWithArgs "sc.exe" [ "delete"; serviceName ]

        if exitCode = 0 then
            printfn "  [âœ“] Service %s supprimÃ©" serviceName
            return true
        else
            printfn "  [!] Code retour %d (service peut-Ãªtre dÃ©jÃ  supprimÃ©)" exitCode

            return (exitCode = 1072 || exitCode = 1060) // dÃ©jÃ  supprimÃ© / inexistant : OK
    }

// â”€â”€â”€ Configuration containerd â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

let buildContainerdConfigToml () =
    let legacy = isLegacyContainerd ()

    String.concat
        "\n"
        [ "# configuration containerd Diplo"
          "# Genere par DiploWalker.Installer"
          if legacy then
              "# containerd 1.6.x (LTS â€” Windows Server 2016)"
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

// â”€â”€â”€ Configuration CNI â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

let createCniConfig () =
    ensureDirectory cniConfDir
    let configPath = Path.Combine(cniConfDir, "0-containerd-nat.conf")

    if not (File.Exists(configPath)) then
        let (_, subnet, gateway) = DiploWalker.Abstractions.NetworkConfig.resolveAll None |> Result.defaultWith failwith

        let config =
            DiploWalker.Abstractions.NetworkConfig.generateCniConflistJson
                DiploWalker.Abstractions.NetworkConfig.defaultConfig
                subnet
                gateway

        File.WriteAllText(configPath, config)
        printfn "  [+] config CNI: %s (subnet: %s, gateway: %s)" (Path.GetFileName(configPath)) subnet gateway
    else
        printfn "  [=] config CNI existe dÃ©jÃ , ignorÃ©"

// â”€â”€â”€ Configuration services â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    let options = DiploWalkerJson.defaultOptions
    root.ToJsonString(options)

let createConfigFiles () =
    printfn "=== CrÃ©ation de la configuration ==="
    ensureDirectory configDir

    let containerdConfigPath = Path.Combine(containerdDir, "config.toml")

    if not (File.Exists(containerdConfigPath)) then
        File.WriteAllText(containerdConfigPath, buildContainerdConfigToml ())
        printfn "  [+] config.toml (tous les plugins containerd configurÃ©s)"
    else
        printfn "  [=] config.toml existe dÃ©jÃ , ignorÃ©"

    createCniConfig ()

    let tokenPath = DiploWalker.Abstractions.AuthToken.tokenPath ()

    if not (File.Exists(tokenPath)) then
        let token = DiploWalker.Abstractions.AuthToken.generateToken ()
        DiploWalker.Abstractions.AuthToken.saveToken token

        // saveToken n'accorde FullControl qu'Ã  l'utilisateur COURANT (l'admin
        // qui lance l'installeur) : or les services tournent en LocalSystem.
        // Sans cette ouverture, les lectures du token Ã©chouent en AccessDenied
        // juste aprÃ¨s l'installation.
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
                    printfn "  [!] Octroi de lecture Ã  '%s' impossible : %s" account ex.Message

            grantRead "SYSTEM"
            grantRead "Administrators"
            fileInfo.SetAccessControl(acl)
        with ex ->
            printfn "  [!] Ajustement ACL du token impossible : %s" ex.Message

        printfn "  [+] auth-token.json (token gRPC gÃ©nÃ©rÃ©)"
    else
        printfn "  [=] auth-token.json existe dÃ©jÃ , ignorÃ©"

    for (serviceName, _, port) in services do
        let pipeName = serviceName.ToLowerInvariant().Replace(".", "-")

        let isolationType =
            if serviceName = "DiploWalker.Container" then
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
            printfn "  [=] %s existe dÃ©jÃ , ignorÃ©" (Path.GetFileName(settingsPath))

    let containerSettingsPath =
        Path.Combine(configDir, "DiploWalker.Container.appsettings.json")

    try
        let content = File.ReadAllText(containerSettingsPath)
        let doc = JsonNode.Parse(content) :?> JsonObject
        doc.["ContainerdSocket"] <- JsonValue.Create("npipe:////./pipe/containerd-containerd")
        let options = DiploWalkerJson.defaultOptions
        File.WriteAllText(containerSettingsPath, doc.ToJsonString(options))
    with ex ->
        printfn "  [!] Erreur lors de la mise Ã  jour de %s: %s" (Path.GetFileName(containerSettingsPath)) ex.Message

    printfn "  [âœ“] Configuration crÃ©Ã©e dans %s" configDir

let createDirectories () =
    printfn "=== CrÃ©ation des rÃ©pertoires ==="
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



