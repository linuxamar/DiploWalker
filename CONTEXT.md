# CONTEXT.md

MÃ©moire de travail de session (Ã  rÃ©Ã©crire Ã  chaque session). Les rÃ¨gles stables vivent dans `AGENTS.md` et `CLAUDE.md` ; ici uniquement l'Ã©tat prÃ©sent et la connaissance non Ã©crite ailleurs.

## Session en cours

- **Date :** 2026-09-11
- **Branche :** `dev`
- **Objectif :** ticket C3 â€” intÃ©gritÃ© des artefacts installateur : manifeste de checksums signÃ© RSA-4096/SHA-384, clÃ© privÃ©e hors bande, vÃ©rification fail-closed embarquÃ©e.
- **Dernier commit avant C3 :** `7815e1b` (mise Ã  jour du contexte de session aprÃ¨s corrections flaky).

## Ã‰tat Git

- `dev` â†’ `main` : 12 commits Ã  promouvoir (merge direct, pas de PR).
- C3 non commitÃ© : 6 fichiers modifiÃ©s + 4 nouveaux.

## Corrections livrÃ©es (commit 7b7a2a5)

- `pipeline.ps1` : converti en UTF-8 sans BOM + bloc `[Console]::OutputEncoding = UTF-8` aprÃ¨s `param()`.
- `OptionalBehaviorTests.fs` : `waitUntil` timeout 5 s â†’ 30 s (test disque rÃ©el Â« CreateImage raw Â»).
- `HeadlessBehaviorTests.fs` : test `AboutCommand` attend sur `LogOutput` (via `UiThread.Post`) au lieu de `LogLines.Count` (synchrone) ; `waitPump` timeout 3 s â†’ 10 s.
- `CatalogViewModelTests.fs` : attente `vm.Catalogue.Count = 1` avant `Directory.Delete` (race `RefreshCatalogue` relit le fichier via `UiThread.Post` post-warning) ; `waitUntil` timeout 2 s â†’ 10 s.

## Reste Ã  faire

- **Promotion `dev` â†’ `main`** : 12 commits Ã  merger directement.

## C3 â€” intÃ©gritÃ© des artefacts (nouveau, commit en cours)

- Checksums centralisÃ©s dans `src/DiploWalker.Installer/assets/artifacts.manifest`, signÃ© **RSA-4096/SHA-384** (PKCS#1 v1.5) par une clÃ© privÃ©e **hors bande** (`~/.diplo/diplo-release.key`, chiffrÃ©e **AES-256-CBC** â€” PKCS#8 `ENCRYPTED PRIVATE KEY` â€” jamais dans le dÃ©pÃ´t). La passphrase est transmise Ã  l'outil via `-Passphrase` ou `$env:DIPLO_KEY_PASSPHRASE`.
- Trois ressources embarquÃ©es dans l'assembly (LogicalNames `DiploWalker.Installer.assets.*`) : manifeste, signature (**base64**, dÃ©codÃ©e via `Convert.FromBase64String`), clÃ© publique `diplo-release.pub`. Lecture via `typeof<AssemblyAnchor>.Assembly` (pas `GetExecutingAssembly`, fiable depuis les tests).
- VÃ©rification **fail-closed** dans `ArtifactSigning.loadVerifiedManifest` (levÃ©e dÃ¨s chargement), appelÃ©e une fois au dÃ©marrage dans `Core.fs` (`artifactChecksums` remplace les 3 constantes codÃ©es en dur). `verifyChecksum` accepte `string option`, `None` â†’ Ã©chec.
- `.gitattributes` : les 3 assets signÃ©s sont forcÃ©s en `text eol=lf` (autocrlf=true casserait la signature). Manifeste vÃ©rifiÃ© LF-only (8 LF, 0 CRLF), `.sig` sur une seule ligne (684 octets).
- `tools/sign-artifacts.ps1` : re-signature openssl + vÃ©rification croisÃ©e + Ã©criture base64, la clÃ© publique de contrÃ´le dÃ©rive de `-KeyPath` (ou `.pub` dans le dÃ©pÃ´t), et la passphrase est lue en prioritÃ© depuis `-Passphrase`, sinon `$env:DIPLO_KEY_PASSPHRASE`, sinon pas de passphrase (clÃ© non chiffrÃ©e). La signature est **dÃ©terministe** (PKCS#1 v1.5 / SHA-384) : re-signer le mÃªme manifeste avec la mÃªme clÃ© rÃ©gÃ©nÃ¨re exactement le mÃªme `.sig` â€” un diff aprÃ¨s re-signature signale une clÃ© ou un manifeste diffÃ©rent.
- Tests : `ArtifactSigningTests.fs` (10 tests : parsing, signature valide/altÃ©rÃ©e/autre clÃ©, checksums dorÃ©s, lookup inconnu). Pipeline complet vert (`./pipeline.ps1 -DoTests`, ~6 min sur ce poste).
- Renouvellement/manip clÃ© : `openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:4096` ; empreinte sha256 du `.pub` actuel `02:7F:8A:F3:95:5F:FD:9C:A6:D1:28:DC:FC:EE:9E:85:7F:7E:7A:5F:27:C8:F2:B5:C0:44:81:AE:40:D6:7F:81`.

## Connaissance tribale

- F# : pas de `try/with/finally` combinÃ© (imbrication), pas de `do!` en `finally`, protocole CTS (Cancel sous verrou + null, Dispose par le worker), debounce UI 250 ms (DispatcherTimer).
- `TreatWarningsAsErrors=true` â€” 0 avertissement attendu.
- Tests : 1 612, dont 1 ignorÃ© (symlink NTFS attendu).
- Validation : `./pipeline.ps1 -DoTests` (~3 min).
- Docs, comments et commits en franÃ§ais, sans trailer.
- SÃ©curitÃ© : dÃ©tails dans `SECURITY.md` (bornes gRPC 64 Mo, WriteFile 50 Mo, no-store 401/429, DPAPI/AES-GCM pour les identifiants de registre).
- **Architecture flaky** : `waitPump` (HeadlessBehaviorTests) = polling 10 ms sur Avalonia headless ; `waitUntil` (CatalogViewModelTests) = polling 20 ms sans headless ; `UiThread.Post` exÃ©cute inline si `Application.Current = null` (pas de dispatcher).

