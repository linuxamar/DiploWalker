module DiploWalker.Installer.Program

open System
open System.IO
open System.ServiceProcess
open DiploWalker.Installer.Core

// â”€â”€â”€ Commandes CLI â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

let installAll () =
    task {
        if not (isWindows ()) then
            printfn "[ERREUR] L'installation ne fonctionne que sous Windows."
            return 1
        elif not (isAdministrator ()) then
            printfn "[ERREUR] L'installation nÃ©cessite les droits administrateur."
            printfn "  Relancez la console en tant qu'administrateur, puis rÃ©exÃ©cutez :"
            printfn "    DiploWalker.Installer.exe install"
            return 1
        else
            let osVersion = getWindowsServerVersion ()
            let osYear = getWindowsServerYear ()
            printfn "=== DÃ©tection du systÃ¨me ==="
            printfn "  Windows Server %s (build %d)" osYear Environment.OSVersion.Version.Build

            if isWs2016 () then
                printfn ""
                printfn "  [!] ATTENTION: Windows Server 2016 dÃ©tectÃ©"
                printfn "      - Containerd 1.6.x (LTS) sera installÃ©"
                printfn "      - Isolation process uniquement (pas de Hyper-V requis)"
                printfn "      - Certaines fonctionnalitÃ©s 1.7.x ne seront pas disponibles"
                printfn ""

            createDirectories ()
            do! installContainerd ()
            do! downloadCniPlugins ()
            createConfigFiles ()

            let mutable failures = 0

            for service in services do
                let! ok = installWindowsService service

                if not ok then
                    failures <- failures + 1

            printfn ""
            printfn "=== Installation terminÃ©e ==="

            if failures > 0 then
                printfn "  [!] %d service(s) NON installÃ©(s) â€” installation partielle" failures
                return 1
            else
                printfn "  SystÃ¨me: Windows Server %s (build %d)" osYear Environment.OSVersion.Version.Build
                printfn "  RÃ©pertoire: %s" installDir
                printfn "  Services: %d installÃ©s" services.Length
                printfn "  Containerd: %s (isolation process)" downloadContainerdVersion
                printfn "  Sandbox image: %s" (getSandboxImage ())
                printfn "  Plugins CNI: Microsoft v%s + Standards v%s" winCniVersion cniPluginsVersion
                printfn "  Logs: %s" logDir
                printfn "  Config: %s" configDir
                printfn ""
                printfn "  Pour dÃ©marrer les services:"

                for (name, _, _) in services do
                    printfn "    sc.exe start \"%s\"" name

                return 0
    }

let uninstallAll () =
    task {
        if not (isWindows ()) then
            printfn "[ERREUR] La dÃ©sinstallation ne fonctionne que sous Windows."
            return 1
        elif not (isAdministrator ()) then
            printfn "[ERREUR] La dÃ©sinstallation nÃ©cessite les droits administrateur."
            return 1
        else
            for (serviceName, _, _) in services do
                let! _ok = removeWindowsService serviceName
                ()

            let tokenPath = DiploWalker.Abstractions.AuthToken.tokenPath ()

            if File.Exists(tokenPath) then
                try
                    File.Delete(tokenPath)
                with ex ->
                    eprintfn "  [!] Impossible de supprimer %s : %s" tokenPath ex.Message

                printfn "  [+] auth-token.json supprimÃ©"

            let tokenDir = DiploWalker.Abstractions.AuthToken.authTokenDir

            if Directory.Exists(tokenDir) then
                try
                    if
                        Directory.GetFiles(tokenDir).Length = 0
                        && Directory.GetDirectories(tokenDir).Length = 0
                    then
                        Directory.Delete(tokenDir)
                        printfn "  [+] RÃ©pertoire %s supprimÃ©" tokenDir
                with _ ->
                    ()

            printfn ""
            printfn "=== DÃ©sinstallation des services terminÃ©e ==="
            printfn "  Les fichiers dans %s n'ont pas Ã©tÃ© supprimÃ©s." installDir
            printfn "  Supprimez manuellement si nÃ©cessaire."
            return 0
    }

let statusAll () =
    printfn "=== Statut des services Diplo ==="

    for (serviceName, displayName, port) in services do
        try
            use svc = new ServiceController(serviceName)
            let status = svc.Status
            printfn "  %-20s [%-12s] port %d" serviceName (status.ToString()) port
        with _ ->
            printfn "  %-20s [INCONNU]      port %d" serviceName port

// â”€â”€â”€ Point d'entrÃ©e â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

[<EntryPoint>]
let main argv =
    if not (isWindows ()) then
        printfn "DiploWalker.Installer est uniquement disponible sur Windows."
        1
    else
        match argv with
        | [| "install" |] ->
            // Propager le code retour : un dÃ©ploiement partiellement ratÃ© ne
            // doit pas Ãªtre indistinguable d'un succÃ¨s pour l'outillage.
            installAll () |> Async.AwaitTask |> Async.RunSynchronously
        | [| "uninstall" |] ->
            uninstallAll () |> Async.AwaitTask |> Async.RunSynchronously
        | [| "status" |] ->
            statusAll ()
            0
        | _ ->
            printfn "DiploWalker.Installer â€” Installation des services Windows Diplo"
            printfn ""
            printfn "Usage:"
            printfn "  DiploWalker.Installer.exe install      Installer containerd + plugins CNI + services"
            printfn "  DiploWalker.Installer.exe uninstall    Supprimer les services"
            printfn "  DiploWalker.Installer.exe status       Afficher l'Ã©tat des services"
            printfn ""
            printfn "PrÃ©requis:"
            printfn "  - Windows Server (2016 ou plus rÃ©cent)"
            printfn "  - .NET 10 Runtime"
            printfn "  - Droits administrateur"
            printfn "  - PAS de virtualisation requise (isolation process uniquement)"
            printfn ""
            printfn "FonctionnalitÃ©s:"
            printfn "  - TÃ©lÃ©charge et installe containerd %s" downloadContainerdVersion
            printfn "  - Installe les plugins CNI Microsoft v%s (nat, overlay, l2bridge)" winCniVersion
            printfn "  - Installe les plugins CNI standards v%s (bridge, host-local, portmap)" cniPluginsVersion
            printfn "  - Configure containerd avec tous les plugins (isolation process, pas de Hyper-V)"
            printfn "  - CrÃ©e la config CNI par dÃ©faut (rÃ©seau nat 172.20.0.0/16)"
            printfn "  - CrÃ©e les services Windows DiploWalker.Container, DiploWalker.Volume, DiploWalker.Network"
            printfn "  - Configure les logs, la config et les rÃ©pertoires"
            0


