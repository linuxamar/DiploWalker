# DiploWalker – Tests en conteneur Windows (Docker)

Ce répertoire contient les fichiers nécessaires pour lancer la suite de tests
du projet DiploWalker dans un conteneur Windows, compatible **Docker Desktop (mode Windows containers)**.

## Fichiers

| Fichier                    | Description                                                      |
| -------------------------- | ---------------------------------------------------------------- |
| `Dockerfile`               | Image Windows avec .NET 10 SDK, PowerShell 7, Git LFS           |
| `docker-compose.yml`       | Service `diplowalker-tests` pour `docker compose`               |
| `run-tests-in-docker.ps1`  | Script PowerShell d'entrée (appelle `pipeline.ps1`)             |
| `run-tests-in-docker.sh`   | Script shell d'entrée (appelle `pipeline.ps1` via `pwsh`)       |
| `.dockerignore`            | Exclusions du contexte de build                                  |

## Prérequis

- **Docker Desktop sur Windows** avec le mode **Windows containers** activé
- Ou un **runner Windows** en CI (GitHub Actions `windows-latest`, Azure DevOps, etc.)

> ⚠️ **Important** : Ce Dockerfile utilise une image de base Windows (`mcr.microsoft.com/dotnet/sdk:10.0-windowsservercore-ltsc2022`). Il **ne peut pas** être construit sur un démon Docker Linux (ex: `podman-machine-default`, Docker Desktop Linux, CI Linux). Le build échouera avec `InvalidBaseImagePlatform`.

## Build

```bash
docker build -t diplowalker-tests -f Docker/Dockerfile .
```

## Lancer les tests

### Option 1 : image directement

```bash
# Tests unitaires (par défaut)
docker run --rm -v ${PWD}:/workspaces/DiploWalker diplowalker-tests

# Tests + publication
docker run --rm -v ${PWD}:/workspaces/DiploWalker diplowalker-tests ./run-tests-in-docker.sh -DoTests -DoPublish

# Nettoyage + restauration
docker run --rm -v ${PWD}:/workspaces/DiploWalker diplowalker-tests ./run-tests-in-docker.sh -Clean -Restore
```

### Option 2 : docker-compose

```bash
# Tests par défaut
docker compose -f Docker/docker-compose.yml run --rm diplowalker-tests

# Tests + publication
docker compose -f Docker/docker-compose.yml run --rm diplowalker-tests ./run-tests-in-docker.sh -DoTests -DoPublish
```

## Notes

- La signature Authenticode (`Directory.Build.targets`) dépend de Windows et de
  `signtool.exe`. Sur Linux, le flag `-NoSignTests` (ou
  `-p:DiploWalkerSignOutputAfterBuild=false`) est requis pour éviter les erreurs
  de build.
- Les tests portant le trait `Platform=Windows` sont automatiquement exclus par
  `pipeline.ps1` sur Linux.
- Le montage en volume `..:/workspaces/DiploWalker` permet de modifier le code
  source depuis le conteneur et de lire les résultats sur l'hôte.
