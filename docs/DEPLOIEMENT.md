# Déploiement sur le serveur Linux (Raspberry Pi ARM64)

## Ce qui tourne

| Unité systemd | Rôle | Port (local) |
|---|---|---|
| `deekeep@Gateway` | GraphQL, application web, authentification, relais des flux | 5000 |
| `deekeep@Catalog` | Deezer, MusicBrainz, AcoustID, durées de référence, images | 5101 |
| `deekeep@Library` | Titres, réglages, index de présence, rangement, tags, flux audio | 5102 |
| `deekeep@Downloader` | yt-dlp, conversions, retéléchargements | 5103 |
| `deekeep@Quality` | Analyse ffmpeg des défauts | 5104 |
| `deekeep@Storage` | Archive sans perte | 5105 |
| `deekeep.target` | Démarre / arrête l'ensemble | |
| `deekeep-health.timer` | Relance un service qui ne répond plus (3 échecs de suite) | |
| `deekeep-backup.timer` | Sauvegarde quotidienne des bases (04:30, 7 jours) | |
| `caddy` | HTTPS, compression, WebSocket | 443 |

Seul Caddy écoute sur le réseau ; les services n'écoutent que sur
`127.0.0.1` et exigent la clé interne générée à l'installation.

Empreinte mémoire visée : 50 à 90 Mo par service, soit 400 à 500 Mo au total
(Raspberry Pi 4 ou 5 avec 2 Go conseillés ; 1 Go possible en réduisant la
concurrence des tâches).

## Construire le paquet (sur le poste Windows)

```powershell
./deploy/publish.ps1
```

Produit `deploy/out/deekeep-<version>-linux-arm64.tar.gz` (client Angular
compilé, six services publiés en ReadyToRun pour ARM64, unités systemd,
scripts, modèle de configuration). `-Runtime linux-x64` pour un PC.

## Installer (sur le serveur)

```bash
scp deploy/out/deekeep-*.tar.gz pi@serveur:
ssh pi@serveur
tar xzf deekeep-*.tar.gz && cd deekeep-*/
sudo ./install.sh
```

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
2. **Musique** : copier la bibliothèque sur le serveur en préservant les dates
   (`rsync -a --times`), par exemple dans `/srv/musique`, propriétaire
   `deekeep`.
3. **Correspondance des chemins** — `/etc/deekeep/appsettings.json`,
   section `PathMap` : la base stocke les chemins Windows (`D:\Artiste\…`) ;
   `{ "From": "D:\\", "To": "/srv/musique/" }` les fait correspondre sans
   réécrire la base (l'application desktop peut toujours la rouvrir).
   Ajuster aussi `Library:BrowseRoots` (dossiers que l'interface peut parcourir)
   et `Storage:ArchiveRoot` (disque de l'archive, idéalement un autre disque).
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
   accès demande de choisir le mot de passe.
8. Facultatif : clé AcoustID dans `/etc/deekeep/secrets.json`
   (`Secrets:AcoustIdKey`), fichier `cookies.txt` YouTube (Paramètres) si
   YouTube bloque l'adresse du serveur.

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
