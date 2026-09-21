# Signe le manifeste des checksums des artefacts (C3).
#
# Génère une signature détachée RSA-4096/SHA-384 (PKCS#1 v1.5) du fichier
# artifacts.manifest et l'enregistre en base64 dans artifacts.manifest.sig.
#
# Prérequis : openssl dans le PATH. La clé privée n'est JAMAIS stockée dans le
# dépôt — elle vit hors bande, chiffrée en AES-256-CBC (emplacement par défaut :
# $env:USERPROFILE\.diplo\diplo-release.key).
#
# Passphrase : le script cherche le passphrase dans cet ordre :
#   1. Paramètre -Passphrase (texte clair ou SecureString)
#   2. Variable d'environnement DIPLO_KEY_PASSPHRASE
#   3. Si la clé n'est pas chiffrée, aucune passphrase n'est requise
#
# Renouvellement des checksums :
#   1. Mettre à jour assets/artifacts.manifest (garder les fins de ligne LF).
#   2. Lancer ce script (la vérification échouera si la clé privée ne
#      correspond pas à la clé publique embarquée dans l'installateur).
#   3. Commiter les deux fichiers + la clé publique si elle a changé.
#
# Exemples :
#   .\tools\sign-artifacts.ps1
#   $env:DIPLO_KEY_PASSPHRASE = Read-Host -AsSecureString "Passphrase" ; .\tools\sign-artifacts.ps1
#   .\tools\sign-artifacts.ps1 -ManifestPath .\assets\artifacts.manifest -KeyPath D:\secrets\diplo-signing.key

param(
    [string]$ManifestPath = (Join-Path $PSScriptRoot "..\src\DiploWalker.Installer\assets\artifacts.manifest"),
    [string]$KeyPath = (Join-Path $env:USERPROFILE ".diplo\diplo-release.key"),
    [System.Security.SecureString]$Passphrase
)

$ErrorActionPreference = "Stop"

function Resolve-OpenSsl {
    $cmd = Get-Command openssl -ErrorAction SilentlyContinue
    if (-not $cmd) { throw "openssl introuvable dans le PATH. Installez OpenSSL (scoop install openssl)." }
    $cmd.Source
}

function Resolve-Passin {
    if ($Passphrase -ne $null) {
        $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($Passphrase)
        $plain = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
        [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        return "pass:$plain"
    }
    if ($env:DIPLO_KEY_PASSPHRASE) { return "pass:$($env:DIPLO_KEY_PASSPHRASE)" }
    return $null
}

$manifest = [System.IO.Path]::GetFullPath($ManifestPath)
$key = [System.IO.Path]::GetFullPath($KeyPath)
$signaturePath = "$manifest.sig"
$pub = [System.IO.Path]::ChangeExtension($key, ".pub")

if (-not (Test-Path $manifest)) { throw "Manifeste introuvable : $manifest" }
if (-not (Test-Path $key)) { throw "Clé privée introuvable : $key (générer via : openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:4096 -out <chemin>.key)." }

$openssl = Resolve-OpenSsl
$passin = Resolve-Passin
$temp = [System.IO.Path]::GetTempFileName()
try {
    $dgstArgs = @("dgst", "-sha384", "-sign", $key, "-out", $temp, "--", $manifest)
    if ($passin) {
        $dgstArgs = @("dgst", "-sha384", "-passin", $passin, "-sign", $key, "-out", $temp, "--", $manifest)
    }
    # Signature binaire (octets exacts du manifeste, aucune conversion de fins de ligne)
    & $openssl @dgstArgs
    if ($LASTEXITCODE -ne 0) { throw "Signature échouée (openssl). Vérifiez la passphrase avec : openssl pkey -in $key -passin pass:<votre_passphrase>" }

    # Vérification croisée avant d'écrire
    if (Test-Path $pub) {
        $verify = & $openssl dgst -sha384 -verify $pub -signature $temp $manifest 2>&1
        if ($LASTEXITCODE -ne 0) { throw "La vérification de la signature a échoué : $verify" }
    }

    # Stockage base64 sur une seule ligne LF (aucun CRLF)
    $b64 = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($temp))
    $bytes = [Text.Encoding]::UTF8.GetBytes($b64)
    [System.IO.File]::WriteAllBytes($signaturePath, $bytes)

    "Signature écrite : $signaturePath"
    "Empreinte de la clé publique :"
    & $openssl pkey -pubin -in $pub -outform DER 2>$null | & $openssl dgst -sha256
}
finally {
    Remove-Item $temp -Force -ErrorAction SilentlyContinue
}
