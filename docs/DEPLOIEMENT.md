# Déploiement sur le serveur Linux (Raspberry Pi ARM64)

## Ce qui tourne

| Unité systemd | Rôle | Port (local) |
|---|---|---|
| `deekeep@Gateway` | API JSON (`/api`), application web, authentification, relais des flux | 5000 |
| `deekeep@Catalog` | Deezer, MusicBrainz, AcoustID, durées de référence, images | 5101 |
| `deekeep@Library` | Titres, réglages, index de présence, rangement, tags, flux audio | 5102 |
| `deekeep@Downloader` | yt-dlp, conversions, retéléchargements | 5103 |
| `deekeep@Quality` | Analyse ffmpeg des défauts | 5104 |
| `deekeep@Storage` | Archive sans perte | 5105 |
| `deekeep.target` | Démarre / arrête l'ensemble | |
| `deekeep-health.timer` | Relance un service qui ne répond plus (3 échecs de suite) | |
| `deekeep-backup.timer` | Sauvegarde quotidienne des bases (04:30, 7 jours) | |
| `caddy` | HTTPS, compression | 443 |

Seul Caddy écoute sur le réseau ; les services n'écoutent que sur
`127.0.0.1` et exigent la clé interne générée à l'installation.

Empreinte mémoire visée : 50 à 90 Mo par service, soit 400 à 500 Mo au total
(Raspberry Pi 4 ou 5 avec 2 Go conseillés ; 1 Go possible en réduisant la
concurrence des tâches).

Téléchargements simultanés : adaptés au serveur. Le plafond vient de la machine
(un par cœur, 350 Mo de mémoire comptés par extraction après 768 Mo réservés,
6 au plus) ; toutes les 10 s, la limite perd une place si le processeur dépasse
90 % ou si moins de 12 % de la mémoire reste libre, et en gagne une si des
extractions attendent, que le processeur reste sous 70 % et la mémoire libre
au-dessus de 25 %. Réglages (section `Downloader`) : `MaxParallel` (plafond
imposé, 0 = automatique), `MinParallel` (plancher, 1), `AdaptiveParallel`
(`false` = toujours au plafond). La limite courante et la charge mesurée
s'affichent dans la page Diagnostic (« extractions simultanées »).

## Construire le paquet (sur le poste Windows)

```powershell
./deploy/publish.ps1
```

Produit `deploy/out/deekeep-<version>-linux-arm64.tar.gz` (client React
compilé, six services publiés en ReadyToRun pour ARM64, unités systemd,
scripts, modèle de configuration). `-Runtime linux-x64` pour un PC.

## Installer (sur le serveur)

```bash
scp deploy/out/deekeep-*.tar.gz pi@serveur:
ssh pi@serveur
tar xzf deekeep-*.tar.gz && cd deekeep-*/
sudo sh ./install.sh
```

`sh` explicite : le paquet est construit sous Windows, dont le `tar` ne
conserve pas le bit d'exécution des scripts.

Le script installe ffmpeg, fpcalc (Chromaprint), sqlite3, Caddy, le runtime
ASP.NET Core 10, yt-dlp et Deno, crée l'utilisateur `deekeep`, les dossiers
et les services. Relancé, il met à jour l'application sans toucher à la
configuration ni aux bases.

## Première configuration

