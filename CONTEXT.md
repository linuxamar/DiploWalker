# CONTEXT.md

Mémoire de travail de session (à réécrire à chaque session). Les règles stables vivent dans `AGENTS.md` et `CLAUDE.md` ; ici uniquement l'état présent et la connaissance non écrite ailleurs.

## Session en cours

- **Date :** 2026-09-26
- **Branche :** `dev`
- **Objectif :** réparer le doublement d'encodage qui rendait illisibles la documentation, les commentaires et une partie des libellés de tests. **Livré sur `dev`, non promu.**
- **Dernier commit :** `4914512` (restauration de l'encodage des libellés de tests).

## État Git

- `dev` est **6 commits devant `main`** : `6d5241d`, `1702448`, `73d4a2b`, puis les trois couches d'encodage `5e1afb8`, `2d3b424`, `4914512`. Le contenu divergent est de 166 fichiers, dont l'essentiel du diff est de l'encodage.
- Historique de promotion : `main` porte un commit de fusion par campagne, et `dev` est ensuite recalé dessus en fast-forward — d'où les nombreux commits de fusion accumulés sur `main`. Ne pas y lire du travail perdu : `git diff dev main` est l'indicateur fiable, pas `rev-list --count`.
- L'écart de contenu entre `main` et `dev` à la promotion venait de `Directory.Packages.props` : main épinglait `TextMateSharp.Grammars` 2.0.4 alors qu'aucun `PackageReference` ne le demande — `MainWindow.axaml.fs` l'utilise via `AvaloniaEdit.TextMate`, qui l'amène en transitif. Entrée retirée : le code compile et les 173 tests GUI passent sur la 2.0.3 transitive.

## Restauration de l'encodage (livré, non promu)

- **Deux variantes, pas une.** Le dépôt mêlait un doublement Latin-1 (`Ã©` → `é`) et un doublement cp1252 (`â€”` → `—`), parfois dans la même ligne. Le cp1252 est un sur-ensemble du Latin-1 — ils ne diffèrent que sur 0x80–0x9F — donc un décodeur cp1252 à repli Latin-1 couvre les deux. Un décodeur Latin-1 pur **dégrade** les emojis (`ðŸ“Š` → `📊`) et les caractères de dessin, sans marquer l'erreur.
- **Les octets indéfinis en cp1252** (0x81, 0x8D, 0x8F, 0x90, 0x9D) subsistent dans les fichiers comme **contrôles C1 invisibles**. Ils sont invisibles à la relecture et se concentraient dans les filets de séparation de `src/DiploWalker.Grpc/*.fs` (cent lignes). Les assimiler à leur propre valeur d'octet est ce qui permet de restaurer les filets `═` en un seul passage. Un contrôle de non-régression sur les caractères de contrôle C1 est indispensable : une regex anti-contrôle limited à `\x00-\x1f` les laisse passer.
- ** corruption multi-niveaux** : itérer jusqu'au point fixe est sûr, aucun passage supplémentaire ne dégrade une ligne propre. En revanche le garde-fou « ne jamais produire de C1 » doit être appliqué **après** convergence, pas au moment du décodage, sinon les séquences qui absorbent un C1 sont rejetées et la double corruption ne se résout jamais.
- **Trois lots, trois validations** : documentation (331 lignes, 4 fichiers), commentaires (2485 lignes, 154 fichiers, dont 100 lignes de contrôles C1 dans les messages gRPC), libellés de tests (139 lignes). Chaque lot est un commit autonome, annulable indépendamment.
- **Amorçage régressif du BOM** : un script qui décode en `utf-8` sans retirer le BOM puis le réécrit devant **cumule un second BOM** à chaque passage. Le symptôme est invisible dans un diff de contenu et ne se voit qu'en comparant les octets (`efbbbfefbbbf`). Décoder en `utf-8-sig` et vérifier la présence du BOM après écriture.
- **Deux cas hors traitement automatique.** `MessageSerializationTests.fs` portait `prï¿½serve` : le `é` perdu puis corrompu, que le décodeur remplaçait par U+FFFD — libellé réécrit à la main. `FsImageTests.fs` vérifie la conservation de noms de fichiers CJK ; ses littéraux sont des données de test volontaires et le test a été **laissé strictement intact**, y compris une ligne de relecture qu'un premier passage avait corrigée sans son pendant d'écriture, ce qui cassait la cohérence write/read du test.
- **Validation** : build Release à 0 erreur / 0 avertissement, suite complète à 1 626 tests sans échec ni ignoré. Aucun `U+FFFD` ni marqueur résiduel hors des exclusions assumées, `git diff --check` propre, `~/.local/share/Diplo` toujours absent donc l'isolation des tests intacte.

## Portabilité Linux (livré et promu)

- **Signature forte** : le groupe strong-name de `Directory.Build.props` est conditionné à `Windows_NT`. La signature F# impose RSA-SHA1, refusé par les OpenSSL 3.5 des distributions Linux (« invalid digest »), et `PublicSign` échoue avec la clé complète (« StrongNameSignatureSize failed »).
- **Découverte des tests** : `pipeline.ps1` exclut les tests `[<Trait("Platform", "Windows")>]` avec `--filter-not-trait` hors Windows, exécute toute la suite sous Windows.
- **`AppPaths`** (nouveau) : racine des données unifiée et surchargeable par `DIPLO_DATA_ROOT` — `%ProgramData%\Diplo` sous Windows, `$XDG_DATA_HOME/Diplo` ailleurs. `SpecialFolder.CommonApplicationData` valait `/usr/share` sous Linux, non inscriptible sans root. `AppPaths.userRoot` porte la clé AES-GCM des secrets de registres. Remplace les chemins codés en dur dans `AuthToken`, `RegistryAuth`, `ContainerLogs`, `DiskMounter` et `MountState`.
- **Isolation des tests** : fixture d'assembly xUnit (`tests/TestDataRoot.fs`, injectée par `tests/Directory.Build.targets`) qui redirige la racine des données vers `/tmp/diplo-test-data`, pour que la suite n'écrive plus dans `%ProgramData%` ni `~/.local/share`. `F# ModuleInitializer` n'est pas supporté, d'où la fixture.
- **TestDataRoot** : vérifier l'absence de `~/.local/share/Diplo` après une passe complète.
- **Quatre-vingt-quinze** tests taggés au départ, dont 71 qui ne dépendaient d'aucune API Windows ; il n'en reste qu'**un** (named pipe de volume).
- **Helper de credentials** : script PowerShell unique, DPAPI sous Windows et AES-GCM ailleurs (format `base64(nonce | cipher | tag)` de `RegistryAuth.protect`), branche choisie via `[Environment]::OSVersion.Platform` car `$IsWindows` n'existe pas en PowerShell 5.1. Chemins de l'état et de la clé passés par `DIPLO_REGISTRY_AUTH_STATE` / `DIPLO_REGISTRY_KEY_FILE`, sinon déduits de `$PSScriptRoot/..`. `writeHelperTo` installe un lanceur de plateforme (`diplo-cred-helper.cmd` ou `diplo-cred-helper` en `sh`, mode 0755) que containerd exécute tel quel depuis `hosts.toml`.
- **`ensureHelperRuntime`** : refuse l'extraction authentifiée si `pwsh` est absent du `PATH` (`FailedPrecondition`), sinon containerd ne remontait qu'un refus d'authentification opaque. Recherche par `tryFindOnPath`, sans lancer de processus, et sans mapper une entrée vide du PATH sur « . » (le shell POSIX y verrait le répertoire courant).

## Reste à faire

- **Promouvoir les 6 commits de `dev` sur `main`** quand la campagne est jugée terminée. Le diff est majoritairement de l'encodage, ce qui le rend peu risqué à relire, mais `main` porte encore l'épinglage `TextMateSharp.Grammars` et la réécriture de `CONTEXT.md` n'y est pas.
- **Les littéraux CJK de `FsImageTests.fs` restent en mojibake** par décision. Le décodeur sait désormais les restaurer correctement (`ãƒ†ã‚¹ãƒˆ` → `テスト`, `æµ‹è¯•` → `测试`, `í•œêµ­ì–´` → `한국어`) ; les corriger rendrait le test plus fidèle à son intention, à condition de modifier ensemble les lignes d'écriture et de relecture. Ne pas le faire à moitié.

## Décisions actées

- **Le test named pipe reste taggué Windows, délibérément.** `VolumeIntegrationTests.CreateVolume via named pipe fonctionne de bout en bout` est le seul test d'end-to-end qui ne passe pas par TCP : il exerce `ListenNamedPipe` côté serveur et `NamedPipeClientStream` via `SocketsHttpHandler.ConnectCallback` côté client, donc toute la chaîne du transport alternatif. Ce n'est pas du code mort : le CLI l'expose (`config init --transport pipe`, valeurs `tcp` et `pipe` en `ConfigCommands.fs`) et les `appsettings.json` activent `UseTcp` et `UseNamedPipes` ensemble. Le tag est un marqueur de couverture, pas une dette. Le porter sur socket de domaine Unix exigerait d'ajouter un véritable transport au produit (`UseUnixSocket` dans `ServerConfig.configureKestrel`, branche `unix://` dans `GrpcClientFactory`, choix de transport dans le CLI) alors que le déploiement cible Windows Server : ce serait une décision produit, pas un chantier de test. Les 9 autres tests e2e du fichier passent par TCP et couvrent le service ; ne pas proposer d'éliminer ce tag sans que `--transport pipe` reste supporté.

- **`pwsh` n'entre pas dans les prérequis d'installation.** Le déploiement cible Windows Server, où `powershell` 5.1 est toujours présent ; `pwsh` ne concerne que les hôtes Linux (développement, CI) et `ensureHelperRuntime` y est le filet de sécurité.

- **Le nom de test `préserve les collections renseignées` est réécrit à la main, pas décodé.** C'est le seul caractère définitivement perdu du dépôt ; l'automatisation l'aurait transformé en U+FFFD.

## Connaissance tribale

- F# : pas de `try/with/finally` combiné (imbrication), pas de `do!` en `finally`, protocoles CTS (Cancel sous verrou + null, Dispose par le worker), debounce UI 250 ms (DispatcherTimer).
- `TreatWarningsAsErrors=true` — 0 avertissement attendu.
- Tests : 1 626 sous Linux, aucun ignoré, un test `Platform=Windows` exclu (named pipe). Lancer `dotnet test` sans le filtre fait échouer ce test avec `PlatformNotSupportedException`, ce qui ressemble à tort à une régression.
- Validation : `./pipeline.ps1 -DoTests` (~2 min 30 sur ce poste).
- `dotnet test --project <fsproj> ... --filter-not-trait "Platform=Windows"` : syntaxe MTP, et surtout pas de `--nologo`.
- Docs, comments et commits en français, sans trailer.
- Sécurité : détails dans `SECURITY.md` (bornes gRPC 64 Mo, WriteFile 50 Mo, no-store 401/429, DPAPI/AES-GCM pour les identifiants de registre).
- **Architecture flaky** : `waitPump` (HeadlessBehaviorTests) = polling 10 ms sur Avalonia headless ; `waitUntil` (CatalogViewModelTests) = polling 20 ms sans headless ; `UiThread.Post` exécute inline si `Application.Current = null` (pas de dispatcher).
- **C3 — intégrité des artefacts** (livré, `7b7a2a5`) : checksums dans `assets/artifacts.manifest`, signés RSA-4096/SHA-384 par une clé privée hors bande (`~/.diplo/diplo-release.key`, chiffrée AES-256-CBC, jamais dans le dépôt), vérifiés fail-closed au démarrage. Signature déterministe : re-signer le même manifeste régénère le même `.sig`, un diff signale donc un manifeste ou une clé différents. Empreinte sha256 de la clé publique courante : `02:7F:8A:F3:95:5F:FD:9C:A6:D1:28:DC:FC:EE:9E:85:7F:7E:7A:5F:27:C8:F2:B5:C0:44:81:AE:40:D6:7F:81`.
