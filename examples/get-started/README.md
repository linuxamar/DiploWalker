# Get Started — Déploiement rapide de Diplo

Ce guide pas-à-pas vous accompagne depuis l'installation jusqu'au premier
conteneur déployé. Un script PowerShell automatisé est également fourni.

Il est utile de garder la répartition en tête dès le départ : **l'autorité est
sur l'hôte**. Containerd et les trois services gRPC y résident, et c'est eux
qui détiennent les conteneurs, les volumes, les réseaux et les images.
**Le poste de travail n'héberge que les clients** — la commande `diplo` et
l'interface graphique — qui s'y connectent et ne détiennent aucun conteneur.
Le même installeur dépose les deux moitiés : les services sur l'hôte, les
clients sur le poste.

## Prérequis

| Composant             | Version minimale | Vérification                  |
| --------------------- | ---------------- | ----------------------------- |
| Windows Server        | 2016+            | `winver`                      |
| .NET Runtime          | 10.0             | `dotnet --version`            |
| Droits administrateur | —                | PowerShell en tant qu'admin   |
| Espace disque         | ~500 Mo          | pour containerd + plugins CNI |

## Étapes manuelles

### Étape 1 — Installer les services Diplo

L'installeur NSIS (`Diplo-Setup-1.0.1-x64.exe`) installe automatiquement :

- **containerd** avec plugins CNI (réseau nat, bridge, overlay)
- **DiploWalker.Container** — service gRPC port 5001 (Debug) / 6001 (Release)
- **DiploWalker.Volume** — service gRPC port 5002 (Debug) / 6002 (Release)
- **DiploWalker.Network** — service gRPC port 5003 (Debug) / 6003 (Release)
- **DiploWalker.CLI** — ajouté au PATH système
- **DiploWalker.GUI** — raccourci bureau et menu Démarrer
- **Certificats PKI** — racine et intermédiaires dans les magasins Windows

```powershell
# Lancer l'installation (en tant qu'administrateur)
.\Diplo-Setup-1.0.1-x64.exe
```

### Étape 2 — Vérifier l'installation

```powershell
# Vérifier la version de Diplo
diplo container version

# Vérifier l'état des services
diplo status check
```

Sortie attendue :

```
=== Statut des services Diplo ===
  DiploWalker.Container     [Running     ] port 5001 (Debug) / 6001 (Release)
  DiploWalker.Volume        [Running     ] port 5002 (Debug) / 6002 (Release)
  DiploWalker.Network       [Running     ] port 5003 (Debug) / 6003 (Release)
```

### Étape 3 — Démarrer les services

Les services sont installés en démarrage automatique. Si besoin :

```powershell
# Démarrer chaque service
sc.exe start "DiploWalker.Container"
sc.exe start "DiploWalker.Volume"
sc.exe start "DiploWalker.Network"
```

### Étape 4 — Télécharger l'image ServerCode

```powershell
# Télécharger l'image depuis le registre
diplo container pull ServerCode

# Vérifier que l'image est disponible
diplo container image-list
```

### Étape 5 — Créer et démarrer un conteneur

```powershell
# Créer un conteneur à partir de l'image
diplo container create ServerCode mon-serveur

# Démarrer le conteneur
diplo container start mon-serveur

# Vérifier qu'il est en cours d'exécution
diplo container list
```

### Étape 6 — Interagir avec le conteneur

```powershell
# Voir les logs
diplo container logs mon-serveur

# Exécuter une commande dans le conteneur
diplo container exec mon-serveur cmd

# Voir les métriques (CPU, mémoire, réseau)
diplo container stats mon-serveur

# Voir les détails complets
diplo container inspect mon-serveur
```

## Script PowerShell automatisé

Le script `get-started.ps1` exécute toutes les étapes ci-dessus en une
seule commande :

```powershell
# Exécuter en tant qu'administrateur
.\get-started.ps1
```

Le script effectue :

1. Vérification des prérequis (administrateur, .NET 10, installeur)
2. Installation des services (si non déjà installés)
3. Démarrage des services Diplo
4. Téléchargement de l'image ServerCode
5. Création et démarrage du conteneur

## Commandes Diplo de la vie courante

