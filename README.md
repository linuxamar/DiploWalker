# Diplo

**Diplo est un système distribué d'hébergement et de gestion de microservices
dans un ou plusieurs conteneurs Windows.** Le système couvre la chaîne
complète, de la préparation de l'image jusqu'à l'exploitation quotidienne :
création des conteneurs, réseau, volumes persistants et images disque.

Le principe est celui d'une plateforme de service : le poste de travail
n'héberge **ni runtime de conteneurs ni état**, il ne porte que le client
d'administration. Les services qui exercent réellement l'autorité sur les
conteneurs sont déployés sur la ou les machines Windows Server, et le client
n'émet que des requêtes. Une seule opération échappe à cette règle : la
création d'une image disque s'exécute dans le processus du client (voir « Le
contenu du conteneur est géré par des outils centralisés et dédiés »).

### Le conteneur est l'hôte des microservices

Le conteneur est le point d'exécution des microservices : c'est lui qui les
héberge et qui porte leur cycle de vie. Une image de conteneur Windows Server
Core ou Nano Server contient les microservices empaquetés, généralement
self-contained, enregistrés comme services Windows auprès du Service Control
Manager (`sc.exe create/start/stop`) afin d'être démarrés automatiquement
avec le conteneur.

Ce choix a des conséquences directes :

- **Un conteneur = un ensemble cohérent de microservices.** Ils partagent le
  même système de fichiers, la même configuration et le même cycle de vie.
- **Le redémarrage du conteneur redémarre les microservices.** Aucune
  orchestration tierce n'est nécessaire pour les remettre en route.
- **L'isolation est celle du processus**, pas de l'hyperviseur (`runhcs.v1`) :
  aucun Hyper-V requis, démarrage rapide, mais noyau partagé avec l'hôte.
- **Le format d'image est standard.** Les images s'échangent via les
  registres publics et privés (`docker.io`, `quay.io`, `mcr.microsoft.com`,
  `ghcr.io`).

Diplo n'intervient pas dans le code des microservices : c'est une plateforme
d'exécution et d'exploitation, pas un framework applicatif.

### Le conteneur se pilote depuis un client installé sur le poste

L'administration ne se fait pas sur la machine qui porte les conteneurs. Elle
se fait depuis le poste de travail, au moyen d'un **client installé
localement** qui dialogue avec les services distants en gRPC.

- **Le client se compose de deux interfaces** : la CLI `diplo`
  (`DiploWalker.Cli`, Spectre.Console) et une interface graphique native
  (`DiploWalker.Gui`, Avalonia, MVVM). Elles exposent les mêmes opérations.
- **Le poste de travail ne contient aucun conteneur.** Il ne porte ni
  containerd, ni service Diplo, et les images qu'il produit ne sont pas sous
  son autorité : c'est un poste d'administration, éventuellement plusieurs,
  pouvant tous cibler le même hôte.
- **La cible est configurable par service** dans `DiploWalker.json`
  (adresses, `namespace`, `logLevel`), ce qui permet d'administrer un hôte
  distant comme un hôte local.
- **Deux transports sont disponibles** : TCP (`localhost:5001-5003` en
  Debug, `6001-6003` en Release) et named pipes (`diplo-container`,
  `diplo-volume`, `diplo-network`), ce dernier privilégié pour un usage
  strictement local.
- **L'état est consultable à tout moment** : `diplo status check`, liste des
  conteneurs, inspection, journaux, métriques et processus.

### Le contenu du conteneur est géré par des outils centralisés et dédiés

Ce qui fait autorité sur le contenu des conteneurs — leur identité, leur
réseau, leurs volumes, leurs images — n'est pas porté par le client, mais par
des **outils centralisés et dédiés à une seule fonction**, déployés sur
l'hôte :

| Outil dédié                 | Nature              | Rôle                                                                                                                     |
| --------------------------- | ------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| **DiploWalker.Container**   | Service gRPC :5001/6001 | Cycle de vie des conteneurs via containerd : création, démarrage, arrêt, suppression, montage de volumes, journaux, `exec` |
| **DiploWalker.Volume**      | Service gRPC :5002/6002 | Volumes persistants, sous forme de répertoires de l'hôte ou d'images disque                                                  |
| **DiploWalker.Network**     | Service gRPC :5003/6003 | Réseaux de conteneurs (NAT, overlay, l2bridge) et plugins CNI                                                               |
| **DiploWalker.Disk**        | Bibliothèque, sans service | Images disque : création (raw, vhd, vhdx, vmdk, vdi), montage et réécriture (qcow2, qcow1, parallels, vdi, dmg) ; l'ISO se monte en lecture seule |

