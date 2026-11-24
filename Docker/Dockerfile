# syntax=docker/dockerfile:1
# ---------------------------------------------------------------------------
# DiploWalker – Dockerfile pour l'exécution de la suite de tests
# ---------------------------------------------------------------------------
# Ce Dockerfile permet de lancer les tests unitaires du projet DiploWalker
# dans un conteneur Windows. Il utilise :
#   - .NET 10 SDK (requis par global.json) sur Windows Server Core
#   - PowerShell 7 (pwsh) pour exécuter pipeline.ps1
#   - Git LFS (requis par le projet : .gitattributes utilise LFS)
#   - Les outils de build : dotnet-build-server shutdown, etc.
#
# IMPORTANT : Ce Dockerfile nécessite un démon Docker Windows (Docker Desktop
# en mode "Windows containers" sur Windows, ou un runner Windows en CI).
# Il NE FONCTIONNE PAS sur un démon Docker Linux (podman machine, Docker
# Desktop Linux, etc.).
#
# Compatible Docker (sur Windows) :
#   docker build -t diplowalker-tests -f Docker/Dockerfile .
#   docker run --rm -v "${PWD}:C:/workspaces/DiploWalker" diplowalker-tests
#   docker run --rm -v "${PWD}:C:/workspaces/DiploWalker" diplowalker-tests -DoTests
# ---------------------------------------------------------------------------

# Vérification de plateforme : échoue explicitement si on n'est pas sur Windows
FROM --platform=windows/amd64 mcr.microsoft.com/dotnet/sdk:10.0.404-windowsservercore-ltsc2025 AS base

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_NOLOGO=1 \
    COMPLUS_EnableSSE3=1 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

# PowerShell 7 est déjà inclus dans l'image Windows Server Core avec .NET SDK
# Git et Git LFS — requis par le projet (.gitattributes utilise LFS)
RUN powershell -Command "$ErrorActionPreference = 'Stop'; [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072; Invoke-WebRequest -Uri 'https://github.com/git-lfs/git-lfs/releases/download/v3.5.1/git-lfs-windows-amd64-v3.5.1.zip' -OutFile 'C:\git-lfs.zip'; Expand-Archive -Path 'C:\git-lfs.zip' -DestinationPath 'C:\git-lfs' -Force; $gitLfs = Get-ChildItem -Path 'C:\git-lfs' -Filter 'git-lfs.exe' -Recurse | Select-Object -First 1; if (-not $gitLfs) { throw 'git-lfs.exe was not found in the downloaded archive' }; $gitCmd = (Get-Command git.exe -ErrorAction Stop).Source; Copy-Item $gitLfs.FullName (Join-Path (Split-Path $gitCmd) 'git-lfs.exe') -Force; Remove-Item 'C:\git-lfs.zip'" \
    && setx PATH "%PATH%;C:\git-lfs" \
    && git lfs install

WORKDIR C:/workspaces/DiploWalker

# Étape 1 : copie des fichiers de configuration et restauration NuGet
# (ce layer est mis en cache tant que les fichiers .props/.slnx ne changent pas)
COPY global.json .
COPY Directory.Build.props .
COPY Directory.Build.targets .
COPY Directory.Packages.props .
COPY dotnet-tools.json .
COPY DiploWalker.slnx .
COPY src/ src/
COPY tests/ tests/
COPY pipeline.ps1 .
COPY Docker/ Docker/

RUN dotnet restore DiploWalker.slnx

ENTRYPOINT ["pwsh", "-File", "./pipeline.ps1"]
CMD ["-DoTests", "-NoSignTests"]
