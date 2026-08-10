# Diplo

Système distribué de microservices gRPC pour la gestion de conteneurs Windows.

## Architecture

Diplo est composé de quatre services principaux communiquant via gRPC :

| Service | Port | Description |
|---------|------|-------------|
| **Diplo.Container** | 5001 | Cycle de vie des conteneurs via containerd — création, démarrage, arrêt, suppression et montage de volumes |
| **Diplo.Volume** | 5002 | Gestion des volumes persistants |
| **Diplo.Network** | 5003 | Gestion des réseaux de conteneurs (NAT, overlay, l2bridge) |
| **Diplo.Installer** | — | Installation et configuration de l'ensemble du système |

### Clients

- **CLI** : `Diplo.Cli` (Spectre.Console) — toutes les opérations de conteneurs, volumes et réseaux
- **GUI** : `Diplo.Gui` (Avalonia) — interface graphique native multi-plateforme avec MVVM

## Stack technique

- **Runtime** : .NET 10, F#
- **Communication** : gRPC
- **Conteneurs** : containerd (1.6.x LTS pour WS2016, 1.7.x pour WS2019+)
- **Réseau** : Plugins CNI Microsoft + standards (bridge, host-local, portmap)
- **Tests** : xUnit (553 tests)
- **Santé** : gRPC Health Checks (/healthz) + arrêt gracieux (IHostApplicationLifetime)

## Compatibilité Windows Server

### Versions supportées

| Version | Build | containerd | Statut |
|---------|-------|------------|--------|
| Windows Server 2016 LTSC | ≤ 14393 | 1.6.x (LTS) | ✅ Supporté |
| Windows Server 2019 LTSC | ≤ 17763 | 1.7.x | ✅ Supporté |
| Windows Server 2022 LTSC | ≤ 20348 | 1.7.x | ✅ Supporté |
| Windows Server 2025 LTSC | > 20348 | 1.7.x | ✅ Supporté |

### Nano Server vs Server Core

Le choix de l'image de base Windows est crucial pour le fonctionnement de Diplo :

| Capacité | Server Core | Nano Server |
|----------|------------|-------------|
| **Planificateur de tâches** (schtasks) | ✅ Complet — service Schedule présent, gestion CLI/PowerShell entièrement fonctionnelle | ⚠️ Partiel — la commande `schtasks` fonctionne en ligne de commande, mais le service Schedule est limité. Pas de PowerShell, pas de GUI. Fiable uniquement pour des scénarios simples. |
| **Services Windows** (SCM) | ✅ Complet — Service Control Manager complet. `sc.exe create/start/stop/delete` entièrement fonctionnel. Applications .NET Worker Service hôteables. | ✅ Basique — le SCM (composant noyau) est présent. `sc.exe create`, `net start/stop` fonctionnent pour des services .NET Core/self-contained. Certains composants système manquants (AFD, TCP/IP Protocol Driver) peuvent limiter des services dépendants de pilotes réseau spécifiques. |
| **containerd / conteneurs Windows** | ✅ Complet | ✅ Complet |
| **Poids de l'image** | ~2 Go (compressé) | ~175 Mo (compressé) |
| **.NET Framework traditionnel** | ✅ Supporté | ❌ Non disponible |
| **.NET Core / .NET 10 (self-contained)** | ✅ Supporté | ✅ Supporté |
| **PowerShell** | ✅ Disponible | ❌ Non inclus par défaut |

### Recommandation

**Server Core** est recommandé pour Diplo lorsque :
- Le planificateur de tâches (`schtasks`) est nécessaire pour des automatisations
- Des services Windows robustes avec dépendances système sont requis
- Un environnement PowerShell est souhaité pour l'administration

**Nano Server** peut être utilisé lorsque :
- Seuls des microservices .NET self-contained légers sont déployés
- Le planificateur de tâches n'est pas requis
- La taille minimale de l'image est prioritaire

> **Note** : Diplo utilise principalement `sc.exe create/start/stop` pour la gestion des services, ce qui fonctionne sur les deux images. Cependant, si des automatisations basées sur le planificateur de tâches sont ajoutées ultérieurement, Server Core devient indispensable.

## Installation

### Prérequis

- Windows Server 2016 ou plus récent (Server Core recommandé)
- .NET 10 Runtime
- Droits administrateur
- Aucune virtualisation requise (isolation process uniquement, pas de Hyper-V)

### Installation automatique

```powershell
Diplo.Installer.exe install
```

L'installateur effectue automatiquement :
1. Détection de la version de Windows Server
2. Téléchargement et installation de containerd (1.6.x LTS ou 1.7.x selon la version)
3. Installation des plugins CNI Microsoft (nat, overlay, l2bridge)
4. Installation des plugins CNI standards (bridge, host-local, portmap)
5. Génération de la configuration containerd (`config.toml`)
6. Création de la configuration CNI par défaut (réseau NAT `172.20.0.0/16`)
7. Création des services Windows (Diplo.Container, Diplo.Volume, Diplo.Network)

### Commandes

```powershell
Diplo.Installer.exe install      # Installation complète
Diplo.Installer.exe uninstall    # Suppression des services
Diplo.Installer.exe status       # État des services
```

### Démarrage des services

```powershell
sc.exe start "Diplo.Container"
sc.exe start "Diplo.Volume"
sc.exe start "Diplo.Network"
```

