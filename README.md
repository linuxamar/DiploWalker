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
- **Résilience** : les canaux gRPC (`DiploChannel`) appliquent une politique de reprise automatique (5 tentatives, backoff exponentiel) sur les échecs `Unavailable` (service en cours de redémarrage) ; les appels streaming ne sont pas rejoués.

## Stack technique

- **Runtime** : .NET 10, F#
- **Communication** : gRPC
- **Conteneurs** : containerd (1.6.x LTS pour WS2016, 1.7.x pour WS2019+)
- **Réseau** : Plugins CNI Microsoft + standards (bridge, host-local, portmap)
- **Tests** : xUnit (672 tests)
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
8. Import des certificats PKI racine et intermédiaires dans les magasins de certificats de la machine

#### Certificats PKI

L'installateur NSIS embarque les certificats de la PKI Diplo et les importe automatiquement dans les magasins de certificats de la machine Windows :

| Certificat | Magasin | Rôle |
|------------|---------|------|
| `Diplo Root CA` | Racines de confiance (Root) | Autorité racine de la PKI |
| `Authentification` | CA intermédiaires | Authentification de services |
| `CodeSigning` | CA intermédiaires | Signature de code (assemblies, installateur) |
| `System` | CA intermédiaires | Certificats système (TLS, config) |

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
> `Diplo.Installer.exe install`.

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

### Configuration du client (diplo.json)

Les clients (CLI et GUI) résolvent l'adresse de chaque service via le fichier `diplo.json`. Le fichier est cherché par priorité :

1. `%DIPLO_CONFIG_HOME%\diplo.json` si la variable d'environnement est définie (recommandé pour la GUI et les installations : chemin stable, indépendant du répertoire courant) ;
2. `diplo.json` dans le répertoire courant.

S'il est absent ou mal formé, les clients retombent sur les adresses par défaut (`localhost:5001`/`5002`/`5003`).

```powershell
diplo config init                       # génère diplo.json avec le transport TCP par défaut
diplo config init --transport pipe      # génère diplo.json avec des adresses par named pipes
diplo config init --path C:\etc\diplo.json --transport pipe
```

```json
{
  "container": { "address": "http://pipe:/diplo-container", "namespace": "default" },
  "volume":    { "address": "http://pipe:/diplo-volume" },
  "network":   { "address": "http://pipe:/diplo-network" },
  "logLevel":  "Information"
}
```

