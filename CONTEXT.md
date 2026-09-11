# CONTEXT.md

Mémoire de travail de session (à réécrire à chaque session). Les règles stables vivent dans `AGENTS.md` et `CLAUDE.md` ; ici uniquement l'état présent et la connaissance non écrite ailleurs.

## Session en cours

- **Date :** 2026-09-10
- **Branche :** `dev` (synchronisée avec `origin/dev`, arbre propre)
- **Objectif :** livraison historique — lots 1-4 livrés, tickets GitHub #1-#4 fermés.
- **Dernier commit :** `7b7a2a5` (correction tests flaky + encodage UTF-8 pipeline).

## État Git

- `dev` → `main` : 12 commits à promouvoir (merge direct, pas de PR).
- Dernier commit poussé : `7b7a2a5` (corrections flaky + pipeline).

## Corrections livrées (commit 7b7a2a5)

- `pipeline.ps1` : converti en UTF-8 sans BOM + bloc `[Console]::OutputEncoding = UTF-8` après `param()`.
- `OptionalBehaviorTests.fs` : `waitUntil` timeout 5 s → 30 s (test disque réel « CreateImage raw »).
- `HeadlessBehaviorTests.fs` : test `AboutCommand` attend sur `LogOutput` (via `UiThread.Post`) au lieu de `LogLines.Count` (synchrone) ; `waitPump` timeout 3 s → 10 s.
- `CatalogViewModelTests.fs` : attente `vm.Catalogue.Count = 1` avant `Directory.Delete` (race `RefreshCatalogue` relit le fichier via `UiThread.Post` post-warning) ; `waitUntil` timeout 2 s → 10 s.

## Reste à faire

- **C3 (ticket #1) :** signature cosign/GPG des artefacts installateur non implémentée (SHA-256 seul) — candidat ticket de suivi.
- **Promotion `dev` → `main`** : 12 commits à merger directement.

## Connaissance tribale

- F# : pas de `try/with/finally` combiné (imbrication), pas de `do!` en `finally`, protocole CTS (Cancel sous verrou + null, Dispose par le worker), debounce UI 250 ms (DispatcherTimer).
- `TreatWarningsAsErrors=true` — 0 avertissement attendu.
- Tests : 1 612, dont 1 ignoré (symlink NTFS attendu).
- Validation : `./pipeline.ps1 -DoTests` (~3 min).
- Docs, comments et commits en français, sans trailer.
- Sécurité : détails dans `SECURITY.md` (bornes gRPC 64 Mo, WriteFile 50 Mo, no-store 401/429, DPAPI/AES-GCM pour les identifiants de registre).
- **Architecture flaky** : `waitPump` (HeadlessBehaviorTests) = polling 10 ms sur Avalonia headless ; `waitUntil` (CatalogViewModelTests) = polling 20 ms sans headless ; `UiThread.Post` exécute inline si `Application.Current = null` (pas de dispatcher).
