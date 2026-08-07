# OwnLinuxBase

Configuration de boot d'**Ubuntu Server** (sans interface graphique) via l'émulateur **Diplo**.

## But

Fournir une base Linux minimaliste et réutilisable : une image ISO Ubuntu Server officielle, bootée par
l'émulateur Diplo, avec les répertoires système critiques montés en **lecture/écriture** afin que le
système reste fonctionnel (journalisation, paquets, comptes utilisateurs, etc.).

## Prérequis

- Windows 10/11
- SDK .NET 10 (pour la compilation de l'émulateur)
- Connexion Internet (téléchargement de l'ISO ~3,2 Go)

## Installation

1. Télécharger l'image ISO officielle Ubuntu Server :

   ```powershell
   .\Download-UbuntuServer.ps1
   ```

   Le script résout automatiquement le nom exact de l'image courante (via `SHA256SUMS`,
   sans dépendre de la version), la télécharge dans le dossier `iso/` et en vérifie
   l'empreinte SHA256. Le nom résolu est mémorisé dans `iso/iso.json`.

2. Lancer la configuration :

   ```powershell
   .\Launch-OwnLinuxBase.ps1
   ```

   Le script publie automatiquement `diplo-linux.exe` (projet `src/Diplo.Linux.Cli`) si le binaire
   n'est pas encore présent, puis boote l'ISO.

## Options du lanceur

| Option           | Description                                                        |
| ---------------- | ------------------------------------------------------------------ |
| `-Config <fichier>` | Fichier de configuration JSON (défaut : `OwnLinuxBase.json`).    |
| `-Trace`         | Active la trace des appels système (`-t`).                          |
| `-Step`          | Exécution pas à pas (`-s`).                                         |
| `-Breakpoints <adresses>` | Points d'arrêt hexadécimaux, séparés par des virgules (`-b`). |

Exemple :

```powershell
.\Launch-OwnLinuxBase.ps1 -Trace -Breakpoints 0x401000,0x402000
```

## Répertoires en lecture/écriture

La configuration `OwnLinuxBase.json` déclare les répertoires qui doivent être montés en
lecture/écriture via un **overlay tmpfs** :

`/`, `/etc`, `/var`, `/home`, `/tmp`, `/root`

Ces montages permettent au système d'écrire ses journaux, de gérer les paquets et les comptes
utilisateurs, sans modifier l'image de base.

## Configuration

`OwnLinuxBase.json` centralise :

- la série Ubuntu et l'URL de base du répertoire de téléchargement
  (`https://releases.ubuntu.com/24.04/`) ainsi que le motif du nom de fichier
  (`live-server-amd64.iso`) — le nom exact est résolu dynamiquement ;
- le chemin du noyau dans l'image (`/casper/vmlinuz`) et ses arguments ;
- le mode du système de fichiers (`overlay`) et les répertoires rw ;
- la commande de boot de l'émulateur (`diplo-linux boot`).

## Limites actuelles de l'émulateur

- Diplo exécute des **ELF bruts** via `ElfLoader` : il ne sait pas encore charger un noyau
  compressé de type **bzImage** comme `vmlinuz` Ubuntu.
- Le boot d'un vrai Ubuntu nécessitera ultérieurement le support de **GRUB**, des périphériques
  **PCI**, de l'**ACPI** et des **timers**, actuellement non implémentés dans Diplo.
- Aujourd'hui, l'émulateur boote des noyaux ELF bruts compilés pour lui. Le présent dossier prépare
  l'infrastructure (configuration, téléchargement, lanceur) pour basculer sur Ubuntu Server dès que
  ces briques seront disponibles.