## Développement

### Build

```powershell
.\pipeline.ps1
```

Options disponibles :
- `-Clean` : Nettoyage avant build
- `-Restore` : Restauration des packages NuGet
- `-DoTests` : Exécution des tests
- `-DoPublish` : Publication des exécutables

### Structure du projet

```
Diplo/
├── src/
│   ├── Diplo.Abstractions/     # Interfaces partagées, validation, sécurité, ServerConfig
│   ├── Diplo.Container/        # Service gRPC de gestion des conteneurs
│   ├── Diplo.Volume/           # Service gRPC de gestion des volumes
│   ├── Diplo.Network/          # Service gRPC de gestion des réseaux
│   ├── Diplo.Installer/        # Outil d'installation Windows
│   ├── Diplo.Grpc/             # Types messages et services gRPC (protobuf-net)
│   ├── Diplo.Contracts/        # Types partagés entre services
│   ├── Diplo.Core/             # Clients gRPC, abstraction IOutputPort
│   ├── Diplo.Disk/             # Montage d'images disque (qcow2, raw, vhd, vhdx, vmdk)
│   ├── Diplo.Cli/              # Client CLI (Spectre.Console)
│   └── Diplo.Gui/              # Interface graphique Avalonia
├── tests/
│   ├── Diplo.Abstractions.Tests/
│   ├── Diplo.Contracts.Tests/
│   ├── Diplo.Core.Tests/
│   ├── Diplo.Container.Tests/
│   ├── Diplo.Installer.Tests/
│   ├── Diplo.Network.Tests/
│   ├── Diplo.Volume.Tests/
│   ├── Diplo.Disk.Tests/
│   ├── Diplo.Cli.Tests/
│   ├── Diplo.Gui.Tests/
│   └── Diplo.Integration.Tests/
├── pipeline.ps1                # Pipeline de build et déploiement
└── README.md
```

## Aperçu technique

### Configuration containerd

Le fichier `config.toml` est généré automatiquement par l'installateur avec :
- Runtime `runhcs.v1` avec isolation process (pas de Hyper-V)
- Sandbox image : `mcr.microsoft.com/windows/nanoserver:{version}`
- Plugins CNI configurés pour le réseau NAT
- Métriques et événements sur `127.0.0.1:1338` / `127.0.0.1:1339`
- Adaptatif entre containerd 1.6.x (WS2016) et 1.7.x (WS2019+)

### Isolation

Diplo utilise l'**isolation process** (pas d'isolation Hyper-V) :
- Plus léger et plus rapide à démarrer
- Compatible avec Windows Server 2016+
- Ne nécessite pas de virtualisation matérielle
- Partage le noyau hôte avec les conteneurs

## Montage de volumes et d'images disque

À la création, un conteneur peut monter des volumes persistants, sous forme de **répertoires de l'hôte** ou d'**images disque** (qcow2, raw, vhd, vhdx, vmdk) gérées par `Diplo.Disk`. Le montage se fait en bind (`rbind`), en lecture-écriture par défaut.

### CLI

```powershell
diplo container create <image> <nom> --mount "src=C:\donnees,dst=C:\conteneur\donnees"
diplo container create <image> <nom> --mount "src=C:\donnees,dst=C:\conteneur\donnees,ro"
```

- `src` : répertoire de l'hôte ou chemin vers une image disque
- `dst` : destination dans le conteneur
- `ro` (optionnel) : montage en lecture seule

Les sources sont restreintes aux répertoires autorisés par la validation de sécurité (`%TEMP%`, `%ProgramData%\Diplo`, `%ProgramFiles%\Diplo`). Une image disque est montée via un répertoire de préparation pour la durée de vie du conteneur, puis réécrite à la suppression.

### GUI

L'onglet **Conteneurs** expose un champ « Montages: » au format identique (`src=...,dst=...[;ro]`), avec un montage par ligne ou séparé par des points-virgules.

### Validation manuelle sur un hôte containerd réel

Le montage réel des images disque (via `ctr --mount`) n'est pas automatisable dans les tests : il nécessite un hôte Windows avec containerd installé. Procédure de validation manuelle :

1. **Images de test** — créer une image FAT à partir du répertoire de travail :
   ```powershell
   $img = "$env:TEMP\diplo-test.img"
   # Fabriquer l'image : prévoir un utilitaire (ex. DiscUtils) ou un outil
   # externe ; une image qcow2 peut être créée avec qemu-img.
   qemu-img create -f qcow2 $img 64M
   ```
2. **Démarrer le service** :
   ```powershell
   dotnet run --project src\Diplo.Container -p:Platform=x64
   ```
3. **Créer un conteneur avec l'image montée** :
   ```powershell
   dotnet run --project src\Diplo.Cli -p:Platform=x64 -- container create mcr.microsoft.com/windows/nanoserver:ltsc2022 mon-conteneur --mount "src=$img,dst=C:\data"
   ```
4. **Vérifier** que `ctr --mount type=bind,src=<staging>,dst=C:\data,options=rbind` est bien passé à containerd (trace du service) et que le contenu de l'image est visible dans `C:\data` du conteneur.
5. **Supprimer le conteneur** (`diplo container delete mon-conteneur`) et vérifier que l'image a été réécrite : le contenu produit dans le conteneur (ex. `C:\data\resultat.txt`) est relu dans l'image via un nouvel `extract`.