Les trois premiers sont des **services Windows** indépendants, joignables à
distance par le client. Le quatrième est une **bibliothèque** : il n'a ni port ni
processus propre, et Diplo n'expose aucun service pour lui. Il est référencé par
les services comme par le client lui-même, ce qui a une conséquence :
`diplo disk create-image` s'exécute dans le processus du client, alors que le
montage et la réécriture d'une image dans un conteneur relèvent du service
Volume, sur l'hôte.

Ce découpage a trois effets :

- **Une seule autorité par fonction.** Le client ne modifie jamais le système
  de fichiers de l'hôte : il émet une requête, le service dédié décide.
- **Une configuration uniformisée.** Les mêmes règles — registres autorisés,
  chemins de montage restreints, limites de taille, volumes de contrôle — s'appliquent
  quelle que soit l'interface utilisée, CLI ou GUI.
- **Un déploiement standardisé.** `DiploWalker.Installer` installe et configure
  ces services comme services Windows, ce qui rend le déploiement d'un hôte
  reproductible d'un serveur à l'autre.

### Vue d'ensemble

```
Poste de travail (client)               Hôte Windows Server (autorité)
┌──────────────────────────────┐        ┌──────────────────────────────────────┐
│  diplo (CLI)                 │        │  DiploWalker.Container   :5001/6001  │
│  DiploWalker.Gui (Avalonia)  │──gRPC─▶│  DiploWalker.Volume      :5002/6002  │
│                              │        │  DiploWalker.Network     :5003/6003  │
│  DiploWalker.json            │        │  DiploWalker.Installer               │
└──────────────────────────────┘ pipes  │                                      │
                                        │  containerd → runhcs.v1              │
                                        │  ┌────────────────────────────────┐  │
                                        │  ┆ Conteneur : microservices .NET ┆  │
                                        │  ┆ services Windows (sc.exe)      ┆  │
                                        │  └────────────────────────────────┘  │
                                        └──────────────────────────────────────┘
```

## Architecture

Diplo est composé de quatre services principaux communiquant via gRPC :

| Service             | Port | Description                                                                                                |
| ------------------- | ---- | ---------------------------------------------------------------------------------------------------------- |
| **DiploWalker.Container** | 5001 / 6001 | Cycle de vie des conteneurs via containerd — création, démarrage, arrêt, suppression et montage de volumes |
| **DiploWalker.Volume**    | 5002 / 6002 | Gestion des volumes persistants                                                                            |
| **DiploWalker.Network**   | 5003 / 6003 | Gestion des réseaux de conteneurs (NAT, overlay, l2bridge)                                                 |

| **DiploWalker.Installer** | —    | Installation et configuration de l'ensemble du système                                                     |

