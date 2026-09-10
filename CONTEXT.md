# CONTEXT.md

Mémoire de travail de session (à réécrire à chaque session). Les règles stables vivent dans `AGENTS.md` et `CLAUDE.md` ; ici uniquement l'état présent et la connaissance non écrite ailleurs.

## Session en cours

- **Date :** 2026-09-10
- **Branche :** `dev` (synchronisée avec `origin/dev`, arbre propre)
- **Objectif :** livraison historique — lots 1-4 livrés, tickets GitHub #1-#4 fermés.

## État Git

- `dev` → `main` : 11 commits à promouvoir (merge direct, pas de PR).
- Dernier commit poussé : `baa7f41` (docs de contexte + posture de sécurité).

## Reste à faire

- **C3 (ticket #1) :** signature cosign/GPG des artefacts installateur non implémentée (SHA-256 seul) — candidat ticket de suivi.
- **Flaky connu :** `OptionalBehaviorTests.fs:~156` « CreateImage raw » (timeout `waitUntil` ~5 s sous charge disque) — passe isolément et au re-run.

## Connaissance tribale

- F# : pas de `try/with/finally` combiné (imbrication), pas de `do!` en `finally`, protocole CTS (Cancel sous verrou + null, Dispose par le worker), debounce UI 250 ms (DispatcherTimer).
- `TreatWarningsAsErrors=true` — 0 avertissement attendu.
- Tests : 1 612, dont 1 ignoré (symlink NTFS attendu).
- Validation : `./pipeline.ps1 -DoTests` (~3 min).
- Docs, comments et commits en français, sans trailer.
- Securité : détails dans `SECURITY.md` (bornes gRPC 64 Mo, WriteFile 50 Mo, no-store 401/429, DPAPI/AES-GCM pour les identifiants de registre).