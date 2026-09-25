# PKI de dÃ©monstration â€” Diplo

ChaÃ®ne de certificats Ã  usage de dÃ©monstration, gÃ©nÃ©rÃ©e avec OpenSSL 3.
Toutes les clÃ©s sont du **RSA 8192 bits** et les signatures utilisent **SHA-384**.

> âš ï¸ **DÃ©monstration uniquement.** Les clÃ©s privÃ©es sont gÃ©nÃ©rÃ©es **sans phrase de passe**
> et ne sont **jamais versionnÃ©es** (voir `.gitignore`). Pour une exploitation rÃ©elle :
> protÃ©ger les clÃ©s par phrase de passe ou un HSM, et prÃ©voir la rÃ©vocation (CRL/OCSP)
> ainsi qu'un suivi des expirations.

## Structure de la chaÃ®ne

```
â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚  Diplo Root CA                â”‚  auto-signÃ©, 10 ans
â”‚  CA:TRUE, pathlen:2           â”‚  root-ca/openssl.cnf
â”œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¬â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¬â”€â”€â”€â”€â”€â”€â”€â”´â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚ Authentif. â”‚ System   â”‚ CodeSigning             â”‚
â”‚ pathlen:0  â”‚ pathlen:0 â”‚ pathlen:0               â”‚
â”‚ (feuilles  â”‚ (auth.   â”‚ (signature de code)     â”‚
â”‚  auth.)    â”‚ systÃ¨mes)â”‚                         â”‚
â”‚            â”œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¤                         â”‚
â”‚            â”‚leaf-dll- â”‚                         â”‚
â”‚            â”‚validationâ”‚                         â”‚
â”‚            â”‚(signatureâ”‚                         â”‚
â”‚            â”‚ DLL)     â”‚                         â”‚
â”‚            â”œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¤                         â”‚
â”‚            â”‚leaf-     â”‚                         â”‚
â”‚            â”‚config-   â”‚                         â”‚
â”‚            â”‚encryptionâ”‚                         â”‚
â”‚            â”‚(chiffre. â”‚                         â”‚
â”‚            â”‚ config)  â”‚                         â”‚
â”œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¤          â”œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¤
â”‚            â”‚          â”‚ leaves/<Projet>/        â”‚
â”‚            â”‚          â”‚ une feuille par projet  â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”´â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”´â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
```

- `pathlen:n` limite le nombre de niveaux d'intermÃ©diaires autorisÃ©s en dessous :
  la racine tolÃ¨re 2 niveaux ; Â« Authentification Â», Â« System Â» et Â« CodeSigning Â»
  (autoritÃ©s de feuilles) n'en tolÃ¨rent aucun.
- Les SAN et l'usage Ã©tendu demandÃ©s dans la CSR des feuilles sont recopiÃ©s dans le
  certificat grÃ¢ce Ã  `copy_extensions = copy` dans la section `[ CA_default ]`.
- Sous Â« CodeSigning Â», chaque projet de la solution possÃ¨de sa propre feuille de
  signature de code (voir Â« Signature de code : une feuille par projet Â»).
- Sous Â« System Â» : `leaf-dll-validation` (signature des DLL chargÃ©es dynamiquement,
  voir Â« Validation des DLL Â») et `leaf-config-encryption` (chiffrement des fichiers
  de configuration, voir Â« Chiffrement des fichiers de configuration Â»).

## Contenu des rÃ©pertoires