1. **Base existante** : copier `deekeep_cache.db` de Windows
   (`%APPDATA%\DeeKeep`) vers `/var/lib/deekeep/` — de préférence une
   sauvegarde SQLite plutôt qu'une copie à chaud :
   `sqlite3 deekeep_cache.db ".backup deekeep_cache.copy.db"`.
   `sudo chown deekeep:deekeep /var/lib/deekeep/deekeep_cache.db`.
   **Base sur le NAS** (partage SMB, ex. `\\DiskStation\web\Database`) : y
   copier la base (sauvegarde SQLite), créer le fichier d'identifiants du NAS
   (jamais versionné) puis lancer le script, qui monte le partage, y vérifie
   le bon fonctionnement de SQLite en WAL, y fait pointer `Database:MainPath`
   et `Database:WebPath`, et interdit aux services de démarrer sans lui :
   ```bash
   sudo sh -c 'printf "username=UTILISATEUR\npassword=MOT_DE_PASSE\n" > /etc/deekeep/smb-nas'
   sudo sh /opt/deekeep/bin/deekeep-nas-setup.sh //192.168.1.159/web/Database
   sudo systemctl restart deekeep.target
   ```
   Les verrous SQLite ne valent que pour le serveur : **l'application desktop
   ne doit pas ouvrir cette base** pendant que le serveur tourne. La
   sauvegarde quotidienne garde une copie locale dans `/var/lib/deekeep/backups`.
   **Musique sur le NAS et disque USB synchronisé** : la bibliothèque vit sur
   un partage du NAS ; un disque USB branché sur le serveur en est une copie
   tenue à jour dans les deux sens (au branchement, chaque nuit à
   `Sync:NightlyAt`, ou par **Paramètres → Disque USB → Synchroniser**) :
   ```bash
   lsblk -o NAME,UUID,FSTYPE,SIZE          # UUID de la partition du disque
   sudo sh /opt/deekeep/bin/deekeep-usb-setup.sh //192.168.1.159/music 0F6A-107E
   sudo systemctl restart deekeep.target
   ```
   Règles : un fichier présent d'un seul côté est copié de l'autre (même s'il a
   été effacé à la main : la synchronisation est incrémentale) ; deux versions
   différentes → la plus récente l'emporte, l'autre va à la corbeille. Seule une
   **suppression faite dans l'application** (Bibliothèque → Supprimer) se
   propage : le fichier part dans la corbeille `.deekeep-trash` des deux côtés,
   récupérable (Bibliothèque → Corbeille) pendant la durée réglée dans
   **Paramètres → Corbeille** (30 jours par défaut ; 0 = jamais d'effacement
   automatique), puis effacé. Sont
   synchronisés la musique et les jaquettes (`Sync:Extensions`), hors dossiers
   cachés, corbeilles et `Sync:Exclude`. Un disque FAT32 refuse les fichiers
   de plus de 4 Go (signalés, non bloquants).
2. **Musique** : copier la bibliothèque sur le serveur en préservant les dates
   (`rsync -a --times`), par exemple dans `/srv/musique`, propriétaire
   `deekeep`.
3. **Correspondance des chemins** — `/etc/deekeep/appsettings.json`,
   section `PathMap` : la base stocke les chemins Windows (`D:\Artiste\…`) ;
   `{ "From": "D:\\", "To": "/srv/musique/" }` les fait correspondre sans
   réécrire la base (l'application desktop peut toujours la rouvrir).
   Ajuster aussi `Library:BrowseRoots` (dossiers que l'interface peut parcourir,
   par défaut `/srv/musique`, `/mnt` et `/media`) et `Storage:ArchiveRoot`
   (disque de l'archive, idéalement un autre disque).
   **Musique sur un autre appareil** (le PC qui affiche le site, le NAS) : le
   serveur ne peut écrire que sur ses propres disques. Partager le dossier en
   SMB depuis l'appareil, puis le monter sur le serveur sous `/mnt`, par
   exemple dans `/etc/fstab` :
   `//PC-SALON/Musique /mnt/pc-salon cifs credentials=/etc/deekeep/smb-pc,uid=deekeep,gid=deekeep,iocharset=utf8,_netdev,nofail 0 0`
   (paquet `cifs-utils`). Le dossier apparaît alors dans **Paramètres →
   Modifier le dossier**. Sur un poste de développement Windows,
   `Library:BrowseAnywhere: true` ouvre tous les lecteurs (et les chemins UNC
   saisis à la main).
4. **Archive** existante : copier `deekeep-archive.db` dans `ArchiveRoot`.
5. **Données de l'application desktop** (historique, mesures de qualité,
   durées de référence plus récentes que la base) : copier
   `deekeep_config.json`, `quality_cache.json`, `reference_cache.json` dans
   `/var/lib/deekeep/import`, puis **Paramètres → Importer depuis
   l'application desktop**.
6. **HTTPS** — `/etc/caddy/Caddyfile` : nom de domaine public (certificat
   automatique) ou réseau local avec `tls internal` (installer le certificat
   racine de Caddy sur les appareils). HTTPS est indispensable à
   l'installation de l'application sur le téléphone et au mode hors ligne.
7. `sudo systemctl restart deekeep.target caddy`, ouvrir le site : le premier
   accès crée le compte **administrateur** (nom facultatif, `deekeep` par
   défaut, et mot de passe).
8. Facultatif : clé AcoustID dans `/etc/deekeep/secrets.json`
   (`Secrets:AcoustIdKey`), fichier `cookies.txt` YouTube (Paramètres) si
   YouTube bloque l'adresse du serveur.

## Comptes et droits

- Les comptes vivent dans la table `users` de `deekeep_web.db`. Au premier
  démarrage de cette version, la table est complétée (rôle, pages, droits,
  désactivation, identifiant) ; le compte existant devient administrateur avec
  tous les droits et sa session reste ouverte.
- Page **Administration** (administrateurs seulement) : créer un compte (nom,
  mot de passe de 8 caractères au moins), choisir son rôle, les pages qu'il
  voit (Musique, Bibliothèque, Réencoder, Organiser, Qualité, Diagnostic,
  Historique, Paramètres) et ses droits (**Télécharger**, **Supprimer**),
  le désactiver, changer son mot de passe, le supprimer.
- Un administrateur voit tout. L'application refuse de supprimer, rétrograder
  ou désactiver le dernier administrateur actif, et de supprimer son propre compte.
- Les droits sont vérifiés par la passerelle à chaque requête (table
  `Api/Permissions.cs`) : une modification s'applique en quelques secondes,
  un compte désactivé ou supprimé est déconnecté aussitôt.
- Connexion : le champ « Nom d'utilisateur » laissé vide vaut `deekeep`
  (installations antérieures). Un compte sans la page Paramètres se
  déconnecte depuis la barre latérale ; son mot de passe se change par un
  administrateur.
- Mot de passe administrateur perdu : arrêter la passerelle et, avec
  `sqlite3`, rendre administrateur un autre compte connu
  (`UPDATE users SET role='admin', disabled=0 WHERE name='…'`), ou supprimer
  tous les comptes (`DELETE FROM users`) pour revenir à l'écran de premier accès.

## Bibliothèques personnelles

- Chaque utilisateur voit **ses** titres dans la Bibliothèque ; un fichier
  n'existe qu'une fois sur le disque, partagé par tous ceux qui l'ont
  (table `library_user_tracks` de `deekeep_web.db`, service Library).
- **Extraire** un titre déjà présent (pastille ✓, même titre et même
  artiste) ne le retélécharge pas : il rejoint les titres de l'utilisateur.
  Un titre déjà en cours d'extraction pour quelqu'un d'autre (même piste
  Deezer) lui est associé à l'arrivée du fichier.
