# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## État du dépôt

Dépôt **greenfield** : à ce stade il ne contient que des fichiers de configuration
(`README.md`, `LICENSE`, `.gitignore`, `.gitattributes`) — **aucun code source, projet ou solution**.
Les sections ci-dessous décrivent les conventions et l'outillage déjà établis ; elles seront
enrichies (commandes de build/test/lint, architecture) au fur et à mesure que le code apparaît.

## Projet

**Diplo** — un système distribué (cf. `README.md`).

## Stack

- **.NET 10** (`dotnet 10.0.302` installé localement).
- Orientation **F#** : un serveur MCP F# est configuré pour la session (indexation, LSP, symboles).
- Le `.gitignore` est le template .NET standard (`bin/`, `obj/`, NuGet, `.env`, résultats de tests…).

## Conventions Git

- **Git LFS** : les binaires et médias sont stockés via LFS (voir `.gitattributes` — images, archives,
  DLL/EXE, fichiers Office, `.mdf`/`.ldf`/`.bak`). Un `git lfs install` est nécessaire au clone.
- Branche de développement : `dev` ; branche principale : `main`.
- **Commits** : ne jamais ajouter de trailer `Co-Authored-By` ni de mention de co-auteur ; l'auteur
  reste seul auteur. Pas de mention « Generated with Claude Code » dans les PR/issues sauf demande explicite.

## Langue

Le projet est francophone : README, commentaires et documentation en français (avec accents corrects).
