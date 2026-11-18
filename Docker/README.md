# DiploWalker – Tests en conteneur Linux (Podman)

Ce répertoire contient les fichiers nécessaires pour lancer la suite de tests
du projet DiploWalker dans un conteneur Linux, compatible **Docker** et **Podman**.

## Fichiers

| Fichier                    | Description                                                      |
| -------------------------- | ---------------------------------------------------------------- |
| `Dockerfile`               | Image Linux avec .NET 10 SDK, PowerShell 7, Git LFS             |
| `docker-compose.yml`       | Service `diplowalker-tests` pour `podman-compose` / `docker compose` |
| `run-tests-in-docker.ps1`  | Script PowerShell d'entrée (appelle `pipeline.ps1`)              |
| `run-tests-in-docker.sh`   | Script shell d'entrée (appelle `pipeline.ps1` via `pwsh`)       |
| `.dockerignore`            | Exclusions du contexte de build                                  |

## Build

```bash
podman build -t diplowalker-tests -f Docker/Dockerfile .
```

## Lancer les tests

### Option 1 : image directement

```bash
# Tests unitaires (par défaut)
podman run --rm -v ${PWD}:/workspaces/DiploWalker diplowalker-tests

# Tests + publication
podman run --rm -v ${PWD}:/workspaces/DiploWalker diplowalker-tests ./run-tests-in-docker.sh -DoTests -DoPublish

# Nettoyage + restauration
podman run --rm -v ${PWD}:/workspaces/DiploWalker diplowalker-tests ./run-tests-in-docker.sh -Clean -Restore
```

### Option 2 : docker-compose / podman-compose

```bash
# Tests par défaut
podman-compose -f Docker/docker-compose.yml run --rm diplowalker-tests

# Tests + publication
podman-compose -f Docker/docker-compose.yml run --rm diplowalker-tests ./run-tests-in-docker.sh -DoTests -DoPublish
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