- **Supprimer** un titre ne fait que le retirer de la bibliothèque de
  l'utilisateur tant que d'autres l'ont ; le dernier qui le supprime l'envoie
  à la corbeille (et au disque USB). Un administrateur ne peut pas supprimer
  un titre qu'il n'a pas et que d'autres utilisent : il le retire d'abord de
  leurs bibliothèques (menu du titre → « Utilisateurs de ce titre… »).
- Les associations suivent le fichier quand il est rangé, renommé, converti
  ou retéléchargé ; un doublon supprimé par Organiser reporte ses
  utilisateurs sur la copie gardée.
- Les titres sans propriétaire (déjà présents avant cette version, ajoutés à
  la main ou arrivés par le disque USB) sont rattachés au premier
  administrateur (au démarrage puis toutes les 10 minutes).
- L'administrateur voit toute la bibliothèque, filtrable par utilisateur
  (« Titres de : » ; lien « Ses titres » de la page Administration), et les
  utilisateurs de chaque titre (infobulle du titre, menu du titre).

## Exploitation

```bash
systemctl list-units 'deekeep*'          # état
journalctl -u 'deekeep@*' -f             # journaux en direct
journalctl -t deekeep-health -t deekeep-backup
sudo systemctl restart deekeep@Downloader
ls /var/lib/deekeep/backups               # sauvegardes
```

La page **Diagnostic** de l'application montre l'état de chaque service,
leur mémoire, les tâches, la boîte d'envoi inter-services et les versions des
outils, et permet de mettre yt-dlp à jour (YouTube change souvent).

## Résilience : ce qui se passe quand…

| Situation | Comportement |
|---|---|
| Un service plante | systemd le relance en 3 s ; ses tâches `Running` repartent en file au démarrage |
| Un service ne répond plus | `deekeep-health` le relance après 3 contrôles en échec |
| Coupure de courant | Tâches reprises au redémarrage ; écritures SQLite en WAL, transactions courtes ; sauvegarde quotidienne vérifiée (`quick_check`) |
| Un service est arrêté | La passerelle répond pour le reste (erreur partielle) ; les appels qui doivent aboutir attendent dans la boîte d'envoi et partent à son retour |
| Deezer / MusicBrainz / YouTube indisponibles | Réessais exponentiels, disjoncteur, limiteurs de débit ; tâches remises en file plus tard |
| Le téléphone perd le réseau | L'application s'ouvre sur son cache, les actions sont mises en file et rejouées (sans doublon) à la reconnexion, les titres épinglés restent écoutables |
| Le navigateur est fermé pendant une extraction | Rien ne s'arrête : les tâches vivent sur le serveur ; à la réouverture la page montre leur état |
