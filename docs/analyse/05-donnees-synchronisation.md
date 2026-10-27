# 05 — Couche données de DeeKeep : schéma, empreintes, réglages, accès multi-processus

> Analyse de l'application desktop Python/PyQt6 (`D:\000_Logiciel\DeezerMusic`, paquet `deekeep/`) en vue de la réécriture web (Angular + GraphQL + microservices .NET sur un serveur Linux ARM64).
> Base analysée (lecture seule, `file:...?mode=ro`) : `C:\Users\didie\AppData\Roaming\DeeKeep\deekeep_cache.db` (SQLite 3.50.4, 125 Mo + WAL).
> Sources lues : `deekeep/db/db_schema.py`, `local_db.py`, `db_init.py`, `cloud_db.py`, `sync_service.py`, `deekeep/ui/sync_ui.py`, `deekeep/core/settings.py`, plus les appelants (`ui/main_window.py`, `ui/library_page.py`, `audio/library_mirror.py`, `audio/quality_scan.py`, `audio/reference_duration.py`, `workers/download_thread.py`, `library/library_index.py`).
>
> **Périmètre.** La synchronisation Turso est **abandonnée** : elle n'est décrite qu'en § 4, en bref. Les colonnes techniques qu'elle a introduites (`updated_at`, `deleted`, `h`, tables `sync_state` / `sync_outbox`) restent dans le fichier et doivent être tenues à jour pour que la base reste compatible avec l'application desktop.

---

## 1. Inventaire

### 1.1 Fichiers du dossier de données

Le dossier de données vaut `%APPDATA%\DeeKeep` sous Windows et `$XDG_CONFIG_HOME/DeeKeep` (par défaut `~/.config/DeeKeep`) ailleurs. Il est créé s'il manque (`settings.data_dir()`).

| Fichier | Rôle | Statut |
|---|---|---|
| `deekeep_cache.db` (+ `-wal`, `-shm`) | Base SQLite principale, décrite dans ce document. | **À conserver tel quel** |
| `deekeep_config.json` | Réglages et historiques au format JSON, réécrit à chaque enregistrement. Reste la source d'état de l'interface desktop, et la base en est le reflet (§ 5). | Hérité, toujours écrit |
| `quality_cache.json` | Cache des mesures de l'écran Qualité (`{"version":5,"entries":{chemin_minuscule:{stamp,measures}}}`). Importé dans la table `quality` par `db_init`. | Source de collecte |
| `reference_cache.json` | Durées de référence Deezer/MusicBrainz (`{"version":1,"entries":{"artiste|titre":{durations,time}}}`). Importé dans `reference_durations`. | Source de collecte |
| `archive.json`, `store.json` | Configuration du magasin d'archive audio (`{"root": dossier, "prefix": "deekeep/"}`), une base SQLite séparée (`sqlite_store.py`). | Hors périmètre |
| `cloud.json` | Identifiants Turso (`{"url","token"}`). Sur la machine de référence, il est renommé `cloud.json.disabled`, ce qui désactive la synchronisation. | Abandonné |
| `deekeep_cache.db.avant-archive-*` | Sauvegardes manuelles faites avant le renommage de la table `opus_mirror` en `archive`. | À ignorer |

### 1.2 Modules Python de la couche données

| Module | Responsabilité |
|---|---|
| `db/db_schema.py` | Source unique du schéma (`TableSpec`, `TABLES`, `TABLE_ORDER`, DDL, index). Contient aussi l'empreinte de ligne `row_hash`, la requête d'empreinte de table et le JSON canonique (`json_dump`). |
| `db/local_db.py` | Classe `LocalDB` : connexion unique protégée par un verrou, pragmas, `ensure_schema`, lecture, `upsert`/`upsert_many`/`mark_deleted`/`hard_delete`, file `sync_outbox`, `sync_state`, accès aux réglages. Singleton `shared()`. |
| `db/db_init.py` | Collecte (parcours du disque, tags, jaquettes, historique, mesures de qualité, durées de référence) et remplissage du cache. Contient aussi `mirror_state`, appelé à chaque enregistrement de la configuration, et une CLI. |
| `core/settings.py` | Réglages par défaut, chargement et enregistrement de `deekeep_config.json`, migration de l'ancien emplacement. |
| `db/cloud_db.py`, `db/sync_service.py`, `ui/sync_ui.py` | Synchronisation Turso, **abandonnée** (§ 4). |

### 1.3 Contenu actuel de la base (relevé du 26/10/2026)

| Table | Lignes | dont `deleted=1` | `SUM(h)` |
|---|---:|---:|---:|
| `app_settings` | 28 | 0 | 14 274 522 524 |
| `searches` | 79 | 0 | 38 323 594 584 |
| `covers` | 1 943 | 0 | 1 066 563 993 905 |
| `tracks` | 5 165 | 1 373 | 2 765 686 120 011 |
| `quality` | 4 935 | 365 | 2 639 001 043 866 |
| `archive` | 3 581 | 0 | 1 934 675 399 171 |
| `reference_durations` | 3 381 | 0 | 1 797 640 026 201 |
| `schema_meta` | 2 (`schema_version`=`1`, `sync_reference`=`remote`) | – | – |
| `sync_state` | 7 | – | – |
| `sync_outbox` | 8 760 (archive 3 581, quality 4 933, searches 79, tracks 101, covers 66) | – | – |

Pragmas relevés dans le fichier : `journal_mode=wal` (persistant dans le fichier), `page_size=4096`, `encoding=UTF-8`, `user_version=0`, `auto_vacuum=0`.

La clé `schema_meta.sync_reference = 'remote'` n'est écrite par **aucun** code actuel ; elle vient sans doute d'un script ponctuel. Il faut la conserver telle quelle et ne pas s'en servir.

---

## 2. Schéma détaillé

### 2.1 Règles communes aux 7 tables « de contenu »

Ces tables sont `app_settings`, `searches`, `covers`, `tracks`, `quality`, `archive` et `reference_durations`. `TableSpec.create_sql()` les génère toutes sur le même modèle :

```sql
CREATE TABLE IF NOT EXISTS <nom> (
    <pk> <TYPE> PRIMARY KEY,
    <colonnes de contenu…>,
    updated_at REAL NOT NULL DEFAULT 0,
    deleted INTEGER NOT NULL DEFAULT 0,
    h INTEGER NOT NULL DEFAULT 0
)
```

* La clé primaire est **une seule colonne TEXT** (pas de `rowid` exploité, pas de clé composée, aucune clé étrangère déclarée).
* `updated_at` : horodatage Unix en secondes, avec fraction (`time.time()`), de la dernière écriture. Il ne sert à **aucune** décision (même la synchro de l'époque n'utilisait que `h`). Il n'entre pas dans `h`. Il ne doit jamais valoir 0 après une écriture applicative : `upsert` remplace 0 ou une absence par « maintenant ».
* `deleted` : suppression logique (« pierre tombale »). Une pierre tombale garde sa clé, a **toutes ses colonnes de contenu à NULL**, `deleted=1`, et un `h` recalculé. Toutes les lectures applicatives filtrent `WHERE deleted = 0`.
* `h` : empreinte de contenu sur 30 bits (§ 3.1), toujours recalculée à l'écriture.
* L'ordre des colonnes de contenu (ordre DDL) **fait partie de l'algorithme d'empreinte** : il ne faut jamais le changer.

`TABLE_ORDER` (ordre d'écriture et de parcours) : `app_settings, searches, covers, tracks, quality, archive, reference_durations`.

### 2.2 `app_settings`: les réglages, une ligne par clé

| Colonne | Type | Contenu |
|---|---|---|
| `key` (PK) | TEXT | Nom du réglage (liste complète en § 5). |
| `value` | TEXT | Valeur encodée en **JSON canonique** (`json_dump`, § 3.3). |

Valeurs réelles relevées (colonne `value` brute) :

```
cookies_file        ""                       org_filters_open   true
download_dir        "D:/"                    org_keep_albums    true
file_format         "MP3"                    org_pattern        "%titre% - %artiste%"
mirror_bitrate      128                      org_remove_empty   true
mirror_destination  "archive"   (clé héritée) org_rename         false
mirror_source_only  false                    org_report_untagged true
org_album           false                    qual_clipping_pct  0.2
org_artist          true                     qual_crackle_pct   0.0025
org_dedup           true                     qual_deep          true
org_divers          true                     qual_duration_excess 3.0
org_divers_min      3                        qual_duration_gap  10.0
quality             "128 kbps (Standard)"    qual_gap_density_pct 30.0
youtube_browser     "Aucun (anonyme)"        qual_head_seconds  3.0
                                             qual_hiss_db       -65.0
                                             qual_tail_seconds  5.0
```

Attention : un flottant rond s'écrit `3.0` et `-65.0` (représentation Python), pas `3` ni `-65` (§ 3.3).

### 2.3 `searches`: l'historique des recherches

| Colonne | Type | Contenu |
|---|---|---|
| `query` (PK) | TEXT | Texte de recherche, débarrassé des espaces de bord (`strip`). Peut être une URL Deezer (`https://www.deezer.com/fr/playlist/…`). |
| `position` | INTEGER | Rang d'affichage, **0 = la plus récente**. Il n'est pas renuméroté : des trous sont possibles. |

* L'interface insère la nouvelle recherche en tête (`search_history.insert(0, q)`) après avoir retiré un doublon exact.
* `collect_searches` dédoublonne **sans tenir compte de la casse** (la première occurrence gagne) et ignore les chaînes vides. Le rang retenu est l'index dans la liste JSON d'origine.
* `_mirror_searches` réécrit toute ligne dont la position a changé et pose une pierre tombale sur toute requête disparue de la liste. Une nouvelle recherche décale donc tout le monde et réécrit toute la table : les 79 lignes actuelles portent le même `updated_at`.
* Lecture pour l'affichage : `SELECT * FROM searches WHERE deleted = 0 ORDER BY position`.

### 2.4 `covers`: les jaquettes dédoublonnées

| Colonne | Type | Contenu |
|---|---|---|
| `sha1` (PK) | TEXT | SHA-1 en hexadécimal **minuscule** (40 caractères) des **octets bruts de l'image** (`data`). |
| `mime` | TEXT | `image/jpeg`, `image/png`, `image/gif`, `image/bmp`, `image/webp`, ou `application/octet-stream` si le format est inconnu. |
| `bytes` | INTEGER | `len(data)`. |
| `width`, `height` | INTEGER | Dimensions lues dans l'en-tête, 0 si inconnues (toujours 0 pour WebP). |
| `data` | BLOB | L'image, octet pour octet. |

Relevé : 1 941 JPEG (de 1,3 à 537 Ko, de 200 à 1 000 px, 115 Mo au total), 1 PNG et 1 `application/octet-stream` de 113 octets. Pour toutes les lignes, `sha1 = SHA1(data)` (0 écart) et `bytes = length(data)`. Aucune référence orpheline depuis `tracks`. En revanche, 169 jaquettes ne sont plus référencées par aucun titre : il n'existe pas de ramasse-miettes.

**Règle de dédoublonnage** (`db_init.CoverStore.add`) :

* L'empreinte est `sha1 = hashlib.sha1(bytes(data)).hexdigest()`, calculée sur les octets **tels quels**. Il n'y a **ni redimensionnement, ni réencodage, ni normalisation** : deux encodages JPEG différents d'une même pochette donnent deux lignes.
* Les octets proviennent de deux sources :
  1. la **jaquette intégrée au fichier audio**, lue avec mutagen : la liste `pictures` en FLAC (type 3 = face avant, sinon la première), les trames ID3 `APIC` (type 3 sinon la première), l'atome MP4 `covr[0]`, ou le champ Vorbis `metadata_block_picture` (base64 d'un bloc FLAC Picture, dont on garde `.data`) ;
  2. la **jaquette de l'historique de téléchargement** : le champ `cover_b64` de `deekeep_config.json`, décodé depuis le base64. Ce sont les octets reçus de Deezer au téléchargement, sans retouche.
