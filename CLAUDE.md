# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Projet

**Diplo** — système distribué de microservices gRPC pour la gestion de conteneurs Windows (cf. `README.md`).

## Architecture

12 projets source (.NET 10, F#) + 11 projets de test :

| Projet | Rôle |
|--------|------|
| Diplo.Abstractions | Interfaces partagées, validation, sécurité |
| Diplo.Container | Service gRPC de gestion des conteneurs (containerd) |
| Diplo.Volume | Service gRPC de gestion des volumes persistants |
| Diplo.Network | Service gRPC de gestion des réseaux (CNI) |
| Diplo.Installer | Installation Windows (services, containerd, CNI) |
| Diplo.Grpc | Types messages et interfaces de service gRPC (protobuf-net, code-first) |
| Diplo.Contracts | Types partagés entre services |
| Diplo.Core | Clients gRPC, abstraction `IOutputPort` |
| Diplo.Cli | Client CLI (Spectre.Console) |
| Diplo.Gui | Interface graphique Avalonia |
| Diplo.Linux | Émulateur de machine Linux x86-64 en F# (boot ISO, noyau, syscalls) |
| Diplo.Linux.Cli | CLI `diplo-linux.exe` (boot, trace, pas à pas, points d'arrêt) |

## Stack

- **.NET 10** (`dotnet 10.0.302` installé localement).
- Orientation **100 % F#** (services, drivers, CLI et gRPC en code-first protobuf-net).
- **Tests** : xUnit v3 + FsUnit.xUnit — plus de 580 tests au total (dont 27 d'intégration gRPC).

## Commandes

```powershell
.\pipeline.ps1 -DoTests           # Tests unitaires
.\pipeline.ps1 -DoPublish         # Publication self-contained
.\pipeline.ps1 -Clean -Restore    # Nettoyage + restauration NuGet
```

## Émulateur Linux

`Diplo.Linux` émule une machine Linux x86-64 en F# :
- Chargement de noyaux ELF bruts et boot depuis une image ISO (`diplo-linux.exe boot <iso> /chemin/vmlinuz <args...>`).
- Mode noyau, console série COM1, gestion mémoire (mmap), table de syscalls x86-64.
- CLI `diplo-linux.exe` (`Diplo.Linux.Cli`) : trace `-t`, pas à pas `-s`, points d'arrêt `-b <adresse>`.
- Limites actuelles : `vmlinuz` Ubuntu est un bzImage non supporté — GRUB, PCI, ACPI et timers non implémentés.

Configurations prêtes à l'emploi dans `configs/` :

```powershell
.\configs\OwnLinuxBase\Download-UbuntuServer.ps1   # Télécharge l'ISO Ubuntu Server
.\configs\OwnLinuxBase\Launch-OwnLinuxBase.ps1     # Lance l'émulateur diplo-linux.exe
```

## Conventions Git

- **Git LFS** : les binaires et médias sont stockés via LFS (voir `.gitattributes`). Un `git lfs install` est nécessaire au clone.
- Branche de développement : `dev` ; branche principale : `main`.
- **Commits** : ne jamais ajouter de trailer `Co-Authored-By` ni de mention de co-auteur ; l'auteur
  reste seul auteur. Pas de mention « Generated with Claude Code » dans les PR/issues sauf demande explicite.

## Langue

Le projet est francophone : README, commentaires, commits et documentation en français (avec accents corrects).
