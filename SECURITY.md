# Sécurité — Diplo

## Authentification par token

Chaque microservice (Container, Volume, Network) utilise un token partagé pour l'authentification.

### Fonctionnement

1. **Génération** : L'installateur (`Diplo.Installer`) génère un token aléatoire de 32 octets (Base64) et l'écrit dans `C:\ProgramData\Diplo\auth-token.json`.
2. **Validation** : Les middleware HTTP et les intercepteurs gRPC vérifient que chaque requête contient l'en-tête `Authorization: Bearer <token>`.
3. **Comparaison** : La vérification utilise `CryptographicOperations.FixedTimeEquals` pour éviter les attaques par timing.

### Comportement sans fichier token

Si `auth-token.json` n'existe pas, l'authentification est **refusée** (fail-closed). Le middleware retourne un code HTTP 401 et aucune requête n'est autorisée. Cette stratégie garantit qu'une configuration incomplète ne compromet jamais la sécurité.

> **En développement local, créez un fichier token vide ou générez-le avec `AuthToken.createTokenFile()`.**

### Réponses d'erreur

Les réponses d'échec d'authentification (401) et de dépassement du rate limit (429) posent `Cache-Control: no-store`, afin qu'aucun mandataire ni navigateur ne mette en cache une réponse sensible. Le minuteur de purge du rate limiter (`purgeTimer`) est **disposé à l'arrêt du service** (`disposePurge()`), évitant de laisser un `System.Threading.Timer` racine actif indéfiniment.

## Rotation du token

Pour remplacer le token en cours :

1. Appeler `AuthToken.rotateToken()` en F# (génère et enregistre un nouveau token).
2. Ou manuellement : modifier le champ `Token` dans `C:\ProgramData\Diplo\auth-token.json`.
3. Redémarrer les services Container, Volume et Network.
4. Mettre à jour le token côté client (fichier `auth-token.json` du client).

> **Attention** : la rotation nécessite un redémarrage des services pour recharger le fichier.

## Côté client (gRPC)

Utiliser `TokenInterceptor.createTokenCredentials()` pour ajouter automatiquement l'en-tête Bearer :

```fsharp
open Grpc.Net.Client
open Diplo.Abstractions

let channel = GrpcChannel.ForAddress("http://localhost:5001")
let invoker = channel.Intercept(TokenInterceptor.createTokenCredentials())
```

Si le fichier token n'existe pas côté client, aucune en-tête n'est ajoutée.

## Transport gRPC (client)

- **Canal partagé** : `GrpcClientFactory` maintient un cache des canaux par adresse (`ConcurrentDictionary`), recréé/recyclé en cas de panne (`channelCache`), au lieu d'un nouveau `GrpcChannel` par appel — évite la multiplication des connexions TCP et des sockets.
- **Taille des messages** : `MaxReceiveMessageSize`/`MaxSendMessageSize` bornés à **64 Mo**, alignés avec le serveur (`ServerConfig.grpcMaxMessageSize`).
- **Timeout de connexion** : 5 s (`ConnectTimeout`), `UseProxy = false` — une adresse injoignable échoue rapidement (`RpcException` Unavailable) au lieu de suspendre l'appel.
- **Retry** : politique de reprise avec backoff exponentiel (5 tentatives).

## Validation et injection

Toutes les entrées utilisateur sont validées avant traitement :

- **Noms de conteneurs** : `SecurityValidation.validateName` — caractères alphanumériques, tirets et points uniquement.
- **Identifiants conteneur** : `SecurityValidation.validateContainerId` — alphanumériques, tirets et underscores uniquement.
- **Images** : `SecurityValidation.validateImage` — format `registry/repo:tag` validé, interdiction des caractères dangereux.
- **Labels** : `SecurityValidation.validateLabel` — clé et valeur validées individuellement.
- **Commandes** : `SecurityValidation.validateCommand` — chaque argument est validé, interdiction des caractères d'injection shell (`;`, `|`, `&&`, `$(`, backticks).
- **Paramètres PowerShell** : les noms de paramètres sont validés par allowlist `[a-zA-Z_][a-zA-Z0-9_]*` avant toute invocation (`runPowerShell`), rendant impossible l'injection de paramètres arbitraires.
- **Écriture de fichier dans un conteneur (WriteFile)** : le chemin est passé en **argument positionnel** (`sh -c 'base64 -d > "$1"' -- <chemin>`) et le contenu transite par **stdin** (base64) — aucune entrée utilisateur n'est interpolée dans la chaîne de commande.
- **YAML Compose** : le chargement est refusé au-delà de **10 Mo** (vérification avant lecture, pas de `ReadAllText` sans contrôle).
- **Noms de volume** : `SecurityValidation.validateVolumeName` — alphanumériques, tirets et underscores uniquement.
- **Chemins de montage** : `SecurityValidation.validateMountPath` — pas de `..`, pas de chemins absolus, séparateurs de chemin interdits.
- **Noms de réseau** : `SecurityValidation.validateNetworkName` — alphanumériques, tirets et underscores uniquement.
- **CIDR** : `SecurityValidation.validateCidr` — format IPv4/CIDR validé avec regex stricte.

### Interdiction d'injection

