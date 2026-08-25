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
    [string]$Version = (Select-Xml -Path (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..")) "Directory.Build.props") -XPath "//Version").Node.InnerText,
    [ValidateSet("x64", "x86")]
    [string]$Platform = "x64",
    [string]$PublishRoot,
    [switch]$Sign,
    [string]$SignCert,
    [string]$SignPassword,
    [string]$SignThumbprint
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

# Déplacer la sortie générée (le OutFile du .nsi inclut déjà la plateforme)
$defaultOut = Join-Path $setupDir "Diplo-Setup-$Version-$Platform.exe"
if (Test-Path $defaultOut) {
    Move-Item -Path $defaultOut -Destination $outFile -Force
}

# --- Signature (optionnelle) -------------------------------------------------
if ($Sign) {
    $signtool = Get-Command "signtool.exe" -ErrorAction SilentlyContinue
    if (-not $signtool) {
        # signtool fait partie du SDK Windows ; on le cherche dans les kits installés.
        $sdkSigntool = Get-ChildItem -Path (
            "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe",
            "C:\Program Files\Windows Kits\10\bin\*\x64\signtool.exe"
        ) -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($sdkSigntool) {
            $signtool = [pscustomobject]@{ Source = $sdkSigntool.FullName }
        }
    }
    if (-not $signtool) {
        Write-Warning "signtool.exe introuvable (installez le SDK Windows ou ajoutez-le au PATH) — signature ignorée."
    } else {
        Write-Host ""
        Write-Host "═══ Signature ═══" -ForegroundColor Cyan
        Write-Host "  signtool : $($signtool.Source)"

        $signArgs = @("sign", "/fd", "SHA256")

        if ($SignThumbprint) {
            # Certificat du magasin désigné par son empreinte (SHA-1 ou SHA-256).
            $signArgs += "/sha1", $SignThumbprint
        } elseif ($SignCert) {
            $signArgs += "/f", $SignCert
            if ($SignPassword) {
                $signArgs += "/p", $SignPassword
            }
        } else {
            # Par défaut : la feuille de signature de code du projet Diplo.Installer.
            $defaultPfx = Join-Path $repoRoot "certificates\codesigning\leaves\Diplo.Installer\Diplo.Installer.pfx"
            if (Test-Path $defaultPfx) {
                $signArgs += "/f", $defaultPfx
            } else {
                $signArgs += "/a"
            }
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
