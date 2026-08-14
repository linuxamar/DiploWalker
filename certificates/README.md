# PKI de démonstration — Diplo

Chaîne de certificats à usage de démonstration, générée avec OpenSSL 3.
Toutes les clés sont du **RSA 8192 bits** et les signatures utilisent **SHA-384**.

> ⚠️ **Démonstration uniquement.** Les clés privées sont générées **sans phrase de passe**
> et ne sont **jamais versionnées** (voir `.gitignore`). Pour une exploitation réelle :
> protéger les clés par phrase de passe ou un HSM, et prévoir la révocation (CRL/OCSP)
> ainsi qu'un suivi des expirations.

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

## Contenu des répertoires

| Répertoire | Rôle |
|---|---|
| `root-ca/` | Autorité racine (clé, certificat, base, config) |
| `authentification/` | Intermédiaire « Authentification » (signe les feuilles d'authentification) |
| `system/` | Intermédiaire « System » (signe les feuilles d'authentification des systèmes) |
| `codesigning/` | Intermédiaire « CodeSigning » (signe les feuilles de signature de code) |
| `codesigning/leaves/` | Une feuille de signature de code par projet (`leaves/<Projet>/`) |
| `leaf-dll-validation/` | Certificat de signature des DLL chargées dynamiquement (parent : System) |
| `leaf-config-encryption/` | Certificat de chiffrement des fichiers de configuration (parent : System) |

Chaque autorité possède sa configuration `openssl.cnf` avec les sections `[ req ]`
et `[ ca ]`, une base (`db/index.txt`, `db/serial`) et un dossier `certs/`.

Dans chaque répertoire :

- `private/*.key.pem` — clé privée (non versionnée)
- `csr/*.csr.pem` — demande de signature (non versionnée)
- `certs/*.crt.pem` — certificat émis
- `certs/*.chain.crt.pem` — chaîne complète feuille → racine

Dans chaque répertoire `codesigning/leaves/<Projet>/` :

- `<Projet>.key.pem` — clé privée (non versionnée)
- `<Projet>.csr.pem` — demande de signature (non versionnée)
- `<Projet>.crt.pem` — certificat émis (EKU `codeSigning`)
- `<Projet>.pfx` — clé privée + certificat pour signtool (sans mot de passe, non versionné)

## Utilisation avec OpenSSL

Le binaire est disponible à l'emplacement Git pour Windows :
`C:\Program Files\Git\usr\bin\openssl.exe`.

### Vérifier la chaîne d'une feuille de signature de code

```powershell
openssl verify -CAfile certificates/root-ca/certs/root-ca.crt.pem `
  -untrusted certificates/codesigning/certs/codesigning.crt.pem `
  certificates/codesigning/leaves/Diplo.Cli/Diplo.Cli.crt.pem
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
openssl x509 -in certificates/codesigning/leaves/Diplo.Cli/Diplo.Cli.crt.pem -noout -text
```

## Signature de code : une feuille par projet

Chaque projet de la solution (src et tests, cf. `Diplo.slnx`) dispose de son propre
certificat de signature de code, émis par l'intermédiaire « CodeSigning ».

### Générer les feuilles (clés + PFX)

```powershell
powershell -ExecutionPolicy Bypass -File certificates/codesigning/leaves/regenerate-leaves.ps1
```

Le script lit les projets de `Diplo.slnx`, génère pour chacun une clé RSA 8192,
une CSR et un PFX (sans mot de passe, pour signtool). Seule la liste des projets
provient de `Diplo.slnx` : lancer le script n'écrase pas les clés existantes
(déjà générées → ignorées). Pour régénérer un projet : supprimer son dossier
`leaves/<Projet>/` puis relancer le script.

### Signature automatique à la compilation

Le fichier racine `Directory.Build.targets` exécute, après chaque `Build`, la cible
`DiploSignBinary` qui signe `$(TargetPath)` avec le PFX du projet via **signtool**
(`/fd SHA256`, EKU `codeSigning`, chaîne `Feuille ← CodeSigning ← Diplo Root CA`).

Comportement :

- **PFX du projet absent** (poste non initialisé, CI sans clés privées) → binaire
  non signé, simple message d'information. Le build n'échoue pas.
- **PFX présent et signtool introuvable** → erreur de build (signature obligatoire).
- signtool est recherché dans `Windows Kits\10\bin\<version>\x64\` (la plus récente
  installée est utilisée) ; la CI GitHub Windows (`windows-latest`) le fournit.

Réglages possibles (`-p:<Propriété>=...` ou dans un `.csproj`) :

| Propriété | Rôle |
|---|---|
| `DiploSignOutputAfterBuild` | `false` pour désactiver la signature (défaut `true`) |
| `DiploCodeSigningTimestampUrl` | URL RFC 3161 pour horodater la signature (défaut : vide) |
| `DiploSigntoolExe` | Chemin explicite vers signtool.exe (sinon auto-détection) |

### Vérifier une signature

```powershell
signtool verify /pa /v src/Diplo.Cli/bin/Debug/net10.0/Diplo.Cli.dll
```

> La validation complète (« success ») exige que « Diplo Root CA » soit installé
> dans le magasin « Autorités de certification racines de confiance » de la machine
> cible (déploiement de la PKI). Sans cela, signtool signale l'absence de racine de
> confiance, mais la chaîne embarquée reste lisible :
>
> ```
> Issued to: Diplo.Cli     Issued by: CodeSigning
> Issued to: CodeSigning   Issued by: Diplo Root CA
> ```

## Validation des DLL chargées dynamiquement

`leaf-dll-validation` (parent : **System**) permet de garantir qu'une DLL chargée
dynamiquement a bien été produite par le projet Diplo. Le principe :

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

## Régénération complète

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
