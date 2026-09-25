# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Projet

**Diplo** â€” systÃ¨me distribuÃ© de microservices gRPC pour la gestion de conteneurs Windows (cf. `README.md`).

## Architecture

11 projets source (.NET 10, F#) + 11 projets de test :

| Projet             | RÃ´le                                                                                                                                                                                                                                                          |
| ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| DiploWalker.Abstractions | Interfaces partagÃ©es, validation, sÃ©curitÃ©, modules utilitaires mutualisÃ©s                                                                                                                                                                                    |
| DiploWalker.Container    | Service gRPC de gestion des conteneurs (containerd) â€” crÃ©ation, cycle de vie, montage de volumes (`--mount`), recherche d'images en ligne dans les catalogues autorisÃ©s (docker.io, quay.io, mcr.microsoft.com, ghcr.io) |
| DiploWalker.Volume       | Service gRPC de gestion des volumes persistants                                                                                                                                                                                                               |
| DiploWalker.Network      | Service gRPC de gestion des rÃ©seaux (CNI)                                                                                                                                                                                                                     |
| DiploWalker.Installer    | Installation Windows (services, containerd, CNI)                                                                                                                                                                                                              |
| DiploWalker.Grpc         | Types messages et interfaces de service gRPC (protobuf-net, code-first), mappings de drivers                                                                                                                                                                  |
| DiploWalker.Contracts    | Types partagÃ©s entre services                                                                                                                                                                                                                                 |
| DiploWalker.Core         | Clients gRPC, abstraction `IOutputPort`, `MountParser` (format `src=...,dst=...[;ro]`), config client `DiploWalker.json` et support des named pipes (`http://pipe:/<nom>`), factory gRPC mutualisÃ©e                                                                 |
| DiploWalker.Disk         | Montage d'images disque (qcow2, qcow1, raw, vhd, vhdx, vmdk, vdi, dmg, parallels, iso) via DiscUtils/pilotes maison + support R/W Btrfs, XFS, HFS+ via Hawkynt.FileFormats.FileSystems. CrÃ©ation d'images disque (VHD, VHDX, VMDK, VDI, Raw, ISO) via `FsImage.create` ; detection/extraction ISO9660/UDF via le parseur maison `IsoFs`. RÃ©fÃ©rencÃ© par `DiploWalker.Volume` (faÃ§ade `IsoDriver`). |
| DiploWalker.Cli          | Client CLI (Spectre.Console)                                                                                                                                                                                                                                  |
| DiploWalker.Gui          | Interface graphique Avalonia                                                                                                                                                                                                                                  |

### Modules mutualisÃ©s

Les modules suivants ont Ã©tÃ© extraits du code dupliquÃ© et centralisÃ©s dans DiploWalker.Abstractions / DiploWalker.Grpc :

| Module                       | Projet             | RÃ´le                                                                                                                                                                                                                                                                                   |
| ---------------------------- | ------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `JsonHelpers`                | DiploWalker.Abstractions | Extraction typÃ©e de propriÃ©tÃ©s depuis `JsonElement` (`tryGetString`, `tryGetInt64`, `tryGetDouble`, `tryGetBool`, `tryGetElement`, `tryGetStringValue`). Module `[<RequireQualifiedAccess>]` â€” appeler via `JsonHelpers.tryGetString`.                                                 |
| `DiploJson`                  | DiploWalker.Abstractions | Options de sÃ©rialisation JSON centralisÃ©es (`defaultOptions`, `snakeCaseOptions`, `caseInsensitiveOptions`, `withMaxDepth`, `documentOptions`).                                                                                                                                        |
| `ProcessExec`                | DiploWalker.Abstractions | ExÃ©cution de processus externes (`run`, `runWithResult`, `runUnit`) et commandes PowerShell (`runPowerShell`, `runPowerShellScript`). GÃ¨re le timeout, le Kill, et la lecture asynchrone stdout/stderr. `runUnit` exÃ©cute sans retourner la sortie standard (usage montage/dÃ©montage). |
| `ServiceGuards`              | DiploWalker.Abstractions | Guards de validation d'entrÃ©e rÃ©utilisables (`requireNonEmpty`, `requireId`, `requirePositive`, `requireInRange`, `requireSafePath`, `requireLocalAddress`, `requireSafeCommand`). LÃ¨vent `RpcException(InvalidArgument)`.                                                             |
| `CachedConfig<'T>`           | DiploWalker.Abstractions | Cache gÃ©nÃ©rique avec invalidation manuelle, protÃ©gÃ© par un verrou. Chargement paresseux via `Value`, invalidation via `Invalidate()`.                                                                                                                                                  |
| `DriverMappings`             | DiploWalker.Grpc         | Mapping typeâ†”string pour les drivers volume (`StorageDriverType`) et rÃ©seau (`NetworkDriver`). Fonctions `parse*`, `isValid*`, `*ToString`, `all*Names`.                                                                                                                               |
| `GrpcClientFactory`          | DiploWalker.Core         | Construction de canaux gRPC TCP ou named pipe avec retry (5 tentatives, backoff exponentiel) et credentials par token.                                                                                                                                                                 |
| `TestHelpers`                | DiploWalker.TestHelpers  | Helpers pour les tests (`createTempDir`, `cleanupDir`).                                                                                                                                                                                                                                |
| `Cmd`                        | DiploWalker.Gui          | Helpers try/with mutualisÃ©s pour les commandes GUI (`run` async, `runSync` synchrone, `runSyncWith` avec callback d'erreur custom).                                                                                                                                                    |
| `ContainerDetailUserControl` | DiploWalker.Gui          | UserControl XAML pour le dÃ©tail d'un conteneur sÃ©lectionnÃ© (propriÃ©tÃ©s, actions, montage, logs/exec). ChargÃ© via `AvaloniaRuntimeXamlLoader`.                                                                                                                                          |
| `HawkyntFs`                  | DiploWalker.Disk         | Adaptateur Hawkynt.FileFormats.FileSystems pour l'extraction et la rÃ©Ã©criture de Btrfs, XFS et HFS+. Seuil de 2 Go pour Ã©viter le tout-en-mÃ©moire ; fallback DiscUtils au-delÃ . Tous les readers/streams utilisent `use` pour la libÃ©ration garantie.                                  |
| `ComposeEditorViewModel`     | DiploWalker.Gui          | ViewModel de l'Ã©diteur Compose (AvalonEdit) : chargement/sauvegarde de fichiers YAML, validation en temps rÃ©el (clÃ© `services` absente, services sans `image` ni `build`), collection `Errors` exposÃ©e pour le bindind XAML.                                                           |
| `RemoteDriverHelpers`        | DiploWalker.Volume       | Helpers mutualisÃ©s pour les drivers distants (NFS, AWS EFS, GCP Filestore, Azure Files, SMB) : `mountVolume`, `unmountVolume`, `unmountNfsLike` (umount â†’ fallback mount -u).                                                                                                        |
| `RemoteVolumeDriver`         | DiploWalker.Volume       | Classe de base abstraite des drivers distants : mutualise le store, la crÃ©ation via `RemotePath`, le montage/dÃ©montage (`Mount`/`Unmount`, dÃ©faut `unmountNfsLike`) et `PruneVolumes`. Nfs/Smb/Azure/Aws/Gcp n'exposent plus que `RemotePath`/`Mount` (et `Unmount` pour Azure/SMB).        |
| `BinaryIo`                   | DiploWalker.Disk         | Module de lecture/Ã©criture binaire partagÃ© (`be16/32/64`, `le16/32/64`, `putBe*`/`putLe*`, `readFully`) et helper `protect` (try â†’ `Result<_,string>`). UtilisÃ© par Qcow1Fs, Qcow2, ParallelsFs et FsImage.                                                                             |
| `RawImageStream`             | DiploWalker.Disk         | Classe de base abstraite `Stream` des pilotes maison : champs `fs`, `CanRead/CanSeek/CanWrite`, `Flush`, `Dispose` guardÃ©. HÃ©ritÃ©e par `Qcow1Stream`, `Qcow2Stream` et `ParallelsStream` (leurs `Position/Length/Read/Write/Seek/SetLength` restent propres).                           |
| `DiscFsHelper`               | DiploWalker.Disk         | Mutualise `toRealRel`, `realFrom` (confinement anti-traversal), `openFileSystem`, `copyDirectory`, `copyIntoFs`, `deleteFsEntries`. UtilisÃ© par VdiFs, DmgFs et FsImage (VDI/DMG passent par le `realFrom` confinÃ©).                                                                    |
| `VdiFs`                      | DiploWalker.Disk         | Adaptateur DiscUtils.Vdi pour l'extraction et la rÃ©Ã©criture R/W d'images VDI (VirtualBox).                                                                                                                                                                                             |
| `Qcow1Fs`                    | DiploWalker.Disk         | Pilote maison pour les images QCOW v1 (QFI\\xFE) : lecture/Ã©criture in-place via `Qcow1Stream`.                                                                                                                                                                                        |
| `DmgFs`                      | DiploWalker.Disk         | Adaptateur DiscUtils.Dmg pour l'extraction (lecture seule) d'images DMG (Apple Disk Image).                                                                                                                                                                                            |
| `ParallelsFs`                | DiploWalker.Disk         | Pilote maison pour les images Parallels (.hdd, .hds) : lecture/Ã©criture in-place via `ParallelsStream`.                                                                                                                                                                                |
| `FsImage`                    | DiploWalker.Disk         | CrÃ©ation d'images disque (`create`), extraction (`extract`) et rÃ©Ã©criture (`writeBack`) de systÃ¨mes de fichiers. ChaÃ®ne d'adaptateurs IsoFs â†’ Hawkynt â†’ VdiFs â†’ DmgFs â†’ DiscUtils. Supporte Raw, VHD, VHDX, VMDK, VDI et ISO en crÃ©ation (`createCore` court-circuite le format ISO via `IsoFs.createCore`, dÃ©fini en lecture seule). |
| `IsoFs`                      | DiploWalker.Disk         | Parseur maison ISO9660 (`IsoSource`/`Iso9660`/`Udf`/`IsoImage`, dÃ©placÃ© de `DiploWalker.Volume`) + gÃ©nÃ©rateur ISO9660 niveau 1 (`create`, noms 8.3 ASCII, sans Joliet/Rock Ridge). `tryExtract` retourne `None` si le format n'est pas dÃ©tectÃ© ou en cas d'erreur (log warning) ; `tryWriteBack` renvoie `false` (ISO en lecture seule). ConsommÃ© par `FsImage` et par la faÃ§ade `IsoDriver` de `DiploWalker.Volume`. |
| `RegistrySearch`             | DiploWalker.Container    | Recherche d'images dans les catalogues en ligne des registres autorisÃ©s : docker.io et quay.io via leur API de recherche publique ; mcr.microsoft.com via le catalogue `_catalog` public tÃ©lÃ©chargÃ© puis filtrÃ© **localement** (insensible Ã  la casse, pas de requÃªte serveur) ; ghcr.io sans API publique â†’ aucun rÃ©sultat. `searchWith` injecte un `HttpClient` (testable), `search` utilise le client partagÃ© (timeout 30 s). Liste blanche : docker.io, quay.io, ghcr.io, mcr.microsoft.com. |
| `ImagesTabViewModel`         | DiploWalker.Gui          | ViewModel de l'onglet Images : liste des images locales (lister/tÃ©lÃ©charger/inspecter/Ã©tiqueter/supprimer/nettoyer), recherche en ligne dÃ©clenchÃ©e par la touche EntrÃ©e dans le champ dÃ©diÃ© (dÃ©bouncÃ©e 250 ms, annulable via `CancellationTokenSource` sous verrou), grille de rÃ©sultats avec Â« Tirer la sÃ©lection Â». |

## Stack

- **.NET 10** (`dotnet 10.0.400` installÃ© localement).
- Solution : **`DiploWalker.slnx`** (format XML compact .NET 10).
- Orientation **100 % F#** (services, drivers, CLI et gRPC en code-first protobuf-net).
- **GUI** : Avalonia 12.1.2 avec AvalonEdit 12.0.0 (Ã©diteur YAML Compose avec colorisation syntaxique via TextMate). Menu **Fichier** â†’ *Exporter le journalâ€¦* (Ã©crit les lignes horodatÃ©es du journal dans un `.txt` via le sÃ©lecteur de fichier).
- **Tests** : xUnit v4 + FsUnit.xUnit â€” 1 612 tests au total (dont 1 ignorÃ© dans `DiploWalker.Disk.Tests` faute de privilÃ¨ge de crÃ©ation de liens symboliques NTFS).

## Consignes impÃ©ratives pour les agents

- **La solution est `DiploWalker.slnx`** (formate XML compact .NET 10). Construire et tester toujours via `dotnet build DiploWalker.slnx` et `.\pipeline.ps1 -DoTests` â€” il n'existe pas de `DiploWalker.sln`.
- **Suivre impÃ©rativement TOUTES les compÃ©tences dotnet** chargÃ©es (dont `dotnet-practices` et `dotnet-code-review`) Ã  chaque Ã©criture, modification ou relecture de code .NET : aucune exception pour une Ã©dition rapide ou un patch supposÃ© banal.

### Contournement FS0366 (tests Avalonia GUI)

Les fakes des interfaces `Avalonia.Platform.Storage` (`FakeStorageProvider.fs`) dÃ©clenchent l'erreur fantÃ´me FS0366 Â« Aucune implÃ©mentation pour 'IStorageItem...' Â» quand F# compile contre les **rÃ©fÃ©rences** assemblies d'Avalonia (`ref\net10.0`). Dans `tests/DiploWalker.Gui.Tests/DiploWalker.Gui.Tests.fsproj`, la cible `UseLibAvaloniaBase` (aprÃ¨s `ResolvePackageAssets`) retire `Avalonia.Base` des `ResolvedCompileFileDefinitions` et la remplace par l'assembly `lib\net10.0\Avalonia.Base.dll` via `$(PkgAvalonia)` â€” exposÃ© par la `PackageReference Include="Avalonia"` directe. Ne pas retirer cette rÃ©fÃ©rence ni `ExcludeAssets` : le swap ciblÃ© d'`Avalonia.Base` seul prÃ©serve `Avalonia.dll` (AppBuilder, HeadlessApp).

### Flaky connu (Ã  ne pas Â« corriger Â» Ã  la hÃ¢te)

`tests/DiploWalker.Gui.Tests/OptionalBehaviorTests.fs` (~ligne 156) Â« VolumeTabViewModel CreateImage cree une image raw depuis le repertoire source Â» : intermittente en suite complÃ¨te ou sous charge disque (timeout du `waitUntil` â‰ˆ 5 s), **passe systÃ©matiquement en isolation et au re-run**. Ne pas rejouer/patch sur un seul Ã©chec â€” relancer le run concernÃ© avant d'investiguer.

### Ports gRPC (Debug / Release)

Les ports des 3 services sont dÃ©finis **une seule fois** dans `DiploWalker.Abstractions/DiploWalkerPorts.fs` : `#if DEBUG` â†’ 5001/5002/5003, sinon â†’ 6001/6002/6003. `ServerConfig.runGrpcHost` force l'environnement selon la configuration de build (Development en Debug, Production sinon) quand les variables d'environnement sont absentes ; `appsettings.json` porte les ports Release et `appsettings.Development.json` les ports Debug. Les clients (CLI, GUI) et l'installateur consomment les constantes â€” ne jamais coder un port en dur ailleurs.

### Contraintes F#

- Compilation sÃ©quentielle : l'ordre dans `.fsproj` dÃ©termine la visibilitÃ© des modules.
- `JsonHelpers` a `[<RequireQualifiedAccess>]` â€” appels qualifiÃ©s (`JsonHelpers.tryGetString`).
- `[<CLIMutable>]` requis pour les types record envoyÃ©s/reÃ§us par gRPC (protobuf-net).
- **FS0960** : les liaisons `let`/`do` doivent prÃ©cÃ©der les `member` dans les classes F#.
- **Commandes ViewModel** : les `let` bindings (ex. `let cmd = RelayCommand(...)`) sont placÃ©s avant les `member`, les closures capturent `this` via `as this` et ne s'exÃ©cutent qu'au clic.
- **`IDisposable` + `new`** : F# exige `new Type(args)` quand `Type` implÃ©mente `IDisposable`.
- **ParamÃ¨tres optionnels** : `?` interdits hors des `member` F#.
- **`let mutable`** dans les classes : utilise des `let`/`do` bindings pour accÃ©der aux propriÃ©tÃ©s avec `member private`.
- **`try ... with ... finally` combinÃ© impossible** (FS0010) : imbriquer `try (try ... with :? OperationCanceledException -> ()) finally ...`.
- **Pas de `do!` dans un `finally`** (corps synchrone) : pour disposer un `IAsyncEnumerator`, `try do! e.DisposeAsync().AsTask() with _ -> ()` â€” jamais `.AsTask().Wait(...)`.
- **Protocole CTS (suivi de flux/journaux)** : annuler = sous `lock`, `Cancel()` puis champ â† `null` (jamais de `Dispose()` ici) ; disposer = uniquement le worker, dans son `finally`, sous le mÃªme verrou, avec garde d'identitÃ© (`if cts = monInstance`).
- **`ObservableCollection` (Avalonia) sans `RemoveRange`** : journal incrÃ©mental bornÃ© via compteur `renderedLineCount` + suppression des N premiÃ¨res lignes (scan du premier `\n`), rebuild complet seulement si `Clear` externe.
- **Debounce recherche (GUI)** : `DispatcherTimer` ~250 ms redÃ©marrÃ© Ã  chaque frappe (Stop/Start) sur `KeyDown` ; jamais de recherche synchrone dans le thread UI.

## Commandes

```powershell
dotnet build DiploWalker.slnx                       # Build complÃ¨te
.\pipeline.ps1 -DoTests                       # Tests unitaires
.\pipeline.ps1 -DoPublish                     # Publication self-contained
.\pipeline.ps1 -Clean -Restore                # Nettoyage + restauration NuGet
```

> **Attention** : `dotnet test --nologo` casse la dÃ©couverte de tests avec le runner MTP (0 test exÃ©cutÃ©, code de sortie 5). Ne pas utiliser `--nologo`.
>
> **Tests dépendants de Windows** : les tests qui s'appuient sur `cmd.exe`, PowerShell,
> `C:\ProgramData`, les tubes nommés ou le montage d'images portent l'attribut
> `[<Trait("Platform", "Windows")>]`. Sur les autres systèmes, `pipeline.ps1` les exclut avec
> `--filter-not-trait "Platform=Windows"` ; sous Windows, toute la suite est exécutée.

## Environnement opencode

La configuration **globale** d'opencode (`~/.config/opencode/opencode.jsonc`) dÃ©clare deux serveurs LSP : TypeScript (tsserver via scoop) et PowerShell (**PowerShellEditorServices** installÃ© sous `%LOCALAPPDATA%\opencode\tools\powershell-lsp\`, lancÃ© en mode `-Stdio`). ParticularitÃ© : PSES Ã©crit son fichier de statut `PowerShellEditorServices.json` dans le rÃ©pertoire courant du processus â€” le LSP dÃ©marre donc via `Set-Location %LOCALAPPDATA%\opencode` pour Ã©viter de polluer les dÃ©pÃ´ts ; `/PowerShellEditorServices.json` figure aussi dans l'exclusion git globale (`core.excludesFile`). La config n'est pas rechargÃ©e Ã  chaud : redÃ©marrer opencode aprÃ¨s modification.

## Conventions Git

- **Git LFS** : les binaires et mÃ©dias sont stockÃ©s via LFS (voir `.gitattributes`). Un `git lfs install` est nÃ©cessaire au clone.
- Branche de dÃ©veloppement : `dev` ; branche principale : `main`.
- **Commits** : ne jamais ajouter de trailer `Co-Authored-By` ni de mention de co-auteur ; l'auteur
  reste seul auteur. Pas de mention Â« Generated with Claude Code Â» dans les PR/issues sauf demande explicite.
- **Merge vers `main`** : ne jamais merger `dev` vers `main` sans demande explicite de l'utilisateur.

## Langue

Le projet est francophone : README, commentaires, commits et documentation en franÃ§ais (avec accents corrects).

## SÃ©curitÃ©

- **NuGet Audit** : activÃ© (`NuGetAudit=true`, `NuGetAuditMode=latest`) dans `Directory.Build.props` â€” bloque la compilation si des vulnÃ©rabilitÃ©s critiques sont dÃ©tectÃ©es dans les dÃ©pendances NuGet.

## Validation ctr v2 â€” dÃ©tails

Le montage rÃ©el des images disque et la communication gRPC ont Ã©tÃ© validÃ©s en conditions rÃ©elles avec containerd **v2.3.3** (Windows 11 26200, namespace `default`).

### Ã‰carts ctr v2 (â‰¥ v2.0) par rapport Ã  v1

- `--namespace`/`-n` est une option **globale** (avant la sous-commande), et non locale.
- `container create` attend `<IMAGE> <CONTAINER>` (ordre inversÃ© par rapport Ã  v1) et ne produit **aucune sortie** en cas de succÃ¨s.
- `exec` est `tasks exec` ; `task info` et `task logs` ont Ã©tÃ© **supprimÃ©s** en v2 : `task info` est remplacÃ© par le parsing de `tasks list`.
- `PullImage` n'utilise volontairement pas de namespace : `ctr image pull` s'applique au namespace courant.

### Points corrigÃ©s au fil des validations

1. **ContainerLogs.read** ouvrait le fichier avec `FileShare.Read` strict â€” incompatible avec le handle d'Ã©criture du conteneur en cours d'exÃ©cution (IOException). Passage Ã  `FileShare.ReadWrite ||| FileShare.Delete`.
2. **StreamWriter AutoFlush** â€” les lignes de logs restaient en mÃ©moire tant que le conteneur tournait. `AutoFlush <- true` ajoutÃ©.
3. **container create --cmd** â€” la commande Ã©tait passÃ©e via un spec OCI avec l'option `--spec`, absente de `ctr v2.3.3`. La commande est dÃ©sormais passÃ©e en positionnel (`ctr container create <image> <id> <cmd> [args...]`).
4. **UpdateContainer** â€” `ctr task update`/`ctr container update` n'existent pas en v2. LÃ¨ve `RpcException(Unimplemented)` quand une limite est demandÃ©e (no-op sinon).
5. **container delete -f** â€” la suppression forcÃ©e d'un conteneur inexistant Ã©chouait par une erreur gRPC. Elle retourne dÃ©sormais un succÃ¨s (Ã©checs journalisÃ©s en avertissement) â€” idempotent.
6. **Transport named pipes** â€” `PipeSecurity` contenait une rÃ¨gle Â« Deny Everyone Â» qui empÃªchait la crÃ©ation du pipe (rÃ¨gles Deny priment sur Allow sur Windows). RÃ¨gle retirÃ©e, accÃ¨s par `Allow FullControl` pour l'utilisateur courant.
7. **Ctrl+C sur container logs --follow** â€” le CLI sortait avec `STATUS_CONTROL_C_EXIT` (0xC000013A). Ajout de `CtrlCHandler` (pose `e.Cancel=true` + annulation) et rattrapage de `RpcException(Cancelled)`.
8. **container exec arguments espacÃ©s** â€” reconstruction de la ligne de commande avec Ã©chappement.
9. **MountState** â€” Ã©tat des volumes montÃ©s persistÃ© ; restauration au redÃ©marrage du service.

### Logs

Les logs sont capturÃ©s par le service lors du dÃ©marrage dÃ©tachÃ© dans `%ProgramData%\Diplo\logs\<id>.log` et relus par `container logs` (`tail`, `since` ; `--follow` suit le fichier et Ã©met les nouvelles lignes au fil de l'eau).


