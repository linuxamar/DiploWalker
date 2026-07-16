module Diplo.Installer.Program

open System
open System.Diagnostics
open System.IO
open System.IO.Compression
open System.Net.Http
open System.Runtime.InteropServices
open System.ServiceProcess

// ─── Configuration ───────────────────────────────────────────────────────

let installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Diplo")
let containerdDir = Path.Combine(installDir, "containerd")
let logDir = Path.Combine(installDir, "logs")
let configDir = Path.Combine(installDir, "config")

let services =
    [|
        "Diplo.Container", "Diplo.Container Service", 5001
        "Diplo.Volume", "Diplo.Volume Service", 5002
        "Diplo.Network", "Diplo.Network Service", 5003
    |]

// ─── Utilitaires ─────────────────────────────────────────────────────────

let runCommand (exe: string) (args: string) : int =
    let psi = ProcessStartInfo(exe, args)
    psi.UseShellExecute <- false
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.CreateNoWindow <- true
    use proc = Process.Start(psi)
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    proc.WaitForExit()
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

// ─── Téléchargement containerd ───────────────────────────────────────────

let downloadContainerdVersion = "1.7.27"
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

let extractTarGz (archive: string) (destination: string) = task {
    // Sur Windows, utiliser tar.exe intégré
    let exitCode = runCommand "tar" (sprintf "xzf \"%s\" -C \"%s\"" archive destination)
    if exitCode <> 0 then
        failwithf "Échec de l'extraction de %s (code %d)" archive exitCode
    printfn "  [+] Extrait: %s" destination
}

let installContainerd () = task {
    printfn "=== Installation de containerd %s ===" downloadContainerdVersion
    ensureDirectory containerdDir

    let archivePath = Path.Combine(Path.GetTempPath(), containerdArchive)

    // Télécharger
    printfn "  [*] Téléchargement depuis GitHub..."
    do! downloadFile containerdUrl archivePath

    // Extraire
    printfn "  [*] Extraction..."
    do! extractTarGz archivePath containerdDir

    // Nettoyer l'archive
    File.Delete(archivePath)

    printfn "  [✓] containerd installé dans %s" containerdDir
}

// ─── Installation des services Windows ───────────────────────────────────

let serviceDllPath (serviceName: string) =
    Path.Combine(installDir, serviceName, sprintf "%s.exe" serviceName)

let installWindowsService (serviceName: string, displayName: string, port: int) = task {
    printfn "=== Installation du service %s ===" serviceName

    let exePath = serviceDllPath serviceName
    if not (File.Exists(exePath)) then
        printfn "  [!] EXE non trouvé: %s" exePath
        printfn "  [!] Assurez-vous que le build a copié l'exécutable dans %s" (Path.GetDirectoryName(exePath))
    else
        let scArgs = sprintf "create \"%s\" binPath= \"%s\" start= auto DisplayName= \"%s\"" serviceName exePath displayName
        let exitCode = runCommand "sc.exe" scArgs
        if exitCode = 0 then
            printfn "  [✓] Service %s créé" serviceName
        else
            printfn "  [✗] Échec de la création du service %s (code %d)" serviceName exitCode
}

let removeWindowsService (serviceName: string) = task {
    printfn "=== Suppression du service %s ===" serviceName
    let exitCode = runCommand "sc.exe" (sprintf "stop \"%s\"" serviceName)
    if exitCode = 0 then
        printfn "  [+] Service %s arrêté" serviceName
    let exitCode = runCommand "sc.exe" (sprintf "delete \"%s\"" serviceName)
    if exitCode = 0 then
        printfn "  [✓] Service %s supprimé" serviceName
    else
        printfn "  [!] Code retour %d (service peut-être déjà supprimé)" exitCode
}

// ─── Configuration ───────────────────────────────────────────────────────

let buildAppSettingsJson (grpcPort: int) (pipeName: string) =
    let nl = System.Environment.NewLine
    let sb = System.Text.StringBuilder()
    sb.AppendLine("{") |> ignore
    sb.AppendLine("  \"ServiceSettings\": {") |> ignore
    sb.AppendLine(sprintf "    \"GrpcPort\": %d," grpcPort) |> ignore
    sb.AppendLine(sprintf "    \"NamedPipeName\": \"%s\"," pipeName) |> ignore
    sb.AppendLine("    \"UseTcp\": true,") |> ignore
    sb.AppendLine("    \"UseNamedPipes\": true") |> ignore
    sb.AppendLine("  },") |> ignore
    sb.AppendLine("  \"Logging\": {") |> ignore
    sb.AppendLine("    \"LogLevel\": {") |> ignore
    sb.AppendLine("      \"Default\": \"Information\",") |> ignore
    sb.AppendLine("      \"Microsoft.Hosting.Lifetime\": \"Information\"") |> ignore
    sb.AppendLine("    }") |> ignore
    sb.AppendLine("  }") |> ignore
    sb.Append("}") |> ignore
    sb.ToString()

let createConfigFiles () =
    printfn "=== Création de la configuration ==="
    ensureDirectory configDir

    // appsettings pour chaque service
    for (serviceName, _, port) in services do
        let pipeName = serviceName.ToLowerInvariant().Replace(".", "-")
        let settings = buildAppSettingsJson port pipeName

        let settingsPath = Path.Combine(configDir, sprintf "%s.appsettings.json" serviceName)
        if not (File.Exists(settingsPath)) then
            File.WriteAllText(settingsPath, settings)
            printfn "  [+] %s" (Path.GetFileName(settingsPath))
        else
            printfn "  [=] %s existe déjà, ignoré" (Path.GetFileName(settingsPath))

    // Ajouter le socket containerd au service Container
    let containerSettingsPath = Path.Combine(configDir, "Diplo.Container.appsettings.json")
    let content = File.ReadAllText(containerSettingsPath)
    let containerSocket = "\"ContainerdSocket\": \"npipe:////./pipe/containerd-containerd\""
    let content = content.Replace("}", sprintf ", %s }" containerSocket)
    File.WriteAllText(containerSettingsPath, content)

    printfn "  [✓] Configuration créée dans %s" configDir

let createDirectories () =
    printfn "=== Création des répertoires ==="
    ensureDirectory installDir
    ensureDirectory containerdDir
    ensureDirectory logDir
    ensureDirectory configDir
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
        createDirectories ()
        do! installContainerd ()
        createConfigFiles ()
        for service in services do
            do! installWindowsService service
        printfn ""
        printfn "=== Installation terminée ==="
        printfn "  Répertoire: %s" installDir
        printfn "  Services: %d installés" services.Length
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
            printfn "  Diplo.Installer.exe install      Installer containerd + services"
            printfn "  Diplo.Installer.exe uninstall    Supprimer les services"
            printfn "  Diplo.Installer.exe status       Afficher l'état des services"
            printfn ""
            printfn "Prérequis:"
            printfn "  - Windows Server (2019 ou plus récent)"
            printfn "  - .NET 10 Runtime"
            printfn "  - Droits administrateur"
            printfn ""
            printfn "Fonctionnalités:"
            printfn "  - Télécharge et installe containerd %s" downloadContainerdVersion
            printfn "  - Crée les services Windows Diplo.Container, Diplo.Volume, Diplo.Network"
            printfn "  - Configure les logs, la config et les répertoires"
            0
