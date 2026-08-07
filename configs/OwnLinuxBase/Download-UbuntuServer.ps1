# -----
# Téléchargement de l'image ISO officielle Ubuntu Server pour la configuration OwnLinuxBase.
# L'URL ne dépend pas de la version : le nom exact du fichier courant est résolu à partir
# du fichier SHA256SUMS publié sur le serveur Ubuntu, puis l'ISO (~3,2 Go) est téléchargée
# et vérifiée (empreinte SHA256) dans le dossier iso/.
# -----

[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$ConfigDir = $PSScriptRoot
$ConfigFile = Join-Path $ConfigDir "OwnLinuxBase.json"
$IsoDir = Join-Path $ConfigDir "iso"
$StateFile = Join-Path $IsoDir "iso.json"

if (-not (Test-Path -LiteralPath $ConfigFile)) {
    Write-Host "Fichier de configuration introuvable : $ConfigFile" -ForegroundColor Red
    exit 2
}

$Config = Get-Content -LiteralPath $ConfigFile -Raw | ConvertFrom-Json
$BaseUrl = $Config.distribution.url
$Pattern = $Config.distribution.motifNomFichier

$SumsUrl = "${BaseUrl}SHA256SUMS"
Write-Host "Résolution du nom de fichier courant via $SumsUrl..." -ForegroundColor Cyan

$SumsContent = [System.Text.Encoding]::UTF8.GetString((Invoke-WebRequest -Uri $SumsUrl -UseBasicParsing).Content)
$SumsLines = $SumsContent -split "\r?\n"
$HashLines = $SumsLines | Where-Object { $_.Trim() -match "\*[^*]+$([regex]::Escape($Pattern))$" }
if (-not $HashLines) {
    Write-Host "Aucune image correspondant à '$Pattern' dans SHA256SUMS." -ForegroundColor Red
    exit 1
}

$HashLine = ($HashLines | Sort-Object -Descending)[0]
$Hash = ($HashLine -split "\s+\*")[0].Trim()
$FileName = ($HashLine -split "\s+\*")[1].Trim()
$IsoUrl = "${BaseUrl}${FileName}"
$IsoPath = Join-Path $IsoDir $FileName

Write-Host "Image courante détectée : $FileName" -ForegroundColor Green

if ((Test-Path -LiteralPath $IsoPath) -and (-not $Force)) {
    Write-Host "L'ISO est déjà présente : $IsoPath" -ForegroundColor Green
    Write-Host "Utilisez -Force pour la télécharger à nouveau."
    @{ nomFichier = $FileName; url = $IsoUrl; hashSha256 = $Hash } | ConvertTo-Json | Set-Content -LiteralPath $StateFile -Encoding UTF8
    exit 0
}

New-Item -ItemType Directory -Path $IsoDir -Force | Out-Null

Write-Host "Téléchargement de $($Config.distribution.nom) $($Config.distribution.version)..." -ForegroundColor Cyan
Write-Host "Source : $IsoUrl" -ForegroundColor Cyan
Write-Host "Destination : $IsoPath" -ForegroundColor Cyan

try {
    Invoke-WebRequest -Uri $IsoUrl -OutFile $IsoPath
}
catch {
    Write-Host "Échec du téléchargement : $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "Vérification de l'empreinte SHA256..." -ForegroundColor Cyan
$Actual = (Get-FileHash -LiteralPath $IsoPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($Actual -ne $Hash.ToLowerInvariant()) {
    Write-Host "Empreinte SHA256 invalide, téléchargement corrompu." -ForegroundColor Red
    exit 1
}

@{ nomFichier = $FileName; url = $IsoUrl; hashSha256 = $Hash } | ConvertTo-Json | Set-Content -LiteralPath $StateFile -Encoding UTF8
Write-Host "Téléchargement terminé et vérifié." -ForegroundColor Green
exit 0
