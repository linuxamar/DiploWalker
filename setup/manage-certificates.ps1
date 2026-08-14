# ---------------------------------------------------------------------------
#  manage-certificates.ps1 — Installation / désinstallation des certificats
#  PKI Diplo dans les magasins de certificats de la machine.
# ---------------------------------------------------------------------------
#  La racine est placée dans le magasin machine « Autorités de certification
#  racines de confiance » (Cert:\LocalMachine\Root) et les intermédiaires dans
#  « Autorités de certification intermédiaires » (Cert:\LocalMachine\CA).
#
#  Usage :
#    .\manage-certificates.ps1              # installe (ajoute) les certificats
#    .\manage-certificates.ps1 -Remove      # les retire (désinstallation)
# ---------------------------------------------------------------------------

param(
    [switch]$Remove
)

$ErrorActionPreference = "Stop"

$rootCn    = "Diplo Root CA"
$targetCns = @("Authentification", "CodeSigning", "System")

if ($Remove) {
    # Retrait ciblé : ne supprime que les certificats émis par la PKI Diplo
    # (émetteur contenant O=Diplo) dont le CN correspond à la liste.
    # Les correspondances sont collectées d'abord, puis supprimées par chemin
    # (la suppression en direct pendant l'énumération du magasin échoue).
    $cnPattern = "CN=($($targetCns -join '|')|$rootCn)(,|$)"
    $toRemove = @(
        Get-ChildItem Cert:\LocalMachine\Root, Cert:\LocalMachine\CA -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -match $cnPattern -and $_.Issuer -match "O=Diplo" }
    )
    foreach ($cert in $toRemove) {
        Remove-Item -LiteralPath $cert.PSPath -Force
    }
} else {
    $certificates = @(
        @{ File = "root-ca.crt.pem";          Store = "Cert:\LocalMachine\Root" }
        @{ File = "authentification.crt.pem"; Store = "Cert:\LocalMachine\CA" }
        @{ File = "codesigning.crt.pem";      Store = "Cert:\LocalMachine\CA" }
        @{ File = "system.crt.pem";           Store = "Cert:\LocalMachine\CA" }
    )
    foreach ($cert in $certificates) {
        $path = Join-Path $PSScriptRoot $cert.File
        if (-not (Test-Path $path)) {
            Write-Warning "Certificat introuvable : $path"
            continue
        }
        Import-Certificate -FilePath $path -CertStoreLocation $cert.Store | Out-Null
    }
}