* À la première rencontre, la ligne est créée avec `mime`, `width` et `height` lus dans l'en-tête par `image_info` (§ 3.4). Les rencontres suivantes réutilisent la ligne.
* Une donnée vide ou un base64 invalide ne donne aucune jaquette (`None`).

### 2.5 `tracks`: la bibliothèque et l'historique des téléchargements

| # | Colonne | Type | Contenu / origine |
|---|---|---|---|
| 1 | `path` (PK) | TEXT | Chemin absolu **Windows** normalisé (`os.path.normpath`, séparateurs `\`, casse d'origine conservée), par exemple `D:\Nirvana\Come As You Are - Nirvana.mp3`. |
| 2 | `title` | TEXT | Tag titre ; à défaut, le nom de fichier sans extension. |
| 3 | `artist` | TEXT | Tag `TPE1` / `©ART` / `artist`. |
| 4 | `album` | TEXT | Tag `TALB` / `©alb` / `album`. |
| 5 | `album_artist` | TEXT | Tag `TPE2` / `aART` / `albumartist` / `album artist`. |
| 6 | `genre` | TEXT | Tag `TCON` / `©gen` / `genre`. |
| 7 | `year` | TEXT | Tag `TDRC` / `TYER` / `©day` / `date` / `year` / `originaldate`, en **texte**. |
| 8 | `track_no` | TEXT | Tag `TRCK` / `trkn` / `tracknumber` / `track`, en texte (`"3/13"`, `"5"`). |
| 9 | `duration` | REAL | Durée en secondes, arrondie à 3 décimales. |
| 10 | `bitrate` | INTEGER | Débit en kb/s (`bitrate // 1000`). |
| 11 | `sample_rate` | INTEGER | Fréquence d'échantillonnage en Hz. |
| 12 | `channels` | INTEGER | Nombre de canaux. |
| 13 | `codec` | TEXT | Nom du module mutagen du flux (`mp3`, `flac`, `mp4`, `oggvorbis`…). |
| 14 | `ext` | TEXT | Extension en minuscules, sans point. |
| 15 | `file_size` | INTEGER | Taille du fichier en octets. |
| 16 | `file_mtime` | REAL | Date de modification en secondes Unix, arrondie à 3 décimales. |
| 17 | `cover_sha1` | TEXT | Jaquette affichée → `covers.sha1`. C'est la jaquette intégrée, ou à défaut celle de l'historique. |
| 18 | `source` | TEXT | `library`, `history` ou `both` (§ 2.5.1). |
| 19 | `in_history` | INTEGER | 1 si le titre figure dans l'historique des téléchargements, sinon 0. |
| 20 | `history_pos` | INTEGER | Rang dans l'historique, ou NULL. |
| 21 | `history_cover_sha1` | TEXT | Jaquette notée dans l'historique → `covers.sha1`. |

Les chaînes de tags valent `""` et non NULL quand le tag est absent pour un fichier lu. Les colonnes numériques valent NULL quand l'information manque.

Répartition actuelle :

| `source` | `in_history` | `deleted` | lignes | `history_pos` non NULL | `history_cover_sha1` non NULL |
|---|---|---|---:|---:|---:|
| NULL | NULL | 1 | 1 373 | 0 | 0 |
| `both` | 1 | 0 | 305 | 305 | 305 |
| `library` | 0 | 0 | 3 487 | 0 | **109** |

Aucune ligne vivante `source='history'` n'existe aujourd'hui : elles ont toutes été remplacées par des pierres tombales.

#### 2.5.1 Sémantique de l'historique

L'historique des téléchargements vit dans `deekeep_config.json`, sous la clé `downloaded_history` : une liste **ajoutée en fin** (`append`) à chaque téléchargement réussi. L'index 0 est donc le **plus ancien** téléchargement, ce qui est le sens inverse de `searches`. Chaque entrée a la forme :

```json
{"title": "Come As You Are", "artist": "Nirvana", "album": "Nevermind (Remastered)",
 "path": "D:/Nirvana\\Come As You Are - Nirvana.mp3",
 "cover_b64": "/9j/4AAQSkZJRgABAgAAAQABAAD/wA...",
 "tags": {"albumartist": "Nirvana", "genre": "Rock", "date": "2011",
          "track": "3", "tracktotal": "13", "disc": "1"}}
```

Le chemin peut mélanger `/` et `\`. Les 308 entrées actuelles portent toutes une `cover_b64`.

* **`history_pos`** est l'index de l'entrée dans `downloaded_history` **avant** dédoublonnage (`iter_history` saute les chemins déjà vus, comparés sans casse, et garde le rang de la première occurrence). Des trous sont donc possibles. Pour l'affichage, il faut trier `ORDER BY history_pos`.
* **`in_history`** vaut 1 quand le titre est dans l'historique, 0 quand il en a été retiré (purge, vidage) alors que le fichier existe encore sur le disque.
* **`source`** :
  * `library` : fichier trouvé sur le disque, absent de l'historique (ou retiré de celui-ci) ;
  * `both` : fichier présent sur le disque **et** dans l'historique ;
  * `history` : entrée d'historique dont le fichier a disparu. La ligne est construite à partir de l'entrée : `title`/`artist`/`album` de l'entrée, `album_artist`/`genre`/`year`/`track_no` tirés de `tags`, `file_size`, `file_mtime` et `duration` à NULL, `cover_sha1 = history_cover_sha1`.
* **`history_cover_sha1`** désigne la jaquette `cover_b64` de l'entrée, une fois dédoublonnée dans `covers`. Ce peut être une autre image que `cover_sha1` (la jaquette intégrée au fichier) : seules 102 lignes sur 305 ont les deux égales. Quand un titre sort de l'historique, `history_cover_sha1` **n'est pas effacé** : c'est l'origine des 109 lignes `library` qui en portent un.

Correspondance avec les tags de l'historique (`history_tags`) :

| Clé de `tags` | Colonne |
|---|---|
| `albumartist` | `album_artist` |
| `genre` | `genre` |
| `date` | `year` |
| `track` + `tracktotal` | `track_no` = `"track/tracktotal"`, ou `"track"` seul si le total manque |

**Fusion lors de la collecte complète** (`merge_history`, appelée par `build_local`) :

1. On construit l'index `by_name` des lignes de disque relevées, par nom de fichier en minuscules.
2. Pour chaque entrée `(position, entry)` de l'historique dédoublonné :
   * on cherche la ligne par `match_key(path)`, c'est-à-dire `normcase(normpath(path))` : minuscules et `\` ;
   * à défaut, on **rattrape un chemin périmé** (bibliothèque déplacée) : si exactement **une** ligne porte ce nom de fichier sans avoir déjà été réclamée au cours de ce passage (ensemble `claimed`), l'entrée y est rattachée ;
   * si une ligne est trouvée : `source='both'`, `in_history=1`, `history_pos=position`, `history_cover_sha1=sha1(cover_b64)`, `cover_sha1` hérite de la jaquette de l'historique s'il est vide, et `title`, `artist`, `album` ainsi que les tags complémentaires **ne comblent que les vides** (les tags lus dans le fichier font foi) ;
   * sinon, on crée une ligne `source='history'` comme décrit plus haut.

**Mise à jour incrémentale** (`_mirror_history`, à chaque enregistrement de la configuration) :

* `by_name` est construit à partir de `SELECT path FROM tracks WHERE deleted = 0`. Le rattachement par nom de fichier unique suit la même règle.
* Une entrée est laissée intacte si la ligne existe, n'est pas supprimée, et a déjà `in_history=1`, le même `history_pos` et un `history_cover_sha1` non NULL.
* Sinon, on lit la ligne existante (ou une ligne vide), on comble les tags manquants, puis on fixe `path` (le chemin **de la ligne rattachée**, jamais le chemin périmé), `title`/`artist`/`album` (les existants gardent la main), `in_history=1`, `history_pos`, `history_cover_sha1 = sha1(cover_b64)` ou l'ancien, et `deleted=0`. `source` prend la valeur `history` par défaut, puis passe à `both` si le fichier existe sur le disque. `cover_sha1` est comblé si vide.
* Toute ligne `in_history=1 AND deleted=0` absente du passage subit l'un de deux traitements :
  * si `source='history'` et que le fichier est absent : **pierre tombale** (`mark_deleted`) ;
  * sinon : `in_history=0`, `history_pos=NULL`, `source='library'`, avec `history_cover_sha1` **conservé**.
* Les nouvelles jaquettes sont écrites dans `covers` avant les titres.

**Reconstruction de l'historique depuis la base** (`SyncController._history_from_db`) :

```sql
SELECT t.path, t.title, t.artist, t.album,
       COALESCE(t.history_cover_sha1, t.cover_sha1) AS sha1
FROM tracks t WHERE t.in_history = 1 AND t.deleted = 0
ORDER BY t.history_pos
```

Chaque ligne donne `{"title","artist","album","path"}`, plus `cover_b64 = base64(covers.data)` si la jaquette existe. Le champ `tags` n'est **pas** restitué.

### 2.6 `quality`: les mesures de l'écran Qualité

| Colonne | Type | Contenu |
|---|---|---|
| `path` (PK) | TEXT | Chemin réel du titre s'il a été rapproché de `tracks`, sinon la clé du cache JSON, **en minuscules**. Exemple : `d:\$recycle.bin\$rj4z6z1\all i want is you october - u2.mp3`. |
| `stamp` | TEXT | `"{st_mtime_ns}:{st_size}"` : date de modification en **nanosecondes**, deux-points, taille. Exemple : `1784567348000000000:3878924`. |
| `measures` | TEXT (JSON) | Voir ci-dessous. |
| `defects` | TEXT (JSON) | Tableau de codes de défauts. |

Cette table n'est écrite **que** par la collecte (`db_init.collect_quality`) et n'est lue par aucun écran. L'écran Qualité continue d'utiliser `quality_cache.json`.

Forme de `measures` : un objet JSON aux clés triées, en JSON canonique. Les types sont ceux relevés sur les 4 570 lignes vivantes :

| Clé | Type JSON | Note |
|---|---|---|
| `clipping_pct` | nombre | |
| `crackle_pct` | nombre | |
| `deep` | booléen | analyse approfondie |
| `duration` | nombre | |
| `error` | chaîne | `""` si l'analyse a réussi |
| `flat_factor` | nombre | |
| `full_trough_db` | nombre **ou** `"-inf"` | |
| `gap_count` | entier | |
| `gap_density` | nombre | fraction de 0 à 1 |
| `gap_seconds` | nombre | |
| `head_seconds` | nombre | |
| `hiss_db` | nombre **ou** `"-inf"` | |
| `hiss_trough_db` | nombre **ou** `"-inf"` | |
| `peak_db` | nombre (ou `null` si NaN) | |
| `sample_rate` | entier | |
| `tail_seconds` | nombre | |

Codage des valeurs non finies (`_encode_measures`) : −∞ s'écrit `"-inf"`, +∞ s'écrit `"inf"`, NaN s'écrit `null`.

Exemple réel :

```json
{"clipping_pct":0.1793812120375483,"crackle_pct":0.0006427733296521259,"deep":true,"duration":241.47,"error":"","flat_factor":9.740676,"full_trough_db":"-inf","gap_count":1,"gap_density":0.011499999999998067,"gap_seconds":0.11499999999998067,"head_seconds":0.0,"hiss_db":"-inf","hiss_trough_db":"-inf","peak_db":0.000265,"sample_rate":48000,"tail_seconds":3.5717710000000125}
```

Forme de `defects` : un tableau de chaînes dans l'ordre d'évaluation de `quality_scan.evaluate`, soit `hiss`, `crackle`, `clipping`, `gaps`, `duration`, `tail`, `head`. Exemples : `[]`, `["gaps"]`. Ce diagnostic est calculé à la collecte, avec les seuils `qual_*` et la médiane des durées de référence.

### 2.7 `archive`: l'index de l'archive audio

| Colonne | Type | Contenu |
|---|---|---|
| `path` (PK) | TEXT | Chemin du titre source, même forme que `tracks.path`. |
| `sha1` | TEXT | SHA-1 hexadécimal du **contenu du fichier audio**, lu par blocs de 1 Mio. |
| `stamp` | TEXT | `"{int(st_mtime)}:{st_size}"` : date en **secondes entières**. Ce format diffère de `quality.stamp`. |
| `key` | TEXT | Clé de l'objet : `{prefix}audio/source/{sha1[:2]}/{sha1}{ext}`, par exemple `deekeep/audio/source/eb/eb6d…a2.mp3`. |
| `bytes` | INTEGER | Taille de l'objet stocké. |
| `bitrate` | INTEGER | kb/s. |
| `source_bytes` | INTEGER | Taille du fichier source. |
| `uploaded_at` | REAL | Horodatage Unix de l'envoi. |

Écrite par `ui/library_page.py` (`upsert` après archivage ou restauration, `mark_deleted` à la suppression). Lecture :

```sql
SELECT path, sha1, stamp, key, bytes, bitrate, source_bytes FROM archive WHERE deleted = 0
```

La table s'appelait `opus_mirror` ; elle a été renommée par `ALTER TABLE … RENAME`, d'où le DDL `CREATE TABLE "archive"`. Les index ont gardé leurs noms `idx_mirror_*`. **Ses 3 581 empreintes `h` ont été calculées sous l'ancien nom** (§ 3.1.5).

### 2.8 `reference_durations`: les durées de référence

| Colonne | Type | Contenu |
|---|---|---|
| `key` (PK) | TEXT | `canon(artiste) + "|" + canon(titre)`, où `canon(x)` met `x` en minuscules, remplace chaque suite de caractères hors `[a-z0-9]` par une espace, réduit les espaces multiples et retire les espaces de bord. Exemples : `taylor swift|22`, `madonna|4 minutes feat justin timberlake and timbaland`. |
| `durations` | TEXT (JSON) | Tableau d'**entiers** (secondes), éventuellement vide `[]` (148 lignes). Il contient de 0 à plusieurs dizaines de valeurs, dans l'ordre de collecte et non trié. Exemple : `[242,232,303,233,243,232,158,291,280,231,260,63,230,231]`. |
| `fetched_at` | REAL | Champ `time` du cache JSON, arrondi à 3 décimales. |

Usage : la durée de référence d'un titre est la **médiane basse** du tableau, `sorted(d)[len(d)//2]`.

Attention : `db_init._reference_for` construit la clé avec `artist.strip().lower() + "|" + title.strip().lower()`, **sans** la normalisation `canon`. Les clés ne coïncident donc que pour des titres sans ponctuation. C'est une incohérence de l'existant.

### 2.9 Tables de service

```sql
CREATE TABLE IF NOT EXISTS schema_meta (key TEXT PRIMARY KEY, value TEXT)
-- local uniquement :
CREATE TABLE IF NOT EXISTS sync_state (
    table_name TEXT PRIMARY KEY,
    synced_count INTEGER NOT NULL DEFAULT -1,
    synced_sum INTEGER NOT NULL DEFAULT -1,
    synced_at REAL NOT NULL DEFAULT 0)
CREATE TABLE IF NOT EXISTS sync_outbox (
    table_name TEXT NOT NULL,
    pk TEXT NOT NULL,
    queued_at REAL NOT NULL,
    PRIMARY KEY (table_name, pk))
```

* `schema_meta.schema_version` vaut `'1'` (`SCHEMA_VERSION = 1`). **Au démarrage du desktop, si la valeur diffère de 1, `LocalDB.ensure_schema` VIDE les 7 tables, `sync_state` et `sync_outbox`** (`reset_data`). Il ne faut donc jamais modifier cette valeur côté .NET tant que le desktop peut encore ouvrir la base.
* `sync_outbox` contient une ligne par `(table, clé)` modifiée localement. Elle est alimentée par chaque `upsert` (avec `queue=True`, le défaut).

### 2.10 Index

```sql
CREATE INDEX IF NOT EXISTS idx_tracks_artist   ON tracks (artist)
CREATE INDEX IF NOT EXISTS idx_tracks_album    ON tracks (album)
CREATE INDEX IF NOT EXISTS idx_tracks_cover    ON tracks (cover_sha1)
CREATE INDEX IF NOT EXISTS idx_tracks_history  ON tracks (in_history)
CREATE INDEX IF NOT EXISTS idx_tracks_updated  ON tracks (updated_at)
CREATE INDEX IF NOT EXISTS idx_quality_updated ON quality (updated_at)
CREATE INDEX IF NOT EXISTS idx_mirror_sha1     ON archive (sha1)
CREATE INDEX IF NOT EXISTS idx_mirror_updated  ON archive (updated_at)
```

S'y ajoutent les index automatiques des clés primaires (`sqlite_autoindex_*`). Il n'y a pas d'index sur `tracks.history_cover_sha1` ni sur `searches.position`, ce qui est sans importance vu les volumes.

Si l'on ajoute des index côté .NET (par exemple `tracks(deleted, in_history, history_pos)`), il faut utiliser `CREATE INDEX IF NOT EXISTS` sous des noms nouveaux. Le desktop les tolère : son DDL est entièrement en `IF NOT EXISTS`.

### 2.11 Ordre d'initialisation du DDL (`ensure_schema`)

1. `schema_meta`, puis les 7 tables dans `TABLE_ORDER`, puis les 8 index ;
2. `sync_state`, puis `sync_outbox` ;
3. lecture de `schema_version` : absente, elle est écrite à `1` ; différente de 1, c'est le `reset_data` décrit plus haut.

---

## 3. Algorithmes à porter, avec vecteurs de test

### 3.1 Empreinte de ligne `h` (`db_schema.row_hash`)

#### 3.1.1 Algorithme exact

```
msg  = UTF8(nom_de_table)                       // ex. "tracks"
pour chaque colonne de contenu, dans l'ordre DDL (clé primaire comprise,
sans updated_at / deleted / h) :
    msg += 0x1F
    msg += canonical(valeur)
msg += 0x1E
msg += (deleted est « vrai ») ? '1' (0x31) : '0' (0x30)

digest = BLAKE2b(msg) avec digest_size = 8 octets
         (sans clé, sans sel, sans personnalisation)
h      = (uint64 big-endian de digest) & 0x3FFFFFFF      // 30 bits, 0 ≤ h < 2^30
```

Points critiques :

* **BLAKE2b avec une sortie de 8 octets** n'est **pas** la troncature d'un BLAKE2b-512 : la taille de sortie entre dans le bloc de paramètres. Sur le vecteur `searches` ci-dessous, BLAKE2b-8 donne `30badafc96207561` alors que les 8 premiers octets de BLAKE2b-512 donnent `f8dfeee86ed9d7b8`. En .NET, il faut une bibliothèque qui accepte une longueur de sortie : `Blake2Fast` (`Blake2b.ComputeHash(8, data)`) ou BouncyCastle (`new Blake2bDigest(64)`, taille en **bits**).
* Le digest est lu en **big-endian** : on prend les 8 octets dans l'ordre, puis on masque les 30 bits de poids faible, c'est-à-dire les 30 derniers bits du dernier octet et des précédents.
* `deleted` « vrai » signifie toute valeur non nulle ou non vide. En pratique, c'est 0 ou 1.
* Les horodatages (`updated_at`) et `h` lui-même **n'entrent pas** dans l'empreinte.

#### 3.1.2 `canonical(valeur)`

| Valeur (type réellement lu dans SQLite) | Octets produits |
|---|---|
| NULL | `00` |
| booléen (seulement s'il est passé en mémoire ; SQLite n'en stocke pas) | `'b'` + `'1'` ou `'0'` |
| entier (`INTEGER`) | `'i'` + écriture décimale ASCII (`-1`, `128`). Taille arbitraire. |
| flottant (`REAL`) **à valeur entière** (`v == trunc(v)`) | `'i'` + entier décimal. `1.0` donne `i1`, `-65.0` donne `i-65`, `-0.0` donne `i0`, `1e20` donne `i100000000000000000000`. |
| flottant non entier | `'f'` + `repr(round(v, 6))` en Python (voir ci-dessous) |
| BLOB | `'x'` + **SHA-1 brut (20 octets)** du BLOB. On ne hache pas le contenu dans le message : on hache son empreinte. |
| texte | `'s'` + octets UTF-8 (Python `surrogatepass`, ce qui revient à de l'UTF-8 normal pour un texte valide) |

**Flottants non entiers.** On applique `round(v, 6)` de Python, puis `repr()` :

* `round(v, 6)` arrondit au plus proche **la valeur binaire exacte** de `v` à 6 décimales, avec égalité au pair, puis reconvertit en double par un arrondi correct. Ce n'est **pas** `Math.Round(v, 6)` de .NET, qui multiplie par 10⁶ et introduit une erreur. Exemples : `0.0078125` (égalité exacte) donne `0.007812` ; `5e-07` donne `0.0`, car son binaire est légèrement inférieur à 5e-7 ; `1.5e-06` donne `2e-06`.
* `repr()` produit l'écriture **la plus courte qui se relit à l'identique**, selon les règles de Python :
  * soit `d1d2…` les chiffres significatifs, et `decpt` la position de la virgule (valeur = 0,d1d2… × 10^decpt) ;
  * si `decpt <= -4` ou `decpt > 16`, notation scientifique : `d1[.d2…]e±XX`, exposant signé sur 2 chiffres au moins (`1e-05`, `1.5e-05`, `2e-06`) ;
  * sinon, notation fixe, avec au moins un chiffre après le point (`2.0`, `0.3`, `0.0001`, `1000000000000000.5`) ;
  * un résultat nul s'écrit `0.0`, ou `-0.0` s'il est négatif (par exemple `round(-1e-7, 6)`).

#### 3.1.3 Vecteurs de test « valeur → canonical »

Calculés avec le code Python d'origine (Python 3.13) :

| Entrée | `canonical` (repr) | hex |
|---|---|---|
| `None` | `\x00` | `00` |
| `True` / `False` | `b1` / `b0` | `6231` / `6230` |
| `0` / `-1` / `128` | `i0` / `i-1` / `i128` | |
| `1099511627776` | `i1099511627776` | |
| `1.0` | `i1` | `6931` |
| `-65.0` | `i-65` | |
| `-0.0` | `i0` | |
| `1e20` | `i100000000000000000000` | |
| `1784567348.0` | `i1784567348` | |
| `0.2` | `f0.2` | |
| `0.1 + 0.2` | `f0.3` | `66302e33` |
| `241.512` | `f241.512` | |
| `1/3` | `f0.333333` | |
| `2.0000001` | `f2.0` | |
| `1e-05` | `f1e-05` | |
| `0.0001` | `f0.0001` | |
| `1.5e-05` | `f1.5e-05` | |
| `0.0078125` | `f0.007812` | (égalité au pair) |
| `5e-07` | `f0.0` | |
| `1e-06` / `1.25e-06` | `f1e-06` | `6631652d3036` |
| `1.5e-06` / `-1.5e-06` | `f2e-06` / `f-2e-06` | |
| `2.5e-06` | `f3e-06` | |
| `-65.5` | `f-65.5` | |
| `123.4567895` | `f123.456789` | |
| `1000000000000000.5` | `f1000000000000000.5` | |
| `2.675` | `f2.675` | |
| `1792949095.6805658` | `f1792949095.680566` | |
| `""` | `s` | `73` |
| `"é"` | `s\xc3\xa9` | `73c3a9` |
| `"日本"` | | `73e697a5e69cac` |
| `"🎵"` | | `73f09f8eb5` |
| `b""` | `x` + SHA1("") | `78da39a3ee5e6b4b0d3255bfef95601890afd80709` |
| `b"abc"` | `x` + SHA1("abc") | `78a9993e364706816aba3e25717850c26c9cd0d89d` |

Contrôles BLAKE2b-8 isolés : `BLAKE2b-8("") = e4a6a0577479b2b4` ; `BLAKE2b-8("abc") = d8bb14d833d59559`.

#### 3.1.4 Vecteurs de test « ligne → h », vérifiés sur la base réelle

Toutes ces valeurs sont **égales à la colonne `h` stockée**. Sur toute la base, le recalcul concorde pour 100 % des lignes de `app_settings` (28), `searches` (79), `covers` (1 943), `tracks` (5 165), `quality` (4 935) et `reference_durations` (3 381). Pour `archive`, voir § 3.1.5.

**V1 — `searches`, ligne `('linkin park', position 1)`**

```
msg  = b'searches\x1fslinkin park\x1fi1\x1e0'
hex  = 73656172636865731f736c696e6b696e207061726b1f69311e30   (26 octets)
blake2b-8 = 30badafc96207561  (u64 3511359637874046305)
h    = 371225953   ✔ (stocké : 371225953)
```

**V2 — `app_settings`, ligne `('qual_hiss_db', '-65.0')`.** La valeur est du texte JSON, hachée comme texte.

```
msg  = b'app_settings\x1fsqual_hiss_db\x1fs-65.0\x1e0'
hex  = 6170705f73657474696e67731f737175616c5f686973735f64621f732d36352e301e30
blake2b-8 = 2b3b73e272d70c3b
h    = 852954171   ✔
```

**V3 — `app_settings`, ligne `('org_pattern', '"%titre% - %artiste%"')`**

```
hex  = 6170705f73657474696e67731f736f72675f7061747465726e1f732225746974726525202d20256172746973746525221e30
blake2b-8 = 8a8e2cedce5b83d6
h    = 240878550   ✔
```

**V4 — `tracks`, ligne vivante `source='both'`.** Elle combine texte, REAL non entier, REAL entier et INTEGER.

```
path='D:\Nirvana\Come As You Are - Nirvana.mp3', title='Come As You Are', artist='Nirvana',
album='Nevermind (Remastered)', album_artist='Nirvana', genre='Rock', year='2011', track_no='3/13',
duration=219.384, bitrate=128, sample_rate=48000, channels=2, codec='mp3', ext='mp3',
file_size=3538569, file_mtime=1787240944.0, cover_sha1='6f53a9a97ad04157f63c02e1ce07932a8d7d43dd',
source='both', in_history=1, history_pos=0,
history_cover_sha1='27648616c7c916c54f6abaaa78382cdbdf17bf97', deleted=0

msg = b'tracks\x1fsD:\\Nirvana\\Come As You Are - Nirvana.mp3\x1fsCome As You Are\x1fsNirvana
       \x1fsNevermind (Remastered)\x1fsNirvana\x1fsRock\x1fs2011\x1fs3/13\x1ff219.384\x1fi128
       \x1fi48000\x1fi2\x1fsmp3\x1fsmp3\x1fi3538569\x1fi1787240944
       \x1fs6f53a9a97ad04157f63c02e1ce07932a8d7d43dd\x1fsboth\x1fi1\x1fi0
       \x1fs27648616c7c916c54f6abaaa78382cdbdf17bf97\x1e0'        (278 octets, sans les retours à la ligne)
hex = 747261636b731f73443a5c4e697276616e615c436f6d6520417320596f7520417265202d204e697276616e612e6d70331f73436f6d6520417320596f75204172651f734e697276616e611f734e657665726d696e64202852656d61737465726564291f734e697276616e611f73526f636b1f73323031311f73332f31331f663231392e3338341f693132381f6934383030301f69321f736d70331f736d70331f69333533383536391f69313738373234303934341f73366635336139613937616430343135376636336330326531636530373933326138643764343364641f73626f74681f69311f69301f73323736343836313663376339313663353466366162616161373833383263646264663137626639371e30
blake2b-8 = 5f926c0c8461c168
h   = 73515368   ✔
```

**V5 — `tracks`, ligne vivante `source='library'` avec non-ASCII, chaînes vides et NULL**

```
path='D:\Christina Aguilera\Suéltame - Christina Aguilera.mp3', title='Suéltame',
artist='Christina Aguilera', album='Suéltame', album_artist='', genre='', year='', track_no='',
duration=175.464, bitrate=128, sample_rate=48000, channels=2, codec='mp3', ext='mp3',
file_size=2828328, file_mtime=1784398226.0, cover_sha1='29252bede6eebf7210f1b5babf07acd2a2204c30',
source='library', in_history=0, history_pos=NULL, history_cover_sha1=NULL, deleted=0

hex = 747261636b731f73443a5c436872697374696e6120416775696c6572615c5375c3a96c74616d65202d20436872697374696e6120416775696c6572612e6d70331f735375c3a96c74616d651f73436872697374696e6120416775696c6572611f735375c3a96c74616d651f731f731f731f731f663137352e3436341f693132381f6934383030301f69321f736d70331f736d70331f69323832383332381f69313738343339383232361f73323932353262656465366565626637323130663162356261626630376163643261323230346333301f736c6962726172791f69301f001f001e30
blake2b-8 = e5973bcc6f56dac4
h   = 794221252   ✔
```

**V6 — `tracks`, pierre tombale** (20 colonnes à NULL après la clé, `deleted=1`)

```
path='D:\(I Just) Died In Your Arms - Cutting Crew.mp3'
hex = 747261636b731f73443a5c2849204a75737429204469656420496e20596f75722041726d73202d2043757474696e6720437265772e6d70331f001f001f001f001f001f001f001f001f001f001f001f001f001f001f001f001f001f001f001f001e31
blake2b-8 = 5a4950a85694b34a
h   = 378843978   ✔
```

**V7 — `covers`, ligne avec un BLOB.** Comme `sha1 = SHA1(data)`, le test se rejoue **sans les octets de l'image** : l'élément BLOB vaut `'x'` suivi des 20 octets de la clé décodée en hexadécimal.

```
sha1='73d3064fae985f28150315a3828543d4da4dc5fb', mime='image/jpeg', bytes=19710,
width=250, height=250, data=<19 710 octets>, deleted=0
hex = 636f766572731f73373364333036346661653938356632383135303331356133383238353433643464613464633566621f73696d6167652f6a7065671f6931393731301f693235301f693235301f7873d3064fae985f28150315a3828543d4da4dc5fb1e30
blake2b-8 = cfe10c1318ef36ae
h   = 418330286   ✔
```

**V8 — `reference_durations`**

```
key='taylor swift|22', durations='[242,232,303,233,243,232,158,291,280,231,260,63,230,231]',
fetched_at=1786982716.796, deleted=0
hex = 7265666572656e63655f6475726174696f6e731f737461796c6f722073776966747c32321f735b3234322c3233322c3330332c3233332c3234332c3233322c3135382c3239312c3238302c3233312c3236302c36332c3233302c3233315d1f66313738363938323731362e3739361e30
blake2b-8 = c34ff09113d1be25
h   = 332512805   ✔
```

**V9 — `quality`** (texte JSON long, 469 octets de message)

```
path='D:\Around the World (Radio Edit) - Daft Punk.mp3', stamp='1784567348000000000:3878924',
measures=<JSON du § 2.6>, defects='[]', deleted=0
blake2b-8 = c2d79d5b187da7e1
h = 410888161   ✔
```

**V10 — table synthétique `demo(k TEXT PK, v REAL)`**, pour des tests unitaires indépendants de la base :

| Ligne | Message (hex) | blake2b-8 | h |
|---|---|---|---|
| `k='a', v=NULL` | `64656d6f1f73611f001e30` | `e21e51a79adc63a2` | 450651042 |
| `k='a', v=1.0` (ou `v=1`) | `64656d6f1f73611f69311e30` | `d85ee3ca84f86d12` | 83389714 |
| `k='a', v=0.1+0.2` | `64656d6f1f73611f66302e331e30` | `2228629a8f9d6478` | 261973112 |
| `k='a', v=NULL, deleted=1` | `64656d6f1f73611f001e31` | `d863e1be399f7b34` | 966753076 |
| `k='é', v=2.5` | `64656d6f1f73c3a91f66322e351e30` | `916910aaddbaf0cd` | 498790605 |
| `k='x', v=BLOB 010203` | `64656d6f1f73781f787037807198c22a7d2b0807371d763779a84fdfcf1e30` | `f37c4cc6bfd27a10` | 1070758416 |
| `k='t', v=True` (booléen) | `64656d6f1f73741f62311e30` | `8cc880401f838452` | 528712786 |

#### 3.1.5 Anomalie : les `h` de la table `archive`

Les 3 581 lignes de `archive` ont un `h` calculé avec le nom de table **`opus_mirror`**, avant le renommage. Avec le nom `archive`, prévu par le code actuel, **aucune** ne concorde ; avec `opus_mirror`, les 3 581 concordent. Exemple sur la ligne `D:\Divers\Go Go Go Go! (Club Mix) - 89ers.mp3` : le `h` stocké vaut 363339425, qui est `row_hash("opus_mirror", …)`, alors que `row_hash("archive", …)` vaut 1001217216.

Conséquences et règle de portage :

* le code de référence utilise **`archive`** : toute réécriture d'une ligne, par le desktop comme par .NET, lui donne la nouvelle empreinte ;
* il ne faut **pas** « réparer » massivement ces `h` sans raison : ce serait sans effet fonctionnel, mais cela réécrirait 3 581 lignes ;
* un éventuel outil de contrôle d'intégrité doit tolérer cette table, ou accepter les deux noms.

#### 3.1.6 Quelles valeurs hacher ?

Python hache les valeurs **telles qu'il les écrit**, avant que SQLite applique l'affinité de type. Si l'on passait `"128"` (texte) à une colonne INTEGER, SQLite stockerait `128` mais le hash aurait été calculé sur `s128`. La règle sûre pour .NET est donc de hacher **les valeurs telles qu'elles seront relues**, ce qui revient à :

* passer des valeurs déjà typées selon la colonne : `long` pour INTEGER, `double` pour REAL, `string` pour TEXT, `byte[]` pour BLOB, `null` ;
* **ne jamais passer de `bool`** : un `in_history` en `bool` serait haché `b1` mais relu `1`, donc `i1`. Il faut convertir en `0L`/`1L` ;
* garder un REAL en `double`, même rond (`file_mtime`) : il est haché `i…` dans les deux cas, ce qui est cohérent.

Une fois la base relue, `canonical()` appliqué aux valeurs lues doit redonner le `h` stocké. C'est le test d'intégrité de référence (§ 3.1.4).

#### 3.1.7 Implémentation C# de référence

Ce code n'a pas été compilé ici (aucun SDK .NET sur la machine d'analyse). Il doit être validé avec les vecteurs ci-dessus.

```csharp
using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

public static class RowHash
{
    public const long HashMask = (1L << 30) - 1;

    // columns : colonnes de contenu dans l'ordre DDL (clé primaire comprise).
    public static long Compute(string table, IReadOnlyList<string> columns,
                               IReadOnlyDictionary<string, object?> row, bool deleted)
    {
        var ms = new MemoryStream();
        ms.Write(Encoding.UTF8.GetBytes(table));
        foreach (var col in columns)
        {
            ms.WriteByte(0x1F);
            row.TryGetValue(col, out var v);
            ms.Write(Canonical(v));
        }
        ms.WriteByte(0x1E);
        ms.WriteByte(deleted ? (byte)'1' : (byte)'0');

        // BLAKE2b avec une sortie de 8 octets (PAS une troncature de BLAKE2b-512).
        byte[] digest = Blake2Fast.Blake2b.ComputeHash(8, ms.ToArray());
        ulong u = BinaryPrimitives.ReadUInt64BigEndian(digest);
        return (long)(u & (ulong)HashMask);
    }

    public static byte[] Canonical(object? v) => v switch
    {
        null or DBNull => new byte[] { 0x00 },
        bool b         => Ascii("b" + (b ? "1" : "0")),         // à éviter (cf. § 3.1.6)
        long or int or short or byte
                       => Ascii("i" + Convert.ToInt64(v).ToString(CultureInfo.InvariantCulture)),
        double d       => CanonicalDouble(d),
        float f        => CanonicalDouble(f),
        byte[] blob    => Concat((byte)'x', SHA1.HashData(blob)),
        string s       => Concat((byte)'s', Encoding.UTF8.GetBytes(s)),
        _              => Concat((byte)'s', Encoding.UTF8.GetBytes(Convert.ToString(v, CultureInfo.InvariantCulture)!)),
    };

    static byte[] CanonicalDouble(double d)
    {
        if (double.IsFinite(d) && Math.Truncate(d) == d)
            return Ascii("i" + new BigInteger(d).ToString(CultureInfo.InvariantCulture)); // -0.0 -> "0"
        return Ascii("f" + PyRepr(PyRound6(d)));
    }

    // round(x, 6) de Python : arrondi exact de la valeur binaire, égalité au pair,
    // puis conversion décimal -> double correctement arrondie.
    public static double PyRound6(double x)
    {
        long bits = BitConverter.DoubleToInt64Bits(x);
        bool neg = bits < 0;
        int exp = (int)((bits >> 52) & 0x7FF);
        long man = bits & 0xFFFFFFFFFFFFFL;
        if (exp == 0) exp = 1; else man |= 1L << 52;
        exp -= 1075;                                   // |x| = man * 2^exp
        BigInteger num = new BigInteger(man) * 1_000_000, den = BigInteger.One;
        if (exp > 0) num <<= exp; else den <<= -exp;
        BigInteger q = BigInteger.DivRem(num, den, out var rem);
        int cmp = (rem * 2).CompareTo(den);
        if (cmp > 0 || (cmp == 0 && !q.IsEven)) q += 1;
        double r = double.Parse(q.ToString(CultureInfo.InvariantCulture) + "E-6",
                                CultureInfo.InvariantCulture);  // .NET Core 3+ : arrondi correct
        return neg ? -r : r;
    }

    // repr(float) de Python (écriture la plus courte qui se relit à l'identique).
    public static string PyRepr(double d)
    {
        if (d == 0) return double.IsNegative(d) ? "-0.0" : "0.0";
        string r = Math.Abs(d).ToString("R", CultureInfo.InvariantCulture); // "241.512", "1E-05", "1.5E-05"
        int e = 0, ePos = r.IndexOfAny(new[] { 'E', 'e' });
        if (ePos >= 0) { e = int.Parse(r[(ePos + 1)..], CultureInfo.InvariantCulture); r = r[..ePos]; }
        int dot = r.IndexOf('.');
        string intPart = dot >= 0 ? r[..dot] : r, frac = dot >= 0 ? r[(dot + 1)..] : "";
        string digits = intPart + frac;
        int decpt = intPart.Length + e;
        int lead = 0; while (lead < digits.Length - 1 && digits[lead] == '0') lead++;
        digits = digits[lead..]; decpt -= lead;
        digits = digits.TrimEnd('0'); if (digits.Length == 0) digits = "0";

        string body;
        if (decpt <= -4 || decpt > 16)
        {
            int x = decpt - 1;
            body = digits[0] + (digits.Length > 1 ? "." + digits[1..] : "")
                 + "e" + (x < 0 ? "-" : "+") + Math.Abs(x).ToString("00", CultureInfo.InvariantCulture);
        }
        else if (decpt <= 0) body = "0." + new string('0', -decpt) + digits;
        else if (decpt >= digits.Length) body = digits + new string('0', decpt - digits.Length) + ".0";
        else body = digits[..decpt] + "." + digits[decpt..];
        return (d < 0 ? "-" : "") + body;
    }

    static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);
    static byte[] Concat(byte p, byte[] rest) { var a = new byte[rest.Length + 1]; a[0] = p; rest.CopyTo(a, 1); return a; }
}
```

Lors de la lecture avec `Microsoft.Data.Sqlite`, `reader.GetValue(i)` renvoie `long` pour INTEGER, `double` pour REAL, `string` pour TEXT, `byte[]` pour BLOB et `DBNull` pour NULL. C'est exactement la correspondance attendue par `Canonical`.

Colonnes de contenu par table, dans l'ordre exact :

| Table | Colonnes (ordre DDL) |
|---|---|
| `app_settings` | `key, value` |
| `searches` | `query, position` |
| `covers` | `sha1, mime, bytes, width, height, data` |
| `tracks` | `path, title, artist, album, album_artist, genre, year, track_no, duration, bitrate, sample_rate, channels, codec, ext, file_size, file_mtime, cover_sha1, source, in_history, history_pos, history_cover_sha1` |
| `quality` | `path, stamp, measures, defects` |
| `archive` | `path, sha1, stamp, key, bytes, bitrate, source_bytes, uploaded_at` |
| `reference_durations` | `key, durations, fetched_at` |

### 3.2 Écriture d'une ligne : `upsert` / `upsert_many` (contrat à respecter)

C'est **l'unique chemin d'écriture** autorisé dans les 7 tables. Le docstring de `local_db` le dit : « aucun module ne doit écrire dans les tables synchronisées par un SQL direct ».

```
now = time.time()                                     // secondes Unix, double
pour chaque ligne fournie (dictionnaire des colonnes de contenu) :
    deleted    = deleted ? 1 : 0                      (0 par défaut)
    updated_at = paramètre explicite
                 ?? (valeur fournie si non nulle et ≠ 0)
                 ?? now
    h          = row_hash(table, ligne)               // colonnes absentes => NULL
    valeurs    = [ligne[c] pour c dans all_columns]   // contenu + updated_at, deleted, h

INSERT INTO <t> (<all_columns>) VALUES (?, …)
ON CONFLICT(<pk>) DO UPDATE SET <c> = excluded.<c>    -- pour chaque colonne sauf pk

si queue (vrai par défaut) :
INSERT INTO sync_outbox (table_name, pk, queued_at) VALUES (?, ?, now)
ON CONFLICT(table_name, pk) DO UPDATE SET queued_at = excluded.queued_at

COMMIT   (le tout dans une seule transaction par appel)
```

Conséquences importantes :

* **Un upsert remplace toute la ligne** : une colonne absente du dictionnaire est écrite à NULL. Pour modifier un seul champ, il faut relire la ligne, la modifier puis la réécrire en entier. C'est ce que font `_mirror_history` et `library_page`.
* `mark_deleted(t, clés)` est un upsert où toutes les colonnes de contenu valent NULL sauf la clé, avec `deleted=1`. La ligne est ensuite mise en file.
* Faire revivre une ligne revient à faire un upsert avec `deleted=0`.
* `hard_delete(t, clés, queue=False)` exécute `DELETE FROM t WHERE pk = ?`. Avec `queue=True`, il **retire** aussi les clés de `sync_outbox` : la sémantique est inverse de celle de `upsert`. Cette fonction ne sert qu'à la purge.
* `set_settings(map)` ne réécrit une clé que si sa valeur JSON canonique **diffère** de celle stockée (ou si la ligne est absente ou supprimée). Le JSON canonique conditionne donc aussi la détection de changement.

### 3.3 JSON canonique (`db_schema.json_dump`)

Le JSON est produit par `json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))`, ce qui donne :

* aucun espace ;
* les clés d'objet **triées** par point de code (en .NET, `StringComparer.Ordinal`) ;
* les caractères non ASCII écrits **tels quels** (`é`, et non `\u00e9`). Seuls `"`, `\` et les caractères de contrôle sont échappés : `\n`, `\r`, `\t`, `\b`, `\f`, et les autres en `\u00XX`, hexadécimal minuscule. `/` n'est **pas** échappé ;
* `true`, `false`, `null` ;
* les entiers en décimal ;
* les **flottants avec `repr` Python**, donc `3.0`, `-65.0`, `0.2`, `0.0025`, `1e-05`. Pour l'infini et NaN, Python écrirait `Infinity` et `NaN`, mais ces valeurs sont neutralisées en amont pour `measures`.

Points d'attention pour .NET :

* `System.Text.Json` écrit `3` pour `3.0d` et échappe le non-ASCII par défaut. Même avec `UnsafeRelaxedJsonEscaping`, certains caractères restent échappés. Il faut **un écrivain JSON dédié** : un `Utf8JsonWriter` avec `WriteRawValue` pour les nombres formatés par `PyRepr` (§ 3.1.7, sans l'étape `round`), ou une petite sérialisation maison.
* La distinction entier / flottant doit être préservée. Un réglage `qual_*` vaut `3.0` (flottant), alors que `org_divers_min` et `mirror_bitrate` sont des entiers (`3`, `128`). Si une valeur est réécrite avec un autre type, sa forme JSON change, donc son `h` aussi, et la clé est réécrite à chaque enregistrement.

Lecture (`json_load`) : `NULL` ou `""` donnent la valeur par défaut ; un JSON invalide donne aussi la valeur par défaut.

### 3.4 `image_info` (type MIME et dimensions)

| Signature | MIME | Dimensions |
|---|---|---|
| `89 50 4E 47 0D 0A 1A 0A` et longueur ≥ 24 | `image/png` | largeur = BE32 [16..20), hauteur = BE32 [20..24) |
| `GIF8…` et longueur ≥ 10 | `image/gif` | LE16 [6..8), LE16 [8..10) |
| `BM…` et longueur ≥ 26 | `image/bmp` | LE32 [18..22), LE32 [22..26) |
| `RIFF????WEBP` | `image/webp` | 0, 0 |
| `FF D8` | `image/jpeg` | voir ci-dessous ; à défaut 0, 0 |
| autre | `application/octet-stream` | 0, 0 |
| vide | `""` | 0, 0 |

Parcours JPEG : `i = 2` ; tant que `i + 9 < len` :

* si `data[i] != 0xFF`, on avance de 1 ;
* sinon, on lit `m = data[i+1]` ;
* si `m ∈ {0xD8, 0x01} ∪ [0xD0..0xD7]`, on avance de 2 ;
* sinon, `L = BE16(data[i+2..i+4])` ;
* si `0xC0 ≤ m ≤ 0xCF` et `m ∉ {C4, C8, CC}`, on renvoie hauteur = `BE16(data[i+5..i+7])` et largeur = `BE16(data[i+7..i+9])` ;
* si `L ≤ 0`, on sort ;
* sinon, `i += 2 + L`.

### 3.5 Normalisation des chemins

* `canonical_path(p) = os.path.normpath(str(p).replace("/", os.sep))`. Sous Windows, cela donne des `\`, supprime les `.`/`..` et les séparateurs doublés, et **conserve la casse**. Le résultat sert de clé dans `tracks`, `quality` et `archive`.
* `match_key(p) = os.path.normcase(canonical_path(p))`. Sous Windows, c'est le chemin en minuscules avec des `\`. Il sert aux rapprochements insensibles à la casse et n'est pas stocké, sauf dans les clés de `quality_cache.json`.

**Piège du portage sous Linux** : sous Linux, les mêmes fonctions Python ne transformeraient plus `/` en `\` et ne changeraient plus la casse. Les clés existantes sont des chemins Windows (`D:\…`). Voir le § 6.4.

### 3.6 Collecte et initialisation (`db_init.build_local`)

1. `config = deekeep_config.json`.
2. `settings_map = collect_settings(config, include_secrets)` (§ 5.3).
3. Racines à parcourir : `--root`, sinon `[settings_map.download_dir]`.
4. Avec parcours (`scan`), `scan_library` :
   * parcours récursif (`os.walk`) en écartant les dossiers qui commencent par `$` et `System Volume Information` ;
   * extensions retenues, sans tenir compte de la casse : `.mp3 .flac .m4a .aac .ogg .opus .wav .wma .alac .aiff .aif` ;
   * pour chaque fichier : `os.stat`, puis lecture des tags, du flux et de la jaquette avec une seule ouverture mutagen ;
   * la ligne `tracks` produite a `source='library'`, `in_history=0`, `history_pos=NULL`, `history_cover_sha1=NULL`.

   Sans parcours, on repart des lignes vivantes de `tracks`, colonnes de contenu seulement.
5. `merge_history` (§ 2.5.1).
6. `collect_references()` lit `reference_cache.json` et produit des lignes `{key, durations: json_dump(list), fetched_at: round(time, 3)}`.
7. `collect_quality()` lit `quality_cache.json`. Chaque entrée est rapprochée par `match_key` des titres relevés pour retrouver le chemin réel. On en tire `measures = json_dump(measures)` et `defects = json_dump(evaluate(…)[0])`.
8. `collect_searches(config)` (§ 2.3).
9. Écritures, toutes par upsert **mis en file** :
   * `set_settings` ;
   * `searches` ;
   * `covers` par lots de 500 ;
   * `tracks` par lots de 500 ;
   * `quality` par lots de 500 ;
   * `reference_durations`.

   Rien n'est supprimé : il n'y a **pas** de pierre tombale pour les fichiers disparus du disque lors d'une collecte. Une ligne `library` dont le fichier a été supprimé reste donc en base.
10. Bilan : nombre de lignes vivantes par table, octets de jaquettes, nombre d'entrées d'historique, taille de la file, taille du fichier.

**Ligne de commande** : `python -m deekeep.db.db_init [options]`.

| Option | Effet |
|---|---|
| *(aucune)* | Collecte et remplissage du cache local. |
| `--root DOSSIER` | Dossier à parcourir. L'option se répète ; par défaut, c'est `download_dir`. |
| `--no-scan` | Ne reparcourt pas le disque et repart des titres en cache. |
| `--reset` | Vide les 7 tables, `sync_state` et `sync_outbox` (`reset_data`) avant la collecte. |
| `--secrets` | Inclut `arl` et `acoustid_key` dans `app_settings`. |
| `--push`, `--status`, `--resolve local\|remote`, `--table T`, `--url`, `--token` | Turso, abandonné. `--url`/`--token` écrivent `cloud.json`. |

Codes de sortie : 0 en cas de succès, 2 si aucune base en ligne n'est configurée (avec `--push` ou `--resolve`), 1 si l'état final après `--push` n'est pas « synchronisé ».

**Mise à jour continue** (`db_init.mirror_state`, appelée par `SyncController.record_state` au démarrage puis à chaque `save_config`) :

* `set_settings(collect_settings(config, include_secrets=config.cloud_sync_secrets))` ;
* `_mirror_searches` ;
* `_mirror_history`.

Elle n'utilise aucun réseau et ne parcourt pas le disque. En web, l'équivalent est le service qui écrit les réglages, les recherches et l'historique.

---

## 4. Synchronisation Turso : fonctionnalité abandonnée

La version desktop savait répliquer les 7 tables vers une base Turso/libSQL par HTTP (`POST https://<base>.turso.io/v2/pipeline`, jeton `Bearer`, protocole Hrana). Pour décider du sens des échanges, elle comparait trois empreintes de table : locale, distante et « dernière commune » (`sync_state`). L'empreinte d'une table était `SELECT COUNT(*), COALESCE(SUM(h), 0) FROM t`, pierres tombales comprises. Les modifications locales en attente d'envoi étaient tenues dans `sync_outbox`. Les échanges se faisaient au démarrage, 4 s après une modification, puis toutes les 5 min. En cas de conflit, c'est l'utilisateur qui arbitrait. L'état était affiché en pied de barre latérale (« Base synchronisée », « Base locale seule »…).

**Cette fonctionnalité n'est plus utilisée et ne sera pas portée.** Sur la machine de référence, `cloud.json` est renommé `cloud.json.disabled` : l'état est donc « Base locale seule ». `sync_state` est figé depuis le 25/10/2026, et `sync_outbox` contient 8 760 entrées qui ne partiront jamais.

Ce qu'il en reste dans le fichier, à préserver : les colonnes `updated_at`, `deleted` et `h`, les tables `sync_state` et `sync_outbox`, et `schema_meta`. Les règles d'entretien sont au § 6.2.

---

## 5. Réglages (`core/settings.py` et table `app_settings`)

### 5.1 Stockage et cycle de vie

* Fichier : `data_dir()/deekeep_config.json`, en UTF-8, avec `indent=2` et `ensure_ascii=False`. L'écriture est **atomique** : écriture de `deekeep_config.json.tmp`, puis `os.replace`.
* Chargement (`load_settings`) :
  1. migration de l'ancien emplacement ;
  2. `settings = default_settings()` ;
  3. `settings.update(json_lu)`, si le fichier est un objet JSON valide.

  Une clé inconnue est conservée ; une clé manquante reçoit sa valeur par défaut. Une erreur de lecture est journalisée et les défauts sont utilisés.
* **Migration** (`_migrate_legacy_config`) : si `deekeep_config.json` n'existe pas dans le dossier de données, on **copie** le premier fichier trouvé (`shutil.copy2`) parmi :
  1. `<racine du projet>/deekeep_config.json`, la racine étant trois niveaux au-dessus de `settings.py` ;
  2. pour un exécutable PyInstaller, `<dossier de l'exe>/deekeep_config.json`.

  L'ancien fichier est laissé en place. Un tel fichier existe à la racine du projet (`D:\000_Logiciel\DeezerMusic\deekeep_config.json`).
* Desktop : le JSON reste la source de l'interface. À chaque `save_config`, le JSON est écrit, puis `record_state` reporte l'état dans la base. La base n'est relue vers le JSON qu'après un arbitrage Turso en faveur de la base distante (`restore_state`). Pour le web, on fera de la base la source de vérité ; le JSON n'aura plus qu'un rôle de collecte initiale.

### 5.2 Clés connues

| Clé | Type | Défaut | Valeurs, rôle |
|---|---|---|---|
| `download_dir` | chaîne | `~/Music` (chemin absolu, `os.path.join(expanduser("~"), "Music")`) | Dossier de dépôt et racine de la bibliothèque. Valeur actuelle : `"D:/"`. |
| `file_format` | chaîne | `"MP3"` | `MP3`, `FLAC`, `WAV`, `M4A` |
| `quality` | chaîne | `"320 kbps (HQ)"` | `320 kbps (HQ)`, `128 kbps (Standard)` ; `Lossless (Qualité Source)` pour FLAC et WAV |
| `org_artist` | booléen | `true` | Sous-dossier par artiste |
| `org_album` | booléen | `true` | Sous-dossier par album |
| `org_divers` | booléen | `false` | Regrouper les petits artistes dans « Divers » |
| `org_divers_min` | entier | `3` | Seuil de titres pour avoir un dossier propre |
| `org_rename` | booléen | `false` | Renommer selon le motif |
| `org_pattern` | chaîne | `"%titre% - %artiste%"` | Motif de nom (préréglages : `%titre% - %artiste%`, `%artiste% - %titre%`, `%artiste% - %album% - %titre%`, …) |
| `org_remove_empty` | booléen | `true` | Supprimer les dossiers vides |
| `org_report_untagged` | booléen | `true` | Signaler les fichiers sans tags |
| `org_keep_albums` | booléen | `true` | Ne pas défaire les compilations |
| `org_dedup` | booléen | `true` | Ne garder qu'une copie d'un même titre |
| `org_filters_open` | booléen | `false` | Volet des filtres déplié |
| `acoustid_key` | chaîne | `""` | **Secret** : clé d'application AcoustID |
| `youtube_browser` | chaîne | `"Aucun (anonyme)"` | `Aucun (anonyme)`, `Chrome`, `Edge`, `Firefox`, `Brave`, `Opera`, `Vivaldi` |
| `cookies_file` | chaîne | `""` | Chemin d'un fichier cookies.txt au format Netscape |
| `search_history` | liste de chaînes | `[]` | **Pas un réglage** : table `searches` |
| `downloaded_history` | liste d'objets | `[]` | **Pas un réglage** : table `tracks` (§ 2.5.1) |
| `qual_hiss_db` | flottant | `-65.0` | Seuil de l'écran Qualité (`quality_scan.DEFAULT_THRESHOLDS`) |
| `qual_clipping_pct` | flottant | `0.2` | idem |
| `qual_crackle_pct` | flottant | `0.0025` | idem |
| `qual_gap_density_pct` | flottant | `30.0` | idem |
| `qual_tail_seconds` | flottant | `5.0` | idem |
| `qual_head_seconds` | flottant | `3.0` | idem |
| `qual_duration_excess` | flottant | `3.0` | idem |
| `qual_duration_gap` | flottant | `10.0` | idem |
| `qual_deep` | booléen | `true` | Analyse approfondie |
| `mirror_bitrate` | entier | `128` (`opus_mirror.DEFAULT_BITRATE`) | Débit de l'ancien miroir Opus (64, 80, 96, 128, 160 ou 192) |
| `mirror_source_only` | booléen | – | Ancien miroir « tel quel » |
| `mirror_destination` | chaîne | – | **Clé héritée**, présente en base seulement (`"archive"`) et lue par aucun code |
| `arl` | chaîne | – | **Secret** : cookie de session Deezer, présent dans le JSON (vide) |
| `cloud_sync_secrets` | booléen | absent, donc faux | Si vrai, `arl` et `acoustid_key` sont recopiés dans `app_settings` |

Les seuils `qual_*` et `qual_deep` ne figurent pas dans `default_settings()`. `collect_settings` les ajoute avec les défauts de `quality_scan` ; l'écran Qualité lit `settings.get("qual_"+k, DEFAULT_THRESHOLDS[k])`.

### 5.3 `collect_settings`: ce qui va dans `app_settings`

```
result = {}
pour (k, v) dans config :
    k ∈ {search_history, downloaded_history}        → ignorée
    k ∈ {arl, acoustid_key} et non include_secrets  → ignorée
    sinon result[k] = v
pour (k, v) dans default_settings() : si k n'est ni historique ni secret → result.setdefault(k, v)
pour (k, v) dans quality_scan.DEFAULT_THRESHOLDS : result.setdefault("qual_"+k, v)
result.setdefault("qual_deep", True)
```

Puis `set_settings(result)`, qui n'écrit que les clés dont le JSON canonique a changé. **Aucune clé n'est jamais supprimée** de `app_settings` : c'est pourquoi `mirror_destination` y subsiste.

**Secrets.** Ils sont exclus par défaut (`cloud_sync_secrets` absent) : la base actuelle ne contient ni `arl` ni `acoustid_key`. `record_state` lit le drapeau dans la configuration en mémoire, alors que `initialize` le lit dans `app_settings`. Cette incohérence est sans effet aujourd'hui. Pour le web, les secrets (`arl`, `acoustid_key`, `cookies_file`) relèvent d'un coffre (configuration chiffrée ou secrets de déploiement) et **ne doivent pas** être écrits dans `app_settings`.

---

## 6. Points d'attention pour le portage .NET multi-processus

### 6.1 Ce que fait le desktop aujourd'hui

* Il ouvre **une seule connexion** `sqlite3` par processus (`check_same_thread=False`), sérialisée par un `threading.RLock`. Tous les fils passent par elle, lectures comprises.
* Pragmas à l'ouverture : `journal_mode = WAL`, `synchronous = NORMAL`, `foreign_keys = ON`. Ce dernier est sans effet, faute de clés étrangères déclarées.
* Le délai d'attente de verrou est celui de Python par défaut, soit **5 s**. La connexion est en mode transactionnel implicite (`isolation_level=""`, `BEGIN` différé avant chaque écriture). Chaque `upsert_many` forme une transaction qui se termine par un `commit`. Les blocs `with self._conn:` valident ou annulent.
* `VACUUM` est exécuté hors transaction (`vacuum()`). `size_bytes()` additionne `.db`, `-wal` et `-shm`.
* Lecteurs extérieurs à `local_db` : `library_page.reload`, `library_index._tags_from_cache` et `sync_ui._history_from_db`, toujours via `local_db.shared().query(...)`.
* L'archive audio (`sqlite_store`) est un **autre** fichier SQLite, en WAL, `synchronous=NORMAL`, `timeout=30`.

### 6.2 Invariants à respecter pour rester compatible

1. **Toute écriture dans une des 7 tables passe par un `upsert` équivalent** (§ 3.2) : ligne complète, `updated_at = now` (en secondes Unix décimales), `deleted` à 0 ou 1, `h` recalculé (§ 3.1), et `INSERT … ON CONFLICT(table_name, pk) DO UPDATE SET queued_at` dans `sync_outbox`, **dans la même transaction**. La file n'a plus d'usage, mais l'alimenter ne coûte rien et garde le fichier conforme au contrat du desktop. On peut aussi décider explicitement de ne plus l'alimenter : le desktop ne s'en servait que pour Turso. Ne jamais la vider en dehors de `reset_data`.
2. **Suppression = pierre tombale** (`mark_deleted`) : contenu à NULL, `deleted=1`, `h` recalculé. Pas de `DELETE` physique, sauf purge volontaire.
3. **Lectures** : toujours `WHERE deleted = 0`. Pour les comptes, `COUNT(*) … WHERE deleted = 0`.
4. Ne **jamais** changer `schema_meta.schema_version` (le desktop viderait la base, § 2.9). Ne pas toucher `sync_state`. Garder `sync_reference`.
5. **Ne pas modifier le DDL existant** : ni colonne ajoutée ou renommée dans ces 7 tables, ni changement d'ordre, car l'ordre et la liste des colonnes définissent `h`. Pour étendre le modèle, créer **de nouvelles tables** (par exemple `web_users`, `web_jobs`) ou de nouveaux index, en `IF NOT EXISTS`.
6. Typage des paramètres : `long`, `double`, `string`, `byte[]`, `null`. Jamais de `bool`, de `decimal` ou de `DateTime` dans ces tables (§ 3.1.6). Les réglages s'écrivent en JSON canonique (§ 3.3).
7. Les jaquettes s'écrivent **avant** les titres qui les référencent. La clé `sha1` est le SHA-1 hexadécimal minuscule des octets bruts, sans retraitement. Une miniature générée pour le web doit vivre **ailleurs**, dans une table ou un cache séparé : la stocker dans `covers` créerait une seconde entrée.

### 6.3 Accès concurrent depuis plusieurs processus avec `Microsoft.Data.Sqlite`

* **Fichier local uniquement.** Le mode WAL repose sur la mémoire partagée (`-shm`) et les verrous POSIX. Le fichier doit se trouver sur le disque local du serveur ARM64, **jamais** sur un partage NFS ou SMB, ni sur un volume Docker monté depuis le réseau. Tous les conteneurs qui y accèdent doivent monter **le même répertoire**, `-wal` et `-shm` compris, et tourner sur le même noyau.
* **Paquets** : `Microsoft.Data.Sqlite` avec `SQLitePCLRaw.bundle_e_sqlite3`, qui fournit une bibliothèque native `linux-arm64`. Utiliser une version de SQLite récente, au moins 3.35 pour `RETURNING` ; le fichier est actuellement écrit par 3.50.4. La syntaxe UPSERT demande au moins 3.24. Dans `INSERT … SELECT … ON CONFLICT`, il faut garder `WHERE true` avant `ON CONFLICT` (ambiguïté d'analyse de SQLite, cf. `queue_all`).
* **Chaîne de connexion** type : `Data Source=/srv/deekeep/deekeep_cache.db;Mode=ReadWrite;Cache=Private;Pooling=True;Default Timeout=30`. Pour les services de lecture seule : `Mode=ReadOnly`.
* **À chaque ouverture de connexion** : `PRAGMA busy_timeout = 10000;` et `PRAGMA synchronous = NORMAL;`. `journal_mode=WAL` est déjà persistant dans le fichier, inutile de le reposer (c'est sans danger, mais c'est une écriture). Ne pas activer `foreign_keys` : c'est inutile.
* **Transactions d'écriture en `BEGIN IMMEDIATE`** (`connection.BeginTransaction(deferred: false)` ou `IsolationLevel.Serializable`). Cela évite qu'une transaction différée qui lit puis écrit échoue en `SQLITE_BUSY` sans attendre le `busy_timeout`, ce qui arrive en cas de montée en verrou dans un snapshot WAL périmé. Les transactions doivent rester **courtes**, avec des lots de 500 lignes au plus, comme `_write_in_batches`. Le calcul des `h` et des SHA-1 se fait **avant** d'ouvrir la transaction.
* **Un seul écrivain logique.** SQLite n'admet qu'un écrivain à la fois. Sur un ARM64 modeste, le plus simple et le plus sûr est un **service de données unique** (le microservice « bibliothèque/données ») qui détient toutes les écritures et expose GraphQL ou gRPC aux autres. Les autres services lisent en `Mode=ReadOnly`. Si plusieurs processus doivent écrire, tous doivent passer par **la même bibliothèque partagée** (`DeeKeep.Data` : `Upsert`, `MarkDeleted`, `RowHash`, `JsonCanon`) pour que les invariants du § 6.2 restent garantis. Les tâches longues, comme le parcours du disque, calculent hors transaction puis écrivent par lots.
* **Lectures longues et checkpoints.** Un lecteur qui garde une transaction ouverte empêche le checkpoint WAL et fait grossir le fichier `-wal`. Il faut lire par pages et fermer vite les `DataReader`. Les jaquettes se lisent une à une (`SELECT data FROM covers WHERE sha1 = $1 AND deleted = 0`), éventuellement en flux (`GetStream`). Le checkpoint automatique (1 000 pages) suffit ; on peut ajouter un `PRAGMA wal_checkpoint(TRUNCATE)` planifié en heure creuse depuis le service écrivain.
* **`VACUUM`** exige un accès exclusif et réécrit 125 Mo. Il faut le réserver à une opération de maintenance explicite, services arrêtés ou mis en pause.
* **Cohabitation avec le desktop.** Si l'application PyQt ouvre le même fichier en même temps, elle tient des verrous courts mais n'attend que 5 s : le service .NET doit donc garder des transactions courtes. En pratique, le fichier migrera sur le serveur Linux, où le desktop Windows ne pourra pas l'ouvrir en direct. Il faut éviter de copier le fichier pendant qu'un processus écrit : utiliser `VACUUM INTO` ou l'API de sauvegarde, en tenant compte du `-wal`.
* **Horodatage** : `updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0`, ou mieux `(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds`, qui garde la précision en dessous de la milliseconde.

### 6.4 Chemins Windows dans les clés primaires

`tracks.path`, `quality.path`, `archive.path` et `sync_outbox.pk` contiennent des chemins **Windows absolus** (`D:\…`, avec `\`), et `quality` contient même des chemins en minuscules. Sur le serveur Linux, la bibliothèque sera montée ailleurs (par exemple `/srv/musique/`).

* **Ne pas réécrire les clés** : changer la clé primaire revient à supprimer et recréer chaque ligne, ce qui change aussi tous les `h` et pose 5 000 pierres tombales.
* Introduire plutôt une **correspondance de racines** dans la configuration du service (`D:\` ↔ `/srv/musique/`). Pour l'accès disque, on convertit la clé en chemin physique : remplacement du préfixe, puis `\` → `/`. Pour un nouveau fichier trouvé sous Linux, on fait l'inverse pour fabriquer la clé au format Windows, avec la même normalisation que `canonical_path`. On garantit ainsi qu'un fichier déjà connu retrouve sa ligne.
* Les comparaisons insensibles à la casse (`match_key`) se font en `ToLowerInvariant()` sur la forme Windows. Sous Linux, deux fichiers peuvent ne différer que par la casse : il faut en tenir compte, au moins par un journal d'avertissement.
* Les empreintes `stamp` dépendent de `st_mtime` : en nanosecondes pour `quality`, en secondes entières pour `archive`. Une copie du disque vers le serveur qui ne préserve pas les dates de modification invalidera tous les `stamp`, ce qui signifie des mesures de qualité à refaire et une archive marquée « périmée ». Il faut utiliser `rsync -t` ou équivalent.

### 6.5 Requêtes SQL de référence

```sql
-- Bibliothèque (écran Bibliothèque)
SELECT path, title, artist, album, album_artist, genre, year, duration, bitrate,
       codec, ext, file_size, source
FROM tracks WHERE deleted = 0;

-- Index d'archive
SELECT path, sha1, stamp, key, bytes, bitrate, source_bytes FROM archive WHERE deleted = 0;

-- Index de recherche de présence sur disque
SELECT path, title, artist, album FROM tracks WHERE deleted = 0;

-- Historique des téléchargements, dans l'ordre (index 0 = le plus ancien)
SELECT path, title, artist, album, COALESCE(history_cover_sha1, cover_sha1) AS sha1
FROM tracks WHERE in_history = 1 AND deleted = 0 ORDER BY history_pos;

-- Historique des recherches (0 = la plus récente)
SELECT query FROM searches WHERE deleted = 0 ORDER BY position;

-- Jaquette
SELECT mime, data FROM covers WHERE sha1 = $sha1 AND deleted = 0;

-- Réglages
SELECT key, value FROM app_settings WHERE deleted = 0;

-- Statistiques
SELECT COUNT(*) FROM <t> WHERE deleted = 0;
SELECT COALESCE(SUM(bytes), 0) FROM covers WHERE deleted = 0;
SELECT COUNT(*) FROM tracks WHERE in_history = 1 AND deleted = 0;

-- Empreinte de table (contrôle d'intégrité, pierres tombales comprises)
SELECT COUNT(*), COALESCE(SUM(h), 0) FROM <t>;

-- Écriture générique (cf. § 3.2), exemple tracks
INSERT INTO tracks (path, title, …, history_cover_sha1, updated_at, deleted, h)
VALUES ($path, $title, …, $history_cover_sha1, $now, $deleted, $h)
ON CONFLICT(path) DO UPDATE SET title = excluded.title, …, updated_at = excluded.updated_at,
                                deleted = excluded.deleted, h = excluded.h;
INSERT INTO sync_outbox (table_name, pk, queued_at) VALUES ('tracks', $path, $now)
ON CONFLICT(table_name, pk) DO UPDATE SET queued_at = excluded.queued_at;

-- Méta
INSERT INTO schema_meta (key, value) VALUES (?, ?)
ON CONFLICT(key) DO UPDATE SET value = excluded.value;
```

### 6.6 Tests à prévoir dans la solution .NET

1. `RowHash` sur les vecteurs V1 à V10 et sur le tableau du § 3.1.3 (canonical, `PyRound6`, `PyRepr`).
2. **Test d'intégrité sur la base réelle**, ouverte en lecture seule : pour les 6 tables hors `archive`, recalculer `h` pour chaque ligne et exiger 100 % d'égalité, soit 15 531 lignes aujourd'hui. Pour `archive`, exiger l'égalité avec le nom `opus_mirror` ou `archive`.
3. JSON canonique : relire chaque `app_settings.value`, la désérialiser puis la resérialiser, et exiger l'identité octet pour octet (`-65.0`, `0.0025`, `"D:/"`, `true`…). Même exigence pour `quality.measures`, `quality.defects` et `reference_durations.durations`.
4. Upsert, relecture, recalcul : `h` doit être stable, `updated_at` strictement positif, et `sync_outbox` doit contenir la clé.
5. Concurrence : deux processus écrivant en boucle (`BEGIN IMMEDIATE`, `busy_timeout`) plus un lecteur en continu, sans aucun `SQLITE_BUSY` ni `database is locked`.

---

## Annexe : écarts et curiosités relevés dans l'existant

* Les `h` de `archive` ont été calculés sous le nom `opus_mirror` (§ 3.1.5).
* `schema_meta.sync_reference='remote'` n'est écrite par aucun code connu.
* 109 lignes `library` ont `in_history=0` mais un `history_cover_sha1` non NULL (il n'est pas effacé à la sortie de l'historique).
* 169 jaquettes ne sont référencées par aucun titre (pas de ramasse-miettes).
* `quality.path` contient des clés en minuscules, dont des chemins de `$recycle.bin`, quand le rapprochement a échoué. La table `quality` n'est lue par aucun écran.
* `_reference_for` (diagnostic de collecte) ne normalise pas la clé comme `reference_duration.key` (§ 2.8).
* Le format de `stamp` diffère entre `quality` (nanosecondes) et `archive` (secondes).
* `app_settings` garde des clés obsolètes (`mirror_destination`) faute de suppression.
* Une collecte ne pose aucune pierre tombale sur les fichiers disparus : les lignes `library` orphelines persistent.
