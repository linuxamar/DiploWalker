# SÃ©curitÃ© â€” Diplo

## Authentification par token

Chaque microservice (Container, Volume, Network) utilise un token partagÃ© pour l'authentification.

### Fonctionnement

1. **GÃ©nÃ©ration** : L'installateur (`DiploWalker.Installer`) gÃ©nÃ¨re un token alÃ©atoire de 32 octets (Base64) et l'Ã©crit dans `C:\ProgramData\Diplo\auth-token.json`.
2. **Validation** : Les middleware HTTP et les intercepteurs gRPC vÃ©rifient que chaque requÃªte contient l'en-tÃªte `Authorization: Bearer <token>`.
3. **Comparaison** : La vÃ©rification utilise `CryptographicOperations.FixedTimeEquals` pour Ã©viter les attaques par timing.

### Comportement sans fichier token

Si `auth-token.json` n'existe pas, l'authentification est **refusÃ©e** (fail-closed). Le middleware retourne un code HTTP 401 et aucune requÃªte n'est autorisÃ©e. Cette stratÃ©gie garantit qu'une configuration incomplÃ¨te ne compromet jamais la sÃ©curitÃ©.

> **En dÃ©veloppement local, crÃ©ez un fichier token vide ou gÃ©nÃ©rez-le avec `AuthToken.createTokenFile()`.**

### RÃ©ponses d'erreur

Les rÃ©ponses d'Ã©chec d'authentification (401) et de dÃ©passement du rate limit (429) posent `Cache-Control: no-store`, afin qu'aucun mandataire ni navigateur ne mette en cache une rÃ©ponse sensible. Le minuteur de purge du rate limiter (`purgeTimer`) est **disposÃ© Ã  l'arrÃªt du service** (`disposePurge()`), Ã©vitant de laisser un `System.Threading.Timer` racine actif indÃ©finiment.

## Rotation du token

Pour remplacer le token en cours :

1. Appeler `AuthToken.rotateToken()` en F# (gÃ©nÃ¨re et enregistre un nouveau token).
2. Ou manuellement : modifier le champ `Token` dans `C:\ProgramData\Diplo\auth-token.json`.
3. RedÃ©marrer les services Container, Volume et Network.
4. Mettre Ã  jour le token cÃ´tÃ© client (fichier `auth-token.json` du client).

> **Attention** : la rotation nÃ©cessite un redÃ©marrage des services pour recharger le fichier.

## CÃ´tÃ© client (gRPC)

Utiliser `TokenInterceptor.createTokenCredentials()` pour ajouter automatiquement l'en-tÃªte Bearer :

```fsharp
open Grpc.Net.Client
open DiploWalker.Abstractions

let channel = GrpcChannel.ForAddress("http://localhost:5001")
let invoker = channel.Intercept(TokenInterceptor.createTokenCredentials())
```

Si le fichier token n'existe pas cÃ´tÃ© client, aucune en-tÃªte n'est ajoutÃ©e.

## Transport gRPC (client)

- **Canal partagÃ©** : `GrpcClientFactory` maintient un cache des canaux par adresse (`ConcurrentDictionary`), recrÃ©Ã©/recyclÃ© en cas de panne (`channelCache`), au lieu d'un nouveau `GrpcChannel` par appel â€” Ã©vite la multiplication des connexions TCP et des sockets.
- **Taille des messages** : `MaxReceiveMessageSize`/`MaxSendMessageSize` bornÃ©s Ã  **64 Mo**, alignÃ©s avec le serveur (`ServerConfig.grpcMaxMessageSize`).
- **Timeout de connexion** : 5 s (`ConnectTimeout`), `UseProxy = false` â€” une adresse injoignable Ã©choue rapidement (`RpcException` Unavailable) au lieu de suspendre l'appel.
- **Retry** : politique de reprise avec backoff exponentiel (5 tentatives).

## Validation et injection

