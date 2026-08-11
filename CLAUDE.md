# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Projet

**Diplo** — système distribué de microservices gRPC pour la gestion de conteneurs Windows (cf. `README.md`).

## Architecture

11 projets source (.NET 10, F#) + 11 projets de test :

| Projet | Rôle |
|--------|------|
| Diplo.Abstractions | Interfaces partagées, validation, sécurité |
| Diplo.Container | Service gRPC de gestion des conteneurs (containerd) — création, cycle de vie, montage de volumes (`--mount`) |
| Diplo.Volume | Service gRPC de gestion des volumes persistants |
| Diplo.Network | Service gRPC de gestion des réseaux (CNI) |
| Diplo.Installer | Installation Windows (services, containerd, CNI) |
| Diplo.Grpc | Types messages et interfaces de service gRPC (protobuf-net, code-first) |
| Diplo.Contracts | Types partagés entre services |
| Diplo.Core | Clients gRPC, abstraction `IOutputPort`, `MountParser` (format `src=...,dst=...[;ro]`) |
| Diplo.Disk | Montage d'images disque (qcow2 maison, raw, vhd, vhdx, vmdk) via DiscUtils |
| Diplo.Cli | Client CLI (Spectre.Console) |
| Diplo.Gui | Interface graphique Avalonia |

## Stack

- **.NET 10** (`dotnet 10.0.302` installé localement).
- Orientation **100 % F#** (services, drivers, CLI et gRPC en code-first protobuf-net).
- **Tests** : xUnit v3 + FsUnit.xUnit — 620 tests au total (dont 29 d'intégration gRPC).

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
