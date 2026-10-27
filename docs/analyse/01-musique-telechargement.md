# 01 — Musique, recherche Deezer, téléchargement YouTube, fenêtre principale

> Spécification fonctionnelle et technique issue de l'analyse du code Python/PyQt6 de **DeeKeep**
> (`D:\000_Logiciel\DeezerMusic`, paquet `deekeep/`), en vue de la réécriture web
> (Angular + GraphQL + microservices .NET, serveur Linux, base SQLite conservée).
>
> **Périmètre de ce document**
>
> | Fichier | Lignes | Contenu |
> |---|---|---|
> | `deekeep/ui/main_window.py` | 3 471 | Fenêtre principale, barre latérale, pages Musique, Historique, Diagnostic, Paramètres, Réencoder ; zone de notification |
> | `deekeep/ui/add_track_dialog.py` | 1 263 | Fenêtre « Rechercher et ajouter des pistes », pastilles de présence |
> | `deekeep/workers/search_thread.py` | 320 | API Deezer (recherche, liens, correspondance) |
> | `deekeep/workers/download_thread.py` | 643 | yt-dlp, choix de la vidéo, retries, ffmpeg, tags, jaquette |
> | `deekeep/workers/image_thread.py` | 108 | Téléchargement / recherche des jaquettes |
> | `deekeep/ui/styles.py` | 584 | Identité visuelle « Neon Nocturne » |
>
> Les modules appelés depuis ce périmètre et indispensables à la compréhension
> (`tag_lookup`, `library_index`, `library_rules`, `name_pattern`, `tag_write`, `utils`,
> `settings`, `reencode_scan`, `player`) sont résumés là où ils interviennent ; leurs
> écrans propres (Bibliothèque, Organiser, Qualité, lecteur, base de données) relèvent
> d'autres documents d'analyse.
>
> Convention : `fichier:ligne` renvoie au code Python d'origine. Les textes entre « »
> sont les libellés exacts affichés à l'utilisateur (à reprendre tels quels, en français).

---

## Sommaire

