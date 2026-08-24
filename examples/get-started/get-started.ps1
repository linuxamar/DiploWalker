<#
.SYNOPSIS
    Exemple « Get Started » — Installation de Diplo et déploiement d'un conteneur.

.DESCRIPTION
    Ce script illustre les étapes fondamentales :
      1. Installation des services Diplo (release NSIS)
      2. Vérification de l'état des services
      3. Téléchargement de l'image ServerCode
      4. Création et démarrage d'un conteneur
      5. Vérification du fonctionnement

    Prérequis :
      - Windows Server 2016+ ou Windows 10/11
      - .NET 10 Runtime installé
      - Droits administrateur (pour l'installation)
      - Fichier Diplo-Setup-*-x64.exe disponible

.EXAMPLE
    .\get-started.ps1
#>

#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Configuration ────────────────────────────────────────────────────────────

$SetupExe = Join-Path $PSScriptRoot "..\..\Diplo-Setup-1.0.0-x64.exe"
$DiploDir = "$env:ProgramFiles\Diplo"
$CliExe = Join-Path $DiploDir "Diplo.Cli\Diplo.Cli.exe"
$ImageName = "ServerCode"
$ContainerName = "mon-serveur"

# ── Fonctions utilitaires ────────────────────────────────────────────────────

function Write-Step {
    param([string]$Step, [string]$Message)
    Write-Host ""
    Write-Host "═══ Étape $Step ═══" -ForegroundColor Cyan
    Write-Host "  $Message" -ForegroundColor White
}

function Write-Ok {
    param([string]$Message)
    Write-Host "  [✓] $Message" -ForegroundColor Green
}

function Write-Info {
    param([string]$Message)
    Write-Host "  [*] $Message" -ForegroundColor Yellow
}

function Write-Fail {
    param([string]$Message)
    Write-Host "  [✗] $Message" -ForegroundColor Red
}

function Invoke-Diplo {
    param([string[]]$Arguments)
    $output = & $CliExe @Arguments 2>&1
    $output | ForEach-Object { Write-Host "  $_" }
    return $LASTEXITCODE
}

# ══════════════════════════════════════════════════════════════════════════════
# ÉTAPE 1 — Vérification des prérequis
# ══════════════════════════════════════════════════════════════════════════════

Write-Step "1/5" "Vérification des prérequis"

# Vérifier les droits administrateur
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Fail "Ce script nécessite les droits administrateur."
    Write-Info "Relancez PowerShell en tant qu'administrateur."
    exit 1
}
Write-Ok "Droits administrateur confirmés"

# Vérifier .NET 10
$dotnetVersion = try { & dotnet --version 2>$null } catch { "" }
if ($dotnetVersion -match "^10\.") {
    Write-Ok ".NET $dotnetVersion détecté"
}
else {
    Write-Fail ".NET 10 non trouvé (version: $dotnetVersion)"
    Write-Info "Installez .NET 10 depuis https://dotnet.microsoft.com/download"
    exit 1
}

# Vérifier l'installeur
if (Test-Path $SetupExe) {
    Write-Ok "Installeur trouvé : $SetupExe"
}
else {
    Write-Fail "Installeur introuvable : $SetupExe"
    Write-Info "Placez le fichier Diplo-Setup-*-x64.exe dans le répertoire du projet."
    exit 1
}

# ══════════════════════════════════════════════════════════════════════════════
# ÉTAPE 2 — Installation des services Diplo
# ══════════════════════════════════════════════════════════════════════════════

Write-Step "2/5" "Installation des services Diplo"