> **Ports Debug / Release** : en configuration **Debug**, les services écoutent sur 5001-5003 ; en **Release** (+ installation via l'installateur), sur **6001-6003**. Les deux plages permettent une exécution simultanée. Les named pipes (`diplo-container`, `diplo-volume`, `diplo-network`) sont identiques dans les deux configurations.

### Clients

- **CLI** : `DiploWalker.Cli` (Spectre.Console) — toutes les opérations de conteneurs, volumes, réseaux, images disque (`disk create-image`) et recherche d'images en ligne
- **GUI** : `DiploWalker.Gui` (Avalonia) — interface graphique native multi-plateforme avec MVVM
- **Résilience** : les canaux gRPC (`DiploChannel`) appliquent une politique de reprise automatique (5 tentatives, backoff exponentiel) sur les échecs `Unavailable` (service en cours de redémarrage) ; les appels streaming ne sont pas rejoués.

## Stack technique

- **Runtime** : .NET 10, F#
- **Communication** : gRPC
- **Conteneurs** : containerd (1.6.x LTS pour WS2016, 1.7.x pour WS2019+)
- **Réseau** : Plugins CNI Microsoft + standards (bridge, host-local, portmap)
- **Tests** : xUnit v4 (1 627 tests sous Windows, dont 1 ignoré ; 1 626 hors Windows, le test `Platform=Windows` étant exclu)
- **Santé** : gRPC Health Checks (/healthz) + arrêt gracieux (IHostApplicationLifetime)

## Compatibilité Windows Server

### Versions supportées

| Version                  | Build   | containerd  | Statut      |
| ------------------------ | ------- | ----------- | ----------- |
| Windows Server 2016 LTSC | ≤ 14393 | 1.6.x (LTS) | ✅ Supporté |
| Windows Server 2019 LTSC | ≤ 17763 | 1.7.x       | ✅ Supporté |
| Windows Server 2022 LTSC | ≤ 20348 | 1.7.x       | ✅ Supporté |
| Windows Server 2025 LTSC | > 20348 | 1.7.x       | ✅ Supporté |

### Nano Server vs Server Core

Le choix de l'image de base Windows est crucial pour le fonctionnement de Diplo :

| Capacité                                 | Server Core                                                                                                                                          | Nano Server                                                                                                                                                                                                                                                                              |
| ---------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Planificateur de tâches** (schtasks)   | ✅ Complet — service Schedule présent, gestion CLI/PowerShell entièrement fonctionnelle                                                              | ⚠️ Partiel — la commande `schtasks` fonctionne en ligne de commande, mais le service Schedule est limité. Pas de PowerShell, pas de GUI. Fiable uniquement pour des scénarios simples.                                                                                                   |
| **Services Windows** (SCM)               | ✅ Complet — Service Control Manager complet. `sc.exe create/start/stop/delete` entièrement fonctionnel. Applications .NET Worker Service hôteables. | ✅ Basique — le SCM (composant noyau) est présent. `sc.exe create`, `net start/stop` fonctionnent pour des services .NET Core/self-contained. Certains composants système manquants (AFD, TCP/IP Protocol Driver) peuvent limiter des services dépendants de pilotes réseau spécifiques. |
| **containerd / conteneurs Windows**      | ✅ Complet                                                                                                                                           | ✅ Complet                                                                                                                                                                                                                                                                               |
| **Poids de l'image**                     | ~2 Go (compressé)                                                                                                                                    | ~175 Mo (compressé)                                                                                                                                                                                                                                                                      |
| **.NET Framework traditionnel**          | ✅ Supporté                                                                                                                                          | ❌ Non disponible                                                                                                                                                                                                                                                                        |
| **.NET Core / .NET 10 (self-contained)** | ✅ Supporté                                                                                                                                          | ✅ Supporté                                                                                                                                                                                                                                                                              |
| **PowerShell**                           | ✅ Disponible                                                                                                                                        | ❌ Non inclus par défaut                                                                                                                                                                                                                                                                 |

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
DiploWalker.Installer.exe install
```

L'installateur effectue automatiquement :

1. Détection de la version de Windows Server
2. Téléchargement et installation de containerd (1.6.x LTS ou 1.7.x selon la version)
3. Installation des plugins CNI Microsoft (nat, overlay, l2bridge)
4. Installation des plugins CNI standards (bridge, host-local, portmap)
5. Génération de la configuration containerd (`config.toml`)
6. Création de la configuration CNI par défaut (réseau NAT `172.20.0.0/16`)
7. Création des services Windows (DiploWalker.Container, DiploWalker.Volume, DiploWalker.Network)
8. Import des certificats PKI racine et intermédiaires dans les magasins de certificats de la machine

#### Certificats PKI

L'installateur NSIS embarque les certificats de la PKI Diplo et les importe automatiquement dans les magasins de certificats de la machine Windows :

| Certificat         | Magasin                     | Rôle                                         |
| ------------------ | --------------------------- | -------------------------------------------- |
| `Diplo Root CA`    | Racines de confiance (Root) | Autorité racine de la PKI                    |
| `Authentification` | CA intermédiaires           | Authentification de services                 |
| `CodeSigning`      | CA intermédiaires           | Signature de code (assemblies, installateur) |
| `System`           | CA intermédiaires           | Certificats système (TLS, config)            |

Cette importation permet la **validation automatique des chaînes de signature** sans manipulation manuelle — les binaires signés par la PKI Diplo sont reconnus nativement par Windows.

Le désinstalleur retire les certificats des magasins machine et supprime les fichiers `.crt.pem` déposés dans le dossier d'installation.

### Installation autonome de containerd/nerdctl (script Microsoft)

Le dépôt fournit le script officiel Microsoft
[`install-containerd-runtime.ps1`](https://github.com/microsoft/Windows-Containers/blob/Main/helpful_tools/Install-ContainerdRuntime/install-containerd-runtime.ps1)
(microsoft/Windows-Containers), vendoré tel quel dans `setup/`. Il permet
d'installer et de configurer containerd et nerdctl de manière autonome, sans
l'installateur Diplo :

```powershell
# Élevé (PowerShell administrateur)
.\setup\install-containerd-runtime.ps1
```

Le script, exécuté en tant qu'administrateur :

- Active la fonctionnalité Windows **Containers** (et Hyper-V avec `-HyperV`) ;
- Télécharge et installe containerd, nerdctl et les plugins CNI (dernières
  versions par défaut via l'API GitHub, épinglables via `-ContainerDVersion`,
  `-NerdCTLVersion` et `-WinCNIVersion`) ;
- Ajoute le PATH (registre machine) ;
- Génère `config.toml` et enregistre containerd comme service Windows ;
- Attend la disponibilité via `nerdctl version`.

Paramètres notables : `-ExternalNetAdapter` (réseau DHCP), `-ContainerBaseImage`,
`-TransparentNetwork`, `-NoRestart`/`-Force`.

> **Remarque** : ce script installe dans `C:\Program Files\containerd` et
> `C:\Program Files\nerdctl` et enregistre containerd comme service Windows,
> mais **ne crée pas** les services Diplo ni leur configuration. Pour une
> installation Diplo complète (services + `config.toml` Diplo), privilégiez
> `DiploWalker.Installer.exe install`.

### Commandes

```powershell
DiploWalker.Installer.exe install      # Installation complète
DiploWalker.Installer.exe uninstall    # Suppression des services
DiploWalker.Installer.exe status       # État des services
```

### Démarrage des services

```powershell
sc.exe start "DiploWalker.Container"
sc.exe start "DiploWalker.Volume"
sc.exe start "DiploWalker.Network"
```

### Configuration du client (DiploWalker.json)

Les clients (CLI et GUI) résolvent l'adresse de chaque service via le fichier `DiploWalker.json`. Le fichier est cherché par priorité :

1. `%DIPLO_CONFIG_HOME%\DiploWalker.json` si la variable d'environnement est définie (recommandé pour la GUI et les installations : chemin stable, indépendant du répertoire courant) ;
2. `DiploWalker.json` dans le répertoire courant.

S'il est absent ou mal formé, les clients retombent sur les adresses par défaut (`localhost:5001`/`5002`/`5003` en Debug, `localhost:6001`/`6002`/`6003` en Release).

```powershell
diplo config init                       # génère DiploWalker.json avec le transport TCP par défaut
diplo config init --transport pipe      # génère DiploWalker.json avec des adresses par named pipes
diplo config init --path C:\etc\DiploWalker.json --transport pipe
```

```json
{
    "container": {
        "address": "http://pipe:/diplo-container",
        "namespace": "default"
    },
    "volume": { "address": "http://pipe:/diplo-volume" },
    "network": { "address": "http://pipe:/diplo-network" },
    "logLevel": "Information"
}
```

- `tcp` : adresses `localhost:<port>` (http ajouté automatiquement si absent).
- `pipe` : adresses `http://pipe:/<nom>` — canal local par named pipe (transport privilégié sur la machine, aucun port exposé). Les noms correspondent aux tubes créés par l'installateur (`diplo-container`, `diplo-volume`, `diplo-network`).
- Les adresses `http://pipe:/...` sont validées (hôte local uniquement, nom de tube sans `\` ni `..`).
- La GUI propose un onglet **Paramètres** pour éditer ces adresses (écriture de `DiploWalker.json`, champs `namespace` et `logLevel` conservés) ; la configuration est appliquée dès les opérations suivantes, sans redémarrage.

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
- `-SignCert <pfx> [-SignPassword <mot-de-passe>]` : signe les installateurs avec un certificat PFX
- `-SignThumbprint <empreinte>` : signe avec un certificat du magasin (par empreinte SHA-1/SHA-256)
- `-TimestampUrl <url>` : horodate les binaires publiés (RFC 3161, ex. `http://timestamp.digicert.com`)

`signtool.exe` est recherché dans le PATH puis dans les Windows Kits installés. Sans certificat disponible, la signature est ignorée (simple avertissement).

Les clés de signature de la PKI sont versionnées mais chiffrées par git-crypt : un poste neuf doit d'abord `git-crypt unlock <clé>`, faute de quoi la publication produit des binaires non signés. Voir `certificates/README.md`.

### Structure du projet

```
Diplo/
├── src/
│   ├── DiploWalker.Abstractions/     # Interfaces, validation, sécurité, modules mutualisés
│   │   ├── JsonHelpers.fs      # Extraction typée de propriétés JSON
│   │   ├── DiploWalkerJson.fs        # Options sérialisation centralisées
│   │   ├── ProcessExec.fs      # Exécution processus + PowerShell
│   │   ├── ServiceGuards.fs    # Guards de validation d'entrée
│   │   ├── CachedConfig.fs     # Cache générique avec invalidation
│   │   ├── Security.fs         # Validation d'entrée, anti-injection
│   │   ├── ServerConfig.fs     # Configuration Kestrel / named pipes
│   │   └── DiploWalkerPorts.fs       # Ports gRPC partagés (#if DEBUG 5001-5003 / Release 6001-6003)
│   ├── DiploWalker.Container/        # Service gRPC de gestion des conteneurs
│   ├── DiploWalker.Volume/           # Service gRPC de gestion des volumes
│   ├── DiploWalker.Network/          # Service gRPC de gestion des réseaux
│   ├── DiploWalker.Installer/        # Outil d'installation Windows
│   ├── DiploWalker.Grpc/             # Types messages, services gRPC, DriverMappings
│   ├── DiploWalker.Contracts/        # Types partagés entre services
│   ├── DiploWalker.Core/             # Clients gRPC, GrpcClientFactory, DiploConfig
│   ├── DiploWalker.Disk/             # Montage et création d'images disque (qcow2, qcow1, raw, vhd, vhdx, vmdk, vdi, dmg, parallels, iso)
│   │   ├── BinaryIo.fs         # Lecture/écriture binaire + helper `protect`
│   │   ├── DiscFsHelper.fs     # Fonctions DiscUtils partagées + `realFrom` (anti-traversal)
│   │   ├── RawImageStream.fs   # Classe de base Stream des pilotes maison (Qcow1/Qcow2/Parallels)
│   │   ├── Qcow1Fs.fs / Qcow2.fs / ParallelsFs.fs / VdiFs.fs / DmgFs.fs / FsImage.fs
│   │   ├── IsoFs.fs            # Parseur ISO9660/UDF + générateur ISO9660 niveau 1
│   ├── DiploWalker.Cli/              # Client CLI (Spectre.Console)
│   ├── DiploWalker.Gui/              # Interface graphique Avalonia
│   │   ├── Views/MainWindow.axaml(.fs)
│   │   ├── UserControls/ContainerDetailUserControl.axaml(.fs)
│   │   ├── ViewModels/
│   │   ├── Services/
│   │   ├── App.axaml(.fs)
│   │   └── Program.fs
├── tests/
│   ├── DiploWalker.Abstractions.Tests/
│   ├── DiploWalker.Cli.Tests/
│   ├── DiploWalker.Container.Tests/
│   ├── DiploWalker.Core.Tests/
│   ├── DiploWalker.Disk.Tests/
│   ├── DiploWalker.Gui.Tests/
│   ├── DiploWalker.Installer.Tests/
│   ├── DiploWalker.Integration.Tests/
│   ├── DiploWalker.Network.Tests/
│   ├── DiploWalker.TestHelpers/
│   └── DiploWalker.Volume.Tests/
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

À la création, un conteneur peut monter des volumes persistants, sous forme de **répertoires de l'hôte** ou d'**images disque** gérées par `DiploWalker.Disk`. Le montage se fait en bind (`rbind`), en lecture-écriture par défaut.

| Format        | Extension(s)                   | R/W           | Moteur                                       |
| ------------- | ------------------------------ | ------------- | -------------------------------------------- |
| **Qcow2**     | `.qcow2`                       | R/W           | Pilote maison (`Qcow2Stream`)                |
| **QCOW v1**   | `.qcow`                        | R/W           | Pilote maison (`Qcow1Stream`)                |
| **VHD**       | `.vhd`                         | R/W           | DiscUtils                                    |
| **VHDX**      | `.vhdx`                        | R/W           | DiscUtils                                    |
| **VMDK**      | `.vmdk`                        | R/W           | DiscUtils                                    |
| **VDI**       | `.vdi`                         | R/W           | DiscUtils                                    |
| **Raw**       | `.img`, `.raw`, `.bin`         | R/W           | DiscUtils (FAT, NTFS, ext)                   |
| **DMG**       | `.dmg`                         | Lecture seule | DiscUtils                                    |
| **Parallels** | `.hdd`, `.hds`                 | R/W           | Pilote maison (`ParallelsStream`)            |
| **ISO**       | `.iso`, `.udf`                 | Lecture seule | Parseur maison (`IsoFs.fs`) ISO9660/UDF      |
| **Btrfs**     | (via Hawkynt)                  | R/W           | Hawkynt.FileFormats.FileSystems (seuil 2 Go) |
| **XFS**       | (via Hawkynt)                  | R/W           | Hawkynt.FileFormats.FileSystems (seuil 2 Go) |
| **HFS+**      | (via Hawkynt)                  | R/W           | Hawkynt.FileFormats.FileSystems (seuil 2 Go) |

### CLI

```powershell
diplo container create <image> <nom> --mount "src=C:\donnees,dst=C:\conteneur\donnees"
diplo container create <image> <nom> --mount "src=C:\donnees,dst=C:\conteneur\donnees,ro"
```

- `src` : répertoire de l'hôte ou chemin vers une image disque
- `dst` : destination dans le conteneur
- `ro` (optionnel) : montage en lecture seule

Les sources sont restreintes aux répertoires autorisés par la validation de sécurité (`%TEMP%`, `%ProgramData%\Diplo`, `%ProgramFiles%\Diplo`). Une image disque est montée via un répertoire de préparation pour la durée de vie du conteneur, puis réécrite à la suppression.

### Création d'images disque

```powershell
diplo disk create-image <RÉPERTOIRE_SOURCE> <CHEMIN_DESTINATION> [--format vhd|vhdx|vmdk|vdi|raw]
```

| Format   | Extension                      | Moteur    | Note                           |
| -------- | ------------------------------ | --------- | ------------------------------ |
| **Raw**  | `.img`, `.raw`, `.bin`         | DiscUtils | Par défaut                     |
| **VHD**  | `.vhd`                         | DiscUtils | Virtual Hard Disk (dynamic)    |
| **VHDX** | `.vhdx`                        | DiscUtils | Virtual Hard Disk v2 (dynamic) |
| **VMDK** | `.vmdk`                        | DiscUtils | Virtual Machine Disk (dynamic) |
| **VDI**  | `.vdi`                         | DiscUtils | VirtualBox Disk Image          |

Le format ISO peut être produit par la bibliothèque (`FsImage.create`), mais la
commande ne l'expose pas : `iso` fait partie des formats refusés.

La commande crée une image disque contenant une copie NTFS du répertoire source. La taille virtuelle est calculée automatiquement (taille des fichiers + 10 %, minimum 64 Mo). Les fichiers existants dans le répertoire de destination sont écrasés. En cas d'erreur lors du formatage ou de la copie, le fichier partiel est automatiquement supprimé (rollback).

Pour le format **ISO**, le générateur maison `IsoFs.create` produit un système ISO9660 niveau 1 : noms de fichiers 8.3 en majuscules ASCII, sans Joliet ni Rock Ridge. Les noms plus longs sont tronqués à la règle 8.3, et les caractères non ASCII (accents, CJK…) sont réduits à leur équivalent ASCII ou remplacés ; utilisez l'un des autres formats pour préserver ces noms.

Les formats QCOW1, QCOW2, Parallels et DMG ne sont pas supportés en création (pas de factory publique dans DiscUtils).

### Formats supportés — tableau récapitulatif

| Format    | Montage       | Création image | Moteur               |
| --------- | ------------- | -------------- | -------------------- |
| Qcow2     | R/W           | —              | Pilote maison        |
| QCOW v1   | R/W           | —              | Pilote maison        |
| VHD       | R/W           | ✅             | DiscUtils            |
| VHDX      | R/W           | ✅             | DiscUtils            |
| VMDK      | R/W           | ✅             | DiscUtils            |
| VDI       | R/W           | ✅             | DiscUtils            |
| Raw       | R/W           | ✅             | DiscUtils            |
| DMG       | Lecture seule | —              | DiscUtils            |
| Parallels | R/W           | —              | Pilote maison        |
| ISO       | Lecture seule | ✅             | IsoFs (ISO9660/UDF)  |
| Btrfs     | R/W           | —              | Hawkynt (seuil 2 Go) |
| XFS       | R/W           | —              | Hawkynt (seuil 2 Go) |
| HFS+      | R/W           | —              | Hawkynt (seuil 2 Go) |

### GUI

L'interface graphique Avalonia utilise un thème système par défaut avec des onglets organisés :

**Onglet Images** :

- **Toolbar** : Lister, Rechercher, Télécharger, Inspecter, Étiqueter, Supprimer, Nettoyer
- **Champs** : Réf. (pull), User (pull), Espace, image source / image cible (étiqueter), Référence (inspecter/supprimer)
- **DataGrid** (haut) : images locales (Référentiel, Tag, Taille, Créé le)
- **Recherche en ligne** (bas) : résultats des catalogues (Registre, Référence, Étoiles, Description) avec bouton **Tirer la sélection** — déclenchée par la touche Entrée dans le champ de recherche (cf. « Recherche d'images en ligne »)

**Onglet Conteneurs** — Vue splitée avec :

- **Toolbar** en haut : Lister, Créer, Télécharger, Inspecter, Espaces, Version, Nettoyer, Événements, Arrêter
- **Champs** : ID, Nom, Image, User (pull), Espace, Timeout, Tous, Forcer
- **DataGrid** (gauche) : liste des conteneurs avec sélection
- **ContainerDetailUserControl** (droite) : panneau de détail pour le conteneur sélectionné
    - Propriétés : ID, Nom, Image, État, Créé le
    - Actions : Démarrer, Arrêter, Supprimer, Renommer, Processus, Métriques
    - Configuration : Nouveau nom, Montages
    - Journaux & Exec : Suivre, Lignes, Depuis, Commande
    - **Catalogue d'images** : liste locale persistante (`diplo-catalog.json`) avec Lister, Inscrire (pull), Mettre à jour (tag) et Retirer (rmi)

**Onglet Volumes** — Liste + création d'images disque (sélection dossier/fichier, format)

**Onglet Réseaux** — Liste + création de réseaux

**Onglet Compose** — Éditeur YAML avec colorisation syntaxique (AvalonEdit + TextMate) et validation temps réel

**Onglet Paramètres** — Édition de `DiploWalker.json` (adresses services, transport, namespace, logLevel)

**Menu** — *Fichier* → **Exporter le journal…** (écrit les lignes horodatées du journal dans un fichier `.txt` choisi via le sélecteur de fichier), *Quitter* ; *Aide* → **À propos**

## Recherche d'images en ligne

La recherche d'images interroge les catalogues en ligne depuis le CLI (`diplo container image-search`) ou l'onglet **Images** de la GUI (champ de recherche validé par Entrée ; les résultats se tirent via « Tirer la sélection »). Elle est limitée aux **quatre registres autorisés** et ne nécessite **aucun identifiant** (catalogues publics) ; le pull d'un résultat utilise ensuite l'identifiant enregistré pour le registre concerné.

| Registre | Mécanisme de recherche |
| --- | --- |
| **docker.io** | API de recherche publique de Docker Hub |
| **quay.io** | API de recherche publique de Quay |
| **mcr.microsoft.com** | Catalogue public `_catalog` (`/v2/_catalog`) téléchargé puis **filtré localement** : pas de requête serveur (le MCR ignore `?n=`), correspondance insensible à la casse sur le nom du référentiel, limite appliquée côté client |
| **ghcr.io** | Aucune API de recherche publique → aucun résultat |

Sans registre ciblé, les quatre fournisseurs sont interrogés dans l'ordre de la liste blanche (docker.io, quay.io, ghcr.io, mcr.microsoft.com). Un registre hors liste blanche est rejeté avec un avertissement et la recherche s'effectue alors sur tous les registres.

## Authentification aux registres

Les extractions d'images (CLI, GUI) sont limitées à **quatre fournisseurs** : `ghcr.io`, `docker.io`, `quay.io` et `mcr.microsoft.com`. Des alias courts sont acceptés pour `login`/`logout` : `ghcr`, `dockerhub` (ou `docker`), `quay`, `mcr`. Toute autre source est refusée par le serveur, et **toute extraction anonyme est impossible** : un compte doit être enregistré pour le registre concerné (sauf `--user utilisateur:secret` sur la ligne de commande, qui prime sur l'identifiant enregistré).

Les identifiants sont stockés côté serveur, chiffrés avec DPAPI (portée utilisateur courant) sous Windows et en AES-GCM avec une clé par utilisateur ailleurs, dans le fichier `registry-auth.json` du répertoire de données (`%ProgramData%\Diplo` sous Windows, `$XDG_DATA_HOME/Diplo` ailleurs). Ils ne transitent **jamais** par la ligne de commande de `ctr` : lors d'un `pull`, le service génère un **helper d'identification** conforme au protocole `docker-credential` (`%ProgramData%\Diplo\cred-helper\`) qui retourne les identifiants via stdin, et un répertoire hosts temporaire pointant vers ce helper (nettoyé en fin d'opération). Les identifiants sont automatiquement fournis à containerd pour l'image tirée (`docker.io` redirigé vers `registry-1.docker.io`).

### Obtention des jetons par fournisseur

- **ghcr.io** — GitHub Packages : nom d'utilisateur GitHub + *personal access token* (portée `read:packages`).
- **docker.io** — Docker Hub : identifiant Docker Hub + *Access Token* généré dans les paramètres du compte.
- **quay.io** — Red Hat Quay : identifiant du namespace (ou *robot account*) + jeton de l'utilisateur ou du robot.
- **mcr.microsoft.com** — Microsoft Container Registry : identifiant Azure + jeton de registre.

### CLI

```powershell
diplo container login ghcr.io --username user          # le mot de passe est demandé en mode masqué
diplo container login ghcr --username user --password secret   # alias accepté
diplo container logout ghcr                             # alias accepté
diplo container pull ghcr.io/org/app:latest             # utilise l'identifiant enregistré
diplo container pull ghcr.io/org/app:latest --user user:secret   # identifiant explicite (prime sur l'enregistré)
```

### GUI

L'onglet **Conteneurs** propose une ligne « Registre / Utilisateur / Mot de passe » avec les boutons **Se connecter** et **Se déconnecter**. Le pull utilise ensuite l'identifiant enregistré automatiquement.

## Référence CLI

```powershell
# Statut
diplo status check

# Conteneurs
diplo container list [--all] [-n <namespace>]
diplo container create <image> <nom> --mount "src=...,dst=...[;ro]"
diplo container delete <id> [-f]
diplo container start <id>
diplo container stop <id> [--timeout <sec>]
diplo container inspect <id>
diplo container rename <id> <nouveau_nom>
diplo container logs <id> [--follow] [--tail <n>]
diplo container exec <id> <cmd> [args...]
diplo container top <id>
diplo container stats <id>
diplo container namespaces

# Images
diplo container image-list [-n <namespace>]
diplo container image-inspect <ref>
diplo container image-remove <ref>
diplo container image-tag <source> <cible>
diplo container pull <ref> [--user <utilisateur>]
diplo container image-search <terme> [--registry <registre>] [--limit <n>]   # docker.io, quay.io, mcr.microsoft.com ; ghcr.io : aucun résultat (alias <registre> acceptés)
diplo container login <registre> --username <u> [--password <p>]   # ghcr.io, docker.io, quay.io, mcr.microsoft.com (alias : ghcr, dockerhub, quay, mcr)
diplo container logout <registre>

# Catalogue d'images
diplo container catalog-list [--catalog <fichier>]
diplo container catalog-add <ref> [--note <texte>] [--no-pull] [--catalog <fichier>]
diplo container catalog-update <ref> [--target <nouvelle_ref>] [--note <texte>] [--catalog <fichier>]
diplo container catalog-delete <ref> [--no-docker] [--catalog <fichier>]

# Volumes
diplo volume list
diplo volume create <nom> [--driver local|smb|nfs]
diplo volume inspect <id>
diplo volume remove <id> [-f]
diplo volume mount <id> <cible>
diplo volume unmount <id> <cible>
diplo volume prune

# Réseaux
diplo network list
diplo network create <nom> [--driver bridge|none|custom_cni|pod]
diplo network inspect <id>
diplo network remove <id> [-f]
diplo network connect <réseau> <conteneur>
diplo network disconnect <réseau> <conteneur> [-f]
diplo network run-cni-plugin <plugin> <cmd> <conteneur> <netns>
diplo network prune

# Images disque
diplo disk create-image <source> <dest> [--format vhd|vhdx|vmdk|vdi|raw]

# Compose
diplo compose up <fichier>
diplo compose down <fichier>
diplo compose ps <fichier>
diplo compose logs <fichier> [-s <service>]
diplo compose pull <fichier>
diplo compose build <fichier>

# Configuration
diplo config init [--path <chemin>] [--transport tcp|pipe]

# Version
diplo version
```

### Modules mutualisés

| Module                       | Projet       | Rôle                                                                 |
| ---------------------------- | ------------ | -------------------------------------------------------------------- |
| `JsonHelpers`                | Abstractions | Extraction typée de propriétés `JsonElement`                         |
| `DiploJson`                  | Abstractions | Options sérialisation JSON centralisées                              |
| `ProcessExec`                | Abstractions | Exécution processus externes + PowerShell                            |
| `ServiceGuards`              | Abstractions | Guards de validation d'entrée (RpcException)                         |
| `CachedConfig<'T>`           | Abstractions | Cache générique avec invalidation manuelle                           |
| `DriverMappings`             | Grpc         | Mapping type↔string pour drivers volume et réseau                    |
| `GrpcClientFactory`          | Core         | Construction canaux gRPC TCP/pipe avec retry                         |
| `TestHelpers`                | TestHelpers  | Helpers temp dir pour les tests                                      |
| `HawkyntFs`                  | Disk         | Adaptateur Hawkynt pour Btrfs/XFS/HFS+ R/W                           |
| `IsoFs`                      | Disk         | Parseur ISO9660/UDF + générateur ISO9660 niveau 1                    |
| `BinaryIo`                   | Disk         | Lecture/écriture binaire + helper `protect`                          |
| `RawImageStream`             | Disk         | Classe de base `Stream` des pilotes maison (Qcow1/Qcow2/Parallels)   |
| `DiscFsHelper`               | Disk         | Fonctions DiscUtils partagées + `realFrom` (anti-traversal)          |
| `VdiFs`                      | Disk         | Adaptateur DiscUtils.Vdi pour VDI R/W                                |
| `Qcow1Fs`                    | Disk         | Pilote maison QCOW v1                                                |
| `DmgFs`                      | Disk         | Adaptateur DiscUtils.Dmg pour extraction DMG                         |
| `ParallelsFs`                | Disk         | Pilote maison Parallels                                              |
| `FsImage`                    | Disk         | Création, extraction et réécriture d'images disque                   |
| `RemoteDriverHelpers`        | Volume       | Helpers mutualisés pour drivers distants (NFS, AWS, GCP, Azure, SMB) |
| `RemoteVolumeDriver`         | Volume       | Classe de base des drivers distants (store, création, montage, prune)|
| `Cmd`                        | Gui          | Helpers try/with mutualisés pour commandes GUI                       |
| `ContainerDetailUserControl` | Gui          | UserControl détail conteneur sélectionné                             |

### Validation ctr v2 (≥ v2.0)

Le montage réel des images disque et la communication gRPC ont été validés en conditions réelles avec containerd **v2.3.3** (Windows 11 26200, namespace `default`). Points clés de compatibilité ctr v2 :

- `--namespace`/`-n` est une option **globale** (avant la sous-commande).
- `container create` attend `<IMAGE> <CONTAINER>` et ne produit aucune sortie en cas de succès.
- `exec` est `tasks exec` ; `task info` et `task logs` ont été supprimés en v2.
- Les logs sont capturés par le service dans `%ProgramData%\Diplo\logs\<id>.log` et relus par `container logs --follow`.

Résultats validés : montage bind R/W bidirectionnel, `container logs --follow` en streaming, named pipes et TCP, suppression idempotente (`delete -f`), et write-back d'images disque.