| Commande                                    | Description                     |
| ------------------------------------------- | ------------------------------- |
| `diplo container list`                      | Lister tous les conteneurs      |
| `diplo container image-list`                | Lister les images disponibles   |
| `diplo container inspect <nom>`             | Détails complets d'un conteneur |
| `diplo container logs <nom>`                | Logs en temps réel              |
| `diplo container exec <nom> <cmd>`          | Exécuter une commande           |
| `diplo container stop <nom>`                | Arrêter un conteneur            |
| `diplo container delete <nom>`              | Supprimer un conteneur          |
| `diplo container stats <nom>`               | Métriques CPU/mémoire/réseau    |
| `diplo container top <nom>`                 | Processus actifs                |
| `diplo container rename <ancien> <nouveau>` | Renommer un conteneur           |
| `diplo volume list`                         | Lister les volumes              |
| `diplo volume create <nom>`                 | Créer un volume                 |
| `diplo network list`                        | Lister les réseaux              |
| `diplo network create <nom>`                | Créer un réseau                 |
| `diplo status check`                        | Vérifier tous les services      |
| `diplo config init`                         | Générer la config par défaut    |

## Interface graphique

Lancez `DiploWalker.Gui.exe` pour accéder à l'interface graphique Avalonia qui
propose :

- Liste des conteneurs avec état en temps réel
- Détail de chaque conteneur (propriétés, actions, config, logs)
- Gestion des volumes et réseaux
- Éditeur Compose avec colorisation YAML
- Journal d'activité centralisé

## Dépannage

### Problèmes d'installation

#### « L'installation nécessite les droits administrateur »

L'installeur et `DiploWalker.Installer.exe` nécessitent les droits administrateur
pour créer les services Windows et modifier le PATH.

```powershell
# Vérifier si vous êtes administrateur
([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

# Relancer PowerShell en tant qu'administrateur
```

#### « .NET 10 non trouvé »

Diplo nécessite le runtime .NET 10 (pas le SDK, sauf pour compiler).

```powershell
# Vérifier la version installée
dotnet --list-runtimes

# Si absent, télécharger depuis :
# https://dotnet.microsoft.com/download/dotnet/10.0
```

#### « NSIS (makensis) introuvable »

Erreur lors de la compilation de l'installeur (développeurs uniquement).

```powershell
# Installer NSIS depuis https://nsis.sourceforge.io/
# Puis ajouter au PATH :
$env:Path += ";C:\Program Files (x86)\NSIS"
```

---

### Problèmes de démarrage des services

#### Un ou plusieurs services ne démarrent pas

```powershell
# 1. Vérifier l'état de tous les services
diplo status check

# 2. Vérifier les logs application
Get-Content "$env:ProgramFiles\Diplo\logs\diplo-container-*.log" -Tail 50
Get-Content "$env:ProgramFiles\Diplo\logs\diplo-volume-*.log" -Tail 50
Get-Content "$env:ProgramFiles\Diplo\logs\diplo-network-*.log" -Tail 50

# 3. Vérifier les logs Windows Event Log
Get-EventLog -LogName "DiploWalker.Container" -Newest 20 -EntryType Error
Get-EventLog -LogName "DiploWalker.Volume" -Newest 20 -EntryType Error
Get-EventLog -LogName "DiploWalker.Network" -Newest 20 -EntryType Error

# 4. Vérifier que containerd est installé
& "$env:ProgramFiles\Diplo\containerd\containerd.exe" --version

# 5. Redémarrer un service
sc.exe stop "DiploWalker.Container"
Start-Sleep -Seconds 3
sc.exe start "DiploWalker.Container"
```

#### « Le service a échoué au démarrage » (Erreur 1053/1067)

Causes courantes :

- **Port déjà utilisé** : un autre processus écoute sur un port de service (5001-5003 en Debug, 6001-6003 en Release)
- **containerd absent** : le service containerd n'est pas installé ou démarré
- **Fichier de config corrompu** : les fichiers JSON sont invalides

```powershell
# Vérifier les ports utilisés
netstat -ano | findstr "5001 5002 5003 6001 6002 6003"

# Vérifier que containerd fonctionne
& "$env:ProgramFiles\Diplo\containerd\containerd.exe" ctr --address \\.\pipe\diplo-container version

# Régénérer la configuration
diplo config init
```

#### Le service démarre mais s'arrête immédiatement

Les logs détaillés sont dans les fichiers de log quotidiens :

