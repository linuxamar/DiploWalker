# ---------------------------------------------------------------------------
#  regenerate-leaves.ps1 – Certificats de signature de code par projet
# ---------------------------------------------------------------------------
#  Génère, pour chaque projet de la solution Diplo, un certificat de signature
#  de code (RSA 8192, EKU codeSigning) signé par l'intermédiaire "CodeSigning"
#  (certificates/codesigning/), puis le fichier PFX associé.
#
#  Les clés privées et PFX ne sont PAS versionnés (voir .gitignore) : ce script
#  doit être exécuté sur chaque poste de développement / machine de build.
#
#  Usage :
#    .\certificates\codesigning\leaves\regenerate-leaves.ps1          # tous les projets
#    .\certificates\codesigning\leaves\regenerate-leaves.ps1 -Force   # régénère les clés
#    ... -Projects "Diplo.Cli,Diplo.Gui"                              # sous-ensemble
# ---------------------------------------------------------------------------

[CmdletBinding()]
param(
    [switch]$Force,
    [string]$Projects
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$leavesDir = $PSScriptRoot
$caDir = Resolve-Path (Join-Path $PSScriptRoot "..")

# Binaire OpenSSL : Git pour Windows par défaut, sinon le PATH.
$openssl = "C:\Program Files\Git\usr\bin\openssl.exe"
if (-not (Test-Path $openssl)) {
    $cmd = Get-Command "openssl" -ErrorAction SilentlyContinue
    if (-not $cmd) { throw "OpenSSL introuvable (Git pour Windows requis)." }
    $openssl = $cmd.Source
}

# --- Projets de la solution (parsing de Diplo.slnx) -------------------------
$slnx = Join-Path $repoRoot "Diplo.slnx"
if (-not (Test-Path $slnx)) { throw "Diplo.slnx introuvable : $slnx" }
$xml = [xml](Get-Content $slnx)

$names = @()
foreach ($folder in $xml.Solution.Folder) {
    foreach ($proj in $folder.Project) {
        if ($proj.Path -match '[\\/]([^\\/]+)\.fsproj$') { $names += $Matches[1] }
    }
}

if ($Projects) {
    $filter = $Projects.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ }
    $names = $names | Where-Object { $_ -in $filter }
}

if ($names.Count -eq 0) { throw "Aucun projet à traiter." }

Write-Host "==> $($names.Count) projet(s) de la solution" -ForegroundColor Cyan

# --- Modèle de configuration openssl pour une feuille ------------------------
function New-LeafConfig {
    param([string]$Dir, [string]$CommonName)

    $config = @"
# Configuration du certificat de signature de code du projet $CommonName.
# Parent : l'intermédiaire "CodeSigning" (certificates/codesigning/).
# Clé RSA 8192 bits, usage étendu : codeSigning.
#
# Exemples :
#   openssl req -new -config openssl.cnf -key $CommonName.key.pem -out $CommonName.csr.pem
#   # Signature par CodeSigning (exécutée depuis codesigning/)
#   openssl ca -config ../openssl.cnf -extensions leaf_cert -batch -notext `
#              -in ../leaves/$CommonName/$CommonName.csr.pem `
#              -out ../leaves/$CommonName/$CommonName.crt.pem

[ req ]
default_bits       = 8192
default_md         = sha384
distinguished_name = req_distinguished_name
string_mask        = utf8only
prompt             = no
req_extensions     = v3_req

[ req_distinguished_name ]
countryName            = FR
organizationName       = Diplo
organizationalUnitName = Diplo Signature de code
commonName             = $CommonName

# Extensions demandées dans la CSR : recopiées dans le certificat grâce à
# copy_extensions = copy dans la configuration de l'autorité signataire.
[ v3_req ]
keyUsage          = critical, digitalSignature
extendedKeyUsage  = codeSigning
"@

    Set-Content -Path (Join-Path $Dir "openssl.cnf") -Value $config -Encoding utf8
}

# --- Génération --------------------------------------------------------------
foreach ($name in $names) {
    Write-Host "── $name ──" -ForegroundColor Yellow

    $dir = Join-Path $leavesDir $name
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    New-LeafConfig -Dir $dir -CommonName $name

    $key = Join-Path $dir "$name.key.pem"
    $csr = Join-Path $dir "$name.csr.pem"
    $crt = Join-Path $dir "$name.crt.pem"
    $pfx = Join-Path $dir "$name.pfx"

    if ($Force -or -not (Test-Path $key)) {
        Write-Host "  clé RSA 8192..."
        & $openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:8192 -out $key 2>$null
        if ($LASTEXITCODE -ne 0) { throw "Échec de la génération de la clé ($name)." }
    }

    Push-Location $dir
    try {
        & $openssl req -new -config openssl.cnf -key $key -out $csr
        if ($LASTEXITCODE -ne 0) { throw "Échec de la CSR ($name)." }
    }
    finally {
        Pop-Location
    }

    Push-Location $caDir
    try {
        & $openssl ca -config openssl.cnf -extensions leaf_cert -batch -notext `
            -md sha384 -in $csr -out $crt -days 825
        if ($LASTEXITCODE -ne 0) { throw "Échec de la signature ($name)." }
    }
    finally {
        Pop-Location
    }

    & $openssl pkcs12 -export -out $pfx -inkey $key -in $crt -passout pass:
    if ($LASTEXITCODE -ne 0) { throw "Échec de la création du PFX ($name)." }

    Write-Host "  ✓ $name (pfx : $pfx)" -ForegroundColor Green
}

Write-Host ""
Write-Host "Certificats de signature de code générés dans $leavesDir" -ForegroundColor Green
Write-Host "Les binaires seront signés automatiquement à la compilation (Directory.Build.targets)." -ForegroundColor Green
