# Sécurité — Diplo

## Authentification par token

Chaque microservice (Container, Volume, Network) utilise un token partagé pour l'authentification.

### Fonctionnement

1. **Génération** : L'installateur (`Diplo.Installer`) génère un token aléatoire de 32 octets (Base64) et l'écrit dans `C:\ProgramData\Diplo\auth-token.json`.
2. **Validation** : Les middleware HTTP et les intercepteurs gRPC vérifient que chaque requête contient l'en-tête `Authorization: Bearer <token>`.
3. **Comparaison** : La vérification utilise `CryptographicOperations.FixedTimeEquals` pour éviter les attaques par timing.

### Comportement sans fichier token

Si `auth-token.json` n'existe pas, l'authentification est **désactivée** (bypass). Les requêtes passent directement. Ceci est utile lors du développement local.

> **En production, le fichier token doit toujours être présent.**

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

## Recommandations

- Ne jamais commitler `auth-token.json` dans un dépôt git.
- Utiliser des permissions NTFS restrictives sur `C:\ProgramData\Diplo\`.
- Rotation du token régulière en production.