```powershell
# Logs les plus récents
Get-ChildItem "$env:ProgramFiles\Diplo\logs\diplo-container-*.log" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1 -ExpandProperty FullName |
    Get-Content -Tail 100
```

---

### Problèmes de connexion gRPC

#### « Impossible de se connecter au serveur gRPC »

Diplo utilise des **Named Pipes** (par défaut) ou **TCP** pour la communication
inter-services. Le client se connecte automatiquement via le mécanisme configuré.

```powershell
# Vérifier que les services sont en cours d'exécution
diplo status check

# Vérifier les pipes nommées
Get-ChildItem \\.\pipe\ | Where-Object { $_.Name -like "diplo*" }

# Tester la connexion TCP (si UseTcp=true dans appsettings.json)
Test-NetConnection -ComputerName localhost -Port 5001  # 5001 en Debug, 6001 en Release
```

#### « L'adresse gRPC n'est pas autorisée »

La validation de sécurité impose que les connexions gRPC pointent vers
`localhost`, `127.0.0.1`, `::1` ou un named pipe (`http://pipe:/<nom>` : ici `http://`
est une enveloppe et `pipe` est l'hôte qui sélectionne le transport, pas le
schéma — le tube est la partie qui suit `pipe:/`).

```powershell
# Vérifier la configuration de connexion
Get-Content "$env:ProgramFiles\Diplo\DiploWalker.Container\appsettings.json"

# Les adresses autorisées sont :
#   http://localhost:5001 (Debug) | http://localhost:6001 (Release)
#   http://127.0.0.1:5001 (Debug) | http://127.0.0.1:6001 (Release)
#   http://pipe:/diplo-container
#     (enveloppe http://, hote pipe, tube diplo-container)
```

#### « Token d'authentification invalide ou expiré »

Diplo utilise un token d'authentification partagé pour sécuriser les
communications inter-services. Le token expire après 24 heures.

```powershell
# Vérifier le fichier de token
Get-Content "C:\ProgramData\Diplo\auth-token.json"

# Régénérer le token (nécessite un redémarrage des services)
Remove-Item "C:\ProgramData\Diplo\auth-token.json"
sc.exe restart "DiploWalker.Container"
sc.exe restart "DiploWalker.Volume"
sc.exe restart "DiploWalker.Network"
```

#### « Le nom du pipe est invalide »

Le format attendu est `http://pipe:/<nom>` sans caractères spéciaux. Attention :
ne retirez pas le préfixe `http://`, il rend l'adresse valide ; `pipe` est
l'hôte et non le schéma, et le tube est la partie qui suit `pipe:/`.

```powershell
# Configuration correcte dans appsettings.json :
#   "NamedPipeName": "diplo-container"
#   "UseNamedPipes": true
```

---

### Problèmes de conteneurs

#### « L'image est requise » / « Le nom du conteneur est requis »

Vérifiez que vous fournissez tous les arguments obligatoires :

```powershell
# Syntaxe correcte
diplo container pull <IMAGE>
diplo container create <IMAGE> <NOM>
diplo container start <ID_OU_NOM>
```

#### « Conteneur introuvable » / NotFound

```powershell
# Lister tous les conteneurs (y compris arrêtés)
diplo container list

# Utiliser l'ID complet au lieu du nom raccourci
diplo container inspect <ID_COMPLET>
```

#### Timeout en attendant la sortie du conteneur

Le timeout par défaut est de 10 secondes pour l'arrêt. Vous pouvez
l'augmenter :

```powershell
# Arrêter avec un timeout de 30 secondes
diplo container stop <ID> --timeout 30
```

#### « Erreur lors du démarrage avec capture des logs »

Vérifiez les logs de containerd :

```powershell
# Logs de containerd
Get-Content "$env:ProgramFiles\Diplo\containerd\*.log" -Tail 50

# Logs de l'état du conteneur
diplo container inspect <ID>
```

---

### Problèmes de volumes

#### « Volume introuvable »

```powershell
# Lister tous les volumes
diplo volume list

# Détails d'un volume
diplo volume inspect <ID>
```

#### « Erreur lors du montage du volume »

Causes courantes :

- **Chemin source invalide** : le fichier ISO ou le répertoire n'existe pas
- **Permissions insuffisantes** : le service n'a pas accès au chemin
- **Lecteur déjà utilisé** : un autre processus utilise le même point de montage

