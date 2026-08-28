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

## Validation et injection

Toutes les entrées utilisateur sont validées avant traitement :

- **Noms de conteneurs** : `SecurityValidation.validateName` — caractères alphanumériques, tirets et points uniquement.
- **Identifiants conteneur** : `SecurityValidation.validateContainerId` — alphanumériques, tirets et underscores uniquement.
- **Images** : `SecurityValidation.validateImage` — format `registry/repo:tag` validé, interdiction des caractères dangereux.
- **Labels** : `SecurityValidation.validateLabel` — clé et valeur validées individuellement.
- **Commandes** : `SecurityValidation.validateCommand` — chaque argument est validé, interdiction des caractères d'injection shell (`;`, `|`, `&&`, `$(`, backticks).
- **Noms de volume** : `SecurityValidation.validateVolumeName` — alphanumériques, tirets et underscores uniquement.
- **Chemins de montage** : `SecurityValidation.validateMountPath` — pas de `..`, pas de chemins absolus, séparateurs de chemin interdits.
- **Noms de réseau** : `SecurityValidation.validateNetworkName` — alphanumériques, tirets et underscores uniquement.
- **CIDR** : `SecurityValidation.validateCidr` — format IPv4/CIDR validé avec regex stricte.

### Interdiction d'injection

`SecurityValidation.containsShellInjection` bloque les caractères dangereux : `;`, `|`, `&&`, `$(`, `` ` ``, `$(`, `>`, `<`. Ceci empêche toute injection de commandes shell à travers les paramètres utilisateur.

### Confinement des chemins d'images disque

Lors de l'extraction/la réécriture d'une image disque (VDI, DMG, QCOW…), les chemins relatifs provenant de l'image sont résolus via `DiscFsHelper.realFrom` **confiné** au dossier racine monté : toute tentative de sortie (`..`, chemins absolus, séparateurs) est rejetée. Ce correctif anti-traversal empêche une image malveillante d'écrire en dehors de sa propre arborescence (VdiFs et DmgFs passent désormais par cette vérification).

Pour l'extraction **ISO** (lecture seule, parseur maison `IsoFs`), les noms de fichiers issus de l'image ISO9660/UDF sont assainis par `sanitizeName` (`IsoFs.fs`) : les séparateurs de chemin (`/`, `\`) et les caractères de contrôle sont remplacés par `_`, empêchant toute navigation de répertoire via un nom embarqué malveillant.

## Limites de conteneurs

Les ressources des conteneurs (mémoire, CPU, PID) sont transmises à containerd via un spec OCI généré dynamiquement. Les limites sont appliquées au niveau du kernel Windows :

- **Mémoire** : `linux.resources.memory.limit` — limite en octets.
- **CPU** : `linux.resources.cpu.shares` — poids relatif de CPU.
- **PID** : `linux.resources.pids.limit` — nombre maximum de processus.

## Sécurité des logs

Les logs des conteneurs ne transmettent pas d'informations sensibles :

- Les variables d'environnement filtrées dans `InspectContainer` se limitent à un ensemble prédéfini (PATH, ASPNETCORE_ENVIRONMENT, etc.).
- Les secrets et tokens ne doivent jamais être passés via les variables d'environnement — utiliser Key Vault ou des fichiers secrets à la place.

## Recommandations

- Ne jamais committer `auth-token.json` dans un dépôt git.
- Utiliser des permissions NTFS restrictives sur `C:\ProgramData\Diplo\`.
- Rotation du token régulière en production.
- Utiliser des images de conteneurs signées et provenant de registries fiables.
- Limiter les ressources des conteneurs en production pour éviter les dénis de service.
