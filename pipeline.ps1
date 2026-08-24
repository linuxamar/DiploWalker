# ---------------------------------------------------------------------------
#  pipeline.ps1  –  Build, tests et publication self-contained (Release)
# ---------------------------------------------------------------------------
#  Structure de sortie :
#    ./publish/WindowsServices/<platform>/<projet>/
#
#  Usage :
#    .\pipeline.ps1                  # publie x64 + x86
#    .\pipeline.ps1 -Platform x64   # publie x64 uniquement
#    .\pipeline.ps1 -Restore         # restaure les packages NuGet en 1er
#    .\pipeline.ps1 -Clean           # supprime bin/ obj/ avant publication
#    .\pipeline.ps1 -DoTests         # lance uniquement les tests
#    .\pipeline.ps1 -DoPublish       # publie uniquement (sans tests)
#    .\pipeline.ps1 -DoPublish -SignCert C:\certs\code.pfx -SignPassword "***"
#    .\pipeline.ps1 -DoPublish -SignThumbprint <SHA1-du-certificat>
# ---------------------------------------------------------------------------

[CmdletBinding()]
param(
    [ValidateSet("x64", "x86")]
    [string]$Platform,
    [switch]$Restore,
    [switch]$Clean,
    [switch]$DoTests,
    [switch]$DoPublish,
    [string]$SignCert,
    [string]$SignPassword,
    [string]$SignThumbprint
)

# -DoTests et -DoPublish peuvent être combinés :
#   -DoTests -DoPublish  → tests d'abord, puis publication si tests réussis
#   -DoTests             → tests uniquement
#   -DoPublish           → publication uniquement
#   (aucun flag)         → rien, affiche l'aide

$ErrorActionPreference = "Stop"

if (-not $DoTests -and -not $DoPublish -and -not $Clean -and -not $Restore) {
    Write-Host "Usage : .\pipeline.ps1 [-Clean] [-Restore] [-DoTests] [-DoPublish] [-Platform x64|x86]" -ForegroundColor Yellow
    Write-Host "  -Clean      supprime bin/ obj/ avant publication"
    Write-Host "  -Restore    restaure les packages NuGet en 1er"
    Write-Host "  -DoTests    lance les tests unitaires uniquement"
    Write-Host "  -DoPublish  lance la publication uniquement"
    Write-Host ""
    Write-Host "  Exemples :"
    Write-Host "    .\pipeline.ps1 -DoTests -DoPublish   # tests puis publication"
    Write-Host "    .\pipeline.ps1 -Clean -Restore -DoPublish  # nettoyage, restauration, publication"
    exit 0
}

# --- Configuration --------------------------------------------------------

$Projects = @(
    "Diplo.Container",
    "Diplo.Volume",
    "Diplo.Network",
    "Diplo.Installer",
    "Diplo.Abstractions",
    "Diplo.Contracts",
    "Diplo.Grpc",
    "Diplo.Cli",
    "Diplo.Gui"
)

$Platforms = if ($Platform) { @($Platform) } else { @("x64", "x86") }

$PublishRoot = Join-Path (Join-Path $PSScriptRoot "publish") "WindowsServices"

# --- Fonction utilitaire --------------------------------------------------

function Publish-Project {
    param(
        [string]$ProjectName,
        [string]$Plat
    )

    $projectPath = Join-Path (Join-Path (Join-Path $PSScriptRoot "src") $ProjectName) "$ProjectName.fsproj"
    $outputDir = Join-Path (Join-Path $PublishRoot $Plat) $ProjectName

    if (-not (Test-Path $projectPath)) {
        # fallback .csproj (Diplo.Grpc ou futurs projets C#)
        $projectPath = Join-Path (Join-Path (Join-Path $PSScriptRoot "src") $ProjectName) "$ProjectName.csproj"
    }

    if (-not (Test-Path $projectPath)) {
        Write-Warning "Projet introuvable : $projectPath — ignoré."
        return
    }

    $rid = "win-$Plat"

    Write-Host ""
    Write-Host "═══ $ProjectName  ($Plat) ═══" -ForegroundColor Cyan
    Write-Host "  Source  : $projectPath"
    Write-Host "  RID     : $rid"
    Write-Host "  Sortie  : $outputDir"

    dotnet publish $projectPath `
        --configuration Release `
        --output $outputDir `
        --self-contained true `
        -r $rid `
        -p:Platform=$Plat `
        -p:PublishTrimmed=false `
        -p:PublishSingleFile=false `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false

    if ($LASTEXITCODE -ne 0) {
        throw "Échec de la publication de $ProjectName ($Plat)."
    }

    Write-Host "  ✓ OK" -ForegroundColor Green
}

# --- Point d'entrée -------------------------------------------------------

$timer = [System.Diagnostics.Stopwatch]::StartNew()

# --- Calcul total étapes pour progression ----------------------------------

$runTests = $DoTests.IsPresent
$runPublish = $DoPublish.IsPresent

$testProjects = @(
    "Diplo.Abstractions.Tests",
    "Diplo.Container.Tests",
    "Diplo.Volume.Tests",
    "Diplo.Network.Tests",
    "Diplo.Core.Tests",
    "Diplo.Installer.Tests",
    "Diplo.Disk.Tests",
    "Diplo.Cli.Tests",
    "Diplo.Gui.Tests",
    "Diplo.Integration.Tests"
)

$totalSteps = 0
if ($Clean) { $totalSteps++ }
if ($Restore) { $totalSteps++ }
if ($runTests) { $totalSteps += $testProjects.Count }
if ($runPublish) { $totalSteps += $Platforms.Count * $Projects.Count }
$currentStep = 0

