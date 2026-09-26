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
#    ... -Projects "DiploWalker.Cli,DiploWalker.Gui"                              # sous-ensemble
# ---------------------------------------------------------------------------

[CmdletBinding()]
param(
    [switch]$Force,
    [string]$Projects,
    [string]$OpenSSLPath
)

$ErrorActionPreference = "Stop"

$repoRoot  = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$leavesDir = $PSScriptRoot
$caDir     = Resolve-Path (Join-Path $PSScriptRoot "..")

# Binaire OpenSSL : -OpenSSLPath, puis le PATH, puis les installations
# connues de Git pour Windows. La recherche ne doit pas dependre du prefixe
# d'installation : scoop place Git sous %USERPROFILE%\scoop\apps\git\<version>\
# et y fournit openssl.exe, que le chemin code en dur ne trouve pas.
function Resolve-OpenSsl {
    if ($OpenSSLPath) {
        if (-not (Test-Path $OpenSSLPath)) { throw "OpenSSL introuvable : $OpenSSLPath" }
        return (Resolve-Path $OpenSSLPath).Path
    }

    $cmd = Get-Command "openssl" -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @(
        (Join-Path $env:ProgramFiles "Git\usr\bin\openssl.exe")
        (Join-Path ${env:ProgramFiles(x86)} "Git\usr\bin\openssl.exe")
        (Join-Path $env:LOCALAPPDATA "Programs\Git\usr\bin\openssl.exe")
    )

    $scoop = Get-ChildItem -Path "$env:USERPROFILE\scoop\apps\git\*\usr\bin\openssl.exe" `
                          -ErrorAction SilentlyContinue |
                 Sort-Object FullName -Descending
    if ($scoop) { $candidates += $scoop.FullName }

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) { return $candidate }
    }

    throw "OpenSSL introuvable (installez Git pour Windows, ou passez -OpenSSLPath <chemin>)."
}

$openssl = Resolve-OpenSsl
Write-Host "==> OpenSSL : $openssl" -ForegroundColor Cyan

# --- Projets de la solution (parsing de DiploWalker.slnx) -------------------------
$slnx = Join-Path $repoRoot "DiploWalker.slnx"
if (-not (Test-Path $slnx)) { throw "DiploWalker.slnx introuvable : $slnx" }
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

    # UTF-8 sans BOM : `Set-Content -Encoding utf8` en PowerShell 5.1 ajoute un
    # BOM, inutile dans une configuration OpenSSL et source de diff parasites.
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText((Join-Path $Dir "openssl.cnf"), $config, $utf8NoBom)
}

# --- Exécution d'openssl -----------------------------------------------------
# `openssl req`/`ca` écrivent leur barre de progression sur stderr : sous
# $ErrorActionPreference = "Stop", PowerShell 5.1 convertit chaque ligne stderr
# en NativeCommandError terminant. On abaisse la préférence le temps de l'appel
# et on juge sur le code de sortie, seul indicateur fiable ici.
function Invoke-OpenSsl {
    param([string[]]$Arguments, [string]$WorkingDirectory, [string]$What)

    Push-Location $WorkingDirectory
    try {
        $ErrorActionPreference = "Continue"
        & $openssl @Arguments 2> $null
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = "Stop"
        Pop-Location
    }

    if ($code -ne 0) { throw "Échec openssl ($What) — code $code." }
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
        Invoke-OpenSsl -Arguments @("genpkey", "-algorithm", "RSA", "-pkeyopt", "rsa_keygen_bits:8192", "-out", $key) `
                       -WorkingDirectory $dir -What "clé $name"
    }

    # `openssl ca` résout `dir = .` et `certificate`/`private_key` par rapport au
    # répertoire courant : on se place donc dans l'autorité signataire, avec les
    # chemins -in/-out absolus.
    Invoke-OpenSsl -Arguments @("req", "-new", "-config", "openssl.cnf", "-key", $key, "-out", $csr) `
                   -WorkingDirectory $dir -What "CSR $name"

    Invoke-OpenSsl -Arguments @("ca", "-config", "openssl.cnf", "-extensions", "leaf_cert", "-batch", "-notext",
                                "-md", "sha384", "-in", $csr, "-out", $crt, "-days", "825") `
                   -WorkingDirectory $caDir -What "signature $name"

    # La chaîne (CodeSigning + racine) est embarquée dans le PFX : signtool la
    # recopie dans la signature Authenticode, ce qui rend l'origine vérifiable
    # même sans magasin de confiance (cf. certificates\README.md). Elle ne sert
    # pas à signer — seule la feuille porte la clé privée.
    $pkcs12 = @("pkcs12", "-export", "-out", $pfx, "-inkey", $key, "-in", $crt)
    $caChain = Join-Path $caDir "certs\codesigning.chain.crt.pem"
    if (Test-Path $caChain) { $pkcs12 += @("-certfile", $caChain) }
    $pkcs12 += @("-passout", "pass:")

    Invoke-OpenSsl -Arguments $pkcs12 -WorkingDirectory $dir -What "PFX $name"

    Write-Host "  ✓ $name (pfx : $pfx)" -ForegroundColor Green
}

Write-Host ""
Write-Host "Certificats de signature de code générés dans $leavesDir" -ForegroundColor Green
Write-Host "Les binaires seront signés automatiquement à la compilation (Directory.Build.targets)." -ForegroundColor Green

