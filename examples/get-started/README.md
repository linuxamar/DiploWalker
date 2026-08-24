# Get Started — Déploiement rapide de Diplo

Ce guide pas-à-pas vous accompagne depuis l'installation jusqu'au premier
conteneur déployé. Un script PowerShell automatisé est également fourni.

## Prérequis

| Composant | Version minimale | Vérification |
|---|---|---|
| Windows Server | 2016+ | `winver` |
| .NET Runtime | 10.0 | `dotnet --version` |
| Droits administrateur | — | PowerShell en tant qu'admin |
| Espace disque | ~500 Mo | pour containerd + plugins CNI |

## Étapes manuelles

### Étape 1 — Installer les services Diplo

L'installeur NSIS (`Diplo-Setup-1.0.0-x64.exe`) installe automatiquement :

- **containerd** avec plugins CNI (réseau nat, bridge, overlay)
- **Diplo.Container** — service gRPC port 5001
- **Diplo.Volume** — service gRPC port 5002
- **Diplo.Network** — service gRPC port 5003
- **Diplo.CLI** — ajouté au PATH système
- **Diplo.GUI** — raccourci bureau et menu Démarrer
- **Certificats PKI** — racine et intermédiaires dans les magasins Windows

```powershell
# Lancer l'installation (en tant qu'administrateur)
.\Diplo-Setup-1.0.0-x64.exe
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
  Diplo.Container     [Running     ] port 5001
  Diplo.Volume        [Running     ] port 5002
  Diplo.Network       [Running     ] port 5003
```

### Étape 3 — Démarrer les services

Les services sont installés en démarrage automatique. Si besoin :

```powershell
# Démarrer chaque service
sc.exe start "Diplo.Container"
sc.exe start "Diplo.Volume"
sc.exe start "Diplo.Network"
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

| Commande | Description |
|---|---|
| `diplo container list` | Lister tous les conteneurs |
| `diplo container image-list` | Lister les images disponibles |
| `diplo container inspect <nom>` | Détails complets d'un conteneur |
| `diplo container logs <nom>` | Logs en temps réel |
| `diplo container exec <nom> <cmd>` | Exécuter une commande |
| `diplo container stop <nom>` | Arrêter un conteneur |
| `diplo container delete <nom>` | Supprimer un conteneur |
| `diplo container stats <nom>` | Métriques CPU/mémoire/réseau |
| `diplo container top <nom>` | Processus actifs |
| `diplo container rename <ancien> <nouveau>` | Renommer un conteneur |
| `diplo volume list` | Lister les volumes |
| `diplo volume create <nom>` | Créer un volume |
| `diplo network list` | Lister les réseaux |
| `diplo network create <nom>` | Créer un réseau |
| `diplo status check` | Vérifier tous les services |
| `diplo config init` | Générer la config par défaut |

## Interface graphique

Lancez `Diplo.Gui.exe` pour accéder à l'interface graphique Avalonia qui
propose :

- Liste des conteneurs avec état en temps réel
- Détail de chaque conteneur (propriétés, actions, config, logs)
- Gestion des volumes et réseaux
- Éditeur Compose avec colorisation YAML
- Journal d'activité centralisé

## Dépannage

### Les services ne démarrent pas

```powershell
# Vérifier les logs
Get-EventLog -LogName "Diplo.Container" -Newest 10
# ou
Get-Content "$env:ProgramFiles\Diplo\logs\diplo-container-*.log" -Tail 20
```

### Erreur de connexion gRPC

Les services communiquent via des Named Pipes (par défaut) ou TCP.
Vérifiez que les services sont démarrés :

```powershell
diplo status check
```

### Désinstallation

```powershell
# Via Windows Settings > Applications > Diplo
# ou
Diplo-Setup-1.0.0-x64.exe  # le désinstalleur est intégré
```
