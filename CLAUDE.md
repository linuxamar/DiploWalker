# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Projet

**Diplo** — système distribué de microservices gRPC pour la gestion de conteneurs Windows (cf. `README.md`).

## Architecture

11 projets source (.NET 10, F#) + 11 projets de test :

| Projet             | Rôle                                                                                                                                                                                                                                                          |
| ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Diplo.Abstractions | Interfaces partagées, validation, sécurité, modules utilitaires mutualisés                                                                                                                                                                                    |
| Diplo.Container    | Service gRPC de gestion des conteneurs (containerd) — création, cycle de vie, montage de volumes (`--mount`)                                                                                                                                                  |
| Diplo.Volume       | Service gRPC de gestion des volumes persistants                                                                                                                                                                                                               |
| Diplo.Network      | Service gRPC de gestion des réseaux (CNI)                                                                                                                                                                                                                     |
| Diplo.Installer    | Installation Windows (services, containerd, CNI)                                                                                                                                                                                                              |
| Diplo.Grpc         | Types messages et interfaces de service gRPC (protobuf-net, code-first), mappings de drivers                                                                                                                                                                  |
| Diplo.Contracts    | Types partagés entre services                                                                                                                                                                                                                                 |
| Diplo.Core         | Clients gRPC, abstraction `IOutputPort`, `MountParser` (format `src=...,dst=...[;ro]`), config client `diplo.json` et support des named pipes (`http://pipe:/<nom>`), factory gRPC mutualisée                                                                 |
| Diplo.Disk         | Montage d'images disque (qcow2, qcow1, raw, vhd, vhdx, vmdk, vdi, dmg, parallels, iso) via DiscUtils/pilotes maison + support R/W Btrfs, XFS, HFS+ via Hawkynt.FileFormats.FileSystems. Création d'images disque (VHD, VHDX, VMDK, VDI, Raw, ISO) via `FsImage.create` ; detection/extraction ISO9660/UDF via le parseur maison `IsoFs`. Référencé par `Diplo.Volume` (façade `IsoDriver`). |
| Diplo.Cli          | Client CLI (Spectre.Console)                                                                                                                                                                                                                                  |
| Diplo.Gui          | Interface graphique Avalonia                                                                                                                                                                                                                                  |

### Modules mutualisés

Les modules suivants ont été extraits du code dupliqué et centralisés dans Diplo.Abstractions / Diplo.Grpc :

| Module                       | Projet             | Rôle                                                                                                                                                                                                                                                                                   |
| ---------------------------- | ------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `JsonHelpers`                | Diplo.Abstractions | Extraction typée de propriétés depuis `JsonElement` (`tryGetString`, `tryGetInt64`, `tryGetDouble`, `tryGetBool`, `tryGetElement`, `tryGetStringValue`). Module `[<RequireQualifiedAccess>]` — appeler via `JsonHelpers.tryGetString`.                                                 |
| `DiploJson`                  | Diplo.Abstractions | Options de sérialisation JSON centralisées (`defaultOptions`, `snakeCaseOptions`, `caseInsensitiveOptions`, `withMaxDepth`, `documentOptions`).                                                                                                                                        |
| `ProcessExec`                | Diplo.Abstractions | Exécution de processus externes (`run`, `runWithResult`, `runUnit`) et commandes PowerShell (`runPowerShell`, `runPowerShellScript`). Gère le timeout, le Kill, et la lecture asynchrone stdout/stderr. `runUnit` exécute sans retourner la sortie standard (usage montage/démontage). |
| `ServiceGuards`              | Diplo.Abstractions | Guards de validation d'entrée réutilisables (`requireNonEmpty`, `requireId`, `requirePositive`, `requireInRange`, `requireSafePath`, `requireLocalAddress`, `requireSafeCommand`). Lèvent `RpcException(InvalidArgument)`.                                                             |
| `CachedConfig<'T>`           | Diplo.Abstractions | Cache générique avec invalidation manuelle, protégé par un verrou. Chargement paresseux via `Value`, invalidation via `Invalidate()`.                                                                                                                                                  |
| `DriverMappings`             | Diplo.Grpc         | Mapping type↔string pour les drivers volume (`StorageDriverType`) et réseau (`NetworkDriver`). Fonctions `parse*`, `isValid*`, `*ToString`, `all*Names`.                                                                                                                               |
| `GrpcClientFactory`          | Diplo.Core         | Construction de canaux gRPC TCP ou named pipe avec retry (5 tentatives, backoff exponentiel) et credentials par token.                                                                                                                                                                 |
| `TestHelpers`                | Diplo.TestHelpers  | Helpers pour les tests (`createTempDir`, `cleanupDir`).                                                                                                                                                                                                                                |
| `Cmd`                        | Diplo.Gui          | Helpers try/with mutualisés pour les commandes GUI (`run` async, `runSync` synchrone, `runSyncWith` avec callback d'erreur custom).                                                                                                                                                    |
| `ContainerDetailUserControl` | Diplo.Gui          | UserControl XAML pour le détail d'un conteneur sélectionné (propriétés, actions, montage, logs/exec). Chargé via `AvaloniaRuntimeXamlLoader`.                                                                                                                                          |
| `HawkyntFs`                  | Diplo.Disk         | Adaptateur Hawkynt.FileFormats.FileSystems pour l'extraction et la réécriture de Btrfs, XFS et HFS+. Seuil de 2 Go pour éviter le tout-en-mémoire ; fallback DiscUtils au-delà. Tous les readers/streams utilisent `use` pour la libération garantie.                                  |
| `ComposeEditorViewModel`     | Diplo.Gui          | ViewModel de l'éditeur Compose (AvalonEdit) : chargement/sauvegarde de fichiers YAML, validation en temps réel (clé `services` absente, services sans `image` ni `build`), collection `Errors` exposée pour le bindind XAML.                                                           |
| `RemoteDriverHelpers`        | Diplo.Volume       | Helpers mutualisés pour les drivers distants (NFS, AWS EFS, GCP Filestore, Azure Files, SMB) : `mountVolume`, `unmountVolume`, `unmountNfsLike` (umount → fallback mount -u).                                                                                                        |
| `RemoteVolumeDriver`         | Diplo.Volume       | Classe de base abstraite des drivers distants : mutualise le store, la création via `RemotePath`, le montage/démontage (`Mount`/`Unmount`, défaut `unmountNfsLike`) et `PruneVolumes`. Nfs/Smb/Azure/Aws/Gcp n'exposent plus que `RemotePath`/`Mount` (et `Unmount` pour Azure/SMB).        |
| `BinaryIo`                   | Diplo.Disk         | Module de lecture/écriture binaire partagé (`be16/32/64`, `le16/32/64`, `putBe*`/`putLe*`, `readFully`) et helper `protect` (try → `Result<_,string>`). Utilisé par Qcow1Fs, Qcow2, ParallelsFs et FsImage.                                                                             |
| `RawImageStream`             | Diplo.Disk         | Classe de base abstraite `Stream` des pilotes maison : champs `fs`, `CanRead/CanSeek/CanWrite`, `Flush`, `Dispose` guardé. Héritée par `Qcow1Stream`, `Qcow2Stream` et `ParallelsStream` (leurs `Position/Length/Read/Write/Seek/SetLength` restent propres).                           |
| `DiscFsHelper`               | Diplo.Disk         | Mutualise `toRealRel`, `realFrom` (confinement anti-traversal), `openFileSystem`, `copyDirectory`, `copyIntoFs`, `deleteFsEntries`. Utilisé par VdiFs, DmgFs et FsImage (VDI/DMG passent par le `realFrom` confiné).                                                                    |
| `VdiFs`                      | Diplo.Disk         | Adaptateur DiscUtils.Vdi pour l'extraction et la réécriture R/W d'images VDI (VirtualBox).                                                                                                                                                                                             |
| `Qcow1Fs`                    | Diplo.Disk         | Pilote maison pour les images QCOW v1 (QFI\\xFE) : lecture/écriture in-place via `Qcow1Stream`.                                                                                                                                                                                        |
| `DmgFs`                      | Diplo.Disk         | Adaptateur DiscUtils.Dmg pour l'extraction (lecture seule) d'images DMG (Apple Disk Image).                                                                                                                                                                                            |
| `ParallelsFs`                | Diplo.Disk         | Pilote maison pour les images Parallels (.hdd, .hds) : lecture/écriture in-place via `ParallelsStream`.                                                                                                                                                                                |
| `FsImage`                    | Diplo.Disk         | Création d'images disque (`create`), extraction (`extract`) et réécriture (`writeBack`) de systèmes de fichiers. Chaîne d'adaptateurs IsoFs → Hawkynt → VdiFs → DmgFs → DiscUtils. Supporte Raw, VHD, VHDX, VMDK, VDI et ISO en création (`createCore` court-circuite le format ISO via `IsoFs.createCore`, défini en lecture seule). |
| `IsoFs`                      | Diplo.Disk         | Parseur maison ISO9660 (`IsoSource`/`Iso9660`/`Udf`/`IsoImage`, déplacé de `Diplo.Volume`) + générateur ISO9660 niveau 1 (`create`, noms 8.3 ASCII, sans Joliet/Rock Ridge). `tryExtract` retourne `None` si le format n'est pas détecté ou en cas d'erreur (log warning) ; `tryWriteBack` renvoie `false` (ISO en lecture seule). Consommé par `FsImage` et par la façade `IsoDriver` de `Diplo.Volume`. |

## Stack

- **.NET 10** (`dotnet 10.0.400` installé localement).
- Solution : **`Diplo.slnx`** (format XML compact .NET 10).
- Orientation **100 % F#** (services, drivers, CLI et gRPC en code-first protobuf-net).
- **GUI** : Avalonia 12.1.1 avec AvalonEdit 12.0.0 (éditeur YAML Compose avec colorisation syntaxique via TextMate).
- **Tests** : xUnit v4 + FsUnit.xUnit — 960 tests au total.

### Ports gRPC (Debug / Release)

Les ports des 3 services sont définis **une seule fois** dans `Diplo.Abstractions/DiploPorts.fs` : `#if DEBUG` → 5001/5002/5003, sinon → 6001/6002/6003. `ServerConfig.runGrpcHost` force l'environnement selon la configuration de build (Development en Debug, Production sinon) quand les variables d'environnement sont absentes ; `appsettings.json` porte les ports Release et `appsettings.Development.json` les ports Debug. Les clients (CLI, GUI) et l'installateur consomment les constantes — ne jamais coder un port en dur ailleurs.

### Contraintes F#

- Compilation séquentielle : l'ordre dans `.fsproj` détermine la visibilité des modules.
- `JsonHelpers` a `[<RequireQualifiedAccess>]` — appels qualifiés (`JsonHelpers.tryGetString`).
- `[<CLIMutable>]` requis pour les types record envoyés/reçus par gRPC (protobuf-net).
- **FS0960** : les liaisons `let`/`do` doivent précéder les `member` dans les classes F#.
- **Commandes ViewModel** : les `let` bindings (ex. `let cmd = RelayCommand(...)`) sont placés avant les `member`, les closures capturent `this` via `as this` et ne s'exécutent qu'au clic.
- **`IDisposable` + `new`** : F# exige `new Type(args)` quand `Type` implémente `IDisposable`.
- **Paramètres optionnels** : `?` interdits hors des `member` F#.
- **`let mutable`** dans les classes : utilise des `let`/`do` bindings pour accéder aux propriétés avec `member private`.

## Commandes

```powershell
dotnet build Diplo.slnx                       # Build complète
.\pipeline.ps1 -DoTests                       # Tests unitaires
.\pipeline.ps1 -DoPublish                     # Publication self-contained
.\pipeline.ps1 -Clean -Restore                # Nettoyage + restauration NuGet
```

> **Attention** : `dotnet test --nologo` casse la découverte de tests avec le runner MTP (0 test exécuté, code de sortie 5). Ne pas utiliser `--nologo`.

## Environnement opencode

La configuration **globale** d'opencode (`~/.config/opencode/opencode.jsonc`) déclare deux serveurs LSP : TypeScript (tsserver via scoop) et PowerShell (**PowerShellEditorServices** installé sous `%LOCALAPPDATA%\opencode\tools\powershell-lsp\`, lancé en mode `-Stdio`). Particularité : PSES écrit son fichier de statut `PowerShellEditorServices.json` dans le répertoire courant du processus — le LSP démarre donc via `Set-Location %LOCALAPPDATA%\opencode` pour éviter de polluer les dépôts ; `/PowerShellEditorServices.json` figure aussi dans l'exclusion git globale (`core.excludesFile`). La config n'est pas rechargée à chaud : redémarrer opencode après modification.

## Conventions Git

- **Git LFS** : les binaires et médias sont stockés via LFS (voir `.gitattributes`). Un `git lfs install` est nécessaire au clone.
- Branche de développement : `dev` ; branche principale : `main`.
- **Commits** : ne jamais ajouter de trailer `Co-Authored-By` ni de mention de co-auteur ; l'auteur
  reste seul auteur. Pas de mention « Generated with Claude Code » dans les PR/issues sauf demande explicite.
- **Merge vers `main`** : ne jamais merger `dev` vers `main` sans demande explicite de l'utilisateur.

## Langue

Le projet est francophone : README, commentaires, commits et documentation en français (avec accents corrects).

## Sécurité

- **NuGet Audit** : activé (`NuGetAudit=true`, `NuGetAuditMode=latest`) dans `Directory.Build.props` — bloque la compilation si des vulnérabilités critiques sont détectées dans les dépendances NuGet.

## Validation ctr v2 — détails

Le montage réel des images disque et la communication gRPC ont été validés en conditions réelles avec containerd **v2.3.3** (Windows 11 26200, namespace `default`).

### Écarts ctr v2 (≥ v2.0) par rapport à v1

- `--namespace`/`-n` est une option **globale** (avant la sous-commande), et non locale.
- `container create` attend `<IMAGE> <CONTAINER>` (ordre inversé par rapport à v1) et ne produit **aucune sortie** en cas de succès.
- `exec` est `tasks exec` ; `task info` et `task logs` ont été **supprimés** en v2 : `task info` est remplacé par le parsing de `tasks list`.
- `PullImage` n'utilise volontairement pas de namespace : `ctr image pull` s'applique au namespace courant.

### Points corrigés au fil des validations

1. **ContainerLogs.read** ouvrait le fichier avec `FileShare.Read` strict — incompatible avec le handle d'écriture du conteneur en cours d'exécution (IOException). Passage à `FileShare.ReadWrite ||| FileShare.Delete`.
2. **StreamWriter AutoFlush** — les lignes de logs restaient en mémoire tant que le conteneur tournait. `AutoFlush <- true` ajouté.
3. **container create --cmd** — la commande était passée via un spec OCI avec l'option `--spec`, absente de `ctr v2.3.3`. La commande est désormais passée en positionnel (`ctr container create <image> <id> <cmd> [args...]`).
4. **UpdateContainer** — `ctr task update`/`ctr container update` n'existent pas en v2. Lève `RpcException(Unimplemented)` quand une limite est demandée (no-op sinon).
5. **container delete -f** — la suppression forcée d'un conteneur inexistant échouait par une erreur gRPC. Elle retourne désormais un succès (échecs journalisés en avertissement) — idempotent.
6. **Transport named pipes** — `PipeSecurity` contenait une règle « Deny Everyone » qui empêchait la création du pipe (règles Deny priment sur Allow sur Windows). Règle retirée, accès par `Allow FullControl` pour l'utilisateur courant.
7. **Ctrl+C sur container logs --follow** — le CLI sortait avec `STATUS_CONTROL_C_EXIT` (0xC000013A). Ajout de `CtrlCHandler` (pose `e.Cancel=true` + annulation) et rattrapage de `RpcException(Cancelled)`.
8. **container exec arguments espacés** — reconstruction de la ligne de commande avec échappement.
9. **MountState** — état des volumes montés persisté ; restauration au redémarrage du service.

### Logs

Les logs sont capturés par le service lors du démarrage détaché dans `%ProgramData%\Diplo\logs\<id>.log` et relus par `container logs` (`tail`, `since` ; `--follow` suit le fichier et émet les nouvelles lignes au fil de l'eau).