1. [Inventaire des écrans et fonctions](#1-inventaire-des-écrans-et-fonctions)
2. [Détail par fonction](#2-détail-par-fonction)
   - 2.1 Fenêtre principale et cycle de vie
   - 2.2 Barre latérale
   - 2.3 Page Musique (file de téléchargement)
   - 2.4 Fenêtre « Rechercher et ajouter des pistes »
   - 2.5 Page Historique (recherches + téléchargements réussis)
   - 2.6 Page Réencoder (conversion de fichiers locaux)
   - 2.7 Page Paramètres
   - 2.8 Page Diagnostic
   - 2.9 Zone de notification (tray)
3. [Algorithmes à porter](#3-algorithmes-à-porter)
4. [Intégrations externes](#4-intégrations-externes)
5. [Données](#5-données)
6. [Points d'attention pour la version web / serveur Linux](#6-points-dattention-pour-la-version-web--serveur-linux)
7. [Annexe A — Identité visuelle (styles.py)](#annexe-a--identité-visuelle-stylespy)
8. [Annexe B — Catalogue des libellés et messages](#annexe-b--catalogue-des-libellés-et-messages)

---

## 1. Inventaire des écrans et fonctions

### 1.1 Fenêtre principale (`DeeKeepApp`, `main_window.py:70`)

- Titre de fenêtre : « DeeKeep - Music Downloader » ; taille initiale 1280×800, ouverte **maximisée** (`main.py:38`).
- Disposition : barre latérale fixe à gauche (250 px) + zone de contenu à pages empilées (`QStackedWidget`).
- **8 pages** (index de pile `main_window.py:1283-1541`) :

| Index | Page | Accès barre latérale | Classe / construction | Couvert ici |
|---|---|---|---|---|
| 0 | **Musique** (file de téléchargement) | « Musique » | `init_ui` `main_window.py:749-878` | Oui |
| 1 | **Historique** (2 onglets) | « Historique » + badge | `main_window.py:881-987` | Oui |
| 2 | **Diagnostic** | Outils → « Diagnostic » | `main_window.py:990-1010` | Oui |
| 3 | **Paramètres** | « Paramètres » | `main_window.py:1013-1176` | Oui (hors carte Base de données) |
| 4 | **Réencoder** | Outils → « Réencoder » | `_build_reencoder_page` `main_window.py:1347` | Oui |
| 5 | **Organiser** | Outils → « Organiser » | `OrganizePage` (`organize_page.py`) | Non (autre doc) |
| 6 | **Qualité** | Outils → « Qualité » | `QualityPage` (`quality_page.py`) | Non (autre doc) |
| 7 | **Bibliothèque** | « Bibliothèque » | `LibraryPage` (`library_page.py`) | Non (autre doc) |

### 1.2 Fenêtres secondaires (dialogues) du périmètre

| Dialogue | Ouvert depuis | Rôle |
|---|---|---|
| « Rechercher et ajouter des pistes » (`AddTrackDialog`) | Page Musique → « Rechercher » ; Historique → « Relancer » | Recherche Deezer, navigation artiste/album, sélection, pastilles de présence, écoute d'extraits, déduplication |
| Panneau flottant « Dernières recherches » | Focus/clic sur le champ de recherche du dialogue | Historique des 8 dernières recherches avec vignette |
| Agrandissement de jaquette (popup) | Clic sur une vignette de l'onglet « Téléchargements réussis » | Jaquette 320×320, titre/artiste, « Ouvrir le dossier » |
| Écran des doublons (`dedup_dialog.LibraryDuplicateDialog`) | Dialogue de recherche → « Dédoublonner… » | Hors périmètre (décrit ailleurs) ; seul son résultat (liste de fichiers à supprimer) est traité ici |
| Boîtes de message (information / avertissement / question) | Multiples | Voir annexe B |
| Sélecteurs de fichiers / dossiers natifs | Réencoder, Paramètres | À remplacer côté web (voir §6) |

### 1.3 Processus de fond (« workers ») du périmètre

| Worker | Fichier | Rôle |
|---|---|---|
| `DeezerSearchThread` | `search_thread.py:29` | Une recherche Deezer (pistes ou entités) |
| `DeezerMatchThread` | `search_thread.py:284` | Correspondance Deezer pour une liste de fichiers locaux (Réencoder) |
| `deezer_best_match()` | `search_thread.py:233` | Fonction synchrone de correspondance (artiste, titre) → piste Deezer |
| `TrackDownloaderThread` | `download_thread.py:133` | Un téléchargement YouTube complet (recherche vidéo, extraction, conversion, tags, rangement), avec retries |
| `CoverDownloadThread` | `image_thread.py:66` | Téléchargement séquentiel d'une liste d'images (vignettes) |
| `CoverLookupThread` | `image_thread.py:18` | Recherche d'une jaquette Deezer par (artiste, titre) puis téléchargement |
| `LibraryIndexThread` | `library_index.py:593` | Construction de l'index « ce titre est-il déjà sur le disque ? » |
| `LocalScanThread` | `reencode_scan.py:319` | Parcours + lecture des tags des fichiers ajoutés à Réencoder (8 lectures en parallèle) |

### 1.4 Liste des fonctions utilisateur (vue d'ensemble)

**Page Musique** : rechercher et ajouter des pistes ; extraire toute la liste ; réessayer les échecs ; annuler un téléchargement ; purger les téléchargés ; retirer les lignes cochées ; tout cocher / décocher ; trier par colonne ; écouter l'extrait 30 s ou le fichier extrait ; ouvrir le lecteur complet ; ouvrir le dossier ; voir la pastille de présence disque ; suivre la progression par ligne et globale ; compteurs de session.

**Dialogue de recherche** : recherche par mot-clé (Tout/Titre, Artiste, Album, Playlist) ; recherche par lien Deezer ; historique des recherches (popup, suppression unitaire, effacement) ; navigation entités → pistes avec fil d'Ariane et retour ; en-tête d'album ; liens cliquables artiste/album ; filtre texte instantané ; tri par colonne ; jauge de popularité ; écoute d'extrait ; pastilles de présence (✓ ≈ ○ ⏳) et verrouillage des cases ; « Voir les doublons (n) » ; « Dédoublonner… » ; « Tout sélectionner » (hors doublons) ; « Ajouter la sélection (n) » ; annuler une recherche en cours.

**Historique** : onglet « Recherches effectuées » (relancer, tri) ; onglet « Téléchargements réussis » (jaquette agrandissable, écoute, ouverture du dossier, tri, purge des fichiers introuvables, vidage) ; réparation automatique des chemins après déplacement de la bibliothèque.

**Réencoder** : ajouter des fichiers / un dossier (récursif) ; correspondance Deezer automatique (≤ 150 fichiers) ; jaquettes ; filtre « Masquer ceux déjà au format cible » ; tri ; sélection ; conversion en lot (10 en parallèle) avec remplacement sur place ; purge des convertis ; retrait ; écoute ; ouverture du dossier.

**Paramètres** : format, qualité, navigateur pour cookies YouTube, fichier cookies.txt, sous-dossiers artiste/album, regroupement « Divers » et seuil, clé AcoustID, dossier de sortie ; (carte Base de données hors périmètre).

**Diagnostic** : état de yt-dlp, mutagen, FFmpeg ; dossier d'enregistrement.

**Tray** : réduction en zone de notification, restauration, quitter.

---

## 2. Détail par fonction

### 2.1 Fenêtre principale et cycle de vie

#### Démarrage (`main.py:16-39`, `main_window.py:77-153`)

Ordre exact :
1. `ensure_deno_on_path()` : préfixe le PATH du processus avec le dossier `deno/` embarqué (runtime JavaScript exigé par yt-dlp pour YouTube) (`utils.py:74-87`).
2. Windows uniquement : `SetCurrentProcessExplicitAppUserModelID("DeeKeep.MusicDownloader")`.
3. `QApplication` style « Fusion », icône applicative, `setQuitOnLastWindowClosed(False)`.
4. Constructeur de `DeeKeepApp` :
   - charge la configuration (`settings.load_settings()`, fichier JSON `%APPDATA%\DeeKeep\deekeep_config.json`, fusion avec les défauts — voir §5) ;
   - `download_dir` = réglage `download_dir`, défaut `~/Music` ;
   - état en mémoire : `active_downloads = {}` (ligne → worker), `_download_queue = []`, **`MAX_CONCURRENT_DOWNLOADS = 10`** (`main_window.py:94`), `_download_tally = None`, `search_history`, `downloaded_history`, `cookies_file`, compteurs `success_count = failed_count = 0` (non persistés) ;
   - état Réencoder : **`REENCODE_MAX_CONCURRENT = 10`** (`:137`), **`REENCODE_AUTOMATCH_LIMIT = 150`** (`:141`) ;
   - `init_ui()`, `init_tray()`, `apply_loaded_settings()`, `init_database()` (contrôleur de synchronisation, hors périmètre), `refresh_library_index()` (index disque en tâche de fond).

#### Index du dossier de dépôt (`main_window.py:159-283`)

- `refresh_library_index()` : lance `LibraryIndexThread(download_dir, downloaded_history)` **sauf si un parcours est déjà en cours** (pas de doublon). Appelé : au démarrage, à chaque ouverture du dialogue de recherche, à chaque « Relancer » d'une recherche, après changement de dossier de sortie, après un rangement (signal `libraryChanged` de la page Organiser).
- À l'arrivée de l'index (`_on_library_index_ready`) : mémorise l'index, **répare les chemins de l'historique** (`_repair_history_paths`, voir §3.10), rafraîchit les pastilles du tableau Musique, émet `libraryIndexChanged` (le dialogue de recherche ouvert s'y abonne).
- `on_library_files_deleted(paths)` : appelé par le dialogue après suppression de doublons ; retire chaque chemin de l'index (`LibraryIndex.remove`) sans relecture du disque, rafraîchit les pastilles, réémet l'index.
- Changement de dossier de sortie : l'index est mis à `None` (pastilles « ⏳ ») puis reconstruit.

#### Enregistrement de la configuration (`save_config`, `main_window.py:371-400`)

Appelé après presque chaque action. Écrit dans le dictionnaire de réglages :
`download_dir`, `search_history`, `downloaded_history` et — si l'interface est construite — `file_format`, `quality`, `org_artist`, `org_album`, `org_divers`, `org_divers_min`, `youtube_browser`, `cookies_file`, `acoustid_key`.
Puis : écriture atomique du JSON (fichier `.tmp` + `os.replace`, `settings.py:169-184`), puis `sync_controller.record_state(settings)` (report différentiel dans le cache SQLite : clés de réglage → `app_settings`, `search_history` → `searches`, `downloaded_history` → `tracks`/`covers` ; secrets `acoustid_key`/`arl` exclus sauf `cloud_sync_secrets=true`). Une synchronisation distante est déclenchée 4 s après la dernière modification (`sync_ui.py:40`) — hors périmètre.

#### Options d'export à la demande (`current_export_options`, `main_window.py:402-415`)

Fournit aux pages Réencoder/Qualité : `file_format` (texte du combo, ex. « MP3 »), `quality` (texte du combo), `cookies_browser` (`None` si le libellé commence par « Aucun », sinon le libellé en minuscules : `chrome`, `edge`, `firefox`, `brave`, `opera`, `vivaldi`), `cookies_file`.

#### Synchronisation des options « Organiser » (`on_organize_options_changed`, `:417-445`)

La page Organiser modifie `org_artist`, `org_album`, `org_divers`, `org_divers_min` dans les réglages ; la fenêtre recopie ces valeurs dans les widgets des Paramètres (sans déclencher leurs signaux) puis enregistre. **Règle web : une seule source de vérité (les réglages) partagée par les deux écrans.**

#### Fermeture (`closeEvent`, `main_window.py:547-604`)

1. Si un rangement est en cours (Organiser) : question « Rangement en cours » — « Un rangement de la bibliothèque est en cours.\nQuitter maintenant attendra la fin du fichier en cours de déplacement. Continuer ? » (Oui/Non, défaut Non). Non → annulation de la fermeture.
2. Si un retéléchargement est en cours (Qualité) : question « Retéléchargement en cours » — « Des titres sont en cours de retéléchargement.\nQuitter maintenant les abandonnera. Continuer ? ».
3. `save_config()` ; arrêt des threads (Organiser, Qualité, jaquettes, Réencoder) ; attente ≤ 2 s du parcours d'index ; fermeture du lecteur ; arrêt de la synchronisation (≤ 5 s) ; masquage du tray ; `QApplication.quit()`.
4. **Les téléchargements de la page Musique en cours ne sont pas attendus ni signalés** (ils meurent avec le processus) ; les fichiers temporaires `.dk_*` peuvent rester (voir §6).

### 2.2 Barre latérale (`main_window.py:615-743`, `1249-1342`)

Contenu, de haut en bas :

| Élément | Détail |
|---|---|
| Logo | Icône applicative 36×36 + « DeeKeep » (`logo_title` : Montserrat 20 px, poids 800, cyan `#00e5ff`) + sous-titre « A U D I O P H I L E   G R A D E » (9 px, poids 600, `#bac9cc`). Marges 24 px gauche/droite, 24 px sous le logo. |
| « Musique » | Bouton de navigation (`nav-btn`), actif au démarrage → page 0 |
| « Bibliothèque » | → page 7 (relit les réglages puis recharge la liste à chaque affichage) |
| « ▾ Outils » / « ▸ Outils » | En-tête de groupe repliable (`nav_group`, gris discret `#849396`, 13 px). Déplié par défaut. Clic = bascule. Afficher une sous-page du groupe le redéplie automatiquement. |
| ↳ « Réencoder » | Sous-élément (`nav-sub-btn`, indentation 44 px) → page 4 |
| ↳ « Organiser » | → page 5 (`organize_page.sync_from_settings()` à chaque affichage) |
| ↳ « Qualité » | → page 6 (`quality_page.sync_from_settings()`) |
| ↳ « Diagnostic » | → page 2 (rapport recalculé à chaque affichage) |
| « Historique » + **badge** | → page 1 (reconstruit les deux tableaux à chaque affichage). Badge cyan arrondi = `len(search_history) + len(downloaded_history)` (`:3279-3280`). |
| « Paramètres » | → page 3 |
| (espace extensible) | |
| **État de la base** (pied) | Libellé cliquable (→ Paramètres), 11 px, retour à la ligne, infobulle = message détaillé. Texte initial « Base locale ». Libellés/couleurs par état ci-dessous. |

États de synchronisation affichés en pied (`main_window.py:311-328`, couleurs `sync_ui.py:42-50`) :

| État | Libellé | Couleur |
|---|---|---|
| IN_SYNC | « Base synchronisée » | `#00e5ff` |
| PUSH_NEEDED | « Envoi en attente » | `#ffc857` |
| PULL_NEEDED | « Réception en attente » | `#ffc857` |
| CONFLICT | « Base désynchronisée ! » | `#ff5f6d` |
| OFFLINE | « Base hors ligne » | `#849396` |
| NOT_CONFIGURED | « Base locale seule » | `#849396` |
| ERROR | « Erreur de base » | `#ff5f6d` |

Règle d'activation : un seul bouton actif à la fois (propriété `active="true"` : barre gauche cyan 3 px, fond `rgba(0,229,255,.10)`, texte cyan gras).

### 2.3 Page Musique (file de téléchargement)

#### 2.3.1 Ce que voit l'utilisateur (`main_window.py:749-878`)

Marges 40/30/40/24, espacement 20.

1. Sur-titre « STATUT ACTUEL » (11 px, 600, `#849396`).
2. **Compteurs de session** (`update_stats_label`, `:3270-3277`) : « **N** fichiers téléchargés | **M** fichiers abandonnés » — nombres en `#e5e2e1` 26 px gras Montserrat, libellés `#bac9cc` 15 px normal, séparateur « | » `#3b494c`. Remis à 0 uniquement par « Retirer la sélection » quand au moins une ligne est retirée (et au redémarrage).
3. « Progression globale du traitement : » + barre globale (`global_progress_bar`, hauteur 20 px) = **moyenne arithmétique des progressions de toutes les lignes du tableau** (y compris terminées à 100 et échouées à 0) (`:3282-3297`).
4. **Barre d'actions** (ordre gauche → droite) :

| Bouton | Classe | Infobulle | Action |
|---|---|---|---|
| « Tout cocher » | control-btn | — | coche toutes les cases |
| « Tout décocher » | control-btn | — | décoche toutes les cases |
| « Retirer la sélection » | control-btn | « Retirer de la liste les lignes cochées (les fichiers déjà extraits sont conservés sur le disque) » | voir 2.3.6 |
| *(espace)* | | | |
| « Purger les téléchargés » | control-btn | « Retirer de la liste toutes les lignes téléchargées avec succès » | voir 2.3.6 |
| « Réessayer » | control-btn | « Relancer les téléchargements en échec » | voir 2.3.4 |
| « Rechercher » | accent-btn | « Chercher des titres à ajouter à la liste » | ouvre le dialogue de recherche (2.4) |
| « Extraire » | primary-btn | « Télécharger tous les titres de la liste » | voir 2.3.4 |

5. **Tableau des téléchargements** — 9 colonnes, lignes de 55 px, sélection par ligne, pas d'édition, en-têtes alignés à gauche, cliquables (tri) :

| # | En-tête | Contenu | Largeur |
|---|---|---|---|
| 0 | (vide) | Pastille de présence disque (18 px) + case à cocher | au contenu |
| 1 | « JAQUETTE » | Vignette 45×45 (ou « 🎵 » gris sur `#1c1e26`) | au contenu |
| 2 | « ID » | Identifiant Deezer de la piste | 85 px, redimensionnable |
| 3 | « TITRE » | Titre | extensible |
| 4 | « ARTISTE » | Artiste | 130 px, redimensionnable |
| 5 | « ALBUM » | Album | 130 px, redimensionnable |
| 6 | « DURÉE / PROGRESSION » | Barre de progression au format « `mm:ss` - `%p%` » + bouton rond rouge « X » 20×20 (infobulle « Annuler ce téléchargement »), visible seulement pendant le téléchargement | extensible |
| 7 | « STATUT » | Texte coloré (voir états) ; infobulle = cause exacte d'échec | extensible |
| 8 | « ÉCOUTE » | Glyphe ▶ (ou ■ pendant l'extrait), cyan si actif, gris sinon | 90 px fixe |

Largeur minimale de section : 90 px. La page démarre **vide** à chaque lancement (la file n'est pas persistée).

#### 2.3.2 Ajout de lignes (`add_table_row`, `main_window.py:2559-2644`)

Entrées : `track_id, title, artist, album, duration ("mm:ss"), cover_bytes, cover_url, local_path="", preview_url=""`.
- Pas de dédoublonnage : la même piste peut être ajoutée deux fois (voir §6).
- Statut initial « En attente », couleur `#bac9cc`.
- Vignette : si `cover_bytes` → image 45×45 ; sinon « 🎵 ». La ligne mémorise `cover_data` et `cover_url`.
- Cellule ÉCOUTE (`_set_main_play_cell`, `:2649-2682`) : mémorise deux sources — chemin du fichier extrait et URL d'extrait Deezer (30 s). Infobulle :
  - fichier présent : « Écouter le fichier extrait » ;
  - extrait seulement : « Écouter l'extrait de 30 secondes (le titre n'est pas encore extrait) » ;
  - aucun : « Titre pas encore extrait, et Deezer n'en donne aucun extrait » (bouton désactivé).
- Pastille de présence calculée sur (titre, artiste, album) de la ligne (`_set_library_badge`, `:264-283`). **Sur cette page la pastille est informative : la case n'est jamais désactivée** (elle sert à retirer des lignes, pas à choisir quoi extraire).
- Après validation du dialogue : `_fetch_missing_covers()` (`:2925-2966`) télécharge en tâche de fond (`CoverDownloadThread`, taille « small ») les vignettes des lignes qui n'ont qu'une `cover_url` ; l'image obtenue devient `cover_data` **et sera celle intégrée au fichier**.

#### 2.3.3 États d'une ligne (colonne STATUT)

| Statut (texte exact) | Couleur | Quand |
|---|---|---|
| « En attente » | `#bac9cc` | ajout |
| « En file d'attente » | `#bac9cc` | mise en file (`_enqueue_download`) |
| Texte de progression du worker (voir §3.4) : « Recherche Deezer... », « Recherche YouTube... », « Initialisation... », « Extraction... », « Finalisation... », « Autre version (2/3)... », « Échec, nouvelle tentative dans 1.1 s (essai 3/10)... », « Terminé ✓ » ; suffixe « (essai n/10) » à partir du 2ᵉ essai | `#00e5ff` | pendant l'extraction |
| « Téléchargé ✓ » ou « Téléchargé ✓ (n essais) » si n > 1 | `#5eeb7f` | succès ; barre à 100 |
| « Erreur » (1 essai) ou « Erreur / Abandonné (n essais) » | `#ffb4ab` | échec ; barre à 0 ; **infobulle = message d'erreur complet** |
| « Annulé » | `#ffb4ab` | annulation |

#### 2.3.4 Lancer les téléchargements

**« Extraire »** (`start_all_downloads`, `:3299-3313`) :
1. Si mutagen absent : avertissement « Diagnostic Tags » (« La bibliothèque 'mutagen' n'est pas installée. Les tags et pochettes ne pourront pas être intégrés.\nInstallez-la via : pip install mutagen ») — non bloquant.
2. Met en file **toutes** les lignes dont le statut vaut exactement « En attente » ou contient « Abandonné », « Erreur » ou « Annulé ». **Les cases à cocher sont ignorées** (elles ne servent qu'au retrait).
3. Démarre la pompe.

**« Réessayer »** (`retry_failed_downloads`, `:3315-3320`) : met en file les lignes dont le statut contient « Erreur », « Abandonné » ou « Annulé ».

**Mise en file** (`_enqueue_download`, `:3335-3349`) : ignorée si la ligne est déjà active ou en file. Sinon :
- **réserve l'artiste** dans le décompte du lot (`Tally.reserve`, voir §3.7) **avant** tout démarrage ;
- statut « En file d'attente ».

Le décompte du lot (`_batch_tally`, `:3322-3333`) est recréé (relecture disque) si : aucun décompte, ou dossier de dépôt changé, ou plus rien d'actif ni en file (nouveau lot).

**Pompe** (`_pump_downloads`, `:3351-3356`) : tant qu'il reste des lignes en file **et** moins de **10** téléchargements actifs, dépile la première (FIFO) et lance `launch_single_download`.

**Lancement** (`launch_single_download`, `:3358-3396`) : lit id/titre/artiste/album/jaquette de la ligne, le format et la qualité **au moment du lancement**, le navigateur cookies, affiche le bouton « X », crée un worker avec :
- `save_dir = download_dir`, `layout = library_rules.rules_from_settings(settings)` (rangement selon les règles Organiser), `tally = _download_tally`, `cookies_browser`, `cookies_file`, `max_retries = 10`, `resolve_metadata = False`.

**Progression** (`on_download_progress`, `:3398-3409`) : texte du statut en cyan, barre de la ligne au pourcentage, recalcul de la barre globale.

**Fin** (`on_download_finished`, `:3411-3472`) :
- masque « X » ; nombre d'essais lu sur le worker ;
- succès : statut vert (voir tableau), barre 100, `success_count += 1` ; si infos rendues : **ajout à `downloaded_history`** + `save_config()` ; cellule ÉCOUTE → chemin du fichier ; **ajout à l'index disque sans relecture** (`LibraryIndex.add(path, title, artist, album)`) puis rafraîchissement de toutes les pastilles (page + dialogue) ;
- échec : statut rouge + infobulle = message, barre 0, `failed_count += 1`, message en console ;
- retrait de `active_downloads` puis relance de la pompe.

#### 2.3.5 Annuler (`cancel_download_by_id`, `:3104-3134`)

Clic sur « X » d'une ligne : recherche la **première** ligne dont l'ID vaut l'ID de la piste.
- Si en file : retirée de la file, statut « Annulé » (`#ffb4ab`).
- Si active : **arrêt brutal du thread** (`terminate()` + `wait()`), suppression de `active_downloads`, statut « Annulé », masque « X », recalcul progression globale.
- **Défaut existant** : la pompe n'est pas relancée après une annulation (la place libérée n'est réutilisée qu'à la fin d'un autre téléchargement) ; le dossier temporaire `.dk_*` et un éventuel fichier partiel restent sur le disque. À corriger dans la version web.

#### 2.3.6 Retirer / purger / cocher

- **« Retirer la sélection »** (`delete_selected_tracks`, `:3201-3219`) : arrête l'extrait en cours, supprime les lignes cochées (de bas en haut). Si ≥ 1 supprimée : **remet les compteurs de session à 0**, recalcule, message « Retrait » — « N élément(s) retiré(s) de votre liste. ». **Aucune garde** contre les téléchargements actifs (risque de décalage d'index, voir §6).
- **« Purger les téléchargés »** (`purge_successful_downloads`, `:3169-3199`) : refusé si un téléchargement est actif (« Purge indisponible » — « Attendez la fin des téléchargements en cours avant de purger. »). Sinon supprime les lignes dont le statut commence par « Téléchargé ». Message « Purge » — « N téléchargement(s) réussi(s) retiré(s) de la liste. » ou « Aucun téléchargement réussi à purger. ».
- **« Tout cocher » / « Tout décocher »** : toutes les lignes.

#### 2.3.7 Tri (`_on_main_header_clicked`, `:2993-3076`)

- Colonnes triables : 2 (ID, numérique ; non numérique → 0), 3 titre, 4 artiste, 5 album (texte insensible à la casse), 6 durée (secondes, `mm:ss` ou `h:mm:ss`), 7 statut (texte). Colonnes 0, 1, 8 : rien.
- Premier clic sur une colonne : ascendant ; reclic : bascule.
- **Interdit pendant des téléchargements actifs** : message « Tri indisponible » — « Le tri est désactivé pendant des téléchargements en cours. » (les workers repèrent leur ligne par index).
- Arrête l'extrait en cours ; reconstruit toutes les lignes en conservant : coche, jaquette, sources d'écoute, progression, statut et couleur.

#### 2.3.8 Écoute (`main_window.py:2684-2861`)

- **Clic sur la cellule ÉCOUTE** : si fichier extrait → lecteur complet sur ce fichier, **avec l'historique des téléchargements comme file d'attente** (`_play_local` → `_play_history`) ; sinon si extrait Deezer → **bascule lecture/arrêt de l'extrait 30 s sans ouvrir de fenêtre** (un seul extrait à la fois ; glyphe ■ pendant la lecture ; le lecteur complet se met en pause pendant un extrait).
- **Double-clic sur une ligne** (hors colonne ÉCOUTE) : ouvre le lecteur complet — sur le fichier, ou sur l'extrait (sous-titre « Extrait de 30 secondes — le titre n'est pas encore extrait », jaquette de la ligne).
- **Menu contextuel** d'une ligne :
  - « Écouter dans le lecteur » (actif si fichier ou extrait) ;
  - « Arrêter l'extrait » si l'extrait de cette ligne joue, sinon « Écouter l'extrait de 30 s (sans fenêtre) » (actif si extrait **et** pas de fichier) ;
  - « Ouvrir le dossier » (actif si fichier) → explorateur sur le fichier sélectionné.
- Un fichier extrait puis purgé de l'historique reste jouable seul (titre = nom de fichier).
- Si Qt Multimedia est absent : « Lecture indisponible : le module Qt Multimedia (PyQt6-Multimedia) est absent de cette installation. ».

### 2.4 Fenêtre « Rechercher et ajouter des pistes » (`add_track_dialog.py`)

#### 2.4.1 Mise en page (`add_track_dialog.py:89-320`)

- Dialogue modal ; titre « Rechercher et ajouter des pistes » ; min 950×600 ; taille = min(1400, 90 % de la largeur d'écran) × 90 % de la hauteur. Marges 24, espacement 15.
- Libellé « Rechercher par morceau, artiste, album ou playlist : » (14 px gras blanc).
- Ligne 1 : champ de recherche (placeholder « Saisissez un titre, un artiste, ou collez un lien de playlist... », **Entrée = rechercher**) + liste « Tout / Titre », « Artiste », « Album », « Playlist » (130 px, défaut « Tout / Titre ») + bouton « Rechercher » (accent-btn).
- Ligne 2 : champ lien (placeholder « Collez un lien Deezer (https://www.deezer.com/track/… ou /playlist/…) », **Entrée = rechercher par lien**) + « Coller et rechercher » (control-btn). Effet : copie le lien dans le champ de recherche puis lance la recherche normale (les liens sont reconnus quel que soit le type choisi).
- Barre de progression indéterminée (8 px) pendant une recherche.
- Navigation : « ← Retour » (control-btn, masqué à la racine) + fil d'Ariane (`#bac9cc`, 13 px).
- **En-tête d'album** (hauteur fixe 98 px, masqué par défaut) : jaquette 90×90 (loader « ⏳ » puis image), titre 18 px gras blanc, sous-titre (artiste) 13 px.
- **Barre de filtres** (visible en vue pistes seulement) : case « Tout sélectionner » (600) ; champ « Filtrer instantanément les résultats... » ; bilan disque (12 px) ; « Voir les doublons (n) » (control-btn **à bascule**, masqué si 0) ; « Dédoublonner… » (control-btn, masqué s'il n'y a pas de candidat).
- Tableau de résultats (lignes 60 px, aucune sélection de ligne, pas d'édition, en-têtes triables).
- Pied : « Annuler » (control-btn) + « Ajouter la sélection » (primary-btn, désactivé tant que rien n'est coché ; devient « Ajouter la sélection (N) »).

#### 2.4.2 Panneau « Dernières recherches » (`add_track_dialog.py:325-481`)

- S'affiche à la prise de focus ou au clic sur le champ de recherche, **si l'historique n'est pas vide** ; positionné 6 px sous le champ, largeur = max(460, largeur du champ) ; fond `#22242e`, bordure `rgba(255,255,255,.12)`, rayon 14.
- Se masque : frappe dans le champ, perte de focus (clic ailleurs), **Échap**, lancement d'une recherche.
- En-tête « Dernières recherches » (16 px gras) + corbeille « 🗑 » (infobulle « Effacer les dernières recherches ») → **vide tout l'historique des recherches**.
- **8 premières** recherches (les plus récentes d'abord). Chaque ligne : vignette 42×42 (« 🎵 » puis pochette du premier résultat Deezer pour cette requête, cache mémoire partagé entre ouvertures `_SEARCH_COVER_CACHE`), libellé de la requête (14 px 600 blanc) + « Recherche » (11 px gris), bouton « ✕ » (infobulle « Retirer cette recherche ») → retire cette entrée. Clic sur la ligne → remplit le champ et lance la recherche.
- Les modifications de l'historique faites ici sont conservées même si le dialogue est annulé.

#### 2.4.3 Lancer une recherche (`start_search`, `_launch_thread`, `:493-535`)

1. Requête = texte du champ, espaces de bord retirés ; vide → rien.
2. Historique : la requête est retirée si présente puis **insérée en tête** (pas de limite de taille).
3. Réinitialise la navigation (pile vidée, « Retour » masqué, en-tête d'album masqué, fil d'Ariane vide).
4. Arrête les téléchargements de vignettes en cours, bouton « Rechercher » désactivé et renommé « Recherche... », « Ajouter » désactivé, « Tout sélectionner » décoché, **filtre vidé**, barre de progression visible.
5. Lance `DeezerSearchThread(query, type)` (algorithme §3.1). Trois retours possibles : pistes, entités (artistes/albums), erreur.
- Erreur → « Erreur de connexion » — « Erreur lors de la recherche : {message} ».
- 0 piste → « Aucun résultat » — « Aucun morceau trouvé pour cette recherche. ».
- 0 entité → « Aucun résultat » — « Aucun résultat trouvé. ».

#### 2.4.4 Vue « entités » (artistes ou albums) (`:540-593`)

- Filtres masqués, en-tête d'album masqué.
- Tableau 4 colonnes : vignette 50×50 | « ARTISTE » ou « ALBUM » (extensible) | « DÉTAILS » (220 px) | bouton (180 px).
- Détails : artiste → « {nb_album} albums · {nb_fan} fans » (milliers séparés par une espace, ex. « 12 albums · 1 234 567 fans ») ; album → nom de l'artiste.
- Bouton cyan plein « Voir le top 50 » (artiste) ou « Ouvrir l'album » (album) → `open_entity`.
- Vignettes chargées en tâche de fond (loader « ⏳ »).
- Tri : colonne 1 (nom), colonne 2 (détails), texte insensible à la casse.

#### 2.4.5 Explorer une entité, fil d'Ariane, retour (`:595-672`)

- `open_entity(entity)` est appelé depuis une ligne d'entités **ou** depuis un lien artiste/album d'une ligne de pistes.
- Avant la requête, l'état courant est **empilé** (mode, pistes, entités, type, fil d'Ariane, en-tête d'album avec son image).
- Artiste : fil d'Ariane « Artiste : {nom} — top 50 », requête interne `artist/{id}` (→ top 50).
- Album : fil d'Ariane « Album : {nom} », requête interne `album/{id}` avec titre et jaquette de l'album fournis (toutes les pistes recevront ce nom d'album et cette jaquette), puis affichage de l'en-tête d'album (jaquette téléchargée en taille « big »).
- « ← Retour » : dépile et **restaure sans requête réseau** (y compris l'en-tête d'album). Masqué quand la pile est vide.

#### 2.4.6 Vue « pistes » (`_render_tracks`, `:687-777`)

Tableau 8 colonnes :

| # | En-tête | Contenu |
|---|---|---|
| 0 | (vide) | pastille de présence + case à cocher |
| 1 | « JAQUETTE » | vignette 50×50 (loader « ⏳ ») — 60 px |
| 2 | « TITRE » | texte — extensible |
| 3 | « ARTISTE » | **lien cyan** (sans soulignement, infobulle « Cliquer pour explorer ») si l'ID artiste est connu → top 50 ; sinon texte — 170 px |
| 4 | « ALBUM » | **lien cyan** si l'ID album est connu → ouvre l'album ; sinon texte — 170 px |
| 5 | « DURÉE » | `mm:ss` centré |
| 6 | « POP. » | jauge de popularité (100 px) |
| 7 | « ÉCOUTE » | bouton rond 34×34 « ▶ » / « ⏸ » (100 px) |

Largeur minimale de section 100 px.

**Jauge de popularité** (`PopularityBars`, `:54-85`) : 7 barres verticales arrondies (4 px de large, 4 px d'écart, hauteur 16 px, widget 52×20) ; nombre de barres allumées = `round(min(rank / 1 000 000, 1) × 7)` ; allumée `#00e5ff`, éteinte `#3a3d47` ; infobulle « Popularité Deezer : 123 456 ».

**Bouton d'écoute** (`:879-942`) : fond `rgba(0,229,255,.15)`, glyphe cyan, survol plein cyan / texte `#00363d`, désactivé `rgba(255,255,255,.05)` / `#5f7d84`. Désactivé si lecteur indisponible ou si la piste n'a pas d'extrait (infobulle « Aucun extrait disponible pour ce titre ») ; sinon infobulle « Écouter un extrait de 30 secondes ». Clic : lance l'extrait (« ⏸ ») ou l'arrête s'il joue déjà ; un seul extrait à la fois (le précédent repasse à « ▶ ») ; l'extrait est arrêté à chaque re-rendu du tableau, au passage en vue entités, à l'ouverture de l'écran des doublons et à la fermeture du dialogue.

#### 2.4.7 Pastilles de présence et verrouillage (`:782-862`)

Pour chaque ligne, `library_index.lookup(index, titre, artiste, album)` (algorithme §3.8) rend un état :

| État | Glyphe | Couleur | Case de sélection | Infobulle de la case |
|---|---|---|---|---|
| PRESENT | « ✓ » | `#5eeb7f` | **décochée et désactivée** | infobulle de la pastille (motif + fichiers) |
| SIMILAR | « ≈ » | `#ffcf7a` | **décochée**, reste cochable | « Ajouter ce titre à la file de téléchargement » |
| ABSENT | « ○ » | `#3b494c` | libre | idem |
| UNKNOWN (index pas prêt) | « ⏳ » | `#5f7d84` | libre, **rien n'est décoché** | idem |

- Pastille : largeur fixe 18 px, centrée, 13 px gras. Infobulle = motif puis jusqu'à **5** chemins (« • chemin »), puis « … et N autre(s) ».
- Motifs exacts : « Déjà présent (même titre, même artiste) », « Déjà présent (fichier du même nom) », « Doublon probable : même titre, même artiste principal (l'invité diffère) », « Doublon possible : même titre, artiste similaire », « Doublon possible : même titre, même album », « Doublon possible : autre version du même titre », « Doublon possible : titre proche, artiste compatible », « Absent du dossier de dépôt », « Lecture du dossier de dépôt en cours… ».
- Les pastilles sont recalculées : au rendu des pistes, quand l'index arrive ou change (signal de la fenêtre principale), après suppression de doublons. Le dialogue se désabonne à sa fermeture.

**Bilan** (`_update_library_summary`) : si `présents + similaires = 0` → texte vide, bouton doublons masqué (et relâché s'il était enfoncé). Sinon :
- texte : « ✓ {n} déjà sur le disque » (vert) et/ou « ≈ {n} doublon(s) possible(s) » (orange), séparés par « · » ;
- infobulle : « Les titres déjà présents ne peuvent pas être sélectionnés ; les doublons possibles restent libres mais décochés.\n« Tout sélectionner » ne coche ni les uns ni les autres.\n« Voir les doublons » n'affiche que ces lignes ; « Dédoublonner… » montre les fichiers du disque en cause. » ;
- bouton « Voir les doublons ({total}) » visible ; « Dédoublonner… » visible s'il existe au moins une ligne verte/orange **ayant au moins un chemin de fichier**.

#### 2.4.8 Filtre, « Voir les doublons », « Tout sélectionner » (`:1126-1178`)

- **Filtre texte** : sous-chaîne, insensible à la casse, **sur le titre uniquement** ; instantané à chaque frappe.
- **« Voir les doublons »** (bascule, infobulle « N'afficher que les titres déjà sur le disque ou soupçonnés d'y être ») : se **cumule** au filtre texte (ligne visible = filtre texte ET état PRESENT/SIMILAR).
- **« Tout sélectionner »** : n'agit que sur les lignes **visibles** ; en cochant, ignore les lignes PRESENT/SIMILAR ; en décochant, décoche toutes les visibles.
- Chaque changement met à jour le libellé « Ajouter la sélection (N) » (N = cases cochées, visibles ou non).

#### 2.4.9 Tri (`_on_header_clicked`, `:1043-1099`)

- Vue pistes : 2 titre, 3 artiste, 4 album (texte casse ignorée), 5 durée (secondes), 6 popularité (`rank` numérique). Premier clic : ascendant, **sauf popularité : descendant** (les plus populaires en tête). Reclic : bascule.
- Avant tri : arrêt des téléchargements de vignettes ; après : re-rendu, relance des vignettes, **restauration des cases cochées par ID de piste**, réapplication du filtre.

#### 2.4.10 Dédoublonner (`:1183-1251`)

1. Candidats = lignes PRESENT/SIMILAR ayant au moins un chemin. Aucun → « Doublons » — « Aucun fichier du dossier de dépôt n'est rapproché des titres affichés. ».
2. Ouvre l'écran des doublons (hors périmètre) avec `{track, match}` par candidat. Si validé : liste de chemins à supprimer.
3. Suppression : `os.remove` de chaque fichier existant ; erreurs collectées (« nom : erreur »).
4. Vide le cache de tags, retire les chemins disparus de l'index, prévient la fenêtre principale (`on_library_files_deleted`), recalcule les pastilles (les pistes repassent au gris et redeviennent sélectionnables).
5. Message « Déduplication » — « N fichier(s) supprimé(s). » + « \n\nErreurs :\n » et jusqu'à 10 erreurs.

**Suppression physique de fichiers : à protéger côté web (confirmation, droits, journal).**

#### 2.4.11 Validation / annulation (`:1104-1124`, `:1253-1263`, `main_window.py:3136-3167`)

- « Annuler » : si une recherche tourne → elle est **interrompue** (thread tué) et le dialogue **reste ouvert** ; sinon fermeture (rejet).
- « Ajouter la sélection » : fermeture acceptée ; la fenêtre principale ajoute à la page Musique chaque piste cochée ayant un ID (id, titre, artiste, album, durée, jaquette déjà chargée, URL de jaquette, URL d'extrait), puis rattrape les jaquettes manquantes.
- Dans tous les cas : l'historique des recherches modifié est repris, le tableau « Recherches effectuées » reconstruit, le badge mis à jour, la configuration enregistrée.

### 2.5 Page Historique (`main_window.py:881-987`, `2222-2557`)

Titre « Historique d'utilisation » (Montserrat 26 px gras cyan). Deux onglets (soulignement cyan) :

#### 2.5.1 Onglet « Recherches effectuées »

Tableau 4 colonnes, lignes 58 px :

| # | En-tête | Contenu |
|---|---|---|
| 0 | « RECHERCHE » | texte de la requête (extensible) |
| 1 | « CATEGORIE » | toujours « Recherche Globale » (le type de recherche n'est pas mémorisé) |
| 2 | « JAQUETTE » | vignette 46×46 : « ⏳ » puis pochette du 1er résultat Deezer (`CoverLookupThread("", requête)`), « ♪ » si rien — 110 px |
| 3 | « ACTION » | bouton cyan plein « Relancer » 96×32 (rayon 16) — 130 px |

- **Tri** : seule la colonne 0, bascule asc/desc à chaque clic ; **trie la liste `search_history` elle-même** (l'ordre trié devient l'ordre persistant au prochain enregistrement — y compris celui du panneau « Dernières recherches »).
- **« Relancer »** (`relaunch_search_from_history`, `:2276-2304`) : affiche la page Musique, relance l'index disque, ouvre le dialogue de recherche avec la requête pré-remplie et **lance la recherche** (type par défaut « Tout / Titre ») ; à la validation, ajoute les pistes comme 2.4.11.

#### 2.5.2 Onglet « Téléchargements réussis »

Barre d'actions (à droite) : « Purger les fichiers introuvables » (infobulle « Retirer de l'historique les téléchargements dont le fichier n'existe plus sur le disque ») et « Vider l'historique » (infobulle « Retirer toutes les entrées de l'historique (les fichiers du disque sont conservés) »).

Tableau 6 colonnes, lignes 56 px, pas d'édition :

| # | En-tête | Contenu |
|---|---|---|
| 0 | « TITRE » | au contenu |
| 1 | « ARTISTE » | au contenu |
| 2 | « ALBUM » | au contenu |
| 3 | « CHEMIN LOCAL » | chemin complet (extensible) |
| 4 | « JAQUETTE » | vignette 46×46 recadrée carrée, coins arrondis 6 px ; clic = agrandir (infobulle « Cliquer pour agrandir ») — 110 px |
| 5 | « ÉCOUTE » | ▶ (infobulle « Écouter ce titre dans le lecteur ») — 70 px |

**Résolution de la jaquette** (`_make_history_cover`, `:2324-2370`), dans l'ordre :
1. `cover_b64` stocké dans l'entrée ;
2. pochette intégrée au fichier local (MP3 : première trame `APIC*` ; FLAC : première image) — mémorisée dans l'entrée (`cover_b64`) ;
3. recherche Deezer asynchrone (`CoverLookupThread(artiste, titre, cover_url)`, voir §3.6) — « ⏳ » puis image (mémorisée en `cover_b64`) ou « ♪ ».

**Popup jaquette** (`show_cover_popup`, `:2469-2520`) : titre de fenêtre « {titre} — {artiste} », image 320×320 (mêmes sources ; sinon « Chargement de la jaquette… » puis image ou « Aucune jaquette disponible »), légende « **titre** / artiste », bouton « Ouvrir le dossier ».

**Actions** :
- Clic sur ÉCOUTE ou double-clic sur la ligne : lecteur complet, **file d'attente = tout l'historique** (entrées ayant un chemin), départ sur la ligne cliquée ; jaquette = `cover_b64` (sinon le lecteur lit la pochette intégrée).
- Menu contextuel : « Écouter dans le lecteur », « Ouvrir le dossier » (actifs si chemin).
- **Tri** colonnes 0-3 (texte casse ignorée, asc puis bascule) : trie `downloaded_history` en place (ordre persisté au prochain enregistrement).
- **« Purger les fichiers introuvables »** (`:2529-2551`) : retire les entrées sans chemin ou dont le fichier n'existe pas ; « Purge » — « N entrée(s) purgée(s) : fichier(s) introuvable(s) sur le disque. » ou « Rien à purger : tous les fichiers de l'historique existent sur le disque. ».
- **« Vider l'historique »** (`:2553-2557`) : vide tout, **sans confirmation**.

#### 2.5.3 Réparation automatique des chemins (`_repair_history_paths`, `main_window.py:201-257`)

Voir algorithme §3.10. Message console « [historique] N chemin(s) rattrapé(s) après déplacement de la bibliothèque. » et enregistrement si au moins une réparation.

### 2.6 Page Réencoder (conversion de fichiers locaux)

#### 2.6.1 Ce que voit l'utilisateur (`main_window.py:1347-1534`)

- Titre « Réencoder des fichiers » ; sous-titre « Ajoutez des fichiers MP3 : une correspondance Deezer est recherchée automatiquement, puis « Convertir » réextrait chaque titre au format des paramètres et remplace le fichier dans son dossier d'origine. » (12 px `#849396`).
- **Barre d'actions** : « Tout cocher », « Tout décocher », « Retirer la sélection » (infobulle « Retirer de la liste les fichiers cochés (les fichiers du disque sont conservés) ») | espace | « Purger les convertis » (infobulle « Retirer de la liste les fichiers réencodés avec succès »), « Ajouter un dossier » (control-btn, infobulle « Ajouter tout le contenu audio d'un dossier et de ses sous-dossiers »), « Ajouter des fichiers » (accent-btn, infobulle « Parcourir et ajouter des fichiers audio »), « Convertir » (primary-btn, infobulle « Réextraire et réencoder les fichiers sélectionnés, puis les remplacer dans leur dossier d'origine »).
- Barre indéterminée 8 px : pendant l'analyse locale et les recherches de correspondance.
- Bloc « Progression globale de la conversion : » + barre globale (visible pendant/après une conversion) = moyenne des pourcentages des lignes **du lot courant**.
- Ligne de filtre : bouton à bascule « Masquer ceux déjà au format cible » ↔ « Afficher tous les fichiers » (infobulle « Masquer les fichiers déjà dans le format et la qualité définis dans les Paramètres (rien à réencoder) »).
- Tableau 9 colonnes (lignes 48 px, icônes 40×40, **uniquement des cellules texte/icône — pas de widget par ligne, pour tenir des milliers de lignes**) :

| Const. | # | En-tête | Contenu | Largeur |
|---|---|---|---|---|
| RC_CHECK | 0 | (vide) | case à cocher | au contenu |
| RC_PLAY | 1 | « ÉCOUTE » | ▶ (« Écouter ce fichier dans le lecteur ») | 66 |
| RC_COVER | 2 | « JAQUETTE » | icône ou « 🎵 » | 60 |
| RC_TITLE | 3 | « TITRE » | | 200 |
| RC_ARTIST | 4 | « ARTISTE » | | 150 |
| RC_PATH | 5 | « FICHIER » | chemin (infobulle = chemin) | extensible |
| RC_FMT | 6 | « FORMAT / DÉBIT » | « MP3 · 320 kbps » ; sans débit : « MP3 » ; sans format : « ? » | 130 |
| RC_MATCH | 7 | « CORRESPONDANCE » | voir ci-dessous | 130 |
| RC_STATUS | 8 | « STATUT » | texte ; infobulle = cause d'échec | 220 |

- Texte d'aide sous le tableau (`#5f7d84`, 12 px) : « Aucun fichier. Ajoutez des fichiers ou un dossier entier (sous-dossiers inclus) pour commencer. » (visible si liste vide) ; pendant l'analyse : « Analyse en cours… {n} / {total} fichier(s) analysé(s) ».

Cellule CORRESPONDANCE (`_set_match_cell`, `:1751-1768`) :

| Situation | Texte | Couleur |
|---|---|---|
| correspondance trouvée (ou résolue pendant la conversion) | « ✓ Trouvé » | `#5eeb7f` |
| recherche sautée (lot > 150) | « — » + infobulle « Correspondance non recherchée (trop de fichiers) » | `#849396` |
| recherchée sans résultat | « ✗ Introuvable » | `#ffb4ab` |
| en attente | « ⏳ » | `#849396` |

#### 2.6.2 Ajout de fichiers (`:1543-1687`)

- Garde (`_reencode_can_add`) : refus si conversion en cours ou en file (« Conversion en cours » — « Attendez la fin des conversions avant d'ajouter des fichiers. ») ou si une analyse tourne (« Analyse en cours » — « Une analyse de fichiers est déjà en cours. »).
- « Ajouter des fichiers » : sélection multiple ; dossier de départ = dernier dossier utilisé s'il existe, sinon dossier de sortie ; filtres « Fichiers audio (*.mp3 *.flac *.wav *.m4a *.aac *.ogg *.opus *.wma) », « Fichiers MP3 (*.mp3) », « Tous les fichiers (*) ».
- « Ajouter un dossier » : « Choisir un dossier de musique (sous-dossiers inclus) ».
- **Analyse** (`LocalScanThread`) : parcours récursif (extensions `AUDIO_EXTENSIONS`), lecture des tags et du débit (8 lectures en parallèle, lots de 200), boutons d'ajout désactivés pendant l'analyse. Chaque entrée : `path, title, artist, album, bitrate, format` (extension en majuscules).
- **Ingestion** (`_reencode_ingest`) : dédoublonnage par chemin absolu ; nouvel élément `{path, title, artist, album, bitrate, format, match: None, cover_bytes: None, cover_url: None, status: "En attente", selected: False, _searched: False, _uid}`.
  - Si **nouveaux ≤ 150** : recherche automatique des correspondances Deezer (`DeezerMatchThread`, 1 thread séquentiel par ajout).
  - Si **> 150** : correspondance sautée, **tous présélectionnés** ; message « Bibliothèque volumineuse » — « {n} fichiers ajoutés. La recherche automatique de correspondances Deezer (jaquettes) a été désactivée pour préserver la fluidité ; les fichiers sont présélectionnés et seront réencodés à partir de leurs métadonnées locales.\n\nAstuce : utilisez « Masquer ceux déjà au format cible » pour ne convertir que le nécessaire. ».
  - Si des fichiers ont été trouvés mais aucun ajouté : « Aucun ajout » — « Tous les fichiers trouvés sont déjà présents dans la liste. ».
- **Correspondance trouvée** (`_on_reencode_match_ready`) : `match` mémorisé, `cover_url = match.cover_url`, **ligne cochée automatiquement**, téléchargement de la vignette (1 thread par image, en parallèle). Sans correspondance : reste décochée.

#### 2.6.3 Sélection, retrait, purge, tri, filtre

- Case cochée → `selected`. « Tout cocher/décocher » : **seulement les lignes visibles** (filtre).
- « Retirer la sélection » : refus si conversion en cours (« Attendez la fin des conversions avant de retirer des fichiers. ») ; retire les cochées (visibles ou non).
- « Purger les convertis » : refus si conversion en cours (« Attendez la fin des conversions avant de purger. ») ; retire les lignes dont le statut contient « ✓ » ; « Purge » — « N fichier(s) converti(s) retiré(s) de la liste. » / « Aucun fichier converti à purger. ».
- Tri (`:1937-1970`) : ignoré pendant une conversion ; colonnes TITRE, ARTISTE, FICHIER (texte casse ignorée), FORMAT (format puis débit), CORRESPONDANCE (trouvée/non) ; asc puis bascule.
- **Filtre « déjà au format cible »** (`_is_at_target`, `:2170-2186`) — voir §3.9.
- Écoute : clic ÉCOUTE / double-clic → lecteur complet, **file = lignes visibles** (sous-titre « {FORMAT · débit}  ·  {chemin} ») ; menu contextuel « Écouter dans le lecteur », « Ouvrir le dossier ».

#### 2.6.4 Conversion (`reencoder_convert` et suivants, `:1973-2158`)

1. Refus si déjà en cours (« Conversion en cours » — « Des conversions sont déjà en cours. ») ; si yt-dlp absent : « Extraction indisponible » — « yt-dlp n'est pas installé. Installez-le via : pip install yt-dlp ».
2. Lignes retenues = **cochées ET visibles** ; aucune → « Aucune sélection » — « Sélectionnez au moins un fichier à réencoder (visible). ».
3. Statut « En attente... », barre globale 0 visible, boutons Ajouter/Retirer/Purger/Convertir **désactivés** jusqu'à la fin du lot.
4. Pompe : **10 conversions simultanées** max (FIFO).
5. Lancement d'une conversion (`_launch_reencode_worker`) :
   - titre/artiste/album = ceux de la correspondance Deezer, à défaut ceux du fichier ; `track_id` = ID Deezer de la correspondance (ou vide) ;
   - `save_dir = dossier du fichier`, `layout = None` (**pas de rangement : conversion sur place**, nom « Titre - Artiste »), `original_path = chemin du fichier`, `resolve_metadata = (pas de correspondance)`, cookies, format/qualité courants, jaquette de la ligne ;
   - statut « Initialisation... ».
6. Progression : statut « {texte} — {pct}% » ; barre globale du lot.
7. Fin (`_on_reencode_finished`) :
   - succès : chemin mis à jour sur le fichier produit, format = format cible (majuscules), débit = débit cible (320/128/0), titre/artiste repris des infos rendues, jaquette rendue (base64) appliquée et correspondance marquée « ✓ Trouvé » ; statut « Réencodé ✓ » ;
   - échec : statut « Erreur », **infobulle = cause** (conservée dans l'élément, survit au tri/filtre) ;
   - pompe ; fin de lot → boutons réactivés.
8. Effet disque : voir §3.4 (le fichier d'origine est **remplacé** ; s'il change d'extension, l'original est supprimé).

> Écart à noter : un commentaire du code parle de « 5 conversions simultanées » (`:2043`) mais la constante vaut 10. La page Réencoder ne transmet **pas** les tags complémentaires du fichier d'origine (`extra_tags`) : ce sont ceux de Deezer qui sont écrits.

### 2.7 Page Paramètres (`main_window.py:1013-1176`, `3230-3268`)

Titre « Paramètres d'exportation ». Carte translucide (fond `rgba(28,30,38,.60)`, bordure verre, rayon 12, padding 20) :

| Libellé | Contrôle | Valeurs | Clé | Enregistrement |
|---|---|---|---|---|
| « Format de fichier d'export : » | liste | « MP3 », « FLAC », « WAV », « M4A » | `file_format` | à chaque changement |
| « Qualité d'enregistrement : » | liste **dépendante du format** : FLAC/WAV → « Lossless (Qualité Source) » seul ; MP3/M4A → « 320 kbps (HQ) », « 128 kbps (Standard) » | | `quality` | à chaque changement |
| « Compte YouTube (cookies du navigateur) : » | liste « Aucun (anonyme) », « Chrome », « Edge », « Firefox », « Brave », « Opera », « Vivaldi » ; infobulle : « Réutilise la session YouTube du navigateur choisi.\nPour changer de compte Google, changez le compte connecté\nsur youtube.com dans ce navigateur.\nAttention : sous Windows, seul Firefox fonctionne par cette\nméthode (Chrome/Edge chiffrent leurs cookies). Pour Chrome,\nutilisez plutôt un fichier cookies.txt ci-dessous. » | | `youtube_browser` | à chaque changement |
| « Cookies YouTube : {chemin} » / « Cookies YouTube : aucun fichier » | « Choisir cookies.txt » (infobulle « Fichier de cookies YouTube au format Netscape, exporté depuis\nvotre navigateur avec l'extension « Get cookies.txt LOCALLY ».\nPrioritaire sur le choix du navigateur ci-dessus. ») + « Retirer » | sélecteur « Fichier cookies.txt (format Netscape) » dans `~/Downloads`, filtres « Cookies (*.txt) », « Tous les fichiers (*) » | `cookies_file` | immédiat |
| « Créer un sous-dossier par Artiste » | case (défaut cochée) | | `org_artist` | à chaque changement |
| « Créer un sous-dossier par Album » | case (défaut cochée) | | `org_album` | à chaque changement |
| « Regrouper dans un dossier « Divers » les artistes de moins de » + compteur | case + compteur 2…10, suffixe « titres », défaut 3 ; infobulle « À l'organisation de la bibliothèque, les artistes comptant trop\npeu de titres ne reçoivent pas leur propre dossier : leurs\nfichiers sont rassemblés dans un dossier « Divers ».\nÀ l'inverse, un titre déjà dans « Divers » rejoint le dossier de\nson artiste dès que celui-ci atteint le seuil. » | | `org_divers`, `org_divers_min` | à chaque changement |
| « Clé AcoustID : » | champ texte, placeholder « facultative — identifie les fichiers sans tags par empreinte acoustique » ; infobulle détaillée (acoustid.org/new-application, fpcalc/chromaprint) | | `acoustid_key` (**secret**) | à la fin de l'édition |
| « Dossier de sortie actuel : {chemin} » | « Modifier le dossier » | sélecteur « Dossier d'exportation » | `download_dir` | immédiat |

Changement de dossier de sortie (`select_download_folder`, `:3252-3268`) : met à jour le libellé, la racine des pages Organiser et Qualité, invalide l'index (pastilles ⏳), relance l'index, enregistre, message « Dossier configuré » — « Musique enregistrée dans :\n{dossier} ».

Au chargement (`apply_loaded_settings`, `:447-480`) : le format est appliqué d'abord (ce qui reconstruit la liste des qualités), puis la qualité si elle existe dans la liste. Les signaux d'enregistrement automatique ne sont branchés qu'après.

Sous la carte : la carte « Base de données » (`sync_ui.DatabasePanel`) — hors périmètre.

Les options `org_rename` et `org_pattern` (renommage par motif) ne sont pas réglables ici mais dans « Organiser » ; elles **s'appliquent aux téléchargements** (voir §3.7).

### 2.8 Page Diagnostic (`main_window.py:990-1010`, `1225-1246`)

Titre « Diagnostic ». Carte (fond `rgba(28,30,38,.60)`, padding 24, rayon 12, 13 px) au contenu HTML recalculé à chaque affichage :

```
RAPPORT DE DIAGNOSTIC COMPATIBILITÉ

• Moteur YouTube (yt-dlp) : Installé ✓ | Non Détecté ✗ (Veuillez faire 'pip install yt-dlp')
• Moteur de Métadonnées (mutagen) : Installé ✓ | Non Détecté ✗ (Veuillez faire 'pip install mutagen')
• Convertisseur audio (FFmpeg) : Embarqué avec l'application ✓ | Détecté sur le système ✓ | Non Détecté ✗ (la conversion audio échouera)

• Dossier d'enregistrement actuel : {download_dir}

Note : Si mutagen n'est pas détecté, les jaquettes et tags ID3 ne seront pas injectés à l'intérieur des fichiers extraits.
```

FFmpeg « embarqué » = `ffmpeg/ffmpeg(.exe)` présent à côté de l'application ; « système » = trouvé dans le PATH. Deno n'est pas diagnostiqué (à ajouter côté web, voir §6).

### 2.9 Zone de notification (`main_window.py:484-545`)

- Icône de l'application, infobulle « DeeKeep - Music Downloader ».
- Menu : « Afficher DeeKeep », séparateur, « Quitter ».
- Clic simple ou double sur l'icône → fenêtre restaurée **maximisée** et mise au premier plan.
- Réduire la fenêtre → elle est masquée (retirée de la barre des tâches) ; la **première fois** seulement, bulle « DeeKeep » — « L'application continue en arrière-plan. Cliquez sur l'icône pour la rouvrir. » (3 s).
- Fermer la fenêtre quitte réellement l'application.

**Raccourcis clavier du périmètre** : Entrée (champ de recherche → rechercher ; champ lien → rechercher par lien) ; Échap (ferme le panneau « Dernières recherches »). Aucun autre raccourci dans la fenêtre principale (ceux du lecteur — Espace, ←/→ ±5 s, ↑/↓ volume, P/N — relèvent de `player.py`).

---

## 3. Algorithmes à porter

### 3.1 Recherche Deezer (`DeezerSearchThread.run`, `search_thread.py:42-114`)

Entrées : `query` (texte brut), `search_type` ∈ {« Tout / Titre », « Artiste », « Album », « Playlist »}, et pour l'exploration d'album `album_title`, `album_cover_url`.

**Ordre de décision (le premier qui s'applique gagne)** — détection par **sous-chaîne**, quel que soit le type choisi :

| Ordre | Condition | Extraction de l'ID | Requête(s) | Résultat |
|---|---|---|---|---|
| 1 | `"track/"` dans la requête | `query.split("track/")[-1].split("?")[0].strip()` | `GET https://api.deezer.com/track/{id}` | 1 piste (ou liste vide si la réponse contient `error`) |
| 2 | `"playlist/"` | idem avec `playlist/` | `GET https://api.deezer.com/playlist/{id}/tracks` (**sans `limit`**) | pistes |
| 3 | `"album/"` | idem avec `album/` | si titre ou jaquette manquants : `GET https://api.deezer.com/album/{id}` → `title`, `cover_medium` ou `cover_small` ; puis `GET https://api.deezer.com/album/{id}/tracks?limit=100` | pistes, toutes avec le titre et la jaquette de l'album |
| 4 | `"artist/"` | idem avec `artist/` | `GET https://api.deezer.com/artist/{id}/top?limit=50` | pistes (top 50) |
| 5 | type « Artiste » | — | `GET https://api.deezer.com/search/artist?q={query}&limit=25` | entités artiste |
| 6 | type « Album » | — | `GET https://api.deezer.com/search/album?q={query}&limit=25` | entités album |
| 7 | type « Playlist » | si la requête n'est faite que de chiffres → c'est l'ID ; sinon `GET https://api.deezer.com/search/playlist?q={query}` et ID = `data[0].id` (aucun résultat → liste vide) | `GET https://api.deezer.com/playlist/{id}/tracks?limit=100` | pistes |
| 8 | sinon (« Tout / Titre ») | — | `GET https://api.deezer.com/search?q={query}` (**sans `limit`**) | pistes |

Exemples de liens reconnus : `https://www.deezer.com/fr/track/3135556`, `https://www.deezer.com/playlist/908622995?utm_source=…`, `…/album/302127`, `…/artist/27`. Les liens courts `https://deezer.page.link/…` et `link.deezer.com/…` **ne sont pas** reconnus (à ajouter : suivre la redirection HTTP).

Les internes « artist/{id} » et « album/{id} » utilisés par la navigation passent par les mêmes règles 3 et 4.

**Paramètres communs** : `timeout = 10 s` ; **aucune pagination** (le champ `next` des réponses est ignoré : recherche libre et playlist par lien = 1ʳᵉ page, défaut Deezer ; playlist par recherche/album limités à 100, top artiste à 50, recherches d'entités à 25) ; **aucun jeton/ARL** : API publique anonyme. La requête est interpolée **sans encodage explicite** dans l'URL (voir §6).

**Formatage d'une piste** (`search_thread.py:151-199`) :

```text
id         = str(track.id)
title      = track.title
artist     = track.artist.name   défaut "Artiste Inconnu"
artist_id  = str(track.artist.id) ou ""
album      = album_title fourni, sinon track.album.title   défaut "Album Inconnu"
album_id   = str(track.album.id) ou ""
duration   = format_duration(track.duration)  -> "mm:ss" (minutes sur 2 chiffres au moins, pas d'heures ; "--:--" si invalide)
rank       = track.rank   défaut 0
preview    = track.preview ou ""     (URL MP3 de 30 s)
cover_url  = album_cover_url fourni, sinon track.album.cover_medium, sinon track.album.cover_small
cover_bytes = null (chargé ensuite)
```

**Formatage d'une entité** (`:119-146`) :
- artiste : `{kind:"artist", id, name (défaut "Artiste inconnu"), subtitle:"{nb_album} albums · {nb_fan} fans" (séparateur de milliers = espace), cover_url: picture_medium ou picture_small}` ;
- album : `{kind:"album", id, name: title (défaut "Album inconnu"), subtitle: artist.name (défaut "Artiste inconnu"), cover_url: cover_medium ou cover_small}`.

**Erreurs** : statut HTTP ≠ 200 → « Impossible de récupérer les résultats. » (entités) / « Impossible de récupérer les pistes. » (listes) ; exception (réseau, JSON) → message de l'exception. **Cas non géré** : lien `track/` avec HTTP ≠ 200 → rien n'est émis (la recherche reste « en cours » indéfiniment) — à corriger. Les erreurs Deezer renvoyées en HTTP 200 avec `{"error":{…}}` (ex. quota) donnent une liste vide sans message, sauf pour `track/`.

### 3.2 Correspondance Deezer d'un fichier local (`deezer_best_match`, `search_thread.py:233-281`)

Utilisée par Réencoder (automatique) et par le téléchargement quand `resolve_metadata = true`.

```text
queries = []
si artist et title : queries += 'artist:"{artist}" track:"{title}"'
free = trim("{artist} {title}") ; si non vide : queries += free
pour q dans queries :
    t = deezer_first(q)          # GET https://api.deezer.com/search ?q=q &limit=1, timeout 8 s ; None si erreur/HTTP≠200/vide
    si t : retourner match_from(t)            # SANS vérification
# Repli « morceau nettoyé »
wanted    = core_title(title)
performer = main_artist(artist)
pour q dans ('artist:"{performer}" track:"{wanted}"', trim("{performer} {wanted}")) :
    si q vide ou q déjà essayée : continuer
    t = deezer_first(q)
    si t et contained(wanted, t.title) >= 0.7 (MIN_TITLE_MATCH) : retourner match_from(t)
retourner None
```

`match_from(track)` (`:215-230`) : `{id, title, artist, album, duration "mm:ss", cover_url: album.cover_medium|cover_small, cover_big: album.cover_xl|cover_big|cover_medium}`.

`DeezerMatchThread` (`:284-320`) : traite séquentiellement une liste `(uid, artiste, titre)`, émet `(uid, match|None)` à chaque élément (exception → None), arrêt coopératif (`stop()`), signal de fin.

### 3.3 Fonctions de normalisation de texte (`tag_lookup.py:109-210`) — à recopier à l'identique

```python
_NOISE_RE = re.compile(
    r"(?i)\((?:official[^)]*|lyrics?[^)]*|audio|video|clip|hd|hq|full)\)"
    r"|\[[^\]]*\]"
    r"|\b(?:official (?:video|audio|lyric video)|lyrics?)\b"
    r"|[\s\-_]+\d{2,3}\s*k?b?(?:ps)?\s*$"
)
_LEADING_TRACK_RE = re.compile(r"^\s*\d{1,3}\s*[.)\-_]\s*|^\s*\d{2}\s+")
_QUALIFIER_RE = re.compile(r"\s*[(\[][^)\]]*[)\]]\s*")
_VERSION_TAIL_RE = re.compile(
    r"(?i)[\s\-–_]+(?:full|radio|extended|club|maxi|original|album|single"
    r"|vf|vo)?\s*(?:edit|mix|version|remix|remaster(?:ed)?)\s*\d*$"
)
_GUEST_RE = re.compile(
    r"(?i)\s*[(\[]?\b(?:feat\.?|featuring|ft\.?|avec|with|vs\.?|versus)\b.*$"
)
_STOPWORDS = {"la","le","les","l","un","une","des","de","du","d","et","ou",
              "au","aux","en","the","an","of","to","in","on","and","or"}
```

- `words(text)` : NFKD + `casefold()`, suppression des diacritiques, puis `re.findall(r"[a-z0-9]+")`.
- `_significant(words)` : mots hors `_STOPWORDS` ; si aucun, tous les mots.
- **`contained(needle, haystack)`** : `cible = _significant(words(needle))` ; si vide → **0.0** ; sinon part des mots de la cible présents dans l'ensemble `words(haystack)`. Asymétrique.
- **`clean_stem(name)`** : `splitext(basename(name))[0]` → retire `_LEADING_TRACK_RE` → remplace `_NOISE_RE` par « » → espaces compactés → `strip(" -_.")`.
- **`core_title(title)`** : remplace `_NOISE_RE`, puis `_QUALIFIER_RE` (toute parenthèse/crochet) par « », supprime `_VERSION_TAIL_RE`, compacte, `strip(" -_.")` ; si résultat vide → titre d'origine trimé. Ex. « White Knuckle Ride Full Edit [Www Keepvid Com] » → « White Knuckle Ride ».
- **`main_artist(artist)`** : supprime `_GUEST_RE`, `strip(" -_,&")`, compacte ; vide → artiste d'origine trimé. Ex. « Mad Ice Feat. Irina » → « Mad Ice ».

> Particularités à décider explicitement en .NET : (1) `clean_stem` passe le **titre** par `basename`/`splitext` : « Mr. Brightside » devient « Mr », « AC/DC » devient « DC » ; (2) `[a-z0-9]+` ignore tout alphabet non latin : un titre entièrement cyrillique/japonais donne `contained = 0` → **aucune vidéo jamais retenue** (échec définitif « Aucune vidéo ne correspond… »). Reproduire pour l'iso-fonctionnalité ou corriger (recommandé : `\p{L}\p{N}`).

### 3.4 Téléchargement d'une piste (`TrackDownloaderThread`, `download_thread.py`)

#### 3.4.1 Paramètres du job

| Paramètre | Musique | Réencoder | Remarque |
|---|---|---|---|
| `track_id` | ID Deezer | ID de la correspondance ou "" | |
| `title`, `artist`, `album` | de la ligne | correspondance sinon tags | |
| `save_dir` | `download_dir` | dossier du fichier | |
| `file_format` | combo (minuscules : mp3/flac/wav/m4a) | idem | |
| `quality` | texte du combo | idem | |
| `cover_bytes` | vignette de la ligne | vignette de la ligne | |
| `layout` | `rules_from_settings(settings)` | `None` | None = pas de rangement |
| `tally` | décompte du lot | — | |
| `max_retries` | 10 | 10 | |
| `cookies_browser` | `None` / `chrome` / … | idem | |
| `cookies_file` | chemin ou "" | idem | prioritaire |
| `original_path` | — | chemin du fichier remplacé | |
| `resolve_metadata` | false | true si pas de correspondance | |
| `extra_tags` | {} | {} | tags complémentaires imposés |

#### 3.4.2 Boucle de tentatives (`run`, `:236-285`)

```text
pour attempt = 1..max_retries (10) :
    (ok, msg, info) = download_via_youtube()   # exception -> (False, "Erreur: {e}", {})
    si ok : FIN succès (info)
    si permanent_failure positionné : FIN échec (permanent_failure)     # pas de nouvel essai
    si cookies_browser et "cookie" ∈ msg.lower() :
        FIN échec ("Cookies {navigateur} illisibles : fermez complètement le navigateur, ou choisissez Firefox / « Aucun (anonyme) » dans les Paramètres. ({msg})")
    si attempt < max_retries :
        delay = min(5.0, attempt * 5.0 / max(1, max_retries - 1))      # 0.56, 1.11, …, 4.44 s pour 10 essais (~25 s cumulées)
        progression(5, "Échec, nouvelle tentative dans {delay:.1f} s (essai {attempt+1}/{max_retries})...")
        attendre delay
FIN échec ("Abandonné après {max_retries} tentatives : {dernier msg}")
```

Le texte de progression reçoit le suffixe « (essai {n}/{max}) » dès que n > 1 (`_emit_progress`, `:192-196`).

#### 3.4.3 Une tentative (`download_via_youtube`, `:542-621`)

1. yt-dlp absent → `(False, "yt-dlp est manquant.")`.
2. **Résolution Deezer** (une seule fois par job, seulement si `resolve_metadata`) (`:287-316`) : progression (3, « Recherche Deezer... ») ; `deezer_best_match(artist, title)` ; si trouvé : `track_id` (s'il était vide), `title`, `artist`, `album` remplacés ; jaquette = `GET cover_big` (ou `cover_url`), timeout 8 s → `cover_bytes`. Toute erreur est ignorée.
3. **Tags complémentaires** (une seule fois, si `track_id`) (`:318-342`) : `tag_lookup.deezer_details(track_id)` (voir §4.1) ; chaque champ n'est ajouté à `extra_tags` que s'il y est vide.
4. Progression (5, « Initialisation... »).
5. **Destination** (`_destination`, `:218-234`) : `(dossier, nom)` — voir §3.7. Création du dossier.
6. **Candidats YouTube** (§3.5). Aucun → échec **définitif** : « Aucune vidéo ne correspond à « {main_artist(artist)} - {core_title(title)} » sur YouTube : les tags du fichier ne mènent à rien de reconnaissable. »
7. Pour chaque candidat (dans l'ordre) : si position > 1, progression (5, « Autre version ({pos}/{n})... ») ; extraction (§3.4.4).
   - succès → sortir ;
   - échec **non définitif** (§3.4.5) → retourner `(False, msg)` **immédiatement** (la boucle `run` refera une tentative sur le **même** premier candidat ; on ne dégrade pas le choix) ;
   - échec **définitif** → candidat suivant.
   - Tous définitifs → échec définitif : « La seule vidéo trouvée n'est pas accessible : {msg} » (1 candidat) ou « Aucune des {n} vidéos trouvées n'est accessible : {msg} ».
8. Si `original_path` existe et diffère du fichier produit → **suppression de l'original** (changement d'extension MP3 → FLAC, ou de nom).
9. Progression (100, « Terminé ✓ ») ; retour `(True, "Sauvegardé", info)` avec

```json
{ "title": "...", "artist": "...", "album": "...", "path": "<chemin final>",
  "cover_b64": "<jaquette d'origine (non marquée) en base64 ou null>",
  "tags": { "albumartist": "...", "genre": "...", "date": "1995", "track": "5", "tracktotal": "12", "disc": "1" } }
```
(`tags` = `tag_write.clean_values(extra_tags)` : champs connus non vides, en texte.)

#### 3.4.4 Extraction d'un candidat (`_extract_candidate`, `:416-540`)

**Qualité yt-dlp** (`:425-429`) : `'320'` par défaut ; `'128'` si « 128 » ∈ texte qualité ; sinon `'0'` si format flac ou wav.

**Progression** (hook) : `downloading` → `pct = int(downloaded_bytes / (total_bytes | total_bytes_estimate | 1) × 90)`, émis `max(5, pct)` avec « Extraction... » ; `finished` → (95, « Finalisation... »).

**Dossier de travail** : `tempfile.mkdtemp(prefix=".dk_", dir=final_dir)` — un dossier temporaire unique **dans le dossier de destination** (même volume → déplacement atomique ; évite les collisions entre extractions parallèles). Toujours supprimé en fin (`finally`).

**Options yt-dlp exactes** (`:447-490`) :

```python
ydl_opts = {
    'format': 'bestaudio/best',
    'outtmpl': os.path.join(work_dir, f"{filename_clean}.%(ext)s"),
    'noplaylist': True,
    'quiet': True,
    'logger': _YtdlpLogger(),            # journal muet (debug/warning/error ignorés)
    'overwrites': True,
    'remote_components': ['ejs:github'], # = CLI --remote-components ejs:github
    'extractor_args': {'youtube': {'player_client': ['web_embedded', 'mweb', 'tv_simply']}},
    'progress_hooks': [ytdl_hook],
    'postprocessors': [{
        'key': 'FFmpegExtractAudio',
        'preferredcodec': file_format,   # 'mp3' | 'flac' | 'wav' | 'm4a'
        'preferredquality': ytdl_quality # '320' | '128' | '0'
    }],
}
# Identification (cookies) :
if cookies_file and os.path.exists(cookies_file): ydl_opts['cookiefile'] = cookies_file
elif cookies_browser:                              ydl_opts['cookiesfrombrowser'] = (cookies_browser,)
# FFmpeg embarqué :
if ffmpeg_location(): ydl_opts['ffmpeg_location'] = <dossier ffmpeg>
# Téléchargement :
ydl.download([f"https://www.youtube.com/watch?v={video_id}"])
```

Équivalent ligne de commande (pour un appel du binaire `yt-dlp` depuis .NET) :

```bash
yt-dlp -f "bestaudio/best" --no-playlist --force-overwrites \
  --remote-components ejs:github \
  --extractor-args "youtube:player_client=web_embedded,mweb,tv_simply" \
  -x --audio-format mp3 --audio-quality 320K \      # 128K | 0 (lossless/VBR max)
  [--cookies cookies.txt | --cookies-from-browser firefox] \
  [--ffmpeg-location /opt/deekeep/ffmpeg] \
  --newline --progress-template "download:%(progress.downloaded_bytes)s/%(progress.total_bytes)s/%(progress.total_bytes_estimate)s" \
  -o "<work_dir>/<nom>.%(ext)s" "https://www.youtube.com/watch?v=<id>"
```
(Avec `preferredquality`, yt-dlp interprète une valeur < 10 comme une qualité VBR `-q:a`, sinon comme un débit en kb/s : `'0'` = meilleure qualité VBR pour MP3, ignoré pour FLAC/WAV.)

**Après le téléchargement** :
1. Fichier attendu `work_dir/{nom}.{format}` ; s'il manque, repli sur le **plus gros** fichier du dossier de travail hors `.part`/`.ytdl` ; aucun → « Fichier extrait introuvable. ».
2. **Tags + jaquette** (§3.4.6) sur le fichier temporaire.
3. **Suppression des homonymes** dans le dossier final (sauf `original_path`) : `{nom}.{fmt}`, `sanitize("{title} - {artist}").{fmt}`, `sanitize("{artist} - {title}").{fmt}`.
4. `os.replace(temp, final)` (remplacement atomique).
5. Exception yt-dlp → `(False, "Erreur: {message}")`.

#### 3.4.5 Échecs définitifs vs passagers (`:68-78`)

Un message est **définitif** s'il contient (insensible à la casse) l'un de :

```python
_PERMANENT_MARKERS = (
    "confirm your age", "age-restricted", "private video",
    "removed by the uploader", "members-only", "this video is unavailable",
    "video unavailable", "not available in your country", "who has blocked it",
)
```

Tout autre échec (ex. « HTTP Error 403: Forbidden », coupure réseau) est **passager** → nouvel essai. Cas particulier cookies navigateur (§3.4.2).

#### 3.4.6 Métadonnées et jaquette (`apply_metadata`, `:623-643` ; `tag_write.py`)

- Rien si mutagen absent ou fichier inexistant.
- Valeurs = `extra_tags` + `{title, artist, album, format: FORMAT}` (« format » n'est pas un champ écrit).
- **Marquage de la jaquette** (`utils.stamp_app_icon`, `utils.py:278-320`) : si la jaquette décodée mesure ≥ **96 px** de petit côté → icône de l'application de côté `max(16, round(petit_côté × 0.18))` collée **en haut à droite** avec une marge `max(1, round(petit_côté × 0.04))`, image convertie en RGB et réencodée **JPEG qualité 92**. Sinon / en cas d'erreur : octets d'origine. L'historique conserve la jaquette **non marquée**.
- Écriture (`tag_write.write`) — champs reconnus `title, artist, album, albumartist, genre, date, track, tracktotal, disc, disctotal, publisher` (valeurs vides ignorées) :
  - **MP3** : ID3 (version par défaut de mutagen : 2.4), encodage UTF-8 ; `TIT2, TPE1, TALB, TPE2 (albumartist), TCON (genre), TDRC (date), TPUB (publisher)` ; `TRCK = "track/tracktotal"` et `TPOS = "disc/disctotal"` (en reprenant numéro/total existants ; jamais de « /12 » sans numéro) ; jaquette `APIC` type 3 (couverture avant), mime `image/jpeg`, description « Cover ».
  - **FLAC** : commentaires Vorbis `title, artist, album, albumartist, genre, date, tracknumber, tracktotal, discnumber, disctotal, organization` ; image type 3 jpeg « Cover » (remplace les images existantes).
  - **Autres (M4A, WAV…)** : interface « easy » de mutagen, champs `title, artist, album, albumartist, genre, date, tracknumber, discnumber` ; **pas de jaquette** ; champ refusé ignoré. (WAV peut ne rien recevoir.)
- Échec : message console « [Tags] Erreur lors de l'application : … » — n'empêche pas le succès.

### 3.5 Choix de la vidéo YouTube (`search_queries`, `_search`, `_youtube_candidates`, `download_thread.py:81-101`, `344-414`)

**Requêtes, de la plus fidèle à la plus large** :

```text
performer = main_artist(artist)
candidats = [
  "{artist} - {title} (Official Audio)",
  "{performer} - {clean_stem(title)}",
  "{performer} - {core_title(title)}",
]
pour chaque : q = re.sub(r"\s+", " ", c).strip(" -") ; garder si non vide et pas déjà présente
```

**Recherche à plat** (`_search`) : yt-dlp `extract_info("ytsearch3:{q}", download=False)` avec `{quiet, logger muet, skip_download, extract_flat: True, noplaylist}` → liste `(id, titre)` des entrées ayant un id ; toute exception → liste vide. (**SEARCH_CANDIDATES = 3**.) CLI : `yt-dlp --flat-playlist -J "ytsearch3:<q>"`.

**Filtrage et score** :

```text
wanted = core_title(title) ; art = main_artist(artist)
kept = [] ; seen = {}
pour q dans requêtes :
    progression(4, "Recherche YouTube...")
    pour (vid, name) dans search(q) :
        si vid ∈ seen : continuer ; seen += vid
        score = contained(wanted, name)
        si score < 0.7 : rejeter                     # MIN_TITLE_MATCH
        si contained(art, name) >= 0.7 : score += 1   # l'artiste figure dans le titre de la vidéo
        kept += (score, vid, name)
    si kept non vide : arrêter (ne pas essayer les requêtes plus larges)
trier kept par score décroissant, TRI STABLE (à égalité, ordre de pertinence YouTube)
```

La liste est **mémorisée** pour les tentatives suivantes du même job (la recherche n'est pas refaite).

### 3.6 Jaquettes (`image_thread.py`)

**`CoverDownloadThread(items, size)`** : traite séquentiellement `(index, url)` ; pour `size="small"`, remplace dans l'URL `cover_medium`→`cover_small` et `picture_medium`→`picture_small` (**sans effet sur les URL réelles du CDN Deezer**, qui ne contiennent pas ces mots : voir §6) ; `GET` timeout **6 s** ; émet `(index, octets)` si HTTP 200 ; erreurs ignorées ; arrêt coopératif.

**`CoverLookupThread(artist, title, cover_url)`** :
1. si `cover_url` → la télécharger ;
2. sinon `GET https://api.deezer.com/search?q=artist:"{artist}" track:"{title}"&limit=1` (timeout 8 s) ; vide → `GET …/search?q={artist} {title}&limit=1` ; URL = `album.cover_big` | `cover_medium` | `cover_small` du premier résultat ;
3. téléchargement (timeout 8 s) ; émet les octets ou `null`.

Usage particulier : pour une **requête de l'historique**, l'appel est `CoverLookupThread("", requête)` → première requête `artist:"" track:"{requête}"` puis repli ` {requête}`.

Vignettes d'affichage : Musique 45×45 (rapport conservé) ; dialogue 50×50 ; popup recherches 42×42 ; historique 46×46 **recadré au centre en carré et arrondi (rayon 6)** ; en-tête d'album 90×90 ; popup 320×320.

### 3.7 Destination d'un fichier extrait (`library_rules.py`)

**Règles** (`rules_from_settings`, `:129-144`) :

```text
artist     = bool(org_artist, défaut true)
album      = bool(org_album, défaut true)
rename     = bool(org_rename, défaut false)
pattern    = org_pattern ou "%titre% - %artiste%"
divers     = bool(org_divers, défaut false)
divers_min = max(1, int(org_divers_min ou 3))
```

**Nom de fichier** (`target_stem`, `:385-401`) : motif = `pattern` si `rename`, sinon **« %titre% - %artiste% »** (convention historique), y compris quand `layout = None` (Réencoder). `stem = safe_component(format_name(motif, tags))` ; vide → « {titre} - {artiste} » → « {titre} » → « {artiste} » → « piste ».
- `sanitize_filename(name) = re.sub(r'[\\/*?:"<>|]', "", name)` (`utils.py:150-152`) — pas de compactage des espaces.
- `safe_component(name)` : `sanitize_filename`, caractères `[\x00-\x1f]` → espace, `strip()`, puis suppression des « . » et espaces finaux.
- `format_name` remplace les balises `%titre% %artiste% %artiste_album% %artiste_album_ou_artiste% %album% %piste% %piste_00% %total_pistes% %disque% %disque_00% %total_disques% %date% %annee% %decennie% %genre% %compositeur% %editeur% %commentaire% %compilation% %nom_fichier% %dossier_parent% %format% %debit%` (alias `%piste2% %disque2% %nb_pistes% %nb_disques%`) puis recolle les séparateurs orphelins (détail dans le document « Organiser »).

**Dossier** (`target_dir`, `:349-382`) :

```text
si layout absent ou !artist : dossier = racine          # (Réencoder : dossier du fichier)
nom = trim(tags.artist) ; vide -> racine
dossier_artiste = tally.folder(nom)        # écriture du dossier existant si l'artiste en a déjà un, sinon safe_component(artiste principal)
si divers et tally.count(nom, exclude=original_path) < divers_min :
    retourner racine/"Divers"              # plat : ni artiste ni album
chemin = racine/dossier_artiste
si album et safe_component(album) non vide : chemin /= safe_component(album)
```

**Décompte « Divers » (`Tally`, `:169-346`)** — un objet partagé par tous les téléchargements d'un lot, protégé par verrou :
- `artist_key(name) = safe_component(name).casefold()` ;
- artiste principal (`main_artist(name, known)`) : coupe toujours au premier marqueur d'invité `(?i)\s+(?:feat\.?|featuring|ft\.?|vs\.?|versus|with|avec|présente|presente|presents?)\s+` ; coupe aux séparateurs ambigus `(?i)\s*(?:&|\+|,|/|\bx\b|\band\b|\bet\b)\s*` **seulement** si le préfixe correspond déjà à un dossier d'artiste existant (« Calvin Harris & Sam Smith » → « Calvin Harris » si ce dossier existe ; « Earth, Wind & Fire » reste entier) ;
- dossiers d'artiste = dossiers de 1ᵉʳ niveau de la racine sauf « Divers » ;
- titres « en vrac » = fichiers audio à la racine et dans « Divers », attribués à l'artiste lu dans le nom « Titre - Artiste » (partie après le dernier « ␣-␣ ») ;
- `on_disk(artiste)` = vrac + nombre de fichiers audio (récursif) du dossier de l'artiste (mis en cache) ;
- `reserve(artiste)` : +1 au lot (appelé à la **mise en file**) ;
- `count(artiste, exclude)` = `max(0, on_disk − (1 si le fichier remplacé est compté)) + max(réservés, 1)`.

### 3.8 Pastille de présence (`library_index.lookup`, `library_index.py:474-578`)

**Construction de l'index** (`LibraryIndex.build`, `:343-355`) : pour chaque fichier audio du dossier de dépôt (récursif), métadonnées = historique des téléchargements (prioritaire), sinon cache `tracks` (`SELECT path,title,artist,album FROM tracks WHERE deleted=0`), sinon **déduites du chemin** :
- `stem` = nom sans extension ; 1ᵉʳ niveau de dossier = artiste (sauf « Divers ») ; 2ᵉ niveau = album (si ≥ 3 niveaux) ;
- découpe « Titre - Artiste » sur `\s+-\s+` (gauche = tout sauf le dernier morceau) ; si le dossier d'artiste correspond (forme canonique) à un côté, il tranche ; sinon la convention « Titre - Artiste » et **la lecture inverse sont indexées toutes deux**.

**Normalisations** (à recopier) :

```python
_FEAT_RE = re.compile(r"(?i)\s*\b(?:feat\.?|featuring|ft\.?|avec|with|vs\.?|versus)\s+")
_BRACKETS_RE = re.compile(r"[\(\[\{][^\)\]\}]*[\)\]\}]")
_VARIANT_RE = re.compile(
    r"(?i)\b(?:remix(?:es|ed)?|remaster(?:ed|isation|ise|isé|isée)?|live"
    r"|version|edit|mix|instrumental|acoustic|acoustique|unplugged|extended"
    r"|radio|single|album|original|explicit|clean|cover|karaoke|karaoké|demo"
    r"|mono|stereo|stéréo|bonus|deluxe|edition|édition|maxi|club|dub"
    r"|bootleg|reprise|inédit|inedit|remake|rework|slowed)\b")
_ARTIST_NOISE = {"and","et","the","les","los","feat","featuring","ft","with",
                 "avec","vs","versus","presents","present","presente","dj"}
TITLE_OVERLAP_MIN = 0.5 ; TITLE_RATIO_MIN = 0.85 ; ARTIST_OVERLAP_MIN = 0.5
```

- `canonical(t)` : NFKD + casefold, sans diacritiques ; `&`→« and », `$`→« s » ; retire `[\(\[]\s*(?:copy|copie|\d+)\s*[\)\]]` et `^\s*\d{1,3}\s*[-\._]\s*` ; tout non `[a-z0-9]` → espace ; compacte.
- `title_key(t)` : groupes entre parenthèses contenant un marqueur d'invité supprimés, coupe au premier marqueur d'invité, `canonical`.
- `base_key(t)` : groupes entre parenthèses contenant un mot de variante ou d'invité supprimés ; découpe sur `\s+-\s+` et retire en fin les segments contenant un mot de variante ; `canonical`.
- `artist_tokens(a)` = mots de `canonical(a)` hors `_ARTIST_NOISE` ; `artist_key` = ces mots triés joints ; `main_artist_key(a)` = `artist_key` de la partie avant le premier marqueur d'invité.
- `_overlap(A,B) = |A∩B| / min(|A|,|B|)` (0 si vide) ; `_ratio` = `difflib.SequenceMatcher(None, a, b).ratio()` (équivalent .NET à écrire : algorithme de Ratcliff/Obershelp, `2·M/T`).

**Décision (premier rapprochement qui aboutit)** :

| # | Test | État | Motif |
|---|---|---|---|
| — | `title_key` vide ou index vide | ABSENT | |
| 1 | `artist_key|title_key` identique | PRESENT | « Déjà présent (même titre, même artiste) » |
| 2 | nom de fichier canonique = `canonical("{title} - {artist}")` ou `canonical("{artist} - {title}")` | PRESENT | « Déjà présent (fichier du même nom) » |
| 3 | `main_artist_key|title_key` identique | SIMILAR | « Doublon probable : même titre, même artiste principal (l'invité diffère) » |
| 4a | même `title_key` et chevauchement d'artistes ≥ 0.5 | SIMILAR | « Doublon possible : même titre, artiste similaire » |
| 4b | même `title_key` et même album canonique | SIMILAR | « Doublon possible : même titre, même album » |
| 5 | même `base_key` et (même artiste principal ou chevauchement ≥ 0.5) | SIMILAR | « Doublon possible : autre version du même titre » |
| 6 | candidats par mots du titre (index inversé) avec chevauchement de mots ≥ 0.5, puis artiste compatible (même principal ou chevauchement ≥ 0.5) **et** ratio des titres ≥ 0.85 | SIMILAR | « Doublon possible : titre proche, artiste compatible » |
| — | sinon | ABSENT | « Absent du dossier de dépôt » |

Le résultat porte la liste des chemins en cause (dédoublonnée). Index pas encore prêt → UNKNOWN.
Mises à jour incrémentales : `add(path, title, artist, album)` après chaque extraction ; `remove(path)` après suppression (tables reconstruites depuis les lectures restantes).

### 3.9 Filtre « déjà au format cible » (Réencoder, `main_window.py:2161-2186`)

```text
cible_debit = 128 si "128" ∈ qualité ; 320 si "320" ∈ qualité ; sinon 0
au_format(fichier) :
    si upper(fichier.format) ≠ upper(format_cible) : faux
    si format_cible ∈ {FLAC, WAV} : vrai
    si cible_debit = 0 : vrai
    retourner |fichier.bitrate − cible_debit| ≤ 5          # un 320 n'est PAS « au format » d'une cible 128
```
Le filtre masque les lignes au format ; les lignes masquées sont exclues de « Tout cocher », de la conversion et de la file du lecteur.

### 3.10 Réparation des chemins de l'historique (`main_window.py:201-257`)

```text
claimed = {} ; stale = []
pour (rang, entrée) dans historique avec chemin :
    si index.has_path(chemin) ou fichier existe : claimed += clé(chemin)
    sinon stale += (rang, entrée)
resolved = {}
pour (rang, entrée) dans stale :
    found = resolved[clé(entrée.path)]
    si absent :
        libres = [c ∈ index.paths_named(nom de fichier de entrée.path) si clé(c) ∉ claimed]   # nom comparé sans casse
        si |libres| ≠ 1 : continuer                      # ambigu ou introuvable : on ne touche pas
        found = libres[0] ; resolved[clé(entrée.path)] = found ; claimed += clé(found)
    entrée.path = found     # deux lignes de même chemin d'origine -> même fichier
```
`clé(chemin) = normcase(normpath(chemin))` — **insensible à la casse sous Windows uniquement** (voir §6).

### 3.11 Progressions et compteurs

- Page Musique : globale = `int(Σ progression des lignes / nombre de lignes)` ; 0 si tableau vide.
- Réencoder : globale = `int(Σ _pct des lignes du lot / taille du lot)` ; une ligne finie compte 100 (succès) ou 0 (échec).
- Badge Historique = recherches + téléchargements.
- Durées : `"mm:ss"` ↔ secondes = `Σ int(partie) × 60^i` sur les parties inversées (`h:mm:ss` accepté) ; erreur → 0.

---

## 4. Intégrations externes

### 4.1 API publique Deezer (`https://api.deezer.com`)

Aucune authentification, aucun jeton, aucun ARL utilisé dans ce périmètre. (La clé `arl` figure dans la liste des secrets de `db_init.SECRET_KEYS` mais **aucun code ne la lit** : vestige, à ne pas reprendre sauf besoin futur.)

| Endpoint | Paramètres | Timeout | Appelant |
|---|---|---|---|
| `GET /search` | `q` (texte libre ou syntaxe avancée `artist:"…" track:"…"`), `limit` (1 pour correspondances/jaquettes ; absent pour la recherche « Tout/Titre ») | 10 s (recherche) / 8 s (correspondance, jaquette) | recherche, `deezer_best_match`, `CoverLookupThread` |
| `GET /search/artist` | `q`, `limit=25` | 10 s | recherche Artiste |
| `GET /search/album` | `q`, `limit=25` | 10 s | recherche Album |
| `GET /search/playlist` | `q` (pas de limit ; seul le 1ᵉʳ résultat sert) | 10 s | recherche Playlist |
| `GET /track/{id}` | — | 10 s | lien piste ; `deezer_details` |
| `GET /album/{id}` | — | 10 s | méta album (titre, jaquette) ; `deezer_details` (genre, artiste, nb pistes, date) — **mis en cache par album** en mémoire (échecs non mis en cache) |
| `GET /album/{id}/tracks` | `limit=100` | 10 s | exploration d'album / lien |
| `GET /playlist/{id}/tracks` | `limit=100` (recherche) ou aucun (lien) | 10 s | playlist |
| `GET /artist/{id}/top` | `limit=50` | 10 s | top artiste |
| Images CDN (`cover_*`, `picture_*`) | — | 6 s (vignettes) / 8 s (jaquettes) | jaquettes |
| URL `preview` (MP3 30 s, CDN Deezer) | — | lecture directe | écoute d'extrait |

Champs consommés : piste `id, title, duration, rank, preview, artist{id,name}, album{id,title,cover_small,cover_medium,cover_big,cover_xl}, track_position, disk_number, release_date` ; artiste `id, name, nb_album, nb_fan, picture_small, picture_medium` ; album `id, title, cover_*, artist{name}, genres{data[0].name}, nb_tracks, release_date`.

**`deezer_details(track_id)`** (`tag_lookup.py:474-502`) → `{track: track_position, disc: disk_number, date: année(track.release_date) sinon année(album.release_date), genre: album.genres.data[0].name, albumartist: album.artist.name, tracktotal: album.nb_tracks}` (champs vides omis ; année = 4 premiers caractères s'ils sont des chiffres). Une réponse contenant `error` est traitée comme vide.

Limites connues de Deezer (non gérées par le code) : quota d'environ **50 requêtes / 5 s par IP** (erreur `Quota limit exceeded`, code 4, renvoyée en HTTP 200) ; URL `preview` signées et à durée de validité limitée.

### 4.2 YouTube via yt-dlp

- Bibliothèque Python `yt_dlp` (optionnelle ; son absence désactive l'extraction). Recherche `ytsearch3:` à plat, téléchargement par URL `https://www.youtube.com/watch?v={id}`.
- **Clients de lecture imposés** : `['web_embedded', 'mweb', 'tv_simply']` (`download_thread.py:49`) — `web_embedded` en tête car seul à publier des flux audio seuls (opus ≈131 kb/s, m4a ≈129 kb/s) ; les deux autres en repli (flux muxé, audio extrait par FFmpeg).
- **Composants distants** `ejs:github` : yt-dlp télécharge depuis GitHub les scripts de résolution des défis JavaScript ; nécessite un **runtime JavaScript (Deno)** dans le PATH (embarqué dans `deno/`).
- Identification : `cookiefile` (Netscape) prioritaire, sinon `cookiesfrombrowser` (lecture des cookies d'un navigateur installé localement).
- Journal yt-dlp entièrement muet ; seul le message d'exception remonte (c'est lui qui est classé définitif/passager et affiché en infobulle).

### 4.3 FFmpeg

- Utilisé uniquement via le post-traitement yt-dlp `FFmpegExtractAudio` (conversion vers mp3/flac/wav/m4a). Emplacement : dossier `ffmpeg/` embarqué (`ffmpeg.exe`, `ffprobe.exe`) passé en `ffmpeg_location`, sinon PATH.

### 4.4 mutagen (tags)

- Lecture : jaquette intégrée (historique) ; écriture : §3.4.6. Équivalent .NET suggéré : **TagLib#** (ID3v2.4, Vorbis/FLAC Picture, MP4 `covr`).

### 4.5 Qt / système

- Qt Multimedia pour les extraits et le lecteur (remplacé par `<audio>` HTML5 côté web).
- Shell Windows `SHOpenFolderAndSelectItems` / `explorer /select,` pour « Ouvrir le dossier ».
- Icône de zone de notification, AppUserModelID Windows.
- Dessin/composition d'image (QImage) pour le marquage des jaquettes → ImageSharp/SkiaSharp côté .NET.

---

## 5. Données

### 5.1 Clés de paramètres (`app_settings`) du périmètre

Stockage actuel : JSON `%APPDATA%\DeeKeep\deekeep_config.json` (Linux : `$XDG_CONFIG_HOME/DeeKeep` ou `~/.config/DeeKeep`) **et** miroir dans la table SQLite `app_settings(key TEXT PK, value TEXT JSON, updated_at, deleted, h)`. Valeur encodée en JSON (type préservé).

| Clé | Type | Défaut | Effet | Écrit par |
|---|---|---|---|---|
| `download_dir` | chaîne (chemin) | `~/Music` | Dossier de dépôt : racine des téléchargements, de l'index de présence, d'Organiser et de Qualité | Paramètres |
| `file_format` | chaîne : `MP3`/`FLAC`/`WAV`/`M4A` | `MP3` | Codec cible de l'extraction (Musique, Réencoder, Qualité) ; filtre « au format cible » | Paramètres |
| `quality` | chaîne : `320 kbps (HQ)` / `128 kbps (Standard)` / `Lossless (Qualité Source)` | `320 kbps (HQ)` | Débit yt-dlp (`320`/`128`/`0`) ; débit cible du filtre | Paramètres |
| `youtube_browser` | chaîne : `Aucun (anonyme)`/`Chrome`/`Edge`/`Firefox`/`Brave`/`Opera`/`Vivaldi` | `Aucun (anonyme)` | Navigateur dont yt-dlp lit les cookies (minuscules) | Paramètres |
| `cookies_file` | chaîne (chemin) | `""` | Fichier cookies.txt Netscape, prioritaire | Paramètres |
| `org_artist` | booléen | `true` | Sous-dossier par artiste (téléchargements + Organiser) | Paramètres / Organiser |
| `org_album` | booléen | `true` | Sous-dossier par album | Paramètres / Organiser |
| `org_divers` | booléen | `false` | Regroupement « Divers » des artistes peu représentés | Paramètres / Organiser |
| `org_divers_min` | entier (UI 2…10) | `3` | Seuil « Divers » (min. effectif 1) | Paramètres / Organiser |
| `org_rename` | booléen | `false` | Applique `org_pattern` au nom des fichiers extraits | Organiser (lu ici) |
| `org_pattern` | chaîne | `%titre% - %artiste%` | Motif de nommage | Organiser (lu ici) |
| `acoustid_key` | chaîne | `""` | Clé AcoustID (Organiser → Compléter les tags) ; **secret** : non synchronisé sauf `cloud_sync_secrets` | Paramètres |
| `cloud_sync_secrets` | booléen | absent (= faux) | Autorise l'envoi des secrets (`acoustid_key`, `arl`) vers la base en ligne | (aucune UI dans ce périmètre) |
| `arl` | chaîne | absent | **Non utilisé** (vestige listé comme secret) | — |

Les autres clés du fichier (`org_remove_empty`, `org_report_untagged`, `org_keep_albums`, `org_dedup`, `org_filters_open`, `qual_*`, etc.) appartiennent aux pages Organiser / Qualité / Bibliothèque.

### 5.2 Historique des recherches

- En mémoire / JSON : `search_history` = liste de chaînes, **index 0 = la plus récente** ; dédoublonnée à l'insertion (correspondance exacte) ; pas de limite ; triable par l'utilisateur (l'ordre trié est persisté).
- Base : table `searches(query TEXT PK, position INTEGER, updated_at, deleted, h)` ; à la collecte, dédoublonnage insensible à la casse, `position` = rang d'origine.

### 5.3 Historique des téléchargements

- En mémoire / JSON : `downloaded_history` = liste d'objets ajoutés en fin à chaque succès (Musique) :

```json
{ "title": "Titanium", "artist": "David Guetta", "album": "Nothing but the Beat",
  "path": "D:\\Musique\\David Guetta\\Nothing but the Beat\\Titanium - David Guetta.mp3",
  "cover_b64": "<JPEG/PNG base64, jaquette non marquée>",
  "tags": { "albumartist": "David Guetta", "genre": "Dance", "date": "2011", "track": "3", "tracktotal": "14" } }
```
  (Les entrées anciennes peuvent ne pas avoir `tags` ; `cover_b64` peut être ajouté a posteriori depuis la pochette intégrée ou Deezer.) Les conversions Réencoder **ne sont pas** ajoutées à l'historique.
- Base : fondu dans `tracks` (clé `path`) : `in_history=1`, `history_pos` = rang, `history_cover_sha1` → table `covers(sha1 PK, mime, bytes, width, height, data BLOB)` (jaquettes dédoublonnées par SHA-1) ; `source` = `history` (fichier absent) / `both` ; `tags` → colonnes `album_artist`, `genre`, `year`, `track_no` (« n/total »). Relecture : `SELECT path,title,artist,album,COALESCE(history_cover_sha1,cover_sha1) FROM tracks WHERE in_history=1 AND deleted=0 ORDER BY history_pos`.

### 5.4 État non persisté (à décider pour le web)

| Donnée | Actuel | Recommandation web |
|---|---|---|
| File de la page Musique (lignes, statuts, progression) | mémoire, perdue au redémarrage | table de jobs persistante (`download_jobs`) |
| Liste Réencoder | mémoire | idem (`reencode_jobs`) ou session |
| Compteurs de session | mémoire | dérivés des jobs |
| Cache des vignettes de recherches récentes | mémoire (module) | cache HTTP / table `covers` |
| Cache des albums Deezer (`deezer_details`) | mémoire (processus) | cache distribué / mémoire du service |

### 5.5 Disque

| Élément | Emplacement |
|---|---|
| Fichiers extraits (Musique) | `download_dir/[Artiste | Divers]/[Album]/<nom>.<ext>` selon règles §3.7 |
| Fichiers convertis (Réencoder) | dossier d'origine, nom « Titre - Artiste.<ext> » ; original supprimé si chemin différent |
| Dossiers temporaires | `<dossier final>/.dk_XXXXXXXX/` (supprimés en fin de tentative) |
| Fichiers supprimés | homonymes « {nom} », « Titre - Artiste », « Artiste - Titre » dans le dossier final ; doublons choisis dans « Dédoublonner… » |
| Configuration | `%APPDATA%\DeeKeep\deekeep_config.json` (+ `.tmp`) |
| Cache SQLite | `%APPDATA%\DeeKeep\deekeep_cache.db` |
| Glyphes de cases à cocher (SVG) | `%APPDATA%\DeeKeep\icons\check_18.svg`, `check_18_disabled.svg`, `check_16.svg`, `dash_16.svg` (sans objet en web) |

---

## 6. Points d'attention pour la version web / serveur Linux

### 6.1 Dépendances au poste Windows / bureau local

| Fonction actuelle | Problème | Proposition web |
|---|---|---|
| Sélecteurs natifs (fichiers, dossiers, cookies.txt) | Le navigateur ne voit pas le disque du serveur | Explorateur de dossiers **côté serveur** (API listant les répertoires autorisés) pour `download_dir` et Réencoder ; **téléversement** du cookies.txt (stocké chiffré côté serveur) |
| « Ouvrir le dossier » (explorateur Windows) | Sans objet | Remplacer par « Télécharger le fichier », « Copier le chemin », ou lien vers la page Bibliothèque filtrée |
| Cookies lus dans un navigateur installé (`cookiesfrombrowser`) | Aucun navigateur sur le serveur ; ne fonctionnait déjà que pour Firefox sous Windows | Ne garder **que** le fichier cookies.txt (téléversé) ; retirer la liste des navigateurs ou la réserver à un déploiement local |
| Zone de notification, AppUserModelID, minimisation | Sans objet | Notifications navigateur / toast in-app pour fin de lot |
| Qt Multimedia (extraits, lecteur) | Sans objet | `<audio>` HTML5 : extrait Deezer lu directement par le navigateur ; fichiers locaux servis en streaming par le serveur (requêtes `Range`), règle « un seul son à la fois » à conserver |
| `ffmpeg.exe`, `deno.exe` embarqués | Binaires Windows | Paquets Linux (`ffmpeg`, `deno`) dans l'image Docker ; chemin configurable ; **ajouter Deno et la version de yt-dlp au Diagnostic** |
| `%APPDATA%` | | Répertoire de données configurable (`/var/lib/deekeep`) |
| `os.path.normcase` (comparaison de chemins sans casse) | **Identité sous Linux** : la réparation des chemins, l'index (`_match_key`), le dédoublonnage par chemin et `Tally._excluded` deviennent sensibles à la casse | Choisir explicitement : comparer en `OrdinalIgnoreCase` pour reproduire le comportement Windows (recommandé tant que la bibliothèque vient d'un disque Windows/NTFS), ou en ordinal strict |
| Séparateurs `\` dans les chemins stockés (historique, `tracks.path`) | Données existantes contiennent des chemins Windows (`D:\…`) | Migration : table de correspondance de préfixes (`D:\Musique` → `/srv/musique`) et normalisation des séparateurs ; `paths_named` (recherche par nom de fichier) aide à réparer |
| `sanitize_filename` (caractères interdits Windows) | Linux accepte ces caractères | **Conserver la même règle** pour que les noms restent identiques entre l'ancienne bibliothèque et la nouvelle (sinon l'index et Organiser verraient tout à renommer) |
| `tempfile.mkdtemp(dir=final_dir)` + `os.replace` | OK sous Linux si même système de fichiers | Conserver ; **nettoyer au démarrage du service** les `.dk_*` orphelins ; exclure `.dk_*` du parcours d'index |
| Thread tué (`terminate()`) pour annuler | Pas d'équivalent sûr en .NET | Lancer yt-dlp en **processus fils** et le tuer (arbre de processus, y compris ffmpeg) ; jeton d'annulation ; nettoyer le dossier temporaire ; **relancer la pompe** après annulation (corrige le défaut §2.3.5) |

### 6.2 YouTube depuis un serveur

- Les IP de centres de données sont fréquemment bloquées (« Sign in to confirm you're not a bot », 403 systématiques). Prévoir : cookies d'un compte dédié, éventuel proxy résidentiel configurable, et la possibilité de jetons PO. Le classement « définitif / passager » (§3.4.5) doit être enrichi de « sign in to confirm » (actuellement **passager** → 10 essais inutiles).
- yt-dlp évolue toutes les semaines : prévoir une mise à jour automatisée ou manuelle (bouton dans Diagnostic), et figer la liste `player_client` en configuration.
- `remote_components ejs:github` implique un accès sortant à GitHub et l'exécution de scripts téléchargés : à valider côté sécurité (ou fournir les composants EJS localement).
- Parallélisme : 10 extractions simultanées par écran **et** 10 pour Réencoder, sans plafond global. Côté serveur : file de jobs unique avec plafond global configurable (CPU ffmpeg, bande passante, risque de bannissement YouTube).

### 6.3 Deezer depuis un serveur

- Quota ≈ 50 requêtes / 5 s par IP, partagé par tous les utilisateurs et par Réencoder (jusqu'à 150 correspondances × 2 à 4 requêtes) + `deezer_details` (2 requêtes par piste) + jaquettes. Prévoir un **limiteur de débit** centralisé et la détection de `{"error":{"code":4}}` (aujourd'hui traitée comme « aucun résultat »).
- **Encoder la requête** (`q`) : aujourd'hui `?q={query}` est interpolé tel quel ; une requête contenant `&`, `#` ou `+` est tronquée ou altérée (« Simon & Garfunkel » !). Utiliser un vrai paramètre de requête.
- Pagination absente : décider s'il faut suivre `next` (playlists > 100 titres, albums > 100 pistes, recherche libre limitée à la 1ʳᵉ page).
- Liens courts `deezer.page.link` / `link.deezer.com` à résoudre par redirection.
- Le remplacement `cover_medium`→`cover_small` n'a pas d'effet sur les vraies URL ; pour des vignettes légères, remplacer le segment de taille du chemin CDN (`/250x250-` → `/56x56-`) ou utiliser `cover_small` directement. **Attention** : la jaquette intégrée aux fichiers de la page Musique est **la vignette de la recherche** (250×250, `cover_medium`) — seule la résolution Deezer (Réencoder sans correspondance) récupère `cover_xl`/`cover_big`. La version web peut améliorer la qualité en récupérant `cover_xl` au moment du téléchargement (choix fonctionnel à valider).
- Les URL d'extrait (`preview`) expirent : ne pas les persister durablement ; les rafraîchir via `/track/{id}` si la lecture échoue.

### 6.4 Comportements à préserver (règles métier subtiles)

1. La pastille de la page Musique est **informative** ; celle du dialogue **verrouille** (vert) ou **décoche** (orange). « Tout sélectionner » ignore vert et orange.
2. « Extraire » prend **toute la liste** (statuts éligibles), pas les cases cochées.
3. Réservation de l'artiste dans le décompte « Divers » **dès la mise en file**, décompte partagé par tout le lot, réinitialisé quand le lot est vide.
4. Résolution des tags complémentaires **avant** le calcul du nom (le motif peut réclamer `%annee%`).
5. Les tags fournis par l'appelant (`extra_tags`) ne sont jamais écrasés par Deezer.
6. Requêtes YouTube de la plus fidèle à la plus large, arrêt à la première qui produit un candidat ; vérification `contained ≥ 0.7` obligatoire ; bonus artiste ; tri stable.
7. Échec passager → même vidéo réessayée ; échec définitif → vidéo suivante ; aucune vidéo → arrêt immédiat avec explication.
8. Délai entre essais `min(5, n × 5 / 9)` s ; maximum 10 essais ; message « Abandonné après 10 tentatives : … ».
9. Suppression des homonymes « Titre - Artiste » / « Artiste - Titre » avant installation, jamais du fichier d'origine d'un réencodage.
10. L'historique conserve la jaquette **non marquée** ; le fichier reçoit la jaquette **marquée** (icône en haut à droite, 18 %, marge 4 %, ≥ 96 px, JPEG 92).
11. Le succès ajoute immédiatement le fichier à l'index (pastilles à jour sans relecture).
12. Réencoder : pas de rangement, remplacement sur place, ligne cochée automatiquement quand une correspondance est trouvée, correspondance sautée au-delà de 150 fichiers.

### 6.5 Défauts existants à ne pas reproduire (ou à reproduire sciemment)

| Défaut | Référence | Recommandation |
|---|---|---|
| Annulation sans relance de la pompe ni nettoyage | `main_window.py:3115-3134` | Relancer la file, nettoyer `.dk_*` |
| « Retirer la sélection » possible pendant les téléchargements (indices décalés) | `:3201-3219` | Identifier les jobs par ID stable (pas par index de ligne) — supprime aussi les blocages de tri/purge |
| Même piste ajoutable plusieurs fois → deux extractions vers le même fichier | `:2559` | Dédoublonner par ID Deezer à l'ajout, ou signaler |
| Lien `track/` en HTTP ≠ 200 : recherche bloquée | `search_thread.py:151-171` | Émettre une erreur |
| « Vider l'historique » sans confirmation | `:2553` | Confirmer |
| Infobulle d'erreur conservée après un succès ultérieur | `:3398-3409` | Réinitialiser |
| Tri de l'historique modifiant l'ordre persisté | `:3078-3102` | Tri d'affichage uniquement |
| `clean_stem(title)` tronque aux points/barres obliques ; alphabets non latins jamais retenus | §3.3 | Corriger (ou documenter) |
| Catégorie « Recherche Globale » constante (le type n'est pas mémorisé) | `:2233` | Mémoriser le type pour « Relancer » |
| WAV/M4A sans jaquette intégrée | `tag_write.py:163-183` | Ajouter `covr` (M4A) et ID3 dans WAV via TagLib# |

### 6.6 Découpage suggéré (microservices / GraphQL)

- **Service Catalogue** (Deezer) : `searchTracks(query, type)`, `searchEntities`, `album(id)`, `artistTop(id)`, `resolveLink(url)`, `bestMatch(artist, title)`, `trackDetails(id)` ; limiteur de débit et cache.
- **Service Extraction** (yt-dlp/ffmpeg/tags) : file de jobs persistante, plafond global, annulation, progression publiée par **subscription GraphQL** `downloadProgress(jobId)` (pourcentage + texte exact du §2.3.3), journal des causes d'échec.
- **Service Bibliothèque** : index de présence (`lookup` par lot de pistes), règles de rangement, suppression de fichiers, réparation des chemins, streaming audio.
- **Service Réglages/Historique** : `app_settings`, `searches`, `tracks/covers` (schéma SQLite conservé).
- Front Angular : pages et dialogues décrits au §2, palette de l'annexe A.

---

## Annexe A — Identité visuelle (`styles.py`)

Thème **« Neon Nocturne »** : fond charbon profond, accent cyan néon, boutons « pilules », navigation à barre indicatrice néon, dégradé navy → cyan pour les progressions. Thème **sombre uniquement**.

### A.1 Palette (`styles.py:14-29`)

| Jeton | Valeur | Usage |
|---|---|---|
| `BACKGROUND_DEEP` | `#0a0b10` | fond de l'application (niveau 0) |
| `SURFACE` | `#12141c` | dialogues (niveau 1) |
| (barre latérale) | `#101219` | fond de la barre latérale |
| `SURFACE_CHARCOAL` | `#1c1e26` | cartes, champs, vignettes, fond des barres de progression, placeholders d'image |
| `SURFACE_HIGH` | `#252833` | survols, menus, infobulles (niveau 2) |
| `PRIMARY` | `#00e5ff` | cyan néon : interactif, actif, progression, liens |
| `PRIMARY_HOVER` | `#5cefff` | survol du cyan |
| `ON_PRIMARY` | `#00363d` | texte sombre sur cyan |
| `SECONDARY` | `#343d96` | navy (départ des dégradés) |
| `TERTIARY` | `#5eeb7f` | vert succès |
| `ERROR` | `#ffb4ab` | texte d'erreur |
| `ERROR_CONTAINER` | `#93000a` | boutons destructifs (survol `#b1121e`, texte `#ffdad6`) |
| `TEXT` | `#e5e2e1` | texte principal |
| `TEXT_MUTED` | `#bac9cc` | texte secondaire |
| `TEXT_FAINT` | `#849396` | en-têtes de tableau, sur-titres, placeholders |
| `GLASS_BORDER` | `rgba(255,255,255,0.08)` | bordures « verre » |
| Gris désactivé | `#5f7d84` (texte), `#4a5c60` (boutons de contrôle), `#3b494c` (séparateurs, pastille absente) | |
| Orange avertissement | `#ffcf7a` (pastille similaire), `#ffc857` (synchro en attente) | |
| Rouge vif | `#ff5f6d` | conflit / erreur de base |
| Jauge éteinte | `#3a3d47` | barres de popularité |
| Panneau flottant | `#22242e`, bordure `rgba(255,255,255,0.12)` | « Dernières recherches » |

### A.2 Typographies

- Titres : `"Montserrat", "Segoe UI", "Helvetica Neue", Arial, sans-serif` (titres de page 26 px gras cyan ; logo 20 px poids 800).
- Corps : `"Inter", "Segoe UI", "Helvetica Neue", Arial, sans-serif`.
- Tailles usuelles : en-têtes de tableau 10 px gras `TEXT_FAINT` ; boutons 12-13 px ; champs 13 px ; textes d'aide 12 px ; sur-titres 11 px poids 600.

### A.3 Boutons (4 classes + navigation)

| Classe | Repos | Survol | Désactivé | Coché (bascule) | Dimensions |
|---|---|---|---|---|---|
| `control-btn` (pilule « verre ») | fond transparent, bordure `GLASS_BORDER`, texte `TEXT_MUTED`, 12 px 600 | fond `rgba(255,255,255,.06)`, bordure `rgba(255,255,255,.20)`, texte `TEXT` | texte `#4a5c60`, bordure `rgba(255,255,255,.04)` | fond `rgba(0,229,255,.12)`, bordure `rgba(0,229,255,.45)`, texte cyan | padding 10×20, rayon 18 |
| `accent-btn` (cyan ajouré) | fond `rgba(0,229,255,.10)`, bordure cyan 1 px, texte cyan, 13 px gras | fond `rgba(0,229,255,.20)`, bordure/texte `#5cefff` | fond transparent, bordure `rgba(0,229,255,.15)`, texte `#5f7d84` | — | padding 10×22, rayon 18 |
| `primary-btn` (cyan plein) | fond cyan, sans bordure, texte `#00363d`, 13 px gras | fond `#5cefff` | fond `rgba(0,229,255,.15)`, texte `#5f7d84` | — | padding 10×24, rayon 18 |
| `danger-btn` (rouge plein) | fond `#93000a`, texte `#ffdad6`, 13 px gras | fond `#b1121e` | fond `rgba(147,0,10,.35)`, texte `#8a6f6d` | — | padding 10×22, rayon 18 |
| `nav-btn` | transparent, barre gauche 3 px transparente, texte `TEXT_MUTED` 14 px 500, padding 12×20, marge 2 px | fond `rgba(255,255,255,.05)`, texte `TEXT` | — | **actif** : barre gauche 3 px cyan, fond `rgba(0,229,255,.10)`, texte cyan gras | |
| `nav-sub-btn` | idem, 13 px, padding `10px 20px 10px 44px`, marge 1 px | idem | — | idem actif | |
| `nav_group` (en-tête « Outils ») | texte `TEXT_FAINT` 13 px 600 | texte `TEXT` | — | — | |

**Règle de disposition des barres d'actions** (README + commentaires) : à gauche la manipulation de la liste (`control-btn`) ; à droite les actions secondaires (`control-btn`), puis l'action qui **alimente/analyse** la liste (`accent-btn`, avant-dernière), puis l'action qui **écrit** sur le disque (`primary-btn`, **une seule par écran, toujours en dernier**). `danger-btn` réservé aux suppressions de fichiers. Libellés : « Retirer » = liste seulement ; « Supprimer » = disque ; « Purger » = une catégorie ; « Vider » = tout.

Boutons spécifiques : « Relancer » (cyan plein 96×32, rayon 16, 12 px gras) ; « Voir le top 50 » / « Ouvrir l'album » (cyan plein, padding 6×14, rayon 14) ; « X » d'annulation (rond 20×20, `#93000a` / `#ffdad6`, 9 px gras) ; bouton d'écoute du dialogue (rond 34×34, voir 2.4.6) ; badge Historique (fond cyan, texte `#00363d`, rayon 9, 10 px gras, padding 2×7, hauteur 14).

### A.4 Autres composants

- **Tableaux** : fond `rgba(28,30,38,.55)`, bordure verre, rayon 12, pas de quadrillage ; cellule padding 12, trait bas `rgba(255,255,255,.06)` ; sélection `rgba(0,229,255,.08)` ; en-têtes transparents, `TEXT_FAINT`, 10 px gras, padding 10×8, trait bas verre ; en-têtes alignés à gauche ; indicateur de tri affiché.
- **Arborescences** : idem tableaux, 12 px, items padding 4×2, survol `rgba(255,255,255,.04)`, sélection `rgba(0,229,255,.10)` ; cases 16 px rayon 4 (cochée : cyan + coche `#00363d` ; partielle : `rgba(0,229,255,.35)` + tiret).
- **Cases à cocher** : 18 px, rayon 5, bordure `rgba(255,255,255,.20)`, fond `#1c1e26` ; survol bordure cyan ; cochée fond cyan + coche SVG `#00363d` (trait 3.4, chemin `M5 12.5 L10 17 L19 7`) ; désactivée cochée `rgba(0,229,255,.25)` + coche `#849396`.
- **Champs texte** : fond `rgba(28,30,38,.80)`, bordure verre, padding 10×14, rayon 10, 13 px ; focus bordure cyan. **Listes déroulantes** : padding 8×12, rayon 10 ; liste ouverte fond `#1c1e26`, sélection `rgba(0,229,255,.20)` texte cyan. **Compteurs** : padding 6×8, rayon 10. Désactivés : texte `#5f7d84`, fond `rgba(28,30,38,.35)`, bordure `rgba(255,255,255,.04)`.
- **Barres de progression** : fond `#1c1e26`, rayon 6, hauteur 14, texte centré 10 px gras ; remplissage **dégradé horizontal `#343d96` → `#00e5ff`** ; barre globale hauteur 20, rayon 9, 11 px. Barres indéterminées (chargement) : 8 px, fond `#1c1e26`.
- **Onglets** : texte `TEXT_MUTED` 600, padding 10×20, soulignement 2 px cyan sur l'onglet actif ; panneau bordure verre, fond `rgba(28,30,38,.35)`, rayon 12.
- **Menus** : fond `#252833`, bordure verre, rayon 8, padding 6 ; item padding `6px 24px 6px 14px`, rayon 6, texte `TEXT_MUTED` ; survol `rgba(0,229,255,.15)` texte cyan ; séparateur 1 px verre.
- **Bouton à menu** (`QToolButton`) : comme `control-btn` mais padding 8×14, rayon 14, sans flèche.
- **Dialogues** : fond `#12141c`, bordure verre, rayon 12.
- **Barres de défilement** : 8 px, poignée `rgba(0,229,255,.20)` (survol `.40`), rayon 4, sans flèches.
- **Infobulles** : fond `#252833`, texte `TEXT`, bordure verre, padding 4×8.
- **Cartes** (Paramètres, Diagnostic) : fond `rgba(28,30,38,.60)`, bordure verre, rayon 12, padding 20-24.
- **Icône de l'application** (repli vectoriel, `utils.py:233-275`) : carré arrondi (rayon 30/128) cyan `#00e5ff` portant une croche `#00363d`.
- Glyphes : ▶ lecture, ■ arrêt d'extrait, ⏸ pause (dialogue), ⏳ chargement, 🎵/♪ sans jaquette, ✓ ≈ ○ pastilles, ✕ retirer, 🗑 effacer, ← retour, ▾/▸ groupe.

### A.5 Mise en page

Pages : marges 40 (côtés) / 30 (haut) / 24 (bas), espacement vertical 18-25 ; barre d'actions espacement 12 ; barre latérale 250 px fixe, marges verticales 24.

---

## Annexe B — Catalogue des libellés et messages

### B.1 Boîtes de dialogue (titre — message)

| Contexte | Titre | Message |
|---|---|---|
| Extraire sans mutagen | « Diagnostic Tags » | « La bibliothèque 'mutagen' n'est pas installée. Les tags et pochettes ne pourront pas être intégrés.\nInstallez-la via : pip install mutagen » |
| Tri pendant téléchargements | « Tri indisponible » | « Le tri est désactivé pendant des téléchargements en cours. » |
| Purge pendant téléchargements | « Purge indisponible » | « Attendez la fin des téléchargements en cours avant de purger. » |
| Purge Musique | « Purge » | « {n} téléchargement(s) réussi(s) retiré(s) de la liste. » / « Aucun téléchargement réussi à purger. » |
| Retrait Musique | « Retrait » | « {n} élément(s) retiré(s) de votre liste. » |
| Purge historique | « Purge » | « {n} entrée(s) purgée(s) : fichier(s) introuvable(s) sur le disque. » / « Rien à purger : tous les fichiers de l'historique existent sur le disque. » |
| Ouvrir le dossier impossible | « Erreur » | « Impossible d'ouvrir le dossier : {erreur} » |
| Dossier modifié | « Dossier configuré » | « Musique enregistrée dans :\n{dossier} » |
| Lecteur absent | « Lecteur » | « Lecture indisponible : le module Qt Multimedia (PyQt6-Multimedia) est absent de cette installation. » |
| Recherche : erreur | « Erreur de connexion » | « Erreur lors de la recherche : {message} » |
| Recherche : aucune piste | « Aucun résultat » | « Aucun morceau trouvé pour cette recherche. » |
| Recherche : aucune entité | « Aucun résultat » | « Aucun résultat trouvé. » |
| Dédoublonner sans candidat | « Doublons » | « Aucun fichier du dossier de dépôt n'est rapproché des titres affichés. » |
| Après suppression | « Déduplication » | « {n} fichier(s) supprimé(s). » [+ « \n\nErreurs :\n{≤10 lignes} »] |
| Réencoder : ajout pendant conversion | « Conversion en cours » | « Attendez la fin des conversions avant d'ajouter des fichiers. » |
| Réencoder : analyse en cours | « Analyse en cours » | « Une analyse de fichiers est déjà en cours. » |
| Réencoder : rien d'ajouté | « Aucun ajout » | « Tous les fichiers trouvés sont déjà présents dans la liste. » |
| Réencoder : > 150 fichiers | « Bibliothèque volumineuse » | voir §2.6.2 |
| Réencoder : retrait pendant conversion | « Conversion en cours » | « Attendez la fin des conversions avant de retirer des fichiers. » |
| Réencoder : purge pendant conversion | « Conversion en cours » | « Attendez la fin des conversions avant de purger. » |
| Réencoder : purge | « Purge » | « {n} fichier(s) converti(s) retiré(s) de la liste. » / « Aucun fichier converti à purger. » |
| Réencoder : déjà en cours | « Conversion en cours » | « Des conversions sont déjà en cours. » |
| Réencoder : yt-dlp absent | « Extraction indisponible » | « yt-dlp n'est pas installé. Installez-le via : pip install yt-dlp » |
| Réencoder : rien de sélectionné | « Aucune sélection » | « Sélectionnez au moins un fichier à réencoder (visible). » |
| Fermeture : rangement | « Rangement en cours » | voir §2.1 |
| Fermeture : retéléchargement | « Retéléchargement en cours » | voir §2.1 |
| Résolution de conflit (base) | « Base de données » | « {n} élément(s) rechargé(s) depuis la base en ligne. » |
| Tray (1ʳᵉ réduction) | « DeeKeep » | « L'application continue en arrière-plan. Cliquez sur l'icône pour la rouvrir. » |

### B.2 Messages d'échec de téléchargement (infobulle « Statut »)

- « yt-dlp est manquant. »
- « Aucune vidéo ne correspond à « {artiste principal} - {titre nettoyé} » sur YouTube : les tags du fichier ne mènent à rien de reconnaissable. »
- « La seule vidéo trouvée n'est pas accessible : {message yt-dlp} »
- « Aucune des {n} vidéos trouvées n'est accessible : {message yt-dlp} »
- « Cookies {navigateur} illisibles : fermez complètement le navigateur, ou choisissez Firefox / « Aucun (anonyme) » dans les Paramètres. ({message}) »
- « Fichier extrait introuvable. »
- « Erreur: {exception} »
- « Abandonné après {max} tentatives : {dernier message} »

### B.3 Textes de progression

« Recherche Deezer... » (3 %), « Recherche YouTube... » (4 %), « Initialisation... » (5 %), « Autre version ({i}/{n})... » (5 %), « Extraction... » (5-90 %), « Finalisation... » (95 %), « Terminé ✓ » (100 %), « Échec, nouvelle tentative dans {d:.1f} s (essai {n}/{max})... » (5 %), suffixe « (essai {n}/{max}) » à partir du 2ᵉ essai. Réencoder affiche « {texte} — {pct}% ».
