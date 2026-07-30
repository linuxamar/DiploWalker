---
description: Lance une revue de code F# / C# selon les checklists du projet.
---

Utilise l'outil `skill` pour charger le skill "code-review".

Si l'utilisateur a fourni des chemins ou une requête, applique les checklists à ce code. Sinon, examine le diff non commité (git diff) et les fichiers modifiés.

Produis un rapport structuré : problème, sévérité, fichier:ligne, recommendation.
