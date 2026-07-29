# ---------------------------------------------------------------------------
#  build.ps1 – Compile Diplo-Setup.exe (NSIS)
# ---------------------------------------------------------------------------
#  Usage :
#    .\setup\build.ps1                                # version 1.0.0, x64
#    .\setup\build.ps1 -Version 2.1.0                 # version spécifique
#    .\setup\build.ps1 -Platform x86                  # package x86
#    .\setup\build.ps1 -PublishRoot .\publish\...     # répertoire sur mesure
# ---------------------------------------------------------------------------

[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [ValidateSet("x64", "x86")]
    [string]$Platform = "x64",
    [string]$PublishRoot,
    [switch]$Sign,
    [string]$SignCert,
    [string]$SignPassword
)

$ErrorActionPreference = "Stop"

# Vérifier que makensis est disponible
$makensis = Get-Command "makensis" -ErrorAction SilentlyContinue
if (-not $makensis) {
    throw "NSIS (makensis) introuvable. Installez NSIS depuis https://nsis.sourceforge.io/"
}

# Résoudre le répertoire racine du dépôt
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$setupDir  = $PSScriptRoot

# Déterminer le répertoire de publication
if (-not $PublishRoot) {
    $PublishRoot = Join-Path $repoRoot "publish\WindowsServices\$Platform"
}
$PublishRoot = Resolve-Path $PublishRoot

if (-not (Test-Path "$PublishRoot\Diplo.Gui\Diplo.Gui.exe")) {
    Write-Warning "Diplo.Gui.exe introuvable dans $PublishRoot\Diplo.Gui\"
    Write-Host   "Exécutez d'abord : .\pipeline.ps1 -DoPublish -Platform $Platform" -ForegroundColor Yellow
    exit 1
}

# Compiler le NSIS
Write-Host "═══════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Diplo Setup – NSIS" -ForegroundColor Cyan
Write-Host "  Version  : $Version" -ForegroundColor Cyan
Write-Host "  Platform : $Platform" -ForegroundColor Cyan
Write-Host "  Publish  : $PublishRoot" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════" -ForegroundColor Cyan

$nsiFile   = Join-Path $setupDir "setup.nsi"
$outFile   = Join-Path $repoRoot "Diplo-Setup-$Version-$Platform.exe"

$defineVersion   = "-DAPP_VERSION=$Version"
$definePlatform  = "-DPLATFORM=$Platform"
$definePublish   = "-DPUBLISH_ROOT=$PublishRoot"

Push-Location $setupDir
try {
    & $makensis.Source $defineVersion $definePlatform $definePublish $nsiFile

    if ($LASTEXITCODE -ne 0) {
        throw "makensis a échoué (code $LASTEXITCODE)."
    }
} finally {
    Pop-Location
}

# Renommer la sortie générée (le OutFile du .nsi ne contient pas la plateforme)
$defaultOut = Join-Path $setupDir "Diplo-Setup-$Version.exe"
if (Test-Path $defaultOut) {
    Move-Item -Path $defaultOut -Destination $outFile -Force
}

# --- Signature (optionnelle) -------------------------------------------------
if ($Sign) {
    $signtool = Get-Command "signtool.exe" -ErrorAction SilentlyContinue
    if (-not $signtool) {
        Write-Warning "signtool.exe introuvable — signature ignorée."
    } else {
        Write-Host ""
        Write-Host "═══ Signature ═══" -ForegroundColor Cyan

        $signArgs = @("sign", "/fd", "SHA256")

        if ($SignCert) {
            $signArgs += "/f", $SignCert
            if ($SignPassword) {
                $signArgs += "/p", $SignPassword
            }
        } else {
            $signArgs += "/a"
        }

        $signArgs += "/tr", "http://timestamp.digicert.com"
        $signArgs += "/td", "SHA256"
        $signArgs += $outFile

        Write-Host "  Signature de $outFile ..." -ForegroundColor Yellow
        & $signtool.Source $signArgs

        if ($LASTEXITCODE -eq 0) {
            Write-Host "  ✓ Installateur signé." -ForegroundColor Green
        } else {
            Write-Warning "La signature a échoué (code $LASTEXITCODE)."
        }
    }
}

Write-Host ""
Write-Host "  ✓ Installateur créé : $outFile" -ForegroundColor Green
