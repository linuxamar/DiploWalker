# Revue de code — haut niveau

Périmètre : robustesse et correction du code source (`src/`, 105 fichiers F#, 16 774 lignes), sans modification du contrat gRPC.
Chaque point vérifié est listé ci-dessous par criticité, avec son statut. Les correctifs ont été appliqués et commités séparément sur `dev`.

## Conclusion

La base est saine : le code est structuré, commenté en français et correctement couvert par les tests (195 tests OK). Aucune faille de sécurité évidente exploitable. Les points relevés concernent des risques de robustesse et de performance sous contrainte (journaux géants, fichiers image volumineux, client peu coopératif), pas des bugs avérés en fonctionnement nominal.

## Criticité élevée

### Confinement du chemin d'extraction des systèmes de fichiers (HawkyntFs) — corrigé
`extractBtrfs`, `extractXfs` et `extractHfsPlus` construisaient le chemin de sortie par `TrimStart` + `Path.Combine` à partir du nom d'entrée. Un nom d'entrée malveillant (`..\..\x`) pouvait sortir du répertoire de staging et écrire en dehors.
- **Correctif** : les trois extracteurs utilisent désormais `DiscFsHelper.realFrom targetDir` (helper testé — voir `DiscFsHelperTests.fs`), qui rejette `..`, les chemins enracinés et les lettres de lecteur.
- **Commit** : `372fbd7` (« correctif: confiner l'extraction HawkyntFs sous le répertoire de staging »).

### Purge du rate limiter en dehors du verrou (TokenAuthMiddleware) — corrigé
`PurgeExpired` vérifiait `Count = 0` et exécutait `TryRemove` hors du `lock` ; deux appels simultanés pouvaient muter le dictionnaire pendant l'énumération.
- **Correctif** : le test d'énumération et la suppression sont exécutés sous le même verrou.
- **Commit** : `9150f6d` (« correctif: purger le rate limiter sous verrou (TOCTOU) »).

### Lecture complète des journaux non bornée (ContainerLogs.read) — corrigé
La fonction `read` faisait un `ReadToEnd` par appelant (un seul, `ContainerdClient.GetContainerLogs`). Un journal géant pouvait charger des gigaoctets en RAM d'un coup.
- **Correctif** : `read` est supprimé et réintroduit en délégant à `readUpToCore` (fenêtre bornée à 1 Go, tri par `tail`/`since`).
- **Commit** : `20d199e` (« correctif: borner la lecture complète des journaux de conteneur »).

### Tâches productrices jamais observées (ContainerServiceImpl) — corrigé
`observeProducerTask` attache une continuation `OnlyOnFaulted` sur la tâche fournie, mais les quatre sites d'appel passaient `Task.Run(() -> run () |> ignore)` — qui encapsule la *fausse* tâche (le lambda retourne `unit`), pas la tâche productrice. Une exception du producteur devenait `UnobservedTaskException` sans journal.
- **Correctif** : les quatre sites (`stats-stream`, `events-stream`, `exec-stream`, `export-stream`) passent désormais `run ()` directement (la vraie tâche).
- **Commit** : `556f116` (« correctif: canal de flux borné, journalisation des états et nettoyage borné des flux »).

### Canal de flux non borné (ChannelStream) — corrigé
`ChannelStream` utilisait un `Channel.CreateUnbounded<byte[]>`. Un producteur plus rapide que le consommateur pouvait faire croître la mémoire sans limite.
- **Correctif** : canal borné à 32 éléments (`BoundedChannelOptions(32, ...)`), `FullMode.Wait` par défaut → backpressure.
- **Commit** : `556f116`.

## Criticité moyenne

### Attentes non bornées sur les pompes d'exécution (ContainerdClient.StartExec) — corrigé
`Task.WaitAll(pumpOut, pumpErr)` puis `pumpIn.Wait()` sans délai. Un client qui ne ferme jamais le stdin suspendait indéfiniment le serveur.
- **Correctif** : fermeture de `StandardInput` sous `try`, puis `Task.WaitAll(..., 30_000)` sous `try`.
- **Commit** : `0fb0bfc` (« correctif: observer le producteur de journaux en flux et borner l'attente des pompes d'exécution »).

### Producteur de journaux en flux ignoré au fallback (ContainerdClient) — corrigé
Dans `GetContainerLogsStream`, le producteur `Async.StartAsTask` était passé à `ignore` : ses exceptions étaient perdues. Au `DisposeAsync`, seul le canal était complété, le producteur n'était ni arrêté ni observé.
- **Correctif** : arrêt par `cts.Cancel()`, complétion du canal, continuation `OnlyOnFaulted` pour observer les exceptions.
- **Commit** : `0fb0bfc`.

### Lectures post-`Kill` non bornées (ProcessExec) — corrigé
Après un `Kill`, les deux lectures stdout/stderr attendaient chacune leur fin sans délai.
- **Correctif** : `Task.WaitAll([| stdoutRead; stderrRead |], 5_000)` sous `try`.
- **Commit** : `3dd49c9` (« correctif: borner la lecture des flux après l'arrêt du processus »).

### Erreurs rapportées sans type ni cause (CommandHelpers) — corrigé
`Cmd.run`/`runSync`/`runSyncWith` rapportaient uniquement `ex.Message`, masquant la classe d'exception et la cause interne (souvent plus parlante sur un échec de flux ou de registre).
- **Correctif** : helper `describe` → `Type : message (cause : Type : message)`.
- **Commit** : `b30f4a8` (« correctif: enrichir les erreurs rapportées par les commandes (type et cause) »).

### Catches muets masquant la cause (ContainerServiceImpl) — corrigé
`catch _ -> exitCode` (code de sortie) et `catch _ -> ContainerState.Unknown` (état) avalaient l'exception sans la journaliser.
- **Correctif** : `Log.Warning` / `Log.Debug` avec l'exception et le context.
- **Commit** : `556f116`.

### Disposition synchrone synchrone-bloquante des énumérateurs de flux (ContainerServiceImpl) — corrigé
`enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult()` bloquait le thread sans bornes (et rendait le code illisible). Le `task {}` ne permettant pas de `do!` dans un `finally`, le nettoyage est désormais borné par `.Wait(10_000)` sous `try`.
- **Commit** : `556f116`.

## Criticité faible — observations traitées et vérifiées

### `CachedConfig` chargé sous verrou — corrigé
Le loader était exécuté sous le verrou : les lecteurs concurrents étaient bloqués pendant le chargement, et un loader appelant `Value` sur la même instance conduisait à une récursion infinie.
- **Correctif** : le chargement paresseux est confié à `Lazy<'T>` (`ExecutionAndPublication`). Le verrou n'est tenu que pour échanger l'instance, jamais pendant le chargement ; le loader est exécuté une seule fois sous concurrence, et un chargement réentrant lève une `InvalidOperationException` (testé).
- **Commit** : `9c38d06`.

### Sync-over-async du `ChannelStream` — vérifié, aucun correctif nécessaire
`ChannelStream` fournit déjà des surcharges `ReadAsync`/`WriteAsync` correctement asynchrones (canal borné en 32 segments, backpressure). Le `Read`/`Write` synchrone bloquant est le contrat obligatoire de `Stream` et n'est appelé par aucun consommateur : tous les appelants vérifiés passent par les surcharges async.

### `reader.Extract` en RAM par fichier (HawkyntFs) — vérifié, borné
L'API Hawkynt n'expose que `byte[]` pour l'extraction (pas de variante par flux). La mémoire est indirectement bornée : l'image est limitée à `maxInMemoryBytes` (2 Go) avant toute extraction, donc la RAM au pic reste ≤ image (2 Go) + plus gros fichier extrait, lui-même borné par la taille de l'image. Aucun chemin non borné.

### Écriture HawkyntFs vers l'image, pas le disque hôte — vérifié, comportement conforme
`tryWriteBack` écrit dans l'image (via un fichier temporaire à côté de l'image, puis `File.Replace`) — c'est la fonction attendue. Aucune écriture ne cible le système de fichiers hôte en dehors de ce chemin ; `relPath` est toujours dérivé d'une énumération de `sourceDir`, donc confiné.

### Test de liens symboliques NTFS ignoré — inchangé
Le test `create gere les liens symboliques NTFS` requiert un privilège système (mode développeur) non disponible sur la machine. Non couvert, hors périmètre.

## Vérifications finales

- `dotnet build Diplo.slnx -c Release` : **0 avertissement, 0 erreur**.
- `pipeline.ps1 -DoTests` : **194 réussies, 1 ignorée (liens symboliques), 0 échec** ; `Diplo.Abstractions.Tests` : **253/253** après le correctif `CachedConfig`.
- Branche `dev`, 8 commits de correctifs, chacun sur un sujet distinct, message en français.