# ---------------------------------------------------------------------------
#  publish.ps1  –  Publication self-contained (Release) des services Diplo
# ---------------------------------------------------------------------------
#  Structure de sortie :
#    ./publish/WindowsServices/<platform>/<projet>/
#
#  Usage :
#    .\publish.ps1                  # publie x64 + x86
#    .\publish.ps1 -Platform x64   # publie x64 uniquement
# ---------------------------------------------------------------------------

[CmdletBinding()]
param(
    [ValidateSet("x64", "x86")]
    [string]$Platform
)

$ErrorActionPreference = "Stop"

# --- Configuration --------------------------------------------------------

$Projects = @(
    "Diplo.Container",
    "Diplo.Volume",
    "Diplo.Network",
    "Diplo.Installer"
)

$Platforms = if ($Platform) { @($Platform) } else { @("x64", "x86") }

$PublishRoot = Join-Path $PSScriptRoot "publish" "WindowsServices"

# --- Fonction utilitaire --------------------------------------------------

function Publish-Project {
    param(
        [string]$ProjectName,
        [string]$Plat
    )

    $projectPath = Join-Path $PSScriptRoot "src" $ProjectName "$ProjectName.fsproj"
    $outputDir   = Join-Path $PublishRoot $Plat $ProjectName

    if (-not (Test-Path $projectPath)) {
        # fallback .csproj (Diplo.Grpc ou futurs projets C#)
        $projectPath = Join-Path $PSScriptRoot "src" $ProjectName "$ProjectName.csproj"
    }

    if (-not (Test-Path $projectPath)) {
        Write-Warning "Projet introuvable : $ProjectPath — ignoré."
        return
    }

    Write-Host ""
    Write-Host "═══ $ProjectName  ($Plat) ═══" -ForegroundColor Cyan
    Write-Host "  Source  : $projectPath"
    Write-Host "  Sortie  : $outputDir"

    dotnet publish $projectPath `
        --configuration Release `
        --output $outputDir `
        --self-contained true `
        -p:Platform=$Plat `
        -p:PublishTrimmed=false `
        -p:PublishSingleFile=false `
        -p:IncludeNativeLibrariesForSelfExtract=true

    if ($LASTEXITCODE -ne 0) {
        throw "Échec de la publication de $ProjectName ($Plat)."
    }

    Write-Host "  ✓ OK" -ForegroundColor Green
}

# --- Point d'entrée -------------------------------------------------------

$timer = [System.Diagnostics.Stopwatch]::StartNew()

foreach ($plat in $Platforms) {
    foreach ($project in $Projects) {
        Publish-Project -ProjectName $project -Plat $plat
    }
}

$timer.Stop()

Write-Host ""
Write-Host "══════════════════════════════════════" -ForegroundColor Green
Write-Host " Publication terminée en $($timer.Elapsed.TotalSeconds.ToString('F1'))s" -ForegroundColor Green
Write-Host " Sortie : $PublishRoot" -ForegroundColor Green
Write-Host "══════════════════════════════════════" -ForegroundColor Green