Toutes les entrÃ©es utilisateur sont validÃ©es avant traitement :

- **Noms de conteneurs** : `SecurityValidation.validateName` â€” caractÃ¨res alphanumÃ©riques, tirets et points uniquement.
- **Identifiants conteneur** : `SecurityValidation.validateContainerId` â€” alphanumÃ©riques, tirets et underscores uniquement.
- **Images** : `SecurityValidation.validateImage` â€” format `registry/repo:tag` validÃ©, interdiction des caractÃ¨res dangereux.
- **Labels** : `SecurityValidation.validateLabel` â€” clÃ© et valeur validÃ©es individuellement.
- **Commandes** : `SecurityValidation.validateCommand` â€” chaque argument est validÃ©, interdiction des caractÃ¨res d'injection shell (`;`, `|`, `&&`, `$(`, backticks).
- **ParamÃ¨tres PowerShell** : les noms de paramÃ¨tres sont validÃ©s par allowlist `[a-zA-Z_][a-zA-Z0-9_]*` avant toute invocation (`runPowerShell`), rendant impossible l'injection de paramÃ¨tres arbitraires.
- **Ã‰criture de fichier dans un conteneur (WriteFile)** : le chemin est passÃ© en **argument positionnel** (`sh -c 'base64 -d > "$1"' -- <chemin>`) et le contenu transite par **stdin** (base64) â€” aucune entrÃ©e utilisateur n'est interpolÃ©e dans la chaÃ®ne de commande.
- **YAML Compose** : le chargement est refusÃ© au-delÃ  de **10 Mo** (vÃ©rification avant lecture, pas de `ReadAllText` sans contrÃ´le).
- **Noms de volume** : `SecurityValidation.validateVolumeName` â€” alphanumÃ©riques, tirets et underscores uniquement.
- **Chemins de montage** : `SecurityValidation.validateMountPath` â€” pas de `..`, pas de chemins absolus, sÃ©parateurs de chemin interdits.
- **Noms de rÃ©seau** : `SecurityValidation.validateNetworkName` â€” alphanumÃ©riques, tirets et underscores uniquement.
- **CIDR** : `SecurityValidation.validateCidr` â€” format IPv4/CIDR validÃ© avec regex stricte.

### Interdiction d'injection

