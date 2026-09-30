# ---------------------------------------------------------------------------
#  regenerate-pki.ps1 – Regenere les autorites de la PKI de demonstration
# ---------------------------------------------------------------------------
#  Les certificats (.crt.pem) sont versionnes, mais toutes les cles privees
#  sont ignorer par .gitignore : ce script est le seul moyen de les
#  reconstituer. Sans lui, `openssl ca` echoue car
#  `certificates/<autorite>/private/<autorite>.key.pem` n'existe pas et aucun
#  certificat ne peut etre emis — les .crt.pem du depot restent alors
#  verifiables mais orphelins (personne ne peut emettre sous eux).
#
#  Chaine generee :
#    root-ca (auto-signe, pathlen:2)
#      |- authentification  (pathlen:0)  - leaf-tls-server (TLS des named pipes)
#      |- system            (pathlen:0)  - leaf-dll-validation,
#      |                                  leaf-config-encryption
#      |- codesigning       (pathlen:0)  - une feuille par projet
#                                          (certificates/codesigning/leaves/)
#
#  A la fin, `regenerate-leaves.ps1` est appele pour les feuilles de signature
#  de code par projet, sauf si -SkipProjectLeaves est fourni.
#
#  Usage :
#    .\certificates\regenerate-pki.ps1                        # bootstrapping
#    .\certificates\regenerate-pki.ps1 -Force                # regenere les cles
#    .\certificates\regenerate-pki.ps1 -SkipProjectLeaves     # seulement les CAs
#    .\certificates\regenerate-pki.ps1 -OnlyTlsLeaf           # seulement la feuille TLS
#    .\certificates\regenerate-pki.ps1 -OpenSSLPath D:\...\openssl.exe
#
#  Les cles et PFX produits sont sans phrase de passe (usage demonstration
#  uniquement, cf. certificates\README.md).
# ---------------------------------------------------------------------------

[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$SkipProjectLeaves,
    [switch]$OnlyTlsLeaf,
    [string]$OpenSSLPath
)

$ErrorActionPreference = "Stop"

$certDir  = $PSScriptRoot
$rootDir  = Join-Path $certDir "root-ca"

# --- Resolution du binaire OpenSSL -------------------------------------------
# Ordre : -OpenSSLPath, PATH, puis les installations connues de Git pour
# Windows (scoop, programme standard, LocalAppData). La recherche ne doit pas
# dependre du prefixe d'installation : scoop installe Git sous
# %USERPROFILE%\scoop\apps\git\<version>\ et y place openssl.exe.
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

function Invoke-OpenSsl {
    param([string[]]$Arguments, [string]$WorkingDirectory, [string]$What)

    $log = [System.IO.Path]::GetTempFileName()

    try {
        Push-Location $WorkingDirectory
        try {
            # `openssl genpkey`/`req` ecrivent leur barre de progression sur
            # stderr : sous $ErrorActionPreference = "Stop", PowerShell 5.1
            # convertit chaque ligne stderr en NativeCommandError terminant,
            # meme redirigee vers un fichier. On abaisse donc la preference le
            # temps de l'appel et on juge sur le code de sortie.
            $ErrorActionPreference = "Continue"
            & $openssl @Arguments 2> $log
            $code = $LASTEXITCODE
        } finally {
            $ErrorActionPreference = "Stop"
            Pop-Location
        }

        if ($code -ne 0) {
            $detail = Get-Content $log -Raw
            if ([string]::IsNullOrWhiteSpace($detail)) { $detail = "(aucune sortie)" }
            throw "Echec openssl ($What) dans $WorkingDirectory (code $code) : $detail"
        }
    } finally {
        Remove-Item $log -Force -ErrorAction SilentlyContinue
    }
}

# --- Preparation des repertoires d'une autorite ------------------------------
function Initialize-Authority {
    param([string]$Dir)

    foreach ($sub in @("private", "csr", "certs", "db\newcerts", "crl")) {
        New-Item -ItemType Directory -Force -Path (Join-Path $Dir $sub) | Out-Null
    }

    # index.txt est la base de la CA : absente du depot (gitignoree), elle doit
    # exister et vide avant le premier `openssl ca`.
    $index = Join-Path $Dir "db\index.txt"
    if (-not (Test-Path $index)) {
        New-Item -ItemType File -Path $index | Out-Null
    }
}

