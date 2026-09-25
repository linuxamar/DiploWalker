# CONTEXT.md

MÃ©moire de travail de session (Ã  rÃ©Ã©crire Ã  chaque session). Les rÃ¨gles stables vivent dans `AGENTS.md` et `CLAUDE.md` ; ici uniquement l'Ã©tat prÃ©sent et la connaissance non Ã©crite ailleurs.

## Session en cours

- **Date :** 2026-09-25
- **Branche :** `dev`
- **Objectif :** porter la suite de tests et les chemins applicatifs entre Linux et Windows, pour que `./pipeline.ps1 -DoTests` soit exÃ©cutable sous Linux.
- **Dernier commit :** `ada7404` (helper de credentials portable sous Unix).

## Ãtat Git

- `dev` â `main` : 10 commits Ã  promouvoir. `main` est 50 commits en avance : la promotion exige un merge sur place cÃ´tÃ© `main`, pas un fast-forward.
- Arbre propre, tout est poussÃ© sur `origin/dev`.

## PortabilitÃ© Linux (livrÃ© : `9350ed7`, `dba4d25`, `fd8ef57`, `ada7404`)

- **Signature forte** : le groupe strong-name de `Directory.Build.props` est conditionnÃ© Ã  `Windows_NT`. La signature F# impose RSA-SHA1, refusÃ© par les OpenSSL 3.5 des distributions Linux (Â« invalid digest Â»), et `PublicSign` Ã©choue avec la clÃ© complÃ¨te (Â« StrongNameSignatureSize failed Â»).
- **DÃ©couverte des tests** : `pipeline.ps1` exclut les tests `[<Trait("Platform", "Windows")>]` avec `--filter-not-trait` hors Windows, exÃ©cute toute la suite sous Windows.
- **`AppPaths`** (nouveau) : racine des donnÃ©es unifiÃ©e et surchargeable par `DIPLO_DATA_ROOT` â `%ProgramData%\Diplo` sous Windows, `$XDG_DATA_HOME/Diplo` ailleurs. `SpecialFolder.CommonApplicationData` valait `/usr/share` sous Linux, non inscriptible sans root. `AppPaths.userRoot` porte la clÃ© AES-GCM des secrets de registres. Remplace les chemins codÃ©s en dur dans `AuthToken`, `RegistryAuth`, `ContainerLogs`, `DiskMounter` et `MountState`.
- **Isolation des tests** : fixture d'assembly xUnit (`tests/TestDataRoot.fs`, injectÃ©e par `tests/Directory.Build.targets`) qui redirige la racine des donnÃ©es vers `/tmp/diplo-test-data`, pour que la suite n'Ã©crive plus dans `%ProgramData%` ni `~/.local/share`. `F# ModuleInitializer` n'est pas supportÃ©, d'oÃ¹ la fixture.
- **TestDataRoot** : vÃ©rifier l'absence de `~/.local/share/Diplo` aprÃ¨s une passe complÃ¨te.
- **Quatre-vingt-quinze** tests tagged au dÃ©part, dont 71 qui ne dÃ©pendaient d'aucune API Windows ; il n'en reste qu'**un** (named pipe de volume).
- **Helper de credentials** (`ada7404`) : script PowerShell unique, DPAPI sous Windows et AES-GCM ailleurs (format `base64(nonce | cipher | tag)` de `RegistryAuth.protect`), branche choisie via `[Environment]::OSVersion.Platform` car `$IsWindows` n'existe pas en PowerShell 5.1. Chemins de l'Ã©tat et de la clÃ© passÃ©s par `DIPLO_REGISTRY_AUTH_STATE` / `DIPLO_REGISTRY_KEY_FILE`, sinon dÃ©duits de `$PSScriptRoot/..`. `writeHelperTo` installe un lanceur de plateforme (`diplo-cred-helper.cmd` ou `diplo-cred-helper` en `sh`, mode 0755) que containerd exÃ©cute tel quel depuis `hosts.toml`.
- **`ensureHelperRuntime`** : refuse l'extraction authentifiÃ©e si `pwsh` est absent du `PATH` (`FailedPrecondition`), sinon containerd ne remontait qu'un refus d'authentification opaque. Recherche par `tryFindOnPath`, sans lancer de processus, et sans map.Entry du PATH sur Â« . Â» (une entrÃ©e vide vaut le rÃ©pertoire courant pour un shell POSIX : le helper ne doit pas s'y rÃ©soudre).

## Reste Ã  faire

- **Promotion `dev` â `main`** : 10 commits, en merge direct et sans PR.
- **Test named pipe** : `VolumeIntegrationTests.CreateVolume via named pipe fonctionne de bout en bout` reste taguÃ© Windows. Il exerce `ListenNamedPipe` / `NamedPipeClientStream`, un transport que la production n'utilise que sous Windows ; le porter supposerait un socket de domaine Unix que le code n'exerce pas. DÃ©cision Ã  prendre : garder ce test de fonctionnalitÃ© Windows, ou ajouter un Ã©quivalent `ListenUnixSocket` Ã  but de couverture.
- **PrÃ©requis `pwsh`** : le dÃ©ploiement cible Windows Server (prÃ©requis du `README.md`), oÃ¹ `powershell` 5.1 est toujours prÃ©sent et `ensureHelperRuntime` est sans effet. `pwsh` ne concerne que les hÃ´tes Linux (dÃ©veloppement, CI) ; ne pas l'ajouter aux prÃ©requis d'installation Windows.
- **Mojibake** : `CONTEXT.md`, `SECURITY.md` et les `.fs` contiennent du texte doublement encodÃ© (UTF-8 relu en Latin-1). Conserver la convention existante dans les Ã©ditions ciblÃ©es plutÃ´t que de tout rÃ©encoder.

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
