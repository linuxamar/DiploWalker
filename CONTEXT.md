# CONTEXT.md

Mémoire de travail de session (à réécrire à chaque session). Les règles stables vivent dans `AGENTS.md` et `CLAUDE.md` ; ici uniquement l'état présent et la connaissance non écrite ailleurs.

## Session en cours

- **Date :** 2026-09-11
- **Branche :** `dev`
- **Objectif :** ticket C3 — intégrité des artefacts installateur : manifeste de checksums signé RSA-4096/SHA-384, clé privée hors bande, vérification fail-closed embarquée.
- **Dernier commit avant C3 :** `7815e1b` (mise à jour du contexte de session après corrections flaky).

## État Git

- `dev` → `main` : 12 commits à promouvoir (merge direct, pas de PR).
- C3 non commité : 6 fichiers modifiés + 4 nouveaux.

## Corrections livrées (commit 7b7a2a5)

- `pipeline.ps1` : converti en UTF-8 sans BOM + bloc `[Console]::OutputEncoding = UTF-8` après `param()`.
- `OptionalBehaviorTests.fs` : `waitUntil` timeout 5 s → 30 s (test disque réel « CreateImage raw »).
- `HeadlessBehaviorTests.fs` : test `AboutCommand` attend sur `LogOutput` (via `UiThread.Post`) au lieu de `LogLines.Count` (synchrone) ; `waitPump` timeout 3 s → 10 s.
- `CatalogViewModelTests.fs` : attente `vm.Catalogue.Count = 1` avant `Directory.Delete` (race `RefreshCatalogue` relit le fichier via `UiThread.Post` post-warning) ; `waitUntil` timeout 2 s → 10 s.

## Reste à faire

- **Promotion `dev` → `main`** : 12 commits à merger directement.

## C3 — intégrité des artefacts (nouveau, commit en cours)

- Checksums centralisés dans `src/Diplo.Installer/assets/artifacts.manifest`, signé **RSA-4096/SHA-384** (PKCS#1 v1.5) par une clé privée **hors bande** (`~/.diplo/diplo-release.key`, passphrase provisoire `diplo-temp` à migrer en coffre — jamais dans le dépôt).
- Trois ressources embarquées dans l'assembly (LogicalNames `Diplo.Installer.assets.*`) : manifeste, signature (**base64**, décodée via `Convert.FromBase64String`), clé publique `diplo-release.pub`. Lecture via `typeof<AssemblyAnchor>.Assembly` (pas `GetExecutingAssembly`, fiable depuis les tests).
- Vérification **fail-closed** dans `ArtifactSigning.loadVerifiedManifest` (levée dès chargement), appelée une fois au démarrage dans `Core.fs` (`artifactChecksums` remplace les 3 constantes codées en dur). `verifyChecksum` accepte `string option`, `None` → échec.
- `.gitattributes` : les 3 assets signés sont forcés en `text eol=lf` (autocrlf=true casserait la signature). Manifeste vérifié LF-only (8 LF, 0 CRLF), `.sig` sur une seule ligne (684 octets).
- `tools/sign-artifacts.ps1` : re-signature openssl + vérification croisée + écriture base64, la clé publique de contrôle dérive de `-KeyPath`.
- Tests : `ArtifactSigningTests.fs` (10 tests : parsing, signature valide/altérée/autre clé, checksums dorés, lookup inconnu). Pipeline complet vert (`./pipeline.ps1 -DoTests`, ~6 min sur ce poste).
- Renouvellement/manip clé : `openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:4096` ; empreinte sha256 du `.pub` actuel `02:7F:8A:F3:95:5F:FD:9C:A6:D1:28:DC:FC:EE:9E:85:7F:7E:7A:5F:27:C8:F2:B5:C0:44:81:AE:40:D6:7F:81`.

## Connaissance tribale

- F# : pas de `try/with/finally` combiné (imbrication), pas de `do!` en `finally`, protocole CTS (Cancel sous verrou + null, Dispose par le worker), debounce UI 250 ms (DispatcherTimer).
- `TreatWarningsAsErrors=true` — 0 avertissement attendu.
- Tests : 1 612, dont 1 ignoré (symlink NTFS attendu).
- Validation : `./pipeline.ps1 -DoTests` (~3 min).
- Docs, comments et commits en français, sans trailer.
- Sécurité : détails dans `SECURITY.md` (bornes gRPC 64 Mo, WriteFile 50 Mo, no-store 401/429, DPAPI/AES-GCM pour les identifiants de registre).
- **Architecture flaky** : `waitPump` (HeadlessBehaviorTests) = polling 10 ms sur Avalonia headless ; `waitUntil` (CatalogViewModelTests) = polling 20 ms sans headless ; `UiThread.Post` exécute inline si `Application.Current = null` (pas de dispatcher).