| RÃ©pertoire                | RÃ´le                                                                          |
| ------------------------- | ----------------------------------------------------------------------------- |
| `root-ca/`                | AutoritÃ© racine (clÃ©, certificat, base, config)                               |
| `authentification/`       | IntermÃ©diaire Â« Authentification Â» (signe les feuilles d'authentification)    |
| `system/`                 | IntermÃ©diaire Â« System Â» (signe les feuilles d'authentification des systÃ¨mes) |
| `codesigning/`            | IntermÃ©diaire Â« CodeSigning Â» (signe les feuilles de signature de code)       |
| `codesigning/leaves/`     | Une feuille de signature de code par projet (`leaves/<Projet>/`)              |
| `leaf-dll-validation/`    | Certificat de signature des DLL chargÃ©es dynamiquement (parent : System)      |
| `leaf-config-encryption/` | Certificat de chiffrement des fichiers de configuration (parent : System)     |

Chaque autoritÃ© possÃ¨de sa configuration `openssl.cnf` avec les sections `[ req ]`
et `[ ca ]`, une base (`db/index.txt`, `db/serial`) et un dossier `certs/`.

Dans chaque rÃ©pertoire :

- `private/*.key.pem` â€” clÃ© privÃ©e (non versionnÃ©e)
- `csr/*.csr.pem` â€” demande de signature (non versionnÃ©e)
- `certs/*.crt.pem` â€” certificat Ã©mis
- `certs/*.chain.crt.pem` â€” chaÃ®ne complÃ¨te feuille â†’ racine

Dans chaque rÃ©pertoire `codesigning/leaves/<Projet>/` :

- `<Projet>.key.pem` â€” clÃ© privÃ©e (non versionnÃ©e)
- `<Projet>.csr.pem` â€” demande de signature (non versionnÃ©e)
- `<Projet>.crt.pem` â€” certificat Ã©mis (EKU `codeSigning`)
- `<Projet>.pfx` â€” clÃ© privÃ©e + certificat pour signtool (sans mot de passe, non versionnÃ©)

## Utilisation avec OpenSSL

Le binaire est disponible Ã  l'emplacement Git pour Windows :
`C:\Program Files\Git\usr\bin\openssl.exe`.

### VÃ©rifier la chaÃ®ne d'une feuille de signature de code

```powershell
openssl verify -CAfile certificates/root-ca/certs/root-ca.crt.pem `
  -untrusted certificates/codesigning/certs/codesigning.crt.pem `
  certificates/codesigning/leaves/DiploWalker.Cli/DiploWalker.Cli.crt.pem
```

### VÃ©rifier la chaÃ®ne de leaf-dll-validation

```powershell
openssl verify -CAfile certificates/root-ca/certs/root-ca.crt.pem `
  -untrusted certificates/system/certs/system.crt.pem `
  certificates/leaf-dll-validation/certs/leaf-dll-validation.crt.pem
```

### VÃ©rifier la chaÃ®ne de leaf-config-encryption

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
certificat de signature de code, Ã©mis par l'intermÃ©diaire Â« CodeSigning Â».

### GÃ©nÃ©rer les feuilles (clÃ©s + PFX)

```powershell
powershell -ExecutionPolicy Bypass -File certificates/codesigning/leaves/regenerate-leaves.ps1
```

Le script lit les projets de `DiploWalker.slnx`, gÃ©nÃ¨re pour chacun une clÃ© RSA 8192,
une CSR et un PFX (sans mot de passe, pour signtool). Seule la liste des projets
provient de `DiploWalker.slnx` : lancer le script n'Ã©crase pas les clÃ©s existantes
(dÃ©jÃ  gÃ©nÃ©rÃ©es â†’ ignorÃ©es). Pour rÃ©gÃ©nÃ©rer un projet : supprimer son dossier
`leaves/<Projet>/` puis relancer le script.

### Signature automatique Ã  la compilation

Le fichier racine `Directory.Build.targets` exÃ©cute, aprÃ¨s chaque `Build`, la cible
`DiploSignBinary` qui signe `$(TargetPath)` avec le PFX du projet via **signtool**
(`/fd SHA256`, EKU `codeSigning`, chaÃ®ne `Feuille â† CodeSigning â† Diplo Root CA`).

Comportement :

- **PFX du projet absent** (poste non initialisÃ©, CI sans clÃ©s privÃ©es) â†’ binaire
  non signÃ©, simple message d'information. Le build n'Ã©choue pas.
- **PFX prÃ©sent et signtool introuvable** â†’ erreur de build (signature obligatoire).
- signtool est recherchÃ© dans `Windows Kits\10\bin\<version>\x64\` (la plus rÃ©cente
  installÃ©e est utilisÃ©e) ; la CI GitHub Windows (`windows-latest`) le fournit.

RÃ©glages possibles (`-p:<PropriÃ©tÃ©>=...` ou dans un `.csproj`) :

| PropriÃ©tÃ©                      | RÃ´le                                                      |
| ------------------------------ | --------------------------------------------------------- |
| `DiploSignOutputAfterBuild`    | `false` pour dÃ©sactiver la signature (dÃ©faut `true`)      |
| `DiploCodeSigningTimestampUrl` | URL RFC 3161 pour horodater la signature (dÃ©faut : vide)  |
| `DiploSigntoolExe`             | Chemin explicite vers signtool.exe (sinon auto-dÃ©tection) |

### VÃ©rifier une signature

```powershell
signtool verify /pa /v src/DiploWalker.Cli/bin/Debug/net10.0/DiploWalker.Cli.dll
```

> La validation complÃ¨te (Â« success Â») exige que Â« Diplo Root CA Â» soit installÃ©
> dans le magasin Â« AutoritÃ©s de certification racines de confiance Â» de la machine
> cible (dÃ©ploiement de la PKI). Sans cela, signtool signale l'absence de racine de
> confiance, mais la chaÃ®ne embarquÃ©e reste lisible :
>
> ```
> Issued to: DiploWalker.Cli     Issued by: CodeSigning
> Issued to: CodeSigning   Issued by: Diplo Root CA
> ```

## Validation des DLL chargÃ©es dynamiquement

`leaf-dll-validation` (parent : **System**) permet de garantir qu'une DLL chargÃ©e
dynamiquement a bien Ã©tÃ© produite par le projet DiploWalker. Le principe :

1. **Signature** : la DLL est signÃ©e en Authenticode avec le PFX
   `leaf-dll-validation.pfx` (EKU `codeSigning`, chaÃ®ne `leaf-dll-validation â†
System â† Diplo Root CA`).

```powershell
signtool sign /fd SHA256 /f certificates/leaf-dll-validation/leaf-dll-validation.pfx `
  ma-plug-in.dll
```

2. **Validation avant chargement** : le chargeur vÃ©rifie la signature, puis que
   la chaÃ®ne remonte Ã  Â« System Â» et Â« Diplo Root CA Â» (via `X509Chain` en .NET,
   `CertGetCertificateChain`/`WinVerifyTrust` en Win32, ou `signtool verify`).

```powershell
signtool verify /pa /v ma-plug-in.dll
```

> Comme pour le reste de la PKI, la validation complÃ¨te exige que Â« Diplo Root CA Â»
> et Â« System Â» soient installÃ©s dans les magasins de confiance de la machine qui
> charge les DLL. Pour vÃ©rifier la chaÃ®ne cryptographique seule, utiliser la
> commande `openssl verify` de la section prÃ©cÃ©dente.

## Chiffrement des fichiers de configuration

`leaf-config-encryption` (parent : **System**) chiffre les fichiers de configuration
du projet Diplo : seuls les Ã©lÃ©ments disposant de la clÃ© privÃ©e peuvent les
dÃ©chiffrer, et la chaÃ®ne (`leaf-config-encryption â† System â† Diplo Root CA`)
garantit l'origine des donnÃ©es.

SchÃ©ma conseillÃ© â€” **chiffrement hybride** (RSA 8192 trop lent en donnÃ©es brutes) :

1. **Chiffrement** : gÃ©nÃ©rer une clÃ© de session AES-256 alÃ©atoire, chiffrer le
   fichier avec (AES-256-GCM ou AES-256-CBC + HMAC), puis chiffrer la clÃ© de
   session avec la clÃ© publique RSA du certificat (RSA-OAEP, SHA-256). Ne stocker
   que le fichier chiffrÃ© et la clÃ© enveloppÃ©e.
2. **DÃ©chiffrement** : dÃ©chiffrer la clÃ© de session avec la clÃ© privÃ©e
   (`leaf-config-encryption.pfx` ou le magasin de certificats), puis le fichier.

Exemple minimal avec OpenSSL :

```powershell
# Extraire la clÃ© publique du certificat
openssl x509 -in certificates/leaf-config-encryption/certs/leaf-config-encryption.crt.pem -pubkey -noout `
  -out public.pem

# Chiffrer la clÃ© de session (ici le fichier secret.bin, 64 o) avec RSA-OAEP
openssl pkeyutl -encrypt -pubin -inkey public.pem -in secret.bin -out secret.bin.enc `
  -pkeyopt rsa_padding_mode:oaep -pkeyopt rsa_oaep_md:sha256

# DÃ©chiffrer avec la clÃ© privÃ©e
openssl pkeyutl -decrypt -inkey certificates/leaf-config-encryption/private/leaf-config-encryption.key.pem `
  -in secret.bin.enc -out secret.bin.dec `
  -pkeyopt rsa_padding_mode:oaep -pkeyopt rsa_oaep_md:sha256
```

En .NET, charger le certificat privÃ© depuis le PFX ou le magasin, puis utiliser
`RSA.Encrypt`/`RSA.Decrypt` avec `RSAEncryptionPadding.OaepSHA256`.

> La validation complÃ¨te de l'origine exige que Â« System Â» et Â« Diplo Root CA Â»
> soient installÃ©s dans les magasins de confiance (ou fournis explicitement au
> validateur).

## RÃ©gÃ©nÃ©ration complÃ¨te

1. Effacer le contenu de `certificates/` (ou d'un sous-rÃ©pertoire).
2. Suivre l'ordre ci-dessous, en exÃ©cutant chaque commande depuis le rÃ©pertoire
   de l'autoritÃ© concernÃ©e (le config contient les chemins relatifs) :

```powershell
# 1. Racine : clÃ© + auto-signature
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:8192 -out private/root-ca.key.pem
openssl req -x509 -config openssl.cnf -key private/root-ca.key.pem -days 3650 -out certs/root-ca.crt.pem

# 2. IntermÃ©diaires de feuilles : Authentification, System, CodeSigning
#    (mÃªme procÃ©dure pour chacun, signÃ©s par la racine, extensions v3_intermediate_leaf)
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:8192 -out private/authentification.key.pem
openssl req -new -config openssl.cnf -key private/authentification.key.pem -out csr/authentification.csr.pem
openssl ca -config ../root-ca/openssl.cnf -extensions v3_intermediate_leaf -batch -notext `
  -in csr/authentification.csr.pem -out certs/authentification.crt.pem -days 1825

# 3. Feuilles : les autoritÃ©s Authentification, System et CodeSigning signent
#    leurs propres feuilles avec -extensions leaf_cert, depuis leur rÃ©pertoire.
#    Exemple (feuille d'authentification) :
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:8192 -out private/ma-feuille.key.pem
openssl req -new -config openssl.cnf -key private/ma-feuille.key.pem -out csr/ma-feuille.csr.pem
openssl ca -config ../authentification/openssl.cnf -extensions leaf_cert -batch -notext `
  -in csr/ma-feuille.csr.pem -out certs/ma-feuille.crt.pem -days 825
# (recrÃ©er ensuite les fichiers *.chain.crt.pem)
```

> Les feuilles de signature de code par projet sont gÃ©nÃ©rÃ©es automatiquement par
> `certificates/codesigning/leaves/regenerate-leaves.ps1` (voir la section
> Â« Signature de code : une feuille par projet Â»), elles n'apparaissent donc pas ici.

> Chaque commande s'exÃ©cute depuis le rÃ©pertoire de l'autoritÃ©/feuille concernÃ©e
> (par exemple `certificates/root-ca/` pour la premiÃ¨re). Les commandes `openssl ca`
> mettent Ã  jour la base `db/index.txt` ; celle-ci doit exister (vide au dÃ©part).