# --- Nettoyage bin/obj (optionnel) -----------------------------------------

if ($Clean) {
    $currentStep++
    Write-Host ""
    Write-Host "═══ Nettoyage bin/ obj/ ═══" -ForegroundColor Cyan
    Write-Progress -Id 1 -Activity "Publication Diplo" -Status "Nettoyage..." -PercentComplete (($currentStep / $totalSteps) * 100)
    $dirs = @("src", "tests")
    foreach ($dir in $dirs) {
        $base = Join-Path $PSScriptRoot $dir
        if (Test-Path $base) {
            Get-ChildItem -Path $base -Directory | ForEach-Object {
                foreach ($sub in @("bin", "obj")) {
                    $path = Join-Path $_.FullName $sub
                    if (Test-Path $path) {
                        Write-Host "  Suppression : $path"
                        Remove-Item -Path $path -Recurse -Force
                    }
                }
            }
        }
    }
    Write-Host "  ✓ Nettoyage terminé." -ForegroundColor Green
}

# --- Restauration NuGet (optionnel) ----------------------------------------

if ($Restore) {
    $currentStep++
    Write-Host ""
    Write-Host "═══ Restauration NuGet ═══" -ForegroundColor Cyan
    Write-Progress -Id 1 -Activity "Publication Diplo" -Status "Restauration NuGet..." -PercentComplete (($currentStep / $totalSteps) * 100)
    $solutionPath = Join-Path $PSScriptRoot "Diplo.slnx"
    if (-not (Test-Path $solutionPath)) {
        $solutionPath = Join-Path $PSScriptRoot "Diplo.sln"
    }
    dotnet restore $solutionPath
    if ($LASTEXITCODE -ne 0) {
        throw "Échec de la restauration NuGet."
    }
    Write-Host "  ✓ Restauration terminée." -ForegroundColor Green
}

# --- Tests unitaires (avant publication) -----------------------------------

if ($runTests) {
    Write-Host "═══ Tests unitaires ═══" -ForegroundColor Cyan

    $allPassed = $true
    foreach ($test in $testProjects) {
        $currentStep++
        $testPath = Join-Path (Join-Path (Join-Path $PSScriptRoot "tests") $test) "$test.fsproj"
        if (-not (Test-Path $testPath)) {
            Write-Warning "Projet de test introuvable : $testPath — ignoré."
            continue
        }

        Write-Host ""
        Write-Host "  ▸ $test" -ForegroundColor Yellow
        Write-Progress -Id 1 -Activity "Publication Diplo" -Status "Tests : $test ($currentStep/$totalSteps)" -PercentComplete (($currentStep / $totalSteps) * 100)
        dotnet test --project $testPath --configuration Release --no-restore
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  ✗ Échec des tests : $test" -ForegroundColor Red
            $allPassed = $false
        }
        else {
            # Un projet non restauré peut sortir en code 0 sans exécuter les tests :
            # on vérifie que la DLL de test a réellement été produite.
            $testDll = Get-ChildItem -Path (Join-Path $PSScriptRoot "tests\$test\bin") -Filter "$test.dll" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($null -eq $testDll) {
                Write-Host "  ✗ Aucune DLL de test produite : $test — exécutez -Restore avant -DoTests" -ForegroundColor Red
                $allPassed = $false
            }
            else {
                Write-Host "  ✓ OK" -ForegroundColor Green
            }
        }
    }

    if (-not $allPassed) {
        throw "Des tests ont échoué — publication annulée."
    }

    Write-Host ""
    Write-Host "  Tous les tests sont passés." -ForegroundColor Green
}

# --- Publication ------------------------------------------------------------

if ($runPublish) {

    foreach ($plat in $Platforms) {
        foreach ($project in $Projects) {
            $currentStep++
            Write-Progress -Id 1 -Activity "Publication Diplo" -Status "Publication : $project $plat ($currentStep/$totalSteps)" -PercentComplete (($currentStep / $totalSteps) * 100)
            Publish-Project -ProjectName $project -Plat $plat
        }
    }

}

# --- Package NSIS (après publication) ----------------------------------------

if ($runPublish) {
    foreach ($plat in $Platforms) {
        $setupRoot = Join-Path $PSScriptRoot "setup"
        $setupScript = Join-Path $setupRoot "build.ps1"
        if (Test-Path $setupScript) {
            Write-Host ""
            Write-Host "═══ Package NSIS ($plat) ═══" -ForegroundColor Cyan
            $appVersion = (Select-Xml -Path (Join-Path $PSScriptRoot "Directory.Build.props") -XPath "//Version").Node.InnerText
            $setupArgs = @{ Version = $appVersion; Platform = $plat; Sign = $true }
            if ($SignCert) { $setupArgs.SignCert = $SignCert }
            if ($SignPassword) { $setupArgs.SignPassword = $SignPassword }
            if ($SignThumbprint) { $setupArgs.SignThumbprint = $SignThumbprint }
            & $setupScript @setupArgs
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "Le packaging NSIS a échoué pour $plat."
            }
            else {
                Write-Host "  ✓ Package NSIS ($plat) créé." -ForegroundColor Green
            }
        }
    }
}

$timer.Stop()

Write-Progress -Id 1 -Activity "Publication Diplo" -Completed

Write-Host ""
Write-Host "══════════════════════════════════════" -ForegroundColor Green
Write-Host " Diplo terminé en $($timer.Elapsed.TotalSeconds.ToString('F1'))s" -ForegroundColor Green
Write-Host "══════════════════════════════════════" -ForegroundColor Green
