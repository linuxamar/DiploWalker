# -----
# Lancement de la configuration OwnLinuxBase : boot d'Ubuntu Server (sans IHM)
# via l'émulateur Diplo. Construit le binaire diplo-linux.exe s'il est absent.
# -----

[CmdletBinding()]
param(
    [string]$Config = "OwnLinuxBase.json",
    [switch]$Trace,
    [switch]$Step,
    [string[]]$Breakpoints
)

$ErrorActionPreference = "Stop"

$ConfigDir = $PSScriptRoot
$ConfigFile = Join-Path $ConfigDir $Config
$RepoRoot = (Resolve-Path (Join-Path $ConfigDir "..\..")).Path
$BinDir = Join-Path $ConfigDir "bin"
$Binary = Join-Path $BinDir "diplo-linux.exe"

if (-not (Test-Path -LiteralPath $ConfigFile)) {
    Write-Host "Fichier de configuration introuvable : $ConfigFile" -ForegroundColor Red
    exit 2
}

$Cfg = Get-Content -LiteralPath $ConfigFile -Raw | ConvertFrom-Json

Write-Host "Configuration : $($Cfg.nom) — $($Cfg.description)" -ForegroundColor Cyan

if (-not (Test-Path -LiteralPath $Binary)) {
    Write-Host "Binaire diplo-linux.exe absent, publication en cours..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $BinDir -Force | Out-Null
    dotnet publish (Join-Path $RepoRoot "src\Diplo.Linux.Cli") -c Release -o $BinDir `
        --self-contained true -r win-x64 -p:PublishTrimmed=false -p:PublishSingleFile=false
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Échec de la publication de Diplo.Linux.Cli." -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

$StateFile = Join-Path $ConfigDir "iso\iso.json"
$IsoPath = $null
if (Test-Path -LiteralPath $StateFile) {
    $State = Get-Content -LiteralPath $StateFile -Raw | ConvertFrom-Json
    $IsoPath = Join-Path $ConfigDir (Join-Path "iso" $State.nomFichier)
}
if (-not $IsoPath -or -not (Test-Path -LiteralPath $IsoPath)) {
    Write-Host "Image ISO introuvable." -ForegroundColor Red
    Write-Host "Lancez d'abord .\Download-UbuntuServer.ps1 pour télécharger Ubuntu Server."
    exit 2
}

$Options = @()
if ($Trace) { $Options += "-t" }
if ($Step) { $Options += "-s" }
foreach ($Bp in $Breakpoints) {
    $Options += "-b"
    $Options += $Bp
}

$KernelArgs = @($Cfg.noyau.arguments)

Write-Host "Boot d'Ubuntu Server via l'émulateur Diplo..." -ForegroundColor Cyan
Write-Host "  ISO       : $IsoPath" -ForegroundColor Cyan
Write-Host "  Noyau     : $($Cfg.noyau.cheminDansImage)" -ForegroundColor Cyan

& $Binary @Options boot $IsoPath $Cfg.noyau.cheminDansImage @KernelArgs
exit $LASTEXITCODE