```powershell
# Vérifier que le chemin source existe
Test-Path "\\serveur\partage\volume.vhdx"

# Vérifier les volumes montés
diplo volume list
```

#### « Aucun driver enregistré pour le type »

Le type de volume n'est pas supporté. Types supportés :

- `Local` — répertoires locaux
- `SMB` — partages réseau SMB/CIFS
- `NFS` — partages NFS
- `ISO` — fichiers ISO (lecture seule)
- `CloudAzure` — Azure Files
- `CloudAws` — Amazon EBS
- `CloudGcp` — Google Persistent Disk

---

### Problèmes de réseaux

#### « Réseau introuvable »

```powershell
# Lister tous les réseaux
diplo network list

# Détails d'un réseau
diplo network inspect <ID>
```

#### « Erreur lors de l'exécution du plugin CNI »

Le plugin CNI nat n'est pas installé ou mal configuré :

```powershell
# Vérifier les plugins CNI
Get-ChildItem "$env:ProgramFiles\Diplo\containerd\cni\bin\*.exe"

# Vérifier la configuration CNI
Get-Content "$env:ProgramFiles\Diplo\containerd\cni\conf\0-containerd-nat.conf"
```

#### Erreur de connexion/disconnexion réseau

```powershell
# Déconnecter proprement un conteneur d'un réseau
diplo network disconnect <RESEAU_ID> <CONTENEUR_ID>

# Forcer la suppression d'un réseau
diplo network remove <RESEAU_ID>
```

---

### Problèmes de disques image

#### Format de disque non supporté

Diplo supporte les formats suivants :

- **Lecture/écriture** : VHD, VHDX, VMDK, VDI, QCOW2, QCOW1, Parallels, Raw
- **Lecture seule** : ISO, DMG (Apple)

```powershell
# Créer une image à partir d'un répertoire source
# (source et destination sont positionnels ; le format par défaut est raw)
diplo disk create-image <RÉPERTOIRE_SOURCE> <CHEMIN_DESTINATION> --format vhdx
```

#### « Échec détection format Hawkynt »

Le format du fichier disque n'est pas reconnu. Vérifiez l'extension et
le contenu du fichier :

```powershell
# Vérifier l'en-tête du fichier
Format-Hex -Path <CHEMIN> -Count 64
```

---

### Problèmes de l'interface graphique

#### La fenêtre ne s'affiche pas

```powershell
# Lancer en mode console pour voir les erreurs
& "$env:ProgramFiles\Diplo\DiploWalker.Gui\DiploWalker.Gui.exe" 2>&1
```

#### Les onglets sont vides

Les services Diplo doivent être démarrés pour que la GUI fonctionne :

```powershell
diplo status check
```

#### Erreur « Assembly FSharp.Core non trouvé »

Vérifiez que le runtime .NET 10 est installé et que le PATH est correct.

---

### Problèmes de désinstallation

```powershell
# Désinstaller via l'installeur
& "$env:ProgramFiles\Diplo\uninst.exe"

# Ou via Windows Settings > Applications > Diplo

# Supprimer manuellement les certificats PKI
& "$env:ProgramFiles\Diplo\certificates\manage-certificates.ps1" -Remove

# Supprimer le token d'authentification
Remove-Item "C:\ProgramData\Diplo\auth-token.json" -ErrorAction SilentlyContinue

# Supprimer les logs
Remove-Item "$env:ProgramFiles\Diplo\logs" -Recurse -ErrorAction SilentlyContinue
```

---

### Collecte d'informations pour le support

Si vous rencontrez un problème persistant, collectez ces informations :

```powershell
# 1. Version de Diplo
diplo container version

# 2. État des services
diplo status check

# 3. Dernières lignes de chaque log
Get-ChildItem "$env:ProgramFiles\Diplo\logs\diplo-*.log" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 3 |
    ForEach-Object {
        Write-Host "`n=== $($_.Name) ===" -ForegroundColor Cyan
        Get-Content $_.FullName -Tail 30
    }

# 4. Configuration
Get-Content "$env:ProgramFiles\Diplo\DiploWalker.Container\appsettings.json"

# 5. Informations système
[System.Environment]::OSVersion.Version
dotnet --list-runtimes
```

Envoyez ces informations avec une description détaillée du problème.

