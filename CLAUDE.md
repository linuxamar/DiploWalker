# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Projet

**Diplo** — système distribué de microservices gRPC pour la gestion de conteneurs Windows (cf. `README.md`).

## Architecture

10 projets source (.NET 10, F#) + 5 projets de test :

| Projet | Rôle |
|--------|------|
| Diplo.Abstractions | Interfaces partagées, validation, sécurité |
| Diplo.Container | Service gRPC de gestion des conteneurs (containerd) |
| Diplo.Volume | Service gRPC de gestion des volumes persistants |
| Diplo.Network | Service gRPC de gestion des réseaux (CNI) |
| Diplo.Installer | Installation Windows (services, containerd, CNI) |
| Diplo.Grpc | Proto definitions et code généré C# |
| Diplo.Contracts | Types partagés entre services |
| Diplo.Core | Clients gRPC, abstraction `IOutputPort` |
| Diplo.Cli | Client CLI (Spectre.Console) |
| Diplo.Gui | Interface graphique Avalonia |

## Stack

- **.NET 10** (`dotnet 10.0.302` installé localement).
- Orientation **F#** (services, drivers, CLI) + C# (gRPC généré).
- **Tests** : xUnit v3 + FsUnit.xUnit — 263 tests au total.

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
