# syntax=docker/dockerfile:1
# ---------------------------------------------------------------------------
# DiploWalker – Dockerfile pour l'exécution de la suite de tests
# ---------------------------------------------------------------------------
# Ce Dockerfile permet de lancer les tests unitaires du projet DiploWalker
# dans un conteneur Linux. Il installe :
#   - .NET 10 SDK ( requis par global.json )
#   - PowerShell 7 (pwsh) pour exécuter pipeline.ps1
#   - Git LFS ( requis par le projet : .gitattributes utilise LFS )
#   - Les outils de build : dotnet-build-server shutdown, etc.
#
# Compatible Docker et Podman :
#   podman build -t diplowalker-tests -f Docker/Dockerfile .
#   podman run --rm -v ${PWD}:/workspaces/DiploWalker diplowalker-tests
#   podman run --rm -v ${PWD}:/workspaces/DiploWalker diplowalker-tests ./pipeline.ps1 -DoTests
# ---------------------------------------------------------------------------

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS base

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_NOLOGO=1 \
    COMPLUS_EnableSSE3=1 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    PATH="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"

# PowerShell 7 — requis par pipeline.ps1 (appelle pwsh -File ...)
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        wget \
        ca-certificates \
        gnupg \
        git \
        git-lfs \
    && wget -q https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb \
    && dpkg -i /tmp/packages-microsoft-prod.deb \
    && rm /tmp/packages-microsoft-prod.deb \
    && apt-get update \
    && apt-get install -y --no-install-recommends powershell \
    && git lfs install \
    && rm -rf /var/lib/apt/lists/* \
    && rm -rf /tmp/*

WORKDIR /workspaces/DiploWalker

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