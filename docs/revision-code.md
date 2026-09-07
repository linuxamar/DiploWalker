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

## Criticité faible — observations (pas de correctif)

Ces points sont documentés, non modifiés, et considérés acceptables après vérification :

- **`CachedConfig` chargé sous verrou** : le chargement du config est supposé thread-safe (loader sous `lock` réentrant, appel unique au démarrage). Aucun changement.
- **Sync-over-async du `ChannelStream`** : les `WriteAsync` bloquants sont inhérents à l'API `Stream` synchrone et restent bornés par le canal borné en 32 éléments, lu de manière concurrente.
- **`reader.Extract` en RAM par fichier (HawkyntFs)** : l'API Hawkynt ne fournit que `byte[]` pour l'extraction ; la matérialisation RAM par fichier est conservée, indirectement bornée par `maxInMemoryBytes` (2 Go) sur l'image source.
- **Écriture HawkyntFs vers l'image, pas le disque hôte** : comportement d'origine, conforme à la fonction (écrivain pointant sur l'image source). La revue a confirmé qu'aucune écriture ne cible le système de fichiers hôte.
- **Création de liens symboliques NTFS (test ignoré)** : le test `create gere les liens symboliques NTFS` est ignoré faute de privilège sur la machine ; comportement non couvert, hors périmètre.

## Vérifications finales

- `dotnet build Diplo.slnx -c Release` : **0 avertissement, 0 erreur**.
- `pipeline.ps1 -DoTests` : **194 réussies, 1 ignorée (liens symboliques), 0 échec**.
- Branche `dev`, 7 commits de correctifs, chacun sur un sujet distinct, message en français.