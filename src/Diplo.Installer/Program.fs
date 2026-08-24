module Diplo.Installer.Program

open System
open System.IO
open System.ServiceProcess
open Diplo.Installer.Core

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
        let tokenPath = Diplo.Abstractions.AuthToken.authTokenPath
        if File.Exists(tokenPath) then
            try File.Delete(tokenPath) with ex -> eprintfn "  [!] Impossible de supprimer %s : %s" tokenPath ex.Message
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
