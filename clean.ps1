# ---------------------------------------------------------------------------
#  clean.ps1  –  Purge les répertoires bin et obj de tous les projets
# ---------------------------------------------------------------------------

$ErrorActionPreference = "Stop"

$folders = @("bin", "obj")

$count = 0

foreach ($folder in $folders) {
    Get-ChildItem -Path $PSScriptRoot -Filter $folder -Directory -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\src\\" -or $_.FullName -eq (Join-Path $PSScriptRoot $folder) } |
        ForEach-Object {
            Write-Host "Suppression : $($_.FullName)" -ForegroundColor Yellow
            Remove-Item -Path $_.FullName -Recurse -Force
            $count++
        }
}

Write-Host ""
Write-Host "$count répertoire(s) supprimé(s)." -ForegroundColor Green