- `tcp` : adresses `localhost:<port>` (http ajouté automatiquement si absent).
- `pipe` : adresses `http://pipe:/<nom>` — canal local par named pipe (transport privilégié sur la machine, aucun port exposé). Les noms correspondent aux tubes créés par l'installateur (`diplo-container`, `diplo-volume`, `diplo-network`).
- Les adresses `http://pipe:/...` sont validées (hôte local uniquement, nom de tube sans `\` ni `..`).
- La GUI propose un onglet **Paramètres** pour éditer ces adresses (écriture de `diplo.json`, champs `namespace` et `logLevel` conservés) ; la configuration est appliquée dès les opérations suivantes, sans redémarrage.

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

`signtool.exe` est recherché dans le PATH puis dans les Windows Kits installés. Sans certificat disponible, la signature est ignorée (simple avertissement).

### CI (GitHub Actions)

`.github/workflows/ci.yml` :
- **Build + tests** à chaque push/PR sur `dev` et `main` (`pipeline.ps1 -DoTests`) ;
- **Publication** sur les tags `v*` : tests, publication self-contained x64/x86 et setup NSIS téléversés en artefacts.

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

## Authentification aux registres

Les identifiants des registres privés sont stockés côté serveur, chiffrés avec DPAPI (portée utilisateur courant) dans `%ProgramData%\Diplo\registry-auth.json`. Ils sont automatiquement fournis à containerd lors du `pull` d'une image du registre correspondant (registres nommés ou `docker.io` pour Docker Hub).

### CLI

```powershell
diplo container login myregistry.azurecr.io --username user          # le mot de passe est demandé en mode masqué
diplo container login myregistry.azurecr.io --username user --password secret
diplo container logout myregistry.azurecr.io
diplo container pull myregistry.azurecr.io/team/app:latest           # utilise l'identifiant enregistré
diplo container pull myregistry.azurecr.io/team/app:latest --user inline:secret   # identifiant explicite (prime sur l'enregistré)
```

### GUI

L'onglet **Conteneurs** propose une ligne « Registre / Utilisateur / Mot de passe » avec les boutons **Se connecter** et **Se déconnecter**. Le pull utilise ensuite l'identifiant enregistré automatiquement.

### Validation manuelle sur un hôte containerd réel

Le montage réel des images disque (via `ctr --mount`) n'est pas automatisable dans les tests : il nécessite un hôte Windows avec containerd installé. Procédure validée le 11/08/2026 contre containerd **v2.3.3** (namespace `default`) :

1. **Image de test** — créer une image FAT brute (format détecté par `DiskFormat`) contenant `bonjour.txt` :
   ```powershell
   $img = "C:\ProgramData\Diplo\validate\data.img"
   # Fabriquer une image FAT 64 Mo avec DiscUtils (script fsi ou utilitaire dédié).
   ```
2. **Publier et démarrer le service en TCP** (la validation a été effectuée en TCP ; le transport par named pipes est corrigé dans `ServerConfig` — voir ci-dessous) :
   ```powershell
   dotnet publish src\Diplo.Container -c Release -r win-x64 -p:Platform=x64 --self-contained true -o publish\WindowsServices\x64\Diplo.Container
   & publish\WindowsServices\x64\Diplo.Container\Diplo.Container.exe   # service : http://127.0.0.1:5001
   ```
3. **Créer un conteneur avec l'image montée** (le CLI résout l'adresse depuis le token d'auth) :
   ```powershell
   & publish\WindowsServices\x64\Diplo.Cli\Diplo.Cli.exe container create mcr.microsoft.com/windows/nanoserver:ltsc2025 mon-conteneur --mount "src=$img,dst=C:\data"
   ```
4. **Démarrer, exécuter, écrire** :
   ```powershell
   & ...\Diplo.Cli.exe container start mon-conteneur        # démarre la tâche détachée (le stdio n'est plus attaché)
   & ...\Diplo.Cli.exe container exec mon-conteneur cmd /c dir C:\data
   & ...\Diplo.Cli.exe container exec mon-conteneur cmd /c copy nul C:\data\ecrit.txt
   ```
5. **Supprimer le conteneur** — le write-back réécrit `data.img` depuis le staging et nettoie `%ProgramData%\Diplo\volumes` :
   ```powershell
   & ...\Diplo.Cli.exe container delete mon-conteneur -f
   ```

**Résultats de la validation (11/08/2026, containerd v2.3.3, Windows 11 26200)** :

- `container create` avec image FAT montée en `type=bind,src=<staging>,dst=C:\data,options=rbind` : le staging (`%ProgramData%\Diplo\volumes\<guid>`) est extrait de l'image, le contenu est visible dans le conteneur (`dir C:\data` → `bonjour.txt`) et la réponse porte bien l'ID du conteneur.
- Le bind mount est bidirectionnel : `ecrit.txt` créé dans le conteneur apparaît dans le staging hôte.
- `container delete -f` : conteneur et tâche supprimés, staging purgé, et `data.img` réécrit — un nouvel extract relit `bonjour.txt` **et** `ecrit.txt`.
- `ctr tasks exec` s'exécute dans le conteneur (le top-level `ctr exec` n'existe pas en v2).

**Résultats de la validation (12/08/2026)** :

- `container logs --follow` validé de bout en bout en conditions réelles : conteneur `ping 127.0.0.1 -t` (une ligne par seconde) démarré détaché, puis `container logs --follow --tail 3` → instantané de 3 lignes puis ~1 nouvelle ligne par seconde reçue au fil de l'eau, suivi actif jusqu'à Ctrl+C. Cela a nécessité trois corrections : (1) `ContainerLogs.read` ouvrait le fichier avec `FileShare.Read` strict, incompatible avec le handle d'écriture du conteneur en cours d'exécution (IOException) — passage à `FileShare.ReadWrite ||| FileShare.Delete` ; (2) le `StreamWriter` de capture n'avait pas `AutoFlush` : les lignes restaient en mémoire tant que le conteneur tournait — `AutoFlush <- true` ; (3) `container create --cmd` : la commande était passée via un spec OCI avec l'option `--spec`, absente de `ctr v2.3.3` (d'où l'échec silencieux) — la commande est désormais passée en positionnel (`ctr container create <image> <id> <cmd> [args...]`) avec `--env`, `--mount`, `--memory-limit`, `--cpu-shares` en flags.
- Le CLI expose `container create -c|--cmd "<ligne de commande>"` (découpage respectant les guillemets) en plus de `--command` (arguments répétés) ; les tests unitaires du flux (`getContainerLogs stream émet les nouvelles lignes au fil de l'eau et se termine à l'arrêt`) et de création renforcés.

**Points en suspens résolus (12/08/2026)** :

1. **Limites de ressources non prises en charge** : `ctr container create` (v2.3.3) ne propose pas `--pids-limit`, et `ctr task update`/`ctr container update` **n'existent pas** (« No help topic for 'update' »). `UpdateContainer` lève désormais une `RpcException` claire (`Unimplemented`) quand une limite est demandée (no-op sinon) — il n'était utilisé ni par le CLI ni par la GUI.
2. **`container delete -f` tolérant** : la suppression forcée d'un conteneur inexistant échouait par une erreur gRPC (« Exception was thrown by handler ») ; elle retourne désormais un succès (les échecs de `task kill` et `container delete` sont journalisés en avertissement) — l'arrêt forcé devient idempotent.
3. **Source de logs GUI validée en conditions réelles** : `GrpcContainerLogsSource` (`GetSnapshot` → 3 lignes avec `tail=3` ; `GetStream` en `follow` → 67 lignes reçues au fil de l'eau) testé contre le service réel, mêmes endpoints gRPC que le CLI.
4. **Arrêt propre sur Ctrl+C du `container logs --follow`** : validation réelle dans une console dédiée (`AttachConsole` + `GenerateConsoleCtrlEvent`) — le CLI sortait brutalement avec `STATUS_CONTROL_C_EXIT` (0xC000013A). Spectre.Console.Cli 0.55 ne pose pas `e.Cancel` et n'annule pas le jeton : ajout de `CtrlCHandler` (pose `e.Cancel=true` + annulation) et rattrapage de `RpcException` de statut `Cancelled` — `EXIT_CODE=0` désormais.
6. **Transport par named pipes revalidé en direct** : `GetVersion` et `ListContainers` confirmés sur `http://pipe:/diplo-container-validate` (requêtes HTTP/2 200 dans les logs du service) en plus du TCP (port 5001). Au passage, correction d'un vrai bug : la `PipeSecurity` contenait une règle « Deny Everyone » qui empêchait la création du pipe (sur Windows les règles Deny priment sur les Allow, or l'utilisateur courant est membre de Everyone) → échec de liaison et crash du service ; la règle a été retirée et l'accès passe par un `Allow` `FullControl` pour l'utilisateur courant (comme le fait Kestrel avec `CurrentUserOnly`).

**Compatibilité ctr v2 (≥ v2.0) — écarts corrigés au fil de la validation** :

- `--namespace`/`-n` est une option **globale** (avant la sous-commande), et non locale.
- `container create` attend `<IMAGE> <CONTAINER>` (ordre inversé par rapport à v1) et ne produit **aucune sortie** en cas de succès.
- `exec` est `tasks exec` ; `task info` et `task logs` ont été **supprimés** en v2 : `task info` est remplacé par le parsing de `tasks list`. Les logs sont capturés par le service lors du démarrage détaché dans `%ProgramData%\Diplo\logs\<id>.log` et relus par `container logs` (`tail`, `since` ; `--follow` suit le fichier et émet les nouvelles lignes au fil de l'eau jusqu'à la sortie du conteneur ou Ctrl+C).
- `PullImage` n'utilise volontairement pas de namespace : `ctr image pull` s'applique au namespace courant.

**Points corrigés au fil des validations** :

- Transport Kestrel par named pipes : le flag par défaut de `NamedPipeServerStreamAcl.Create` levait une `ArgumentException` ; `ServerConfig` fournit désormais une `PipeSecurity` explicite (`CurrentUserOnly=false`). **Procédure de validation du transport par named pipes** : (1) dans `ServerConfig`, publier avec `UseNamedPipes: true` et `ListenUrls: http://pipe:/Diplo.Container`, puis publier/démarrer le service ; (2) exécuter `diplo container version` et vérifier que le canal `http://pipe:/Diplo.Container` est bien écouté ; (3) exécuter `diplo container list` — une réponse confirme la liaison par named pipes ; (4) si une `ArgumentException` subsiste, vérifier que la `PipeSecurity` explicite (`CurrentUserOnly=false`) est appliquée dans `ServerConfig`. Le transport par named pipes est également couvert par un test d'intégration automatisé (Kestrel `ListenNamedPipe`, canal `npipe://`) en plus des tests TCP (`UseNamedPipes: false`, port 5001).
- `container start` démarre la tâche de manière détachée (`ctr tasks start` sans attache au stdio) et capture les logs dans un fichier par conteneur, ce qui permet une utilisation non interactive.
- `container exec` accepte désormais les arguments contenant des espaces (reconstruction de la ligne de commande avec échappement).
- L'état des volumes montés est persisté (`MountState`) : après un redémarrage du service, les associations sont restaurées et le write-back à la suppression conserve son comportement.
