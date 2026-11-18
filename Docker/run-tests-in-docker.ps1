# ---------------------------------------------------------------------------
# run-tests-in-docker.ps1 – Lance les tests DiploWalker depuis le conteneur
# ---------------------------------------------------------------------------
# Usage :
#   .\run-tests-in-docker.ps1                     # tests unitaires
#   .\run-tests-in-docker.ps1 -DoTests -DoPublish # tests + publication
#   .\run-tests-in-docker.ps1 -Clean -Restore     # nettoyage + restauration
#
# Ce script est destiné à être exécuté à l'intérieur du conteneur Linux
# (Docker ou Podman). Il déléguera à pipeline.ps1 qui gère déjà la logique
# de build, de tests (avec exclusion des tests Windows) et de publication.
#
# Compatible Podman :
#   podman run --rm -v ${PWD}:/workspaces/DiploWalker diplowalker-tests
# ---------------------------------------------------------------------------

[CmdletBinding()]
param(
    [switch]$Clean,
    [switch]$Restore,
    [switch]$DoTests,
    [switch]$DoPublish,
    [switch]$NoSignTests,
    [string]$TestLogDir
)

$ErrorActionPreference = "Stop"

# --- Vérifications préliminaires -------------------------------------------

if ($env:OS -eq "Windows_NT") {
    Write-Warning "Ce script est destiné à un conteneur Linux. Exécutez pipeline.ps1 directement sur Windows."
}

if (-not (Test-Path (Join-Path $PSScriptRoot "pipeline.ps1"))) {
    Write-Error "pipeline.ps1 introuvable. Assurez-vous d'être à la racine du dépôt DiploWalker."
    exit 1
}

# --- Construction des arguments ---------------------------------------------

$psArgs = @()
if ($Clean)       { $psArgs += "-Clean" }
if ($Restore)     { $psArgs += "-Restore" }
if ($DoTests)     { $psArgs += "-DoTests" }
if ($DoPublish)   { $psArgs += "-DoPublish" }
if ($NoSignTests) { $psArgs += "-NoSignTests" }
if ($TestLogDir)  { $psArgs += "-TestLogDir"; $psArgs += $TestLogDir }

# Par défaut : lancer les tests si aucun flag n'est fourni
if (-not $Clean -and -not $Restore -and -not $DoTests -and -not $DoPublish) {
    $psArgs += "-DoTests"
    $psArgs += "-NoSignTests"  # Signature Authenticode impossible sur Linux
}

# --- Lancement --------------------------------------------------------------

Write-Host ""
Write-Host "══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host " DiploWalker – Tests depuis le conteneur Linux (Podman)" -ForegroundColor Cyan
Write-Host "══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

$scriptPath = Join-Path $PSScriptRoot "pipeline.ps1"
& $scriptPath @psArgs

exit $LASTEXITCODE
