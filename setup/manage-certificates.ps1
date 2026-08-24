# ---------------------------------------------------------------------------
#  manage-certificates.ps1 — Installation / désinstallation des certificats
#  PKI Diplo dans les magasins de certificats de la machine.
# ---------------------------------------------------------------------------
#  La racine est placée dans le magasin machine « Autorités de certification
#  racines de confiance » (StoreName.Root) et les intermédiaires dans
#  « Autorités de certification intermédiaires » (StoreName.CertificateAuthority).
#
#  Usage :
#    .\manage-certificates.ps1              # installe (ajoute) les certificats
#    .\manage-certificates.ps1 -Remove      # les retire (désinstallation)
#
#  Note : l'API .NET (System.Security.Cryptography.X509Certificates.X509Store)
#  est utilisée de préférence au fournisseur Cert: — ce dernier n'est pas
#  toujours initialisé quand powershell.exe est lancé via CreateProcess
#  (cas du nsExec de NSIS), ce qui faisait échouer silencieusement le retrait.
# ---------------------------------------------------------------------------

param(
    [switch]$Remove
)

$ErrorActionPreference = "Stop"

$rootCn = "Diplo Root CA"
$targetCns = @("Authentification", "CodeSigning", "System")

function Add-CertificateToStore {
    param(
        [string]$Path,
        [System.Security.Cryptography.X509Certificates.StoreName]$StoreName
    )

    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($Path)
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($StoreName, "LocalMachine")
    try {
        $store.Open("ReadWrite")
        $store.Add($cert)
    }
    finally {
        $store.Close()
        $cert.Dispose()
    }
}

function Remove-CertificatesFromStores {
    # Retrait ciblé : ne supprime que les certificats émis par la PKI Diplo
    # (émetteur contenant O=Diplo) dont le CN correspond à la liste.
    $cnPattern = "CN=($($targetCns -join '|')|$rootCn)(,|$)"
    $storeNames = @(
        [System.Security.Cryptography.X509Certificates.StoreName]::Root,
        [System.Security.Cryptography.X509Certificates.StoreName]::CertificateAuthority
    )
    foreach ($storeName in $storeNames) {
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, "LocalMachine")
        try {
            $store.Open("ReadWrite")
            $toRemove = $store.Certificates |
                Where-Object { $_.Subject -match $cnPattern -and $_.Issuer -match "O=Diplo" }
            foreach ($cert in $toRemove) {
                $store.Remove($cert)
            }
        }
        catch {
            Write-Warning "Impossible de nettoyer le magasin $storeName : $_"
        }
        finally {
            $store.Close()
        }
    }
}

if ($Remove) {
    Remove-CertificatesFromStores
}
else {
    $certificates = @(
        @{ File = "root-ca.crt.pem"; Store = [System.Security.Cryptography.X509Certificates.StoreName]::Root }
        @{ File = "authentification.crt.pem"; Store = [System.Security.Cryptography.X509Certificates.StoreName]::CertificateAuthority }
        @{ File = "codesigning.crt.pem"; Store = [System.Security.Cryptography.X509Certificates.StoreName]::CertificateAuthority }
        @{ File = "system.crt.pem"; Store = [System.Security.Cryptography.X509Certificates.StoreName]::CertificateAuthority }
    )
    foreach ($cert in $certificates) {
        $path = Join-Path $PSScriptRoot $cert.File
        if (-not (Test-Path $path)) {
            Write-Warning "Certificat introuvable : $path"
            continue
        }
        Add-CertificateToStore -Path $path -StoreName $cert.Store
    }
}