if (Test-Path $CliExe) {
    Write-Info "Diplo déjà installé dans $DiploDir"
    Write-Info "Pour réinstaller, désinstallez d'abord via Windows Settings > Applications."
}
else {
    Write-Info "Lancement de l'installeur NSIS..."
    Write-Info "  Une fenêtre d'installation va s'ouvrir."
    Write-Info "  Suivez les instructions à l'écran."

    $process = Start-Process -FilePath $SetupExe -Wait -PassThru
    if ($process.ExitCode -eq 0) {
        Write-Ok "Installation terminée avec succès"
    }
    else {
        Write-Fail "L'installation a échoué (code : $($process.ExitCode))"
        exit 1
    }

    # Rafraîchir le PATH
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + `
        [System.Environment]::GetEnvironmentVariable("Path", "User")
}

# ══════════════════════════════════════════════════════════════════════════════
# ÉTAPE 3 — Démarrage des services
# ══════════════════════════════════════════════════════════════════════════════

Write-Step "3/5" "Démarrage des services Diplo"

$services = @("Diplo.Container", "Diplo.Volume", "Diplo.Network")

foreach ($svcName in $services) {
    $svc = Get-Service -Name $svcName -ErrorAction SilentlyContinue
    if ($null -eq $svc) {
        Write-Info "Service $svcName non trouvé — vérifiez l'installation"
        continue
    }

    if ($svc.Status -eq "Running") {
        Write-Ok "$svcName déjà en cours d'exécution"
    }
    else {
        Write-Info "Démarrage de $svcName..."
        Start-Service -Name $svcName
        Start-Sleep -Seconds 2

        $svc = Get-Service -Name $svcName
        if ($svc.Status -eq "Running") {
            Write-Ok "$svcName démarré"
        }
        else {
            Write-Fail "$svcName n'a pas démarré (état : $($svc.Status))"
        }
    }
}

# Vérification globale
Write-Info "Vérification de l'état des services..."
& $CliExe status check 2>&1 | ForEach-Object { Write-Host "  $_" }

# ══════════════════════════════════════════════════════════════════════════════
# ÉTAPE 4 — Téléchargement de l'image ServerCode
# ══════════════════════════════════════════════════════════════════════════════

Write-Step "4/5" "Téléchargement de l'image $ImageName"

Write-Info "Téléchargement de l'image depuis le registre..."
$pullCode = Invoke-Diplo @("container", "pull", $ImageName)

if ($pullCode -eq 0) {
    Write-Ok "Image $ImageName téléchargée"
}
else {
    Write-Fail "Échec du téléchargement de l'image (code : $pullCode)"
    Write-Info "Vérifiez votre connexion Internet et le nom de l'image."
    exit 1
}

# Vérifier que l'image est disponible
Write-Info "Liste des images disponibles :"
& $CliExe container image-list 2>&1 | ForEach-Object { Write-Host "  $_" }

# ══════════════════════════════════════════════════════════════════════════════
# ÉTAPE 5 — Création et démarrage du conteneur
# ══════════════════════════════════════════════════════════════════════════════

Write-Step "5/5" "Création et démarrage du conteneur"

# Vérifier si un conteneur du même nom existe déjà
$existingContainer = & $CliExe container list 2>&1 | Select-String $ContainerName
if ($existingContainer) {
    Write-Info "Un conteneur nommé '$ContainerName' existe déjà"
    Write-Info "Suppression de l'ancien conteneur..."
    & $CliExe container delete $ContainerName 2>&1 | ForEach-Object { Write-Host "  $_" }
}

Write-Info "Création du conteneur '$ContainerName' à partir de l'image '$ImageName'..."
$createCode = Invoke-Diplo @("container", "create", $ImageName, $ContainerName)

if ($createCode -ne 0) {
    Write-Fail "Échec de la création du conteneur (code : $createCode)"
    exit 1
}

Write-Info "Démarrage du conteneur..."
$startCode = Invoke-Diplo @("container", "start", $ContainerName)

if ($startCode -eq 0) {
    Write-Ok "Conteneur '$ContainerName' démarré"
}
else {
    Write-Fail "Échec du démarrage du conteneur (code : $startCode)"
    exit 1
}

# ══════════════════════════════════════════════════════════════════════════════
# RÉSUMÉ
# ══════════════════════════════════════════════════════════════════════════════

Write-Host ""
Write-Host "═══ Déploiement terminé ═══" -ForegroundColor Green
Write-Host ""
Write-Host "  Services Diplo    : en cours d'exécution" -ForegroundColor White
Write-Host "  Image             : $ImageName" -ForegroundColor White
Write-Host "  Conteneur         : $ContainerName" -ForegroundColor White
Write-Host ""
Write-Host "  Commandes utiles :" -ForegroundColor Cyan
Write-Host "    diplo container list                  # Lister les conteneurs"
Write-Host "    diplo container inspect $ContainerName  # Détails du conteneur"
Write-Host "    diplo container logs $ContainerName     # Voir les logs"
Write-Host "    diplo container exec $ContainerName cmd # Exécuter une commande"
Write-Host "    diplo container stop $ContainerName     # Arrêter le conteneur"
Write-Host "    diplo container stats $ContainerName    # Métriques en temps réel"
Write-Host ""
Write-Host "  GUI : lancez Diplo.Gui.exe pour l'interface graphique" -ForegroundColor Cyan
Write-Host ""