`SecurityValidation.containsShellInjection` bloque les caractères dangereux : `;`, `|`, `&&`, `$(`, `` ` ``, `$(`, `>`, `<`. Ceci empêche toute injection de commandes shell à travers les paramètres utilisateur.

### Confinement des chemins d'images disque

Lors de l'extraction/la réécriture d'une image disque (VDI, DMG, QCOW…), les chemins relatifs provenant de l'image sont résolus via `DiscFsHelper.realFrom` **confiné** au dossier racine monté : toute tentative de sortie (`..`, chemins absolus, séparateurs) est rejetée. Ce correctif anti-traversal empêche une image malveillante d'écrire en dehors de sa propre arborescence (VdiFs et DmgFs passent désormais par cette vérification).

Pour l'extraction **ISO** (lecture seule, parseur maison `IsoFs`), les noms de fichiers issus de l'image ISO9660/UDF sont assainis par `sanitizeName` (`IsoFs.fs`) : les séparateurs de chemin (`/`, `\`) et les caractères de contrôle sont remplacés par `_`, empêchant toute navigation de répertoire via un nom embarqué malveillant.

### Résolution des liens (fail-closed)

La validation des chemins services passe par `SecurityValidation`, et tout échec de résolution d'un lien symbolique (`ResolveLinkTarget`) remonte désormais une **erreur explicite** au lieu d'un repli silencieux : un chemin ambigu est rejeté plutôt qu'interprété.

### Extraction d'archives (installateur)

L'extraction des archives téléchargées (containerd, CNI) est faite entrée par entrée avec **validation anti zip-slip** : chaque chemin résultant est forcé à rester sous le dossier de destination (`Path.GetFullPath` + vérification de préfixe). Les téléchargements passent par un **HttpClient partagé** avec retry/backoff (pas de nouveau client par appel). Chaque artefact est vérifié contre son **SHA-256 embarqué** avant utilisation.

> **État actuel** : la vérification des artefacts repose sur le SHA-256 embarqué. La vérification par **signature** (cosign/GPG) n'est pas encore implémentée — elle est nécessaire pour protéger contre une compromission au build.

## Identifiants de registres (login/logout)

Les mots de passe de registres sont chiffrés au repos :

- **Windows** : chiffrement **DPAPI** (`ProtectedData`, portée `CurrentUser`).
- **Hors Windows** : chiffrement **AES-GCM** scellé par une clé par utilisateur (lue dans un fichier aux droits restreints, mode 0600) — jamais de repli base64 en clair.

Lors des opérations `pull`/`push` avec authentification, les identifiants sont résolus via **`hosts.toml`/credential helper** y compris en mode `--user` explicite : le mot de passe ne transite **jamais** par la ligne de commande (invisible dans la liste des processus).

## Limites de conteneurs

Les ressources des conteneurs (mémoire, CPU, PID) sont transmises à containerd via un spec OCI généré dynamiquement. Les limites sont appliquées au niveau du kernel Windows :

- **Mémoire** : `linux.resources.memory.limit` — limite en octets.
- **CPU** : `linux.resources.cpu.shares` — poids relatif de CPU.
- **PID** : `linux.resources.pids.limit` — nombre maximum de processus.

## Sécurité des logs

Les logs des conteneurs ne transmettent pas d'informations sensibles :

- Les variables d'environnement filtrées dans `InspectContainer` se limitent à un ensemble prédéfini (PATH, ASPNETCORE_ENVIRONMENT, etc.).
- Les secrets et tokens ne doivent jamais être passés via les variables d'environnement — utiliser Key Vault ou des fichiers secrets à la place.
- Les chemins de plugins CNI sont journalisés par **nom de fichier uniquement** (`Path.GetFileName`), jamais le chemin complet qui peut révéler la structure du système.
- Les tokens sont comparés en temps constant (`FixedTimeEquals`) ; les échecs d'authentification journalisent l'adresse IP de l'appelant.

## Bornes d'entrée et garde-fous (anti-DoS)

Toutes les entrées non bornées sont plafonnées côté serveur **et** côté client (double vérification) :

| Surface          | Limite                                                    | Référence                                     |
| ---------------- | --------------------------------------------------------- | --------------------------------------------- |
| Message gRPC     | 64 Mo (`MaxReceiveMessageSize`/`MaxSendMessageSize`)      | `GrpcClientFactory`, `ServerConfig`           |
| WriteFile        | 50 Mo par fichier (`MaxFileTransferBytes`)                | `ServiceGuards.requireFileTransferWithinLimit` |
| Listes (API)     | 10 000 éléments max (`MaxListItems`)                      | `ServiceGuards`                                |
| YAML Compose     | 10 Mo par fichier                                         | `ComposeOrchestrator`                          |
| Connexion gRPC   | ConnectTimeout 5 s, retry 5 tentatives backoff            | `GrpcClientFactory`                            |
| Qcow2 `l1_size`  | ≤ 2²⁴                                                     | `Qcow2.fs`                                     |
| Qcow2 `refcount_table_clusters` | ≤ 2²⁰                                          | `Qcow2.fs`                                     |

Les index L1/L2 des images disque (qcow2, qcow1, parallels) sont validés **avant** tout cast `int` : une image aux dimensions extrêmes est rejetée proprement (pas de dépassement entier, pas de crash). `findFreeCluster` refuse toute allocation dépassant le budget de refcounts (image saturée).

## Recommandations

- Ne jamais committer `auth-token.json` dans un dépôt git.
- Utiliser des permissions NTFS restrictives sur `C:\ProgramData\Diplo\`.
- Rotation du token régulière en production.
- Utiliser des images de conteneurs signées et provenant de registries fiables.
- La recherche et le pull d'images en ligne sont limités à une liste blanche de registres : docker.io, quay.io, mcr.microsoft.com, ghcr.io (`RegistrySearch.fs`).
- Limiter les ressources des conteneurs en production pour éviter les dénis de service.
- Ajouter la vérification par **signature** (cosign/GPG) des artefacts de l'installateur (remplace le SHA-256 seul, voir « Extraction d'archives »).