# --- Generation d'une autorite (racule ou intermediaire) ---------------------
function New-Authority {
    param(
        [string]$Name,
        [string]$Parent,          # $null pour la racine (auto-signee)
        [int]$Days,
        [string]$Extensions
    )

    $dir = Join-Path $certDir $Name
    $key = Join-Path $dir "private\$Name.key.pem"
    $csr = Join-Path $dir "csr\$Name.csr.pem"
    $crt = Join-Path $dir "certs\$Name.crt.pem"

    Initialize-Authority -Dir $dir

    if ($Force -or -not (Test-Path $key)) {
        Write-Host "  cle RSA 8192..."
        Invoke-OpenSsl -Arguments @("genpkey", "-algorithm", "RSA", "-pkeyopt", "rsa_keygen_bits:8192", "-out", $key) `
                       -WorkingDirectory $dir -What "cle $Name"
    } else {
        Write-Host "  cle existante, conservee (-Force pour regenerer)" -ForegroundColor DarkGray
    }

    if (-not $Parent) {
        # Racine : auto-signee, x509_extensions = v3_ca (pathlen:2).
        Write-Host "  certificat auto-signe ($Days jours)..."
        Invoke-OpenSsl -Arguments @("req", "-x509", "-config", "openssl.cnf", "-key", $key, "-days", "$Days", "-out", $crt) `
                       -WorkingDirectory $dir -What "certificat $Name"
    } else {
        Write-Host "  CSR..."
        Invoke-OpenSsl -Arguments @("req", "-new", "-config", "openssl.cnf", "-key", $key, "-out", $csr) `
                       -WorkingDirectory $dir -What "CSR $Name"

        # Signature par le parent : `openssl ca` resout `dir = .` et
        # `certificate`/`private_key` par rapport au repertoire courant, on
        # s'y place donc, avec les chemins -in/-out absolus.
        $parentDir = Join-Path $certDir $Parent
        Write-Host "  signature par $Parent ($Days jours)..."
        Invoke-OpenSsl -Arguments @("ca", "-config", "openssl.cnf", "-extensions", $Extensions,
                                    "-batch", "-notext", "-md", "sha384",
                                    "-in", $csr, "-out", $crt, "-days", "$Days") `
                       -WorkingDirectory $parentDir -What "signature $Name"
    }

    # Chaine complete : certificat emis puis son signataire direct.
    if ($Parent) {
        $parentCrt = Join-Path $certDir "$Parent\certs\$Parent.crt.pem"
        $chain = $crt.Replace(".crt.pem", ".chain.crt.pem")
        [System.IO.File]::WriteAllText($chain, (Get-Content $crt -Raw) + (Get-Content $parentCrt -Raw))
    }

    Write-Host "  OK $Name" -ForegroundColor Green
}

