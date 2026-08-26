# Diplo

Système distribué de microservices gRPC pour la gestion de conteneurs Windows.

## Architecture

Diplo est composé de quatre services principaux communiquant via gRPC :

| Service             | Port | Description                                                                                                |
| ------------------- | ---- | ---------------------------------------------------------------------------------------------------------- |
| **Diplo.Container** | 5001 / 6001 | Cycle de vie des conteneurs via containerd — création, démarrage, arrêt, suppression et montage de volumes |
| **Diplo.Volume**    | 5002 / 6002 | Gestion des volumes persistants                                                                            |
| **Diplo.Network**   | 5003 / 6003 | Gestion des réseaux de conteneurs (NAT, overlay, l2bridge)                                                 |

| **Diplo.Installer** | —    | Installation et configuration de l'ensemble du système                                                     |

> **Ports Debug / Release** : en configuration **Debug**, les services écoutent sur 5001-5003 ; en **Release** (+ installation via l'installateur), sur **6001-6003**. Les deux plages permettent une exécution simultanée. Les named pipes (`diplo-container`, `diplo-volume`, `diplo-network`) sont identiques dans les deux configurations.

### Clients

- **CLI** : `Diplo.Cli` (Spectre.Console) — toutes les opérations de conteneurs, volumes, réseaux et images disque (`disk create-image`)
- **GUI** : `Diplo.Gui` (Avalonia) — interface graphique native multi-plateforme avec MVVM
- **Résilience** : les canaux gRPC (`DiploChannel`) appliquent une politique de reprise automatique (5 tentatives, backoff exponentiel) sur les échecs `Unavailable` (service en cours de redémarrage) ; les appels streaming ne sont pas rejoués.

## Stack technique

- **Runtime** : .NET 10, F#
- **Communication** : gRPC
- **Conteneurs** : containerd (1.6.x LTS pour WS2016, 1.7.x pour WS2019+)
- **Réseau** : Plugins CNI Microsoft + standards (bridge, host-local, portmap)
- **Tests** : xUnit v4 (886 tests)
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

``powershell
Diplo.Installer.exe install
``

L'installateur effectue automatiquement :

1. Détection de la version de Windows Server
2. Téléchargement et installation de containerd (1.6.x LTS ou 1.7.x selon la version)
3. Installation des plugins CNI Microsoft (nat, overlay, l2bridge)
4. Installation des plugins CNI standards (bridge, host-local, portmap)
5. Génération de la configuration containerd (`config.toml`)
6. Création de la configuration CNI par défaut (réseau NAT `172.20.0.0/16`)
7. Création des services Windows (Diplo.Container, Diplo.Volume, Diplo.Network)
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

``powershell
# Élevé (PowerShell administrateur)
.\setup\install-containerd-runtime.ps1
``

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
> `Diplo.Installer.exe install`.

### Commandes

``powershell
Diplo.Installer.exe install      # Installation complète
Diplo.Installer.exe uninstall    # Suppression des services
Diplo.Installer.exe status       # État des services
``

### Démarrage des services

``powershell
sc.exe start "Diplo.Container"
sc.exe start "Diplo.Volume"
sc.exe start "Diplo.Network"
``

### Configuration du client (diplo.json)

Les clients (CLI et GUI) résolvent l'adresse de chaque service via le fichier `diplo.json`. Le fichier est cherché par priorité :

1. `%DIPLO_CONFIG_HOME%\diplo.json` si la variable d'environnement est définie (recommandé pour la GUI et les installations : chemin stable, indépendant du répertoire courant) ;
2. `diplo.json` dans le répertoire courant.

S'il est absent ou mal formé, les clients retombent sur les adresses par défaut (`localhost:5001`/`5002`/`5003` en Debug, `localhost:6001`/`6002`/`6003` en Release).

``powershell
diplo config init                       # génère diplo.json avec le transport TCP par défaut
diplo config init --transport pipe      # génère diplo.json avec des adresses par named pipes
diplo config init --path C:\etc\diplo.json --transport pipe
``

``json
{
    "container": {
        "address": "http://pipe:/diplo-container",
        "namespace": "default"
    },
    "volume": { "address": "http://pipe:/diplo-volume" },
    "network": { "address": "http://pipe:/diplo-network" },
    "logLevel": "Information"
}
``

- `tcp` : adresses `localhost:<port>` (http ajouté automatiquement si absent).
- `pipe` : adresses `http://pipe:/<nom>` — canal local par named pipe (transport privilégié sur la machine, aucun port exposé). Les noms correspondent aux tubes créés par l'installateur (`diplo-container`, `diplo-volume`, `diplo-network`).
- Les adresses `http://pipe:/...` sont validées (hôte local uniquement, nom de tube sans `\` ni `..`).
- La GUI propose un onglet **Paramètres** pour éditer ces adresses (écriture de `diplo.json`, champs `namespace` et `logLevel` conservés) ; la configuration est appliquée dès les opérations suivantes, sans redémarrage.

## Développement

### Build

``powershell
.\pipeline.ps1
``

Options disponibles :

- `-Clean` : Nettoyage avant build
- `-Restore` : Restauration des packages NuGet
- `-DoTests` : Exécution des tests
- `-DoPublish` : Publication des exécutables
- `-SignCert <pfx> [-SignPassword <mot-de-passe>]` : signe les installateurs avec un certificat PFX
- `-SignThumbprint <empreinte>` : signe avec un certificat du magasin (par empreinte SHA-1/SHA-256)

`signtool.exe` est recherché dans le PATH puis dans les Windows Kits installés. Sans certificat disponible, la signature est ignorée (simple avertissement).

### Structure du projet

``
Diplo/
├── src/
│   ├── Diplo.Abstractions/     # Interfaces, validation, sécurité, modules mutualisés
│   │   ├── JsonHelpers.fs      # Extraction typée de propriétés JSON
│   │   ├── DiploJson.fs        # Options sérialisation centralisées
│   │   ├── ProcessExec.fs      # Exécution processus + PowerShell
│   │   ├── ServiceGuards.fs    # Guards de validation d'entrée
│   │   ├── CachedConfig.fs     # Cache générique avec invalidation
│   │   ├── Security.fs         # Validation d'entrée, anti-injection
│   │   ├── ServerConfig.fs     # Configuration Kestrel / named pipes
│   │   └── DiploPorts.fs       # Ports gRPC partagés (#if DEBUG 5001-5003 / Release 6001-6003)
│   ├── Diplo.Container/        # Service gRPC de gestion des conteneurs
│   ├── Diplo.Volume/           # Service gRPC de gestion des volumes
│   ├── Diplo.Network/          # Service gRPC de gestion des réseaux
│   ├── Diplo.Installer/        # Outil d'installation Windows
│   ├── Diplo.Grpc/             # Types messages, services gRPC, DriverMappings
│   ├── Diplo.Contracts/        # Types partagés entre services
│   ├── Diplo.Core/             # Clients gRPC, GrpcClientFactory, DiploConfig
│   ├── Diplo.Disk/             # Montage et création d'images disque (qcow2, qcow1, raw, vhd, vhdx, vmdk, vdi, dmg, parallels)
│   ├── Diplo.Cli/              # Client CLI (Spectre.Console)
│   ├── Diplo.Gui/              # Interface graphique Avalonia
│   │   ├── Views/MainWindow.axaml(.fs)
│   │   ├── UserControls/ContainerDetailUserControl.axaml(.fs)
│   │   ├── ViewModels/
│   │   ├── Services/
│   │   ├── App.axaml(.fs)
│   │   └── Program.fs
├── tests/
│   ├── Diplo.Abstractions.Tests/
│   ├── Diplo.Cli.Tests/
│   ├── Diplo.Container.Tests/
│   ├── Diplo.Core.Tests/
│   ├── Diplo.Disk.Tests/
│   ├── Diplo.Gui.Tests/
│   ├── Diplo.Installer.Tests/
│   ├── Diplo.Integration.Tests/
│   ├── Diplo.Network.Tests/
│   ├── Diplo.TestHelpers/
│   └── Diplo.Volume.Tests/
├── pipeline.ps1                # Pipeline de build et déploiement
└── README.md
``

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

À la création, un conteneur peut monter des volumes persistants, sous forme de **répertoires de l'hôte** ou d'**images disque** gérées par `Diplo.Disk`. Le montage se fait en bind (`rbind`), en lecture-écriture par défaut.

| Format        | Extension(s)                   | R/W           | Moteur                                       |
| ------------- | ------------------------------ | ------------- | -------------------------------------------- |
| **Qcow2**     | `.qcow2`                       | R/W           | Pilote maison (`Qcow2Stream`)                |
| **QCOW v1**   | `.qcow`                        | R/W           | Pilote maison (`Qcow1Stream`)                |
| **VHD**       | `.vhd`                         | R/W           | DiscUtils                                    |
| **VHDX**      | `.vhdx`                        | R/W           | DiscUtils                                    |
| **VMDK**      | `.vmdk`                        | R/W           | DiscUtils                                    |
| **VDI**       | `.vdi`                         | R/W           | DiscUtils                                    |
| **Raw**       | `.img`, `.raw`, `.bin`, `.iso` | R/W           | DiscUtils (FAT, NTFS, ext)                   |
| **DMG**       | `.dmg`                         | Lecture seule | DiscUtils                                    |
| **Parallels** | `.hdd`, `.hds`                 | R/W           | Pilote maison (`ParallelsStream`)            |
| **Btrfs**     | (via Hawkynt)                  | R/W           | Hawkynt.FileFormats.FileSystems (seuil 2 Go) |
| **XFS**       | (via Hawkynt)                  | R/W           | Hawkynt.FileFormats.FileSystems (seuil 2 Go) |
| **HFS+**      | (via Hawkynt)                  | R/W           | Hawkynt.FileFormats.FileSystems (seuil 2 Go) |

### CLI

``powershell
diplo container create <image> <nom> --mount "src=C:\donnees,dst=C:\conteneur\donnees"
diplo container create <image> <nom> --mount "src=C:\donnees,dst=C:\conteneur\donnees,ro"
``

- `src` : répertoire de l'hôte ou chemin vers une image disque
- `dst` : destination dans le conteneur
- `ro` (optionnel) : montage en lecture seule

Les sources sont restreintes aux répertoires autorisés par la validation de sécurité (`%TEMP%`, `%ProgramData%\Diplo`, `%ProgramFiles%\Diplo`). Une image disque est montée via un répertoire de préparation pour la durée de vie du conteneur, puis réécrite à la suppression.

### Création d'images disque

``powershell
diplo disk create-image <RÉPERTOIRE_SOURCE> <CHEMIN_DESTINATION> [--format vhd|vhdx|vmdk|vdi|raw]
``

| Format   | Extension                      | Moteur    | Note                           |
| -------- | ------------------------------ | --------- | ------------------------------ |
| **Raw**  | `.img`, `.raw`, `.bin`, `.iso` | DiscUtils | Par défaut                     |
| **VHD**  | `.vhd`                         | DiscUtils | Virtual Hard Disk (dynamic)    |
| **VHDX** | `.vhdx`                        | DiscUtils | Virtual Hard Disk v2 (dynamic) |
| **VMDK** | `.vmdk`                        | DiscUtils | Virtual Machine Disk (dynamic) |
| **VDI**  | `.vdi`                         | DiscUtils | VirtualBox Disk Image          |

La commande crée une image disque contenant une copie NTFS du répertoire source. La taille virtuelle est calculée automatiquement (taille des fichiers + 10 %, minimum 64 Mo). Les fichiers existants dans le répertoire de destination sont écrasés. En cas d'erreur lors du formatage ou de la copie, le fichier partiel est automatiquement supprimé (rollback).

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
| Btrfs     | R/W           | —              | Hawkynt (seuil 2 Go) |
| XFS       | R/W           | —              | Hawkynt (seuil 2 Go) |
| HFS+      | R/W           | —              | Hawkynt (seuil 2 Go) |

### GUI

L'interface graphique Avalonia utilise un thème système par défaut avec des onglets organisés :

**Onglet Conteneurs** — Vue splitée avec :

- **Toolbar** en haut : Lister, Créer, Télécharger, Inspecter, Espaces, Version
- **Champs** : ID, Nom, Image, User (pull), Espace, Timeout, Tous, Forcer
- **DataGrid** (gauche) : liste des conteneurs avec sélection
- **ContainerDetailUserControl** (droite) : panneau de détail pour le conteneur sélectionné
    - Propriétés : ID, Nom, Image, État, Créé le
    - Actions : Démarrer, Arrêter, Supprimer, Renommer, Processus, Métriques
    - Configuration : Nouveau nom, Montages
    - Journaux & Exec : Suivre, Lignes, Depuis, Commande
- **Images** (Expander repliable en bas) : Liste/Inspecter/Supprimer/Étiqueter

**Onglet Volumes** — Liste + création d'images disque (sélection dossier/fichier, format)

**Onglet Réseaux** — Liste + création de réseaux

**Onglet Compose** — Éditeur YAML avec colorisation syntaxique (AvalonEdit + TextMate) et validation temps réel

**Onglet Paramètres** — Édition de `diplo.json` (adresses services, transport, namespace, logLevel)

## Authentification aux registres

Les identifiants des registres privés sont stockés côté serveur, chiffrés avec DPAPI (portée utilisateur courant) dans `%ProgramData%\Diplo\registry-auth.json`. Ils ne transitent **jamais** par la ligne de commande de `ctr` : lors d'un `pull`, le service génère un **helper d'identification** conforme au protocole `docker-credential` (`%ProgramData%\Diplo\cred-helper\`) qui retourne les identifiants via stdin, et un répertoire hosts temporaire pointant vers ce helper (nettoyé en fin d'opération). Les identifiants sont automatiquement fournis à containerd pour l'image tirée (registres nommés ou `docker.io` redirigé vers `registry-1.docker.io`).

### CLI

``powershell
diplo container login myregistry.azurecr.io --username user          # le mot de passe est demandé en mode masqué
diplo container login myregistry.azurecr.io --username user --password secret
diplo container logout myregistry.azurecr.io
diplo container pull myregistry.azurecr.io/team/app:latest           # utilise l'identifiant enregistré
diplo container pull myregistry.azurecr.io/team/app:latest --user inline:secret   # identifiant explicite (prime sur l'enregistré)
``

### GUI

L'onglet **Conteneurs** propose une ligne « Registre / Utilisateur / Mot de passe » avec les boutons **Se connecter** et **Se déconnecter**. Le pull utilise ensuite l'identifiant enregistré automatiquement.

## Référence CLI

``powershell
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
diplo container login <registre> --username <u> [--password <p>]
diplo container logout <registre>

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
``

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
| `VdiFs`                      | Disk         | Adaptateur DiscUtils.Vdi pour VDI R/W                                |
| `Qcow1Fs`                    | Disk         | Pilote maison QCOW v1                                                |
| `DmgFs`                      | Disk         | Adaptateur DiscUtils.Dmg pour extraction DMG                         |
| `ParallelsFs`                | Disk         | Pilote maison Parallels                                              |
| `FsImage`                    | Disk         | Création, extraction et réécriture d'images disque                   |
| `RemoteDriverHelpers`        | Volume       | Helpers mutualisés pour drivers distants (NFS, AWS, GCP, Azure, SMB) |
| `Cmd`                        | Gui          | Helpers try/with mutualisés pour commandes GUI                       |
| `ContainerDetailUserControl` | Gui          | UserControl détail conteneur sélectionné                             |

### Validation ctr v2 (≥ v2.0)

Le montage réel des images disque et la communication gRPC ont été validés en conditions réelles avec containerd **v2.3.3** (Windows 11 26200, namespace `default`). Points clés de compatibilité ctr v2 :

- `--namespace`/`-n` est une option **globale** (avant la sous-commande).
- `container create` attend `<IMAGE> <CONTAINER>` et ne produit aucune sortie en cas de succès.
- `exec` est `tasks exec` ; `task info` et `task logs` ont été supprimés en v2.
- Les logs sont capturés par le service dans `%ProgramData%\Diplo\logs\<id>.log` et relus par `container logs --follow`.

Résultats validés : montage bind R/W bidirectionnel, `container logs --follow` en streaming, named pipes et TCP, suppression idempotente (`delete -f`), et write-back d'images disque.
