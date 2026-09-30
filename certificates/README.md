# PKI de démonstration — Diplo

Chaîne de certificats à usage de démonstration, générée avec OpenSSL 3.
Toutes les clés sont du **RSA 8192 bits** et les signatures utilisent **SHA-384**.

> ⚠️ **Démonstration uniquement.** Les clés privées sont générées **sans phrase de passe**.
> Elles sont **versionnées, mais chiffrées** par git-crypt (voir « Chiffrer et
> versionner le dépôt » ci-dessous) : le dépôt ne contient que du ciphertext, et la
> clé qui le déchiffre est un secret hors dépôt. Pour une exploitation réelle :
> protéger les clés par phrase de passe ou un HSM, et prévoir la révocation
> (CRL/OCSP) ainsi qu'un suivi des expirations.

## Chiffrer et versionner le dépôt

Les clés privées et les PFX **sont versionnés**, sans quoi la CI ne pourrait pas
signer et aucun artefact signé ne pourrait être produit ailleurs que sur le poste
d'origine. Pour que cela reste acceptable, ces 50 fichiers sont **chiffrés au repos
par [git-crypt](https://github.com/AGL/git-crypt)** (mode symétrique).

| Élément                        | Traitement                                    |
| ------------------------------ | --------------------------------------------- |
| 28 clés `*.key.pem`            | versionnées, chiffrées                        |
| 22 PFX `*.pfx`                 | versionnés, chiffrés                          |
| 27 CSR `*.csr.pem`             | ignorés (aucun intérêt pour le build)         |
| `db/newcerts/`, `db/index.txt*` | ignorés (propres au poste qui émet)          |
| clé symétrique git-crypt       | **jamais** versionnée — secret GitHub + coffre |

Les motifs du filtre sont dans `.gitattributes` (`certificates/**/*.key.pem` et
`certificates/**/*.pfx`). Ils ne chevauchent pas les règles Git LFS du dépôt :
`*.pem` et `*.pfx` n'y figurent pas, donc les deux filtres ne se disputent jamais
un même fichier.

### Premier poste

```powershell
git-crypt unlock C:\chemin\vers\diplo-git-crypt.key
```

C'est tout : `unlock` installe le filtre **et** déchiffre. Il n'existe pas de
sous-commande `git-crypt install` (`git-crypt help` ne propose que `init`, `status`,
`lock`, `unlock`, `export-key`, `keygen`, `migrate-key`), et `init` est inutile ici :
il génère une clé qui ne correspond pas au dépôt existant. La même commande sert
donc au poste de développement et à la CI.

### Ce que fait réellement git-crypt

Le chiffrement est branché sur les filtres `clean`/`smudge` de Git :

- au `git add`, le filtre `clean` écrit du **ciphertext** dans l'objet Git ;
- à l'extraction, le filtre `smudge` réécrit du **clair** dans le worktree.

Un poste de développement a donc **les clés en clair sur son disque** ; git-crypt
protège le dépôt et les clones, pas le poste. Sur un runner éphémère c'est sans
conséquence, sur un poste volé c'est insuffisant. `git-crypt lock` reverrouille le
worktree.

### Publication en CI

Le job `release` de `.github/workflows/ci.yml` déverrouille le dépôt, car il
déchiffre au début et supprime la clé :

```powershell
[System.IO.File]::WriteAllBytes($keyFile, [System.Convert]::FromBase64String($env:GIT_CRYPT_KEY))
git-crypt unlock $keyFile
```

Le job `test` ne déverrouille rien : sans PFX, `Directory.Build.targets` dégrade la
publication en binaires non signés, ce qui suffit pour les tests. Le job `release`
échoue volontairement si le secret est absent, plutôt que de publier des
artefacts non signés.

Le secret GitHub `GIT_CRYPT_KEY` contient la clé **encodée en base64** (la clé
fichée par `git-crypt keygen` est binaire) :

```powershell
$b64 = [System.Convert]::ToBase64String([System.IO.File]::ReadAllBytes("diplo-git-crypt.key"))
```

### Faire évoluer la clé symétrique

```powershell
git-crypt export-key nouvelle-cle.key   # exporte la clé actuelle du dépôt
```

## Rotation

`git-crypt` chiffre le dépôt, il ne gère pas le cycle de vie des certificats.
Une clé compromise se traite comme une clé compromise.

**Divulgation de la clé symétrique** — situation la plus grave, elle expose les 50
fichiers :

1. Régénérer la PKI (§ « Régénération ») et committer les nouveaux fichiers
   chiffrés, **y compris** dans l'historique : retirer les fichiers des commits
   récents ne suffit pas, il faut réécrire l'historique (`git filter-repo`) ou
   repartir d'une nouvelle branche.
2. Révoquer et réémettre tous les certificats concernés.
3. Régénérer la clé symétrique et la redéployer en CI.

**Divulgation d'une seule clé de feuille** — par exemple un PFX d'un poste de
développement compromis :

1. Supprimer `certificates/codesigning/leaves/<Projet>/`.
2. Relancer `regenerate-leaves.ps1` (il régénère le projet absent).
3. Commiter ; la CI resigne avec le nouveau certificat.

> `-Force` n'est pas anodin : il régénère clés, certificats et PFX, et les
> fichiers étant versionnés, cela produit un commit contenant des secrets
> chiffrés. Vérifiez le diff avant de commiter.

## Structure de la chaîne

```
┌───────────────────────────────┐
│  Diplo Root CA                │  auto-signé, 10 ans
│  CA:TRUE, pathlen:2           │  root-ca/openssl.cnf
├────────────┬──────────┬───────┴──────────────────┐
│ Authentif. │ System   │ CodeSigning             │
│ pathlen:0  │ pathlen:0 │ pathlen:0               │
│ (feuilles  │ (auth.   │ (signature de code)     │
│  auth.)    │ systèmes)│                         │
│  ├─────────┤          │                         │
│  │leaf-tls-│          │                         │
│  │server   │          │                         │
│  │(TLS des │          │                         │
│  │ tubes)  │          │                         │
│            ├──────────┤                         │
│            │leaf-dll- │                         │
│            │validation│                         │
│            │(signature│                         │
│            │ DLL)     │                         │
│            ├──────────┤                         │
│            │leaf-     │                         │
│            │config-   │                         │
│            │encryption│                         │
│            │(chiffre. │                         │
│            │ config)  │                         │
├────────────┤          ├─────────────────────────┤
│            │          │ leaves/<Projet>/        │
│            │          │ une feuille par projet  │
└────────────┴──────────┴─────────────────────────┘
```

- `pathlen:n` limite le nombre de niveaux d'intermédiaires autorisés en dessous :
  la racine tolère 2 niveaux ; « Authentification », « System » et « CodeSigning »
  (autorités de feuilles) n'en tolèrent aucun.
- Les SAN et l'usage étendu demandés dans la CSR des feuilles sont recopiés dans le
  certificat grâce à `copy_extensions = copy` dans la section `[ CA_default ]`.
- Sous « CodeSigning », chaque projet de la solution possède sa propre feuille de
  signature de code (voir « Signature de code : une feuille par projet »).
- Sous « System » : `leaf-dll-validation` (signature des DLL chargées dynamiquement,
  voir « Validation des DLL ») et `leaf-config-encryption` (chiffrement des fichiers
  de configuration, voir « Chiffrement des fichiers de configuration »).
- Sous « Authentification » : `leaf-tls-server` (chiffrement des named pipes,
  voir « Chiffrement des named pipes »).

## Contenu des répertoires

| Répertoire                | Rôle                                                                          |
| ------------------------- | ----------------------------------------------------------------------------- |
| `root-ca/`                | Autorité racine (clé, certificat, base, config)                               |
| `authentification/`       | Intermédiaire « Authentification » (signe les feuilles d'authentification)    |
| `system/`                 | Intermédiaire « System » (signe les feuilles d'authentification des systèmes) |
| `codesigning/`            | Intermédiaire « CodeSigning » (signe les feuilles de signature de code)       |
| `codesigning/leaves/`     | Une feuille de signature de code par projet (`leaves/<Projet>/`)              |
| `leaf-dll-validation/`    | Certificat de signature des DLL chargées dynamiquement (parent : System)      |
| `leaf-config-encryption/` | Certificat de chiffrement des fichiers de configuration (parent : System)     |

Chaque autorité possède sa configuration `openssl.cnf` avec les sections `[ req ]`
et `[ ca ]`, une base (`db/index.txt`, `db/serial`) et un dossier `certs/`.

Dans chaque répertoire :

- `private/*.key.pem` — clé privée (versionnée, chiffrée)
- `csr/*.csr.pem` — demande de signature (ignorée)
- `certs/*.crt.pem` — certificat émis
- `certs/*.chain.crt.pem` — chaîne complète feuille → racine

Dans chaque répertoire `codesigning/leaves/<Projet>/` :

- `<Projet>.key.pem` — clé privée (versionnée, chiffrée)
- `<Projet>.csr.pem` — demande de signature (ignorée)
- `<Projet>.crt.pem` — certificat émis (EKU `codeSigning`)
- `<Projet>.pfx` — clé privée + certificat pour signtool, sans mot de passe
  (versionné, chiffré)

## Utilisation avec OpenSSL

Le binaire est disponible à l'emplacement Git pour Windows :
`C:\Program Files\Git\usr\bin\openssl.exe`.

### Vérifier la chaîne d'une feuille de signature de code

```powershell
openssl verify -CAfile certificates/root-ca/certs/root-ca.crt.pem `
  -untrusted certificates/codesigning/certs/codesigning.crt.pem `
  certificates/codesigning/leaves/DiploWalker.Cli/DiploWalker.Cli.crt.pem
```

### Vérifier la chaîne de leaf-dll-validation

```powershell
openssl verify -CAfile certificates/root-ca/certs/root-ca.crt.pem `
  -untrusted certificates/system/certs/system.crt.pem `
  certificates/leaf-dll-validation/certs/leaf-dll-validation.crt.pem
```

### Vérifier la chaîne de leaf-config-encryption

```powershell
openssl verify -CAfile certificates/root-ca/certs/root-ca.crt.pem `
  -untrusted certificates/system/certs/system.crt.pem `
  certificates/leaf-config-encryption/certs/leaf-config-encryption.crt.pem
```

### Inspecter une feuille (EKU `codeSigning`)

```powershell
openssl x509 -in certificates/codesigning/leaves/DiploWalker.Cli/DiploWalker.Cli.crt.pem -noout -text
```

## Signature de code : une feuille par projet

Chaque projet de la solution (src et tests, cf. `DiploWalker.slnx`) dispose de son propre
certificat de signature de code, émis par l'intermédiaire « CodeSigning ».

### Générer les feuilles (clés + PFX)

```powershell
powershell -ExecutionPolicy Bypass -File certificates/codesigning/leaves/regenerate-leaves.ps1
```

Le script lit les projets de `DiploWalker.slnx`, génère pour chacun une clé RSA 8192,
une CSR et un PFX (sans mot de passe, pour signtool). Seule la liste des projets
provient de `DiploWalker.slnx` : lancer le script n'écrase pas les clés existantes
(déjà générées → ignorées). Pour régénérer un projet : supprimer son dossier
`leaves/<Projet>/` puis relancer le script.

### Signature automatique à la compilation

Le fichier racine `Directory.Build.targets` exécute, après chaque `Build`, la cible
`DiploSignBinary` qui signe `$(TargetPath)` avec le PFX du projet via **signtool**
(`/fd SHA256`, EKU `codeSigning`, chaîne `Feuille ← CodeSigning ← Diplo Root CA`).

Comportement :

- **PFX du projet absent** (poste non déverrouillé) → binaire non signé, simple
  message d'information. Le build n'échoue pas.
- **PFX présent et signtool introuvable** → erreur de build (signature obligatoire).
- signtool est recherché dans `Windows Kits\10\bin\<version>\x64\` (la plus récente
  installée est utilisée) ; la CI GitHub Windows (`windows-latest`) le fournit.

Réglages possibles (`-p:<Propriété>=...` ou dans un `.csproj`) :

| Propriété                      | Rôle                                                      |
| ------------------------------ | --------------------------------------------------------- |
| `DiploSignOutputAfterBuild`    | `false` pour désactiver la signature (défaut `true`)      |
| `DiploCodeSigningTimestampUrl` | URL RFC 3161 pour horodater la signature (défaut : vide)  |
| `DiploSigntoolExe`             | Chemin explicite vers signtool.exe (sinon auto-détection) |

### Vérifier une signature

```powershell
signtool verify /pa /v src/DiploWalker.Cli/bin/Debug/net10.0/DiploWalker.Cli.dll
```

> La validation complète (« success ») exige que « Diplo Root CA » soit installé
> dans le magasin « Autorités de certification racines de confiance » de la machine
> cible (déploiement de la PKI). Sans cela, signtool signale l'absence de racine de
> confiance, mais la chaîne embarquée reste lisible :
>
> ```
> Issued to: DiploWalker.Cli     Issued by: CodeSigning
> Issued to: CodeSigning   Issued by: Diplo Root CA
> ```

## Validation des DLL chargées dynamiquement

`leaf-dll-validation` (parent : **System**) permet de garantir qu'une DLL chargée
dynamiquement a bien été produite par le projet DiploWalker. Le principe :

1. **Signature** : la DLL est signée en Authenticode avec le PFX
   `leaf-dll-validation.pfx` (EKU `codeSigning`, chaîne `leaf-dll-validation ←
System ← Diplo Root CA`).

```powershell
signtool sign /fd SHA256 /f certificates/leaf-dll-validation/leaf-dll-validation.pfx `
  ma-plug-in.dll
```

2. **Validation avant chargement** : le chargeur vérifie la signature, puis que
   la chaîne remonte à « System » et « Diplo Root CA » (via `X509Chain` en .NET,
   `CertGetCertificateChain`/`WinVerifyTrust` en Win32, ou `signtool verify`).

```powershell
signtool verify /pa /v ma-plug-in.dll
```

> Comme pour le reste de la PKI, la validation complète exige que « Diplo Root CA »
> et « System » soient installés dans les magasins de confiance de la machine qui
> charge les DLL. Pour vérifier la chaîne cryptographique seule, utiliser la
> commande `openssl verify` de la section précédente.

## Chiffrement des fichiers de configuration

`leaf-config-encryption` (parent : **System**) chiffre les fichiers de configuration
du projet Diplo : seuls les éléments disposant de la clé privée peuvent les
déchiffrer, et la chaîne (`leaf-config-encryption ← System ← Diplo Root CA`)
garantit l'origine des données.

Schéma conseillé — **chiffrement hybride** (RSA 8192 trop lent en données brutes) :

1. **Chiffrement** : générer une clé de session AES-256 aléatoire, chiffrer le
   fichier avec (AES-256-GCM ou AES-256-CBC + HMAC), puis chiffrer la clé de
   session avec la clé publique RSA du certificat (RSA-OAEP, SHA-256). Ne stocker
   que le fichier chiffré et la clé enveloppée.
2. **Déchiffrement** : déchiffrer la clé de session avec la clé privée
   (`leaf-config-encryption.pfx` ou le magasin de certificats), puis le fichier.

Exemple minimal avec OpenSSL :

```powershell
# Extraire la clé publique du certificat
openssl x509 -in certificates/leaf-config-encryption/certs/leaf-config-encryption.crt.pem -pubkey -noout `
  -out public.pem

# Chiffrer la clé de session (ici le fichier secret.bin, 64 o) avec RSA-OAEP
openssl pkeyutl -encrypt -pubin -inkey public.pem -in secret.bin -out secret.bin.enc `
  -pkeyopt rsa_padding_mode:oaep -pkeyopt rsa_oaep_md:sha256

# Déchiffrer avec la clé privée
openssl pkeyutl -decrypt -inkey certificates/leaf-config-encryption/private/leaf-config-encryption.key.pem `
  -in secret.bin.enc -out secret.bin.dec `
  -pkeyopt rsa_padding_mode:oaep -pkeyopt rsa_oaep_md:sha256
```

En .NET, charger le certificat privé depuis le PFX ou le magasin, puis utiliser
`RSA.Encrypt`/`RSA.Decrypt` avec `RSAEncryptionPadding.OaepSHA256`.

> La validation complète de l'origine exige que « System » et « Diplo Root CA »
> soient installés dans les magasins de confiance (ou fournis explicitement au
> validateur).

## Chiffrement des named pipes

`leaf-tls-server` est la feuille qui chiffre le transport local par named pipe.
Elle est émise sous l'autorité « Authentification » avec un EKU `serverAuth`
uniquement (jamais `codeSigning`), un SAN `localhost`, une RSA 8192 et SHA-384.

L'installateur dépose le PFX dans `<configDir>\certs\leaf-tls-server.pfx`, en
restreint l'ACL (il contient la clé privée), puis écrit ce chemin dans
`ServiceSettings:PipeCertificatePath` de chaque `*.appsettings.json`.
`ServerConfig.configureKestrel` bascule alors le tube en `UseHttps`. Sans ce
chemin, les tubes restent en clair — le mode de compatibilité historique.

Côté client, l'adresse `https://pipe:/<nom>` n'accepte aucune autorité : le
canal est construit avec un `SocketsHttpHandler` dont la validation exige
l'empreinte compilée dans `PipeTls.ExpectedServerThumbprint`. Un tube présentant
un autre certificat est donc refusé, sans distribution de la racine et sans
manipulation des magasins Windows.

Le PFX est versionné **chiffré** par git-crypt, comme les autres clés privées. Le
job `test` de la CI ne déverrouille pas le dépôt : les tests TLS génèrent un
certificat à l'exécution et n'utilisent pas ce fichier. `PipeTlsFingerprintTests`
compare en revanche l'empreinte compilée au certificat **public** en clair
(`certs/leaf-tls-server.crt.pem`), de sorte qu'une divergence entre le code et la
PKI versionnée est détectée même sans accès à la clé privée.

### Régénérer uniquement cette feuille

```powershell
# Recrée leaf-tls-server sans toucher aux autres feuilles de la PKI
.\certificates\regenerate-pki.ps1 -OnlyTlsLeaf

# Vérifier la chaîne
openssl verify -CAfile certificates\root-ca\certs\root-ca.crt.pem `
  -untrusted certificates\authentification\certs\authentification.crt.pem `
  certificates\leaf-tls-server\certs\leaf-tls-server.crt.pem
```

Toute régénération change l'empreinte : il faut alors mettre à jour
`PipeTls.ExpectedServerThumbprint`, puis régénérer le PFX avec un mot de passe
vide (`leaf-tls-server.pfx`, sans secret additionnel) car l'installateur le copie
et le lit sans invite.

## Régénération complète

### Via le script d'amorçage (recommandé)

```powershell
# Toute la PKI : racine, intermédiaires, feuilles système et feuilles de signature
.\certificates\regenerate-pki.ps1 -Force

# Autorités seules, sans toucher aux 22 feuilles de signature de code
.\certificates\regenerate-pki.ps1 -SkipProjectLeaves -Force

# Feuille TLS des named pipes seule (voir « Chiffrement des named pipes »)
.\certificates\regenerate-pki.ps1 -OnlyTlsLeaf
```

Le script détecte `openssl.exe` dans le `PATH`, puis dans les installations Git
standard et Git Scoop (`-OpenSSLPath` pour forcer un chemin). Sans `-Force`, il
refuse de révoquer un certificat existant. Comme les clés et les PFX sont versionnés
(chiffrés), la régénération les modifie et se voit dans le diff : c'est le mécanisme
normal de rotation, à commiter après vérification. Pour repartir de zéro sur une
seule autorité, supprimer son répertoire plutôt que d'effacer `certificates/`, sinon
toute la chaîne est à réémettre.

### Procédure manuelle

1. Effacer le contenu de `certificates/` (ou d'un sous-répertoire).
2. Suivre l'ordre ci-dessous, en exécutant chaque commande depuis le répertoire
   de l'autorité concernée (le config contient les chemins relatifs) :

```powershell
# 1. Racine : clé + auto-signature
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:8192 -out private/root-ca.key.pem
openssl req -x509 -config openssl.cnf -key private/root-ca.key.pem -days 3650 -out certs/root-ca.crt.pem

# 2. Intermédiaires de feuilles : Authentification, System, CodeSigning
#    (même procédure pour chacun, signés par la racine, extensions v3_intermediate_leaf)
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:8192 -out private/authentification.key.pem
openssl req -new -config openssl.cnf -key private/authentification.key.pem -out csr/authentification.csr.pem
openssl ca -config ../root-ca/openssl.cnf -extensions v3_intermediate_leaf -batch -notext `
  -in csr/authentification.csr.pem -out certs/authentification.crt.pem -days 1825

# 3. Feuilles : les autorités Authentification, System et CodeSigning signent
#    leurs propres feuilles avec -extensions leaf_cert, depuis leur répertoire.
#    Exemple (feuille d'authentification) :
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:8192 -out private/ma-feuille.key.pem
openssl req -new -config openssl.cnf -key private/ma-feuille.key.pem -out csr/ma-feuille.csr.pem
openssl ca -config ../authentification/openssl.cnf -extensions leaf_cert -batch -notext `
  -in csr/ma-feuille.csr.pem -out certs/ma-feuille.crt.pem -days 825
# (recréer ensuite les fichiers *.chain.crt.pem)
```

> Les feuilles de signature de code par projet sont générées automatiquement par
> `certificates/codesigning/leaves/regenerate-leaves.ps1` (voir la section
> « Signature de code : une feuille par projet »), elles n'apparaissent donc pas ici.

> Chaque commande s'exécute depuis le répertoire de l'autorité/feuille concernée
> (par exemple `certificates/root-ca/` pour la première). Les commandes `openssl ca`
> mettent à jour la base `db/index.txt` ; celle-ci doit exister (vide au départ).