# --- Feuille signee par une autorite de feuilles -----------------------------
# Utilisee pour leaf-dll-validation et leaf-config-encryption, dont la
# configuration vit dans leur propre repertoire (leur parent et System
# fournissent le service de signature).
function New-ExtraLeaf {
    param(
        [string]$Name,
        [string]$Parent,
        [int]$Days
    )

    $dir  = Join-Path $certDir $Name
    $key  = Join-Path $dir "private\$Name.key.pem"
    $csr  = Join-Path $dir "csr\$Name.csr.pem"
    $crt  = Join-Path $dir "certs\$Name.crt.pem"

    Initialize-Authority -Dir $dir

    if ($Force -or -not (Test-Path $key)) {
        Write-Host "  cle RSA 8192..."
        Invoke-OpenSsl -Arguments @("genpkey", "-algorithm", "RSA", "-pkeyopt", "rsa_keygen_bits:8192", "-out", $key) `
                       -WorkingDirectory $dir -What "cle $Name"
    } else {
        Write-Host "  cle existante, conservee (-Force pour regenerer)" -ForegroundColor DarkGray
    }

    Write-Host "  CSR..."
    Invoke-OpenSsl -Arguments @("req", "-new", "-config", "openssl.cnf", "-key", $key, "-out", $csr) `
                   -WorkingDirectory $dir -What "CSR $Name"

    $parentDir = Join-Path $certDir $Parent
    Write-Host "  signature par $Parent ($Days jours)..."
    Invoke-OpenSsl -Arguments @("ca", "-config", "openssl.cnf", "-extensions", "leaf_cert",
                                "-batch", "-notext", "-md", "sha384",
                                "-in", $csr, "-out", $crt, "-days", "$Days") `
                   -WorkingDirectory $parentDir -What "signature $Name"

    $parentCrt = Join-Path $certDir "$Parent\certs\$Parent.crt.pem"
    [System.IO.File]::WriteAllText($crt.Replace(".crt.pem", ".chain.crt.pem"),
                                  (Get-Content $crt -Raw) + (Get-Content $parentCrt -Raw))

    # PFX : la chaîne complète (intermediaire + racine) y est embarquee, afin
    # que le consommateur n'ait pas besoin du magasin de confiance de la machine.
    # La service qui charge ce PFX n'exploite que la feuille (cle + certificat) ;
    # la chaine ne sert qu'a rendre l'origine verifiable hors magasin.
    $pfx = Join-Path $dir "$Name.pfx"

    if ($Force -or -not (Test-Path $pfx)) {
        Write-Host "  PFX..."
        $pkcs12 = @("pkcs12", "-export", "-out", $pfx, "-inkey", $key, "-in", $crt)
        $caChain = Join-Path $certDir "$Parent\certs\$Parent.chain.crt.pem"

        if (Test-Path $caChain) { $pkcs12 += @("-certfile", $caChain) }

        $pkcs12 += @("-passout", "pass:")
        Invoke-OpenSsl -Arguments $pkcs12 -WorkingDirectory $dir -What "PFX $Name"
    } else {
        Write-Host "  PFX existant, conserve (-Force pour regenerer)" -ForegroundColor DarkGray
    }

    Write-Host "  OK $Name" -ForegroundColor Green
}

# --- Deroulement -------------------------------------------------------------
Write-Host "==> OpenSSL : $openssl" -ForegroundColor Cyan

# Cible : n'emettre que la feuille TLS serveur. Les CAs et les autres feuilles
# restent intacts, ce qui evite de regenerer des certificats deja signes (la
# signature de code exige que la feuille Codesigning reste inchangee).
if ($OnlyTlsLeaf) {
    Write-Host "==> Feuille TLS serveur" -ForegroundColor Yellow
    New-ExtraLeaf -Name "leaf-tls-server" -Parent "authentification" -Days 825
    return
}

Write-Host "==> Racine" -ForegroundColor Yellow
New-Authority -Name "root-ca" -Parent $null -Days 3650 -Extensions $null

# pathlen:0 : autorites de feuilles (elles signent des certificats, pas d'autres CA).
$intermediates = @("authentification", "system", "codesigning")
foreach ($name in $intermediates) {
    Write-Host "==> Intermediaire $name" -ForegroundColor Yellow
    New-Authority -Name $name -Parent "root-ca" -Days 1825 -Extensions "v3_intermediate_leaf"
}

Write-Host "==> Feuilles sous System" -ForegroundColor Yellow
New-ExtraLeaf -Name "leaf-dll-validation"   -Parent "system" -Days 825
New-ExtraLeaf -Name "leaf-config-encryption" -Parent "system" -Days 825

Write-Host "==> Feuilles sous Authentification" -ForegroundColor Yellow
New-ExtraLeaf -Name "leaf-tls-server"        -Parent "authentification" -Days 825

# --- Feuilles de signature de code par projet -------------------------------
if (-not $SkipProjectLeaves) {
    $leavesScript = Join-Path $certDir "codesigning\leaves\regenerate-leaves.ps1"

    if (-not (Test-Path $leavesScript)) { throw "regenerate-leaves.ps1 introuvable : $leavesScript" }

    Write-Host ""
    Write-Host "==> Feuilles de signature de code par projet" -ForegroundColor Yellow

    $leafArgs = @{ OpenSSLPath = $openssl }
    if ($Force) { $leafArgs["Force"] = $true }
    & $leavesScript @leafArgs
}

Write-Host ""
Write-Host "PKI regeneree dans $certDir" -ForegroundColor Green
Write-Host "Les cles privees et PFX ne sont pas versionnes (cf. .gitignore) : ce script doit etre"
Write-Host "relance sur chaque poste de developpement ou machine de build." -ForegroundColor Green
