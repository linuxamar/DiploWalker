# CONTEXT.md

MÃ©moire de travail de session (Ã  rÃ©Ã©crire Ã  chaque session). Les rÃ¨gles stables vivent dans `AGENTS.md` et `CLAUDE.md` ; ici uniquement l'Ã©tat prÃ©sent et la connaissance non Ã©crite ailleurs.

## Session en cours

- **Date :** 2026-09-25
- **Branche :** `dev` (synchrone avec `main`)
- **Objectif :** porter la suite de tests et les chemins applicatifs entre Linux et Windows, pour que `./pipeline.ps1 -DoTests` soit exÃ©cutable sous Linux. **LivrÃ© et promu.**
- **Dernier commit :** `87c45a2` (recadrage d'indentation aprÃ¨s la promotion).

## Ãtat Git

- `dev` et `main` sont au mÃªme commit (`87c45a2`), tout est poussÃ© : **0 commit Ã  promouvoir**.
- Historique de promotion : `main` porte un commit de fusion par campagne, et `dev` est ensuite recalÃ© dessus en fast-forward â d'oÃ¹ les nombreux commits de fusion accumulation sur `main`. Ne pas y lire du travail perdu : `git diff dev main` est lindicateur fiable, pas `rev-list --count`.
- L'Ã©cart de contenu entre `main` et `dev` Ã  la promotion venait de `Directory.Packages.props` : main Ã©pinglait `TextMateSharp.Grammars` 2.0.4 alors qu'aucun `PackageReference` ne le demande â€” `MainWindow.axaml.fs` l'utilise via `AvaloniaEdit.TextMate`, qui l'amÃ¨ne en transitif. EntrÃ©e retirÃ©e : le code compile et les 173 tests GUI passent sur la 2.0.3 transitive.

## PortabilitÃ© Linux (livrÃ© et promu)

- **Signature forte** : le groupe strong-name de `Directory.Build.props` est conditionnÃ© Ã  `Windows_NT`. La signature F# impose RSA-SHA1, refusÃ© par les OpenSSL 3.5 des distributions Linux (Â« invalid digest Â»), et `PublicSign` Ã©choue avec la clÃ© complÃ¨te (Â« StrongNameSignatureSize failed Â»).
- **DÃ©couverte des tests** : `pipeline.ps1` exclut les tests `[<Trait("Platform", "Windows")>]` avec `--filter-not-trait` hors Windows, exÃ©cute toute la suite sous Windows.
- **`AppPaths`** (nouveau) : racine des donnÃ©es unifiÃ©e et surchargeable par `DIPLO_DATA_ROOT` â `%ProgramData%\Diplo` sous Windows, `$XDG_DATA_HOME/Diplo` ailleurs. `SpecialFolder.CommonApplicationData` valait `/usr/share` sous Linux, non inscriptible sans root. `AppPaths.userRoot` porte la clÃ© AES-GCM des secrets de registres. Remplace les chemins codÃ©s en dur dans `AuthToken`, `RegistryAuth`, `ContainerLogs`, `DiskMounter` et `MountState`.
- **Isolation des tests** : fixture d'assembly xUnit (`tests/TestDataRoot.fs`, injectÃ©e par `tests/Directory.Build.targets`) qui redirige la racine des donnÃ©es vers `/tmp/diplo-test-data`, pour que la suite n'Ã©crive plus dans `%ProgramData%` ni `~/.local/share`. `F# ModuleInitializer` n'est pas supportÃ©, d'oÃ¹ la fixture.
- **TestDataRoot** : vÃ©rifier l'absence de `~/.local/share/Diplo` aprÃ¨s une passe complÃ¨te.
- **Quatre-vingt-quinze** tests tagged au dÃ©part, dont 71 qui ne dÃ©pendaient d'aucune API Windows ; il n'en reste qu'**un** (named pipe de volume).
- **Helper de credentials** : script PowerShell unique, DPAPI sous Windows et AES-GCM ailleurs (format `base64(nonce | cipher | tag)` de `RegistryAuth.protect`), branche choisie via `[Environment]::OSVersion.Platform` car `$IsWindows` n'existe pas en PowerShell 5.1. Chemins de l'Ã©tat et de la clÃ© passÃ©s par `DIPLO_REGISTRY_AUTH_STATE` / `DIPLO_REGISTRY_KEY_FILE`, sinon dÃ©duits de `$PSScriptRoot/..`. `writeHelperTo` installe un lanceur de plateforme (`diplo-cred-helper.cmd` ou `diplo-cred-helper` en `sh`, mode 0755) que containerd exÃ©cute tel quel depuis `hosts.toml`.
- **`ensureHelperRuntime`** : refuse l'extraction authentifiÃ©e si `pwsh` est absent du `PATH` (`FailedPrecondition`), sinon containerd ne remontait qu'un refus d'authentification opaque. Recherche par `tryFindOnPath`, sans lancer de processus, et sans mapper une entrÃ©e vide du PATH sur Â« . Â» (le shell POSIX y verrait le rÃ©pertoire courant).
- **Validation non rejouÃ©e aprÃ¨s la promotion** : la suite complÃ¨te avait Ã©tÃ© verte sur le contenu promu (1626 tests) ; sa rÃ©exÃ©cution sur l'Ã©tat final a Ã©tÃ© Ã©cartÃ©e Ã  la demande. L'Ã©cart avec l'Ã©tat validÃ© se limite au recadrage d'une ligne dans `Directory.Packages.props`, sans effet sur MSBuild.

## Reste Ã  faire

- **Mojibake** : `CONTEXT.md`, `SECURITY.md` et les `.fs` contiennent du texte doublement encodÃ© (UTF-8 relu en Latin-1). Conserver la convention existante dans les Ã©ditions ciblÃ©es plutÃ´t que de tout rÃ©encoder. Un script de rÃ©Ã©criture de ce fichier applique le mÃªme doublement au texte rÃ©digÃ© : c'est ainsi que le fichier reste homogÃ¨ne.

## DÃ©cisions actÃ©es

- **Le test named pipe reste taguÃ© Windows, dÃ©libÃ©rÃ©ment.** `VolumeIntegrationTests.CreateVolume via named pipe fonctionne de bout en bout` est le seul test d'end-to-end qui ne passe pas par TCP : il exerce `ListenNamedPipe` cÃ´tÃ© serveur et `NamedPipeClientStream` via `SocketsHttpHandler.ConnectCallback` cÃ´tÃ© client, donc toute la chaÃ®ne du transport alternatif. Ce n'est pas du code mort : le CLI l'expose (`config init --transport pipe`, valeurs `tcp` et `pipe` en `ConfigCommands.fs`) et les `appsettings.json` activent `UseTcp` et `UseNamedPipes` ensemble. Le tag est un marqueur de couverture, pas une dette. Le porter sur socket de domaine Unix exigerait d'ajouter un vÃ©ritable transport au produit (`UseUnixSocket` dans `ServerConfig.configureKestrel`, branche `unix://` dans `GrpcClientFactory`, choix de transport dans le CLI) alors que le dÃ©ploiement cible Windows Server : ce serait une dÃ©cision produit, pas un chantier de test. Les 9 autres tests e2e du fichier passent par TCP et couvrent le service ; ne pas proposer d'Ã©liminer ce tag sans que `--transport pipe` reste supportÃ©.

- **`pwsh` n'entre pas dans les prÃ©requis d'installation.** Le dÃ©ploiement cible Windows Server, oÃ¹ `powershell` 5.1 est toujours prÃ©sent ; `pwsh` ne concerne que les hÃ´tes Linux (dÃ©veloppement, CI) et `ensureHelperRuntime` y est le filet de sÃ©curitÃ©.

## Connaissance tribale

- F# : pas de `try/with/finally` combinÃ© (imbrication), pas de `do!` en `finally`, protocoles CTS (Cancel sous verrou + null, Dispose par le worker), debounce UI 250 ms (DispatcherTimer).
- `TreatWarningsAsErrors=true` â 0 avertissement attendu.
- Tests : 1 626 sous Linux, dont 1 ignorÃ© (symlink NTFS attendu) et 1 taguÃ© `Platform=Windows` (named pipe).
- Validation : `./pipeline.ps1 -DoTests` (~2 min 30 sur ce poste).
- `dotnet test --project <fsproj> ... -- --filter-not-trait "Platform=Windows"` : syntaxe MTP, et surtout pas de `--nologo`.
- Docs, comments et commits en franÃ§ais, sans trailer.
- SÃ©curitÃ© : dÃ©tails dans `SECURITY.md` (bornes gRPC 64 Mo, WriteFile 50 Mo, no-store 401/429, DPAPI/AES-GCM pour les identifiants de registre).
- **Architecture flaky** : `waitPump` (HeadlessBehaviorTests) = polling 10 ms sur Avalonia headless ; `waitUntil` (CatalogViewModelTests) = polling 20 ms sans headless ; `UiThread.Post` exÃ©cute inline si `Application.Current = null` (pas de dispatcher).
- **C3 â intÃ©gritÃ© des artefacts** (livrÃ©, `7b7a2a5`) : checksums dans `assets/artifacts.manifest`, signÃ©s RSA-4096/SHA-384 par une clÃ© privÃ©e hors bande (`~/.diplo/diplo-release.key`, chiffrÃ©e AES-256-CBC, jamais dans le dÃ©pÃ´t), vÃ©rifiÃ©s fail-closed au dÃ©marrage. Signature dÃ©terministe : re-signer le mÃªme manifeste rÃ©gÃ©nÃ¨re le mÃªme `.sig`, un diff signale donc un manifeste ou une clÃ© diffÃ©rents. Empreinte sha256 de la clÃ© publique courante : `02:7F:8A:F3:95:5F:FD:9C:A6:D1:28:DC:FC:EE:9E:85:7F:7E:7A:5F:27:C8:F2:B5:C0:44:81:AE:40:D6:7F:81`.
