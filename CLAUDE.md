# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Projet

**Diplo** — système distribué de microservices gRPC pour la gestion de conteneurs Windows (cf. `README.md`).

## Architecture

11 projets source (.NET 10, F#) + 11 projets de test :

| Projet | Rôle |
|--------|------|
| Diplo.Abstractions | Interfaces partagées, validation, sécurité, modules utilitaires mutualisés |
| Diplo.Container | Service gRPC de gestion des conteneurs (containerd) — création, cycle de vie, montage de volumes (`--mount`) |
| Diplo.Volume | Service gRPC de gestion des volumes persistants |
| Diplo.Network | Service gRPC de gestion des réseaux (CNI) |
| Diplo.Installer | Installation Windows (services, containerd, CNI) |
| Diplo.Grpc | Types messages et interfaces de service gRPC (protobuf-net, code-first), mappings de drivers |
| Diplo.Contracts | Types partagés entre services |
| Diplo.Core | Clients gRPC, abstraction `IOutputPort`, `MountParser` (format `src=...,dst=...[;ro]`), config client `diplo.json` et support des named pipes (`http://pipe:/<nom>`), factory gRPC mutualisée |
| Diplo.Disk | Montage d'images disque (qcow2, qcow1, raw, vhd, vhdx, vmdk, vdi, dmg, parallels) via DiscUtils/pilotes maison + support R/W Btrfs, XFS, HFS+ via Hawkynt.FileFormats.FileSystems |
| Diplo.Cli | Client CLI (Spectre.Console) |
| Diplo.Gui | Interface graphique Avalonia |

### Modules mutualisés

Les modules suivants ont été extraits du code dupliqué et centralisés dans Diplo.Abstractions / Diplo.Grpc :

| Module | Projet | Rôle |
|--------|--------|------|
| `JsonHelpers` | Diplo.Abstractions | Extraction typée de propriétés depuis `JsonElement` (`tryGetString`, `tryGetInt64`, `tryGetDouble`, `tryGetBool`, `tryGetElement`, `tryGetStringValue`). Module `[<RequireQualifiedAccess>]` — appeler via `JsonHelpers.tryGetString`. |
| `DiploJson` | Diplo.Abstractions | Options de sérialisation JSON centralisées (`defaultOptions`, `snakeCaseOptions`, `caseInsensitiveOptions`, `withMaxDepth`, `documentOptions`). |
| `ProcessExec` | Diplo.Abstractions | Exécution de processus externes (`run`, `runWithResult`) et commandes PowerShell (`runPowerShell`, `runPowerShellScript`). Gère le timeout, le Kill, et la lecture asynchrone stdout/stderr. |
| `ServiceGuards` | Diplo.Abstractions | Guards de validation d'entrée réutilisables (`requireNonEmpty`, `requireId`, `requirePositive`, `requireInRange`, `requireSafePath`, `requireLocalAddress`, `requireSafeCommand`). Lèvent `RpcException(InvalidArgument)`. |
| `CachedConfig<'T>` | Diplo.Abstractions | Cache générique avec invalidation manuelle, protégé par un verrou. Chargement paresseux via `Value`, invalidation via `Invalidate()`. |
| `DriverMappings` | Diplo.Grpc | Mapping type↔string pour les drivers volume (`StorageDriverType`) et réseau (`NetworkDriver`). Fonctions `parse*`, `isValid*`, `*ToString`, `all*Names`. |
| `GrpcClientFactory` | Diplo.Core | Construction de canaux gRPC TCP ou named pipe avec retry (5 tentatives, backoff exponentiel) et credentials par token. |
| `TestHelpers` | Diplo.TestHelpers | Helpers pour les tests (`createTempDir`, `cleanupDir`). |
| `HawkyntFs` | Diplo.Disk | Adaptateur Hawkynt.FileFormats.FileSystems pour l'extraction et la réécriture de Btrfs, XFS et HFS+. Seuil de 2 Go pour éviter le tout-en-mémoire ; fallback DiscUtils au-delà. |
| `VdiFs` | Diplo.Disk | Adaptateur DiscUtils.Vdi pour l'extraction et la réécriture R/W d'images VDI (VirtualBox). |
| `Qcow1Fs` | Diplo.Disk | Pilote maison pour les images QCOW v1 (QFI\\xFE) : lecture/écriture in-place via `Qcow1Stream`. |
| `DmgFs` | Diplo.Disk | Adaptateur DiscUtils.Dmg pour l'extraction (lecture seule) d'images DMG (Apple Disk Image). |
| `ParallelsFs` | Diplo.Disk | Pilote maison pour les images Parallels (.hdd, .hds) : lecture/écriture in-place via `ParallelsStream`. |

## Stack

- **.NET 10** (`dotnet 10.0.302` installé localement).
- Orientation **100 % F#** (services, drivers, CLI et gRPC en code-first protobuf-net).
- **Tests** : xUnit v4 + FsUnit.xUnit — 787 tests au total (dont 31 d'intégration gRPC).

## Commandes

```powershell
.\pipeline.ps1 -DoTests           # Tests unitaires
.\pipeline.ps1 -DoPublish         # Publication self-contained
.\pipeline.ps1 -Clean -Restore    # Nettoyage + restauration NuGet
```

## Conventions Git

- **Git LFS** : les binaires et médias sont stockés via LFS (voir `.gitattributes`). Un `git lfs install` est nécessaire au clone.
- Branche de développement : `dev` ; branche principale : `main`.
- **Commits** : ne jamais ajouter de trailer `Co-Authored-By` ni de mention de co-auteur ; l'auteur
  reste seul auteur. Pas de mention « Generated with Claude Code » dans les PR/issues sauf demande explicite.

## Langue

Le projet est francophone : README, commentaires, commits et documentation en français (avec accents corrects).