`SecurityValidation.containsShellInjection` bloque les caractÃ¨res dangereux : `;`, `|`, `&&`, `$(`, `` ` ``, `$(`, `>`, `<`. Ceci empÃªche toute injection de commandes shell Ã  travers les paramÃ¨tres utilisateur.

### Confinement des chemins d'images disque

Lors de l'extraction/la rÃ©Ã©criture d'une image disque (VDI, DMG, QCOWâ€¦), les chemins relatifs provenant de l'image sont rÃ©solus via `DiscFsHelper.realFrom` **confinÃ©** au dossier racine montÃ© : toute tentative de sortie (`..`, chemins absolus, sÃ©parateurs) est rejetÃ©e. Ce correctif anti-traversal empÃªche une image malveillante d'Ã©crire en dehors de sa propre arborescence (VdiFs et DmgFs passent dÃ©sormais par cette vÃ©rification).

Pour l'extraction **ISO** (lecture seule, parseur maison `IsoFs`), les noms de fichiers issus de l'image ISO9660/UDF sont assainis par `sanitizeName` (`IsoFs.fs`) : les sÃ©parateurs de chemin (`/`, `\`) et les caractÃ¨res de contrÃ´le sont remplacÃ©s par `_`, empÃªchant toute navigation de rÃ©pertoire via un nom embarquÃ© malveillant.

### RÃ©solution des liens (fail-closed)

La validation des chemins services passe par `SecurityValidation`, et tout Ã©chec de rÃ©solution d'un lien symbolique (`ResolveLinkTarget`) remonte dÃ©sormais une **erreur explicite** au lieu d'un repli silencieux : un chemin ambigu est rejetÃ© plutÃ´t qu'interprÃ©tÃ©.

### Extraction d'archives (installateur)

L'extraction des archives tÃ©lÃ©chargÃ©es (containerd, CNI) est faite entrÃ©e par entrÃ©e avec **validation anti zip-slip** : chaque chemin rÃ©sultant est forcÃ© Ã  rester sous le dossier de destination (`Path.GetFullPath` + vÃ©rification de prÃ©fixe). Les tÃ©lÃ©chargements passent par un **HttpClient partagÃ©** avec retry/backoff (pas de nouveau client par appel). Chaque artefact est vÃ©rifiÃ© contre son **SHA-256 issu d'un manifeste signÃ©** avant utilisation.

> **Ã‰tats** : les checksums sont centralisÃ©s dans `assets/artifacts.manifest`, signÃ© en **RSA-4096/SHA-384** (PKCS#1 v1.5) par une clÃ© privÃ©e **jamais versionnÃ©e** (hors bande, cf. `tools/sign-artifacts.ps1`). Le manifeste, la signature (base64) et la clÃ© publique sont **embarquÃ©s** dans l'assembly (`ArtifactSigning.fs`). Ã€ l'installation, le manifeste est chargÃ© une seule fois et sa signature vÃ©rifiÃ©e avec la clÃ© publique embarquÃ©e : **tout Ã©chec de vÃ©rification lÃ¨ve une erreur (fail-closed)** â€” l'installation s'interrompt. Un build compromis ou une modification des checksums ne peut rÃ©gÃ©nÃ©rer une signature valide sans la clÃ© privÃ©e. Les trois assets signÃ©s sont forcÃ©s en fins de ligne LF (`.gitattributes`) : une conversion autocrlf casserait la vÃ©rification.

## Identifiants de registres (login/logout)

Les mots de passe de registres sont chiffrÃ©s au repos :

- **Windows** : chiffrement **DPAPI** (`ProtectedData`, portÃ©e `CurrentUser`).
- **Hors Windows** : chiffrement **AES-GCM** scellÃ© par une clÃ© par utilisateur (lue dans un fichier aux droits restreints, mode 0600) â€” jamais de repli base64 en clair.

Lors des opÃ©rations `pull`/`push` avec authentification, les identifiants sont rÃ©solus via **`hosts.toml`/credential helper** y compris en mode `--user` explicite : le mot de passe ne transite **jamais** par la ligne de commande (invisible dans la liste des processus).

Le helper est un script PowerShell **unique** et portable, installÃ© dans `<racine>/cred-helper/` avec un lanceur adaptÃ© Ã  la plateforme (`diplo-cred-helper.cmd` sous Windows, `diplo-cred-helper` en `sh` ailleurs) â€” c'est ce lanceur que containerd exÃ©cute. Il se limite Ã  transmettre au script les chemins du fichier d'Ã©tat et de la clÃ©, puis le script dÃ©chiffre : DPAPI sous Windows, AES-GCM ailleurs. Sous Unix, le lanceur est posÃ© en `0755` et l'hÃ´te doit disposer de PowerShell 7 (`pwsh`). Ni l'Ã©tat ni la clÃ© ne transitent par la ligne de commande.

## Limites de conteneurs

Les ressources des conteneurs (mÃ©moire, CPU, PID) sont transmises Ã  containerd via un spec OCI gÃ©nÃ©rÃ© dynamiquement. Les limites sont appliquÃ©es au niveau du kernel Windows :

- **MÃ©moire** : `linux.resources.memory.limit` â€” limite en octets.
- **CPU** : `linux.resources.cpu.shares` â€” poids relatif de CPU.
- **PID** : `linux.resources.pids.limit` â€” nombre maximum de processus.

## SÃ©curitÃ© des logs

Les logs des conteneurs ne transmettent pas d'informations sensibles :

- Les variables d'environnement filtrÃ©es dans `InspectContainer` se limitent Ã  un ensemble prÃ©dÃ©fini (PATH, ASPNETCORE_ENVIRONMENT, etc.).
- Les secrets et tokens ne doivent jamais Ãªtre passÃ©s via les variables d'environnement â€” utiliser Key Vault ou des fichiers secrets Ã  la place.
- Les chemins de plugins CNI sont journalisÃ©s par **nom de fichier uniquement** (`Path.GetFileName`), jamais le chemin complet qui peut rÃ©vÃ©ler la structure du systÃ¨me.
- Les tokens sont comparÃ©s en temps constant (`FixedTimeEquals`) ; les Ã©checs d'authentification journalisent l'adresse IP de l'appelant.

## Bornes d'entrÃ©e et garde-fous (anti-DoS)

Toutes les entrÃ©es non bornÃ©es sont plafonnÃ©es cÃ´tÃ© serveur **et** cÃ´tÃ© client (double vÃ©rification) :

| Surface          | Limite                                                    | RÃ©fÃ©rence                                     |
| ---------------- | --------------------------------------------------------- | --------------------------------------------- |
| Message gRPC     | 64 Mo (`MaxReceiveMessageSize`/`MaxSendMessageSize`)      | `GrpcClientFactory`, `ServerConfig`           |
| WriteFile        | 50 Mo par fichier (`MaxFileTransferBytes`)                | `ServiceGuards.requireFileTransferWithinLimit` |
| Listes (API)     | 10 000 Ã©lÃ©ments max (`MaxListItems`)                      | `ServiceGuards`                                |
| YAML Compose     | 10 Mo par fichier                                         | `ComposeOrchestrator`                          |
| Connexion gRPC   | ConnectTimeout 5 s, retry 5 tentatives backoff            | `GrpcClientFactory`                            |
| Qcow2 `l1_size`  | â‰¤ 2Â²â´                                                     | `Qcow2.fs`                                     |
| Qcow2 `refcount_table_clusters` | â‰¤ 2Â²â°                                          | `Qcow2.fs`                                     |

Les index L1/L2 des images disque (qcow2, qcow1, parallels) sont validÃ©s **avant** tout cast `int` : une image aux dimensions extrÃªmes est rejetÃ©e proprement (pas de dÃ©passement entier, pas de crash). `findFreeCluster` refuse toute allocation dÃ©passant le budget de refcounts (image saturÃ©e).

## Recommandations

- Ne jamais committer `auth-token.json` dans un dÃ©pÃ´t git.
- Utiliser des permissions NTFS restrictives sur `C:\ProgramData\Diplo\`.
- Rotation du token rÃ©guliÃ¨re en production.
- Utiliser des images de conteneurs signÃ©es et provenant de registries fiables.
- La recherche et le pull d'images en ligne sont limitÃ©s Ã  une liste blanche de registres : docker.io, quay.io, mcr.microsoft.com, ghcr.io (`RegistrySearch.fs`).
- Limiter les ressources des conteneurs en production pour Ã©viter les dÃ©nis de service.
- **ProcÃ©dure de renouvellement des checksums des artefacts** : Ã©diter `assets/artifacts.manifest` (fins de ligne LF), lancer `tools/sign-artifacts.ps1` (signe avec la clÃ© privÃ©e hors bande, vÃ©rifie avec la clÃ© publique, rÃ©Ã©crit `artifacts.manifest.sig` en base64), puis commiter le manifeste et la signature. Si la paire de clÃ©s est rÃ©gÃ©nÃ©rÃ©e, mettre Ã  jour `assets/diplo-release.pub` ET garantir sa cohÃ©rence entre le dÃ©pÃ´t et l'emplacement de la clÃ© privÃ©e (une clÃ© publique embarquÃ©e qui ne correspond plus Ã  la clÃ© de signature fait Ã©chouer `sign-artifacts.ps1`).

