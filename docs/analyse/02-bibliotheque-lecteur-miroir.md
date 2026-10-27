# 02 — Bibliothèque, lecteur, index de présence et archive (miroir)

> Spécification fonctionnelle et technique issue de l'analyse du code Python/PyQt6 de DeeKeep
> (`D:\000_Logiciel\DeezerMusic`, paquet `deekeep/`, état du dépôt au commit `1122895`, 25/10/2026).
> Objectif : permettre la réimplémentation en Angular + GraphQL + microservices .NET (serveur Linux ARM64)
> **sans lire le Python**. Toutes les références `fichier:ligne` pointent vers ce dépôt.
>
> Périmètre : `deekeep/ui/library_page.py`, `deekeep/ui/player.py`, `deekeep/library/library_index.py`,
> `deekeep/audio/library_mirror.py`, `deekeep/audio/opus_mirror.py`, `deekeep/db/blob_store.py`,
> `deekeep/db/sqlite_store.py`, fichiers `%APPDATA%\DeeKeep\archive.json`, `store.json`, `bucket.json`,
> table `archive` (ex-`opus_mirror`) du cache `deekeep_cache.db`.

---

## 0. À lire d'abord : le code a changé de doctrine, le README non

Le README (sections « Bibliothèque », « Le flux audio ne tient pas dans la base », « Le miroir Opus »,
« Restaurer le disque depuis le seau », « Mise en service du seau ») décrit une **version antérieure** :
miroir **Opus** envoyé dans un **seau S3** (Cloudflare R2 / Backblaze B2), lecture en flux par URL signée,
restauration Opus→MP3, jauge des 10 Go gratuits, choix du débit.

Le code **actuellement branché** (commit `7700164`, « Add library mirror and SQLite store for lossless local archive ») a remplacé cela par :

| Aspect | Version README (morte) | Version actuelle (vivante) |
|---|---|---|
| Module de travail | `audio/opus_mirror.py` | `audio/library_mirror.py` |
| Magasin | `db/blob_store.py` (S3 SigV4) | `db/sqlite_store.py` (un fichier SQLite local) |
| Configuration | `bucket.json` + `DEEKEEP_S3_*` | `archive.json` + `DEEKEEP_ARCHIVE` |
| Contenu | Opus réencodé (ou source si déjà plus léger) | **toujours** le fichier source, octet pour octet |
| Écoute d'un titre absent du disque | URL S3 pré-signée 1 h | **impossible** (`presigned_get` lève une erreur) |
| Restauration | source vérifiée SHA-1, ou Opus→MP3 192 k | source vérifiée SHA-1 uniquement ; une clé Opus héritée → échec explicite |
| Table | `opus_mirror` | `archive` (mêmes colonnes) |

`opus_mirror.py` et `blob_store.py` **ne sont plus importés nulle part** (vérifié : seul `opus_mirror.py` importe `blob_store`, et rien n'importe `opus_mirror`). Ils sont néanmoins spécifiés ci-dessous (§2.6, §3.6–3.8, §4.2) car la version web pourrait vouloir réintroduire une cible S3 ou un réencodage.

L'interface a gardé une partie du vocabulaire « nuage / seau » (colonne **NUAGE**, menu « Envoyer ce titre au nuage », « Retirer du nuage », message « Seau joignable : identifiants acceptés. », mention de `bucket.json`). C'est du **texte résiduel** : fonctionnellement, il s'agit de l'**archive SQLite locale**.

---

## 1. Inventaire

### 1.1 Fichiers et rôle

| Fichier | Lignes | Rôle | Statut |
|---|---|---|---|
| `deekeep/ui/library_page.py` | 1 459 | Page « Bibliothèque » : liste depuis `tracks`, recherche multi-tags, 5 propositions, tri, colonne NUAGE, titres absents, volet Archive, jauge, archiver / restaurer / mesurer / tester, menus contextuels | vivant |
| `deekeep/ui/player.py` | 1 338 | Lecteur commun : fenêtre unique, file d'attente, égaliseur 10 bandes (FFT maison sur décodage ffmpeg parallèle), barre de position cliquable, clavier, extrait Deezer 30 s, règle « un seul son à la fois » | vivant |
| `deekeep/library/library_index.py` | 657 | Index du dossier de dépôt, `canonical`, clés de rapprochement, états ✓ ≈ ○ ⏳ | vivant (utilisé par la page Musique et la fenêtre de recherche ; `canonical` aussi par la page Bibliothèque) |
| `deekeep/audio/library_mirror.py` | 500 | Archivage « tel quel » et restauration vérifiée SHA-1 ; fils `MirrorThread`, `RestoreThread`, `CheckThread`, `UsageThread` | vivant |
| `deekeep/db/sqlite_store.py` | 406 | Magasin d'objets = un fichier SQLite (`objects`, `archive_meta`) | vivant |
| `deekeep/audio/opus_mirror.py` | 831 | Miroir Opus, plan de débit, ffmpeg libopus, restauration Opus→MP3 | **mort** |
| `deekeep/db/blob_store.py` | 469 | Client S3 compatible (R2/B2), signature SigV4 maison, URL pré-signées | **mort** |

Dépendances internes utiles : `deekeep/audio/reencode_scan.py:34-36` (extensions) et `:290-316` (`iter_audio_files`),
`deekeep/core/settings.py:42-52` (`data_dir`), `deekeep/audio/audio_probe.py:33-86` (`NO_WINDOW`, `ffmpeg_exe`),
`deekeep/db/local_db.py:146-219` (`upsert`, `mark_deleted`), `deekeep/db/db_schema.py` (table `archive`),
`deekeep/ui/styles.py:19-28` (couleurs), `deekeep/ui/main_window.py` (appelants).

### 1.2 Fonctionnalités (vue utilisateur)

**Page Bibliothèque**
1. Liste de tous les titres connus de la table `tracks` (sans relire les tags), 11 colonnes.
2. Vérification présence disque + empreinte date+taille par `stat` à chaque chargement.
3. Relecture du débit des fichiers dont la base l'ignore (en-tête du fichier).
4. Recherche plein-texte multi-tags, tous mots requis, ordre libre, accents/casse/ponctuation ignorés, filtrage différé 220 ms.
5. Cinq propositions de valeurs de tags dès 3 caractères, navigables au clavier.
6. Tri par clic d'en-tête (y compris NUAGE et CHEMIN).
7. Case « Titres absents du disque ».
8. Cases à cocher persistantes au filtrage ; Tout cocher / Tout décocher.
9. Écoute : clic ▶, double-clic, menu ; la liste affichée devient la file du lecteur.
10. Colonne NUAGE = état d'archive (— / ✓ taille / à renvoyer / états transitoires).
11. Volet « Archive » repliable : chemin du fichier d'archive, jauge de place, Tester, Mesurer l'occupation, Enregistrer.
12. Archiver (titres cochés ou, après confirmation, tous les affichés).
13. Restaurer (titres cochés ou, après confirmation, tous les restaurables affichés).
14. Arrêter (l'opération en cours, archivage ou restauration).
15. Menu contextuel : Écouter, Ouvrir le dossier, Archiver ce titre, Restaurer ce titre, Retirer de l'archive, Copier le chemin.
16. Ligne de statut récapitulative.

**Lecteur**
17. Fenêtre unique non modale, réutilisée.
18. File d'attente, précédent / suivant, enchaînement automatique.
19. Égaliseur d'analyse 10 bandes d'octave avec maintien de crête.
20. Barre de position cliquable, temps écoulé / durée.
21. Volume (courbe quadratique), mémorisé pendant la session.
22. Raccourcis clavier.
23. Jaquette (fournie, ou intégrée au fichier MP3/FLAC).
24. Extrait Deezer 30 s sans fenêtre (page Musique, fenêtre de recherche) ; règle « un seul son à la fois ».

**Index de présence**
25. Construction asynchrone de l'index du dossier de dépôt (3 sources de métadonnées).
26. Verdict ✓ / ≈ / ○ / ⏳ avec motif et fichiers en cause (infobulle).
27. Mise à jour incrémentale (ajout après extraction, retrait après suppression).
28. Réparation des chemins d'historique par nom de fichier.

---

## 2. Détail par fonction

### 2.1 Page Bibliothèque — chargement (`library_page.py:596-643`)

**Déclenchement.** À la construction de la page (`:162-164`), à chaque affichage de la page (`main_window.py:1285-1297` appelle `sync_from_settings()` puis `reload()`), au bouton **Actualiser** (`:262-265`), après chaque restauration terminée (`:1458`).

**Lectures en base** (cache local SQLite `deekeep_cache.db`, via `local_db.shared()`), exactement :

```sql
SELECT path, title, artist, album, album_artist, genre, year, duration, bitrate, codec, ext, file_size, source
FROM tracks WHERE deleted = 0;

SELECT path, sha1, stamp, key, bytes, bitrate, source_bytes
FROM archive WHERE deleted = 0;
```

La seconde est indexée en dictionnaire `path → ligne` (égalité **exacte** de chaîne, casse comprise). Remarque : `uploaded_at` n'est **pas** lu (conséquence en §2.10).
En cas d'exception : la ligne de statut affiche `Base illisible : <exception>` et le chargement s'arrête (la liste précédente reste affichée).

**Pour chaque titre** (`:611-636`) :
1. `os.stat(path)` — un seul appel système :
   - succès → `exists = True`, `stamp = "%d:%d" % (int(st_mtime), st_size)`, `file_size = st_size` (la taille du disque **écrase** celle de la base) ;
   - `OSError` → `exists = False`, `stamp = ""` (`file_size` reste celle de la base).
2. Si `exists` et `bitrate` nul/absent → `bitrate = probe_bitrate(path) or 0` (§3.5). **Non persisté** : refait à chaque chargement (≈ 207 fichiers sur la base de référence, 0,4 ms chacun).
3. `format = _format_of(entry)` (`:645-653`) : `codec` sinon `ext`, sans point de tête, en MAJUSCULES ; si débit connu → `"MP3 128"`, sinon `"MP3"`, sinon `""`.
4. `mirror = ligne archive du même path` ou `None`.

Puis : `_by_path` (index chemin→entrée), `_checked ∩= chemins connus` (les coches de titres disparus de la base sont oubliées), recalcul des états d'archive (§2.9), reconstruction de l'index des valeurs (§2.3), application du filtre (§2.2).

**Performance annoncée** : 375 ms pour 3 500 lignes sans réseau (README).

### 2.2 Recherche multi-tags (`library_page.py:738-884`)

**Ce que voit l'utilisateur** : un champ avec bouton d'effacement, texte d'aide « Rechercher dans tous les tags : titre, artiste, album, genre, année, format… ».

**Champs interrogés** (`SEARCH_FIELDS`, `:90-98`), dans cet ordre (l'ordre sert aussi de priorité aux propositions) :

| Priorité | Champ (`tracks`) | Libellé affiché |
|---|---|---|
| 0 | `artist` | Artiste |
| 1 | `album` | Album |
| 2 | `title` | Titre |
| 3 | `album_artist` | Album de |
| 4 | `genre` | Genre |
| 5 | `year` | Année |
| 6 | `format` (calculé, ex. « MP3 128 ») | Format |

**Botte de foin** (`_haystack`, `:841-854`) : `canonical(" ".join([str(v or "") for chaque champ ci-dessus] + [basename(path)]))`. Calculée une fois par entrée et par chargement (cache `_haystack` dans l'entrée).

**Règle de correspondance** (`_apply_query`, `:872-884` ; `_matches`, `:868-870`) :
- `query = texte.strip()` ; `tokens = canonical(query).split()` (mots non vides) ;
- une entrée est affichée si `_selectable(entry)` (§2.5) **et** (aucun token **ou** chaque token est une **sous-chaîne** de la botte de foin). C'est un `contains` par token, pas une égalité de mot : « daft one more » trouve « One More Time – Daft Punk » ; « ore » trouve aussi.

**Différé de frappe** : minuterie mono-coup `FILTER_DELAY_MS = 220` relancée à chaque modification (`:157-160`, `:739`). La touche Entrée filtre immédiatement (`:746-754`).

**Coût** : 5 à 60 ms pour 3 500 titres (README).

### 2.3 Cinq propositions (`library_page.py:676-833`)

**Constantes** : `MIN_SUGGEST_CHARS = 3`, `MAX_SUGGESTIONS = 5` (`:101-102`).

**Index des valeurs** (`_build_value_index`, `:676-720`), reconstruit à chaque chargement, à chaque changement de la case « Titres absents » et après toute opération d'archive (`_refilter`) :

```
counts = {}                                   # clé canonique -> slot
pour chaque entrée e telle que _selectable(e):
    best = {}                                 # par titre : une valeur ne compte qu'une fois
    pour (priorité p, (champ, libellé)) dans SEARCH_FIELDS:
        raw = str(e[champ] or "").strip()
        key = canonical(raw) si raw sinon ""
        si key vide : continuer
        si key absent de best ou p < best[key].p : best[key] = (p, raw, libellé)
    pour key, (p, raw, libellé) dans best:
        slot = counts.get(key)
        si slot absent : counts[key] = {label: libellé, value: raw, key, count: 1, priority: p}
        sinon si p < slot.priority : slot.label=libellé ; slot.value=raw ; slot.priority=p ; slot.count += 1
        sinon : slot.count += 1
values = liste(counts) triée par (-count, priority, key)   # tri ascendant sur ce triplet
```

Conséquences : « Daft Punk » artiste **et** artiste d'album n'apparaît qu'une fois, sous « Artiste » ; la valeur affichée est l'écriture brute du premier titre rencontré avec la meilleure priorité ; seules les valeurs de titres **affichables** sont comptées.

**Sélection pour une frappe** (`_suggestions_for`, `:756-772`) :
```
needle = canonical(texte_strippé) ; si vide → []
starts, contains = [], []
pour item dans values (ordre trié ci-dessus):
    si item.key commence par needle → starts
    sinon si needle est sous-chaîne de item.key → contains
    si len(starts) >= 5 : arrêter le parcours
retour (starts + contains)[:5]
```

**Affichage** (`:774-802`) : liste flottante posée sous le champ, à sa largeur, 4 px sous son bord inférieur, hauteur = hauteur d'une ligne × nombre + 12 ; aucune ligne présélectionnée. Libellé exact :
`"%s · %s   (%d titre%s)" % (label, value, count, "s" si count > 1 sinon "")` — ex. `Artiste · Madonna   (61 titres)`.

**Comportement clavier/souris** :
- frappe (non différée) : si `len(texte.strip()) < 3` → masquer, sinon recalculer et afficher (`:738-744`) ;
- ↓ / ↑ (liste visible) : déplacer la sélection, bornée à `[0, n-1]` ; la première pression de ↓ sélectionne la ligne 0 (`:826-832`) ;
- Échap (liste visible) : masquer (`:823-825`) ;
- Entrée : si liste visible et une ligne sélectionnée → la retenir ; sinon masquer, annuler la minuterie, filtrer immédiatement (`:746-754`) ;
- clic sur une proposition (`:809-817`) : masquer, remplacer le texte du champ par `value` **sans** déclencher la détection de frappe (pas de nouvelles propositions), annuler la minuterie, filtrer immédiatement. Le filtre appliqué est ensuite la recherche plein-texte normale sur `value` (pas un filtre restreint au champ).
- redimensionnement : repositionnement de la liste si visible (`:835-838`).

### 2.4 Tri (`library_page.py:81-85`, `:889-914`)

Colonnes triables et clé : TITRE→`title`, ARTISTE→`artist`, ALBUM→`album`, GENRE→`genre`, ANNÉE→`year`, DURÉE→`duration`, FORMAT→`format`, NUAGE→`cloud_order`, CHEMIN→`path`. Les colonnes case et ÉCOUTE ne trient pas.

- Tri initial : ARTISTE ascendant (`:141`).
- Clic sur la colonne déjà triée → inverse le sens ; clic sur une autre → ascendant.
- Clé : `duration` et `cloud_order` → numérique (`float(value or 0)`) ; autres → `canonical(str(value or ""))`, et chaîne vide remplacée par `"\uFFFF"` (les valeurs vides vont **en fin** en ascendant, en tête en descendant).
- Tri **stable** appliqué sur l'ordre courant des lignes affichées (`sorted(..., reverse=desc)`) : à égalité, l'ordre précédent est conservé. L'année est triée comme **chaîne**.
- `cloud_order` : à jour = 0, à renvoyer = 1, absent = 2.

### 2.5 Titres absents du disque (`library_page.py:252-258`, `:856-866`)

Case à cocher « Titres absents du disque », infobulle : « Afficher aussi les titres que seul l'historique connaît, dont le fichier n'est plus sur le disque. Ceux qui sont en miroir restent écoutables. »

Règle `_selectable(entry)` : `exists` **ou** case cochée **ou** `state == FRESH` (titre absent mais ligne d'archive à jour). Comme un fichier absent a `stamp = ""`, **toute** ligne d'archive avec une clé donne FRESH (§3.4) : les titres absents mais archivés sont donc **toujours** affichés, même case décochée.

Changement de la case → reconstruction des propositions + refiltrage (`:733-736`). La case n'est pas persistée.

Rendu d'un titre absent : texte des colonnes TITRE..CHEMIN en `TEXT_FAINT` (#849396) (`:971-972`).

### 2.6 Tableau et colonnes (`library_page.py:74-78`, `:304-339`, `:916-1000`)

| # | Constante | En-tête | Contenu | Largeur initiale |
|---|---|---|---|---|
| 0 | `LC_CHECK` | (vide) | case à cocher | ajustée au contenu |
| 1 | `LC_PLAY` | ÉCOUTE | glyphe ▶ (13 pt gras, centré) | 66, fixe |
| 2 | `LC_CLOUD` | NUAGE | état d'archive | 96 |
| 3 | `LC_TITLE` | TITRE | `title` sinon nom de fichier | 200 |
| 4 | `LC_ARTIST` | ARTISTE | `artist` | 140 |
| 5 | `LC_ALBUM` | ALBUM | `album` | 150 |
| 6 | `LC_GENRE` | GENRE | `genre` | 90 |
| 7 | `LC_YEAR` | ANNÉE | `year` | 58 |
| 8 | `LC_DURATION` | DURÉE | `mmss(duration)` | 60 |
| 9 | `LC_FORMAT` | FORMAT | `format` | 92 |
| 10 | `LC_PATH` | CHEMIN | chemin complet | étirée |

Hauteur de ligne 36 px, pas d'édition, infobulle = texte de la cellule pour les colonnes 3-10. Somme des largeurs calibrée pour une fenêtre de 1 280 px (`:330-336`). NUAGE est volontairement à gauche (juste après ÉCOUTE) pour ne pas disparaître au défilement horizontal (`:69-73`).

**Cellule ÉCOUTE** (`:950-956`, `player.play_item`, `player.py:1310-1338`) : `playable = exists ou state == FRESH`. Infobulles : « Écouter ce titre » (présent) ; « Écouter la copie en ligne (fichier absent du disque) » (absent + FRESH) ; « Fichier absent du disque, et pas de copie en ligne » (non jouable). Couleur `PRIMARY` (#00e5ff) si jouable, `TEXT_FAINT` sinon. Attention : la cellule reste cliquable même « désactivée » ; c'est `_play` qui refuse (§2.11).

**Cellule NUAGE** (`_cloud_item`, `:977-1000`) :

| État | Texte | Couleur | Infobulle |
|---|---|---|---|
| FRESH | `"✓ " + human_bytes(mirror.bytes)` | TERTIARY #5eeb7f | `"En miroir : %s, %s.\nClé : %s"` avec 2ᵉ terme = `"copié tel quel (déjà à %s kb/s)"` si la clé ne finit pas par `.opus`, sinon `"réencodé en Opus à %s kb/s"` (débit = `mirror.bitrate`) |
| STALE | `à renvoyer` | PRIMARY #00e5ff | « Une copie existe en ligne, mais le fichier local a changé depuis — ou le débit demandé n'est plus le même. » |
| ABSENT | `—` | TEXT_FAINT #849396 | « Ce titre n'est pas dans le seau. » |
| transitoire | `en attente…` | TEXT_FAINT | — |
| transitoire | `échec` | ERROR #ffb4ab | — |
| transitoire | `rendu` | TERTIARY | — |
| transitoire | `converti` | PRIMARY | — |

Les états transitoires sont écrits directement dans la cellule de la ligne affichée (`_set_status_cell`, `:1002-1009`) via l'index `path → rang` reconstruit à chaque rendu (`:927-928`).

**Formats** (`:109-123`) :
- `human_bytes(n)` : seuils décimaux 1e9 « Go », 1e6 « Mo », 1e3 « Ko » ; `"%.1f %s"` avec la **virgule** décimale (`"1,2 Go"`) ; en dessous `"%d o"`.
- `mmss(s)` : `""` si 0/absent ; sinon `total = round(s)` → `"%d:%02d" % divmod(total, 60)` (ex. `3:07`, `61:05` au-delà d'une heure — pas d'heures).

**Ligne de statut** (`_refresh_summary`, `:1025-1054`), segments séparés par `" · "` :
1. `"%d titres affichés sur %d"` (affichés / total des entrées) ;
2. `"%d sur le disque (%s)"` (nombre présents, somme `file_size` des présents) ;
3. si au moins un FRESH : `"%d en miroir (%s, soit %s %% du poids d'origine)"` — taille = Σ `mirror.bytes` des FRESH, pourcentage = `100 × Σ bytes / Σ source_bytes` des FRESH formaté `"%.0f"` (0 si Σ source_bytes = 0) ; sinon `"aucun titre en miroir"` ;
4. si au moins un STALE : `"%d à renvoyer"`.

### 2.7 Sélection (`library_page.py:1091-1123`)

- `_checked` = ensemble de **chemins** ; survit au filtrage et au tri ; purgé des chemins inconnus à chaque chargement.
- Clic sur une case → ajout/retrait du chemin.
- **Tout cocher / Tout décocher** : n'agit que sur les lignes **affichées**.
- `_selected_entries()` = lignes **affichées** ET cochées, dans l'ordre d'affichage. Un titre coché mais masqué par le filtre **n'est pas** traité par Archiver/Restaurer.

### 2.8 Volet « Archive » et jauge (`library_page.py:350-592`)

Bouton bascule à droite du champ de recherche : texte `▸ Archive` (replié, défaut) / `▾ Archive` (déplié) (`:447-450`).

**Contenu du volet** :
- Texte d'introduction (`:362-371`) : copie dans une base SQLite sur un disque à soi, octet pour octet, empreinte revérifiée à la sortie, seule limite = place du disque, déménagement = copier le fichier puis corriger le chemin.
- Champ **Fichier d'archive**, indication `C:\temp\deekeep-archive.db` (`:380-385`). Valeur initiale = `sqlite_store.load_config()["root"]`. Validation à la perte de focus/Entrée (`editingFinished`) → invalide le magasin en cache, recalcule jauge et boutons (`:468-474`) — **sans enregistrer**.
- **Jauge** (`_refresh_archive_gauge`, `:488-527`) — voir ci-dessous.
- Libellé d'état + boutons **Tester**, **Mesurer l'occupation**, **Enregistrer**.
- Texte d'aide **obsolète** (`:434-439`) parlant de `bucket.json` et `DEEKEEP_S3_*` (à ne pas reprendre).

**Jauge**, recalculée à chaque rendu :
```
present = entrées avec exists
poids   = Σ file_size(present)
chemin  = champ.strip() ou archive.json.root
si chemin vide:
    "Indiquez le fichier d'archive : les {n} titres présents sur le disque y entreraient tels quels, soit {human_bytes(poids)}."   (couleur PRIMARY)
sinon:
    libre = free_bytes(chemin)                        # §3.9
    tient = libre > poids × 1.1                       # marge de 10 %
    memes = same_device_as(chemin, present[0].path si present sinon chemin)
    texte = "Les {n} titres présents sur le disque entreraient tels quels dans l'archive, soit {poids} — pour {libre} de libre sur son volume."
            + (" La place ne suffit pas." si non tient sinon " Tous reviendraient octet pour octet.")
            + (" Attention : l'archive est sur le même volume que la bibliothèque — elle ne survivrait pas à une panne de ce disque." si memes)
    couleur = PRIMARY si (non tient ou memes) sinon TERTIARY
```
Remarque : `poids` compte **tous** les présents, y compris ceux déjà archivés (la jauge surestime le besoin résiduel). `same_device_as` ne compare que la **lettre de lecteur** (§3.9) et ne teste que le **premier** titre présent.

**Enregistrer** (`:544-552`) : `save_config(champ.strip())` (§3.9 / §5.2), recharge le champ (chemin rendu absolu), message « Archive enregistrée. » si configurée sinon « Indiquez le fichier d'archive. », rafraîchit jauge et boutons.

**Tester** (`:554-566`) : ignoré si un test tourne déjà ; affiche « Vérification de l'archive… » ; fil `CheckThread` avec `{"root": champ, "prefix": "deekeep/"}` (valeur **du champ**, même non enregistrée) → `SqliteStore.check()` (§3.9). Succès : « Seau joignable : identifiants acceptés. » (libellé résiduel) ; échec : message de l'erreur, en couleur ERROR.

**Mesurer l'occupation** (`:568-586`) : ignoré si une mesure tourne ; « Mesure de l'archive… » ; fil `UsageThread` → `usage()` = (nombre d'objets, Σ `bytes`) sous le préfixe `deekeep/`. Succès : `"%d titres archivés, %s — %s de libre sur le volume."` ; échec : message d'erreur en ERROR.

**Activation des boutons** (`_refresh_controls`, `:1056-1075`), à chaque rendu : `running = archivage ou restauration en cours` ; `configured = archive.json/DEEKEEP_ARCHIVE donne un root non vide`.
- **Archiver** et **Restaurer** : actifs si `!running && configured` ;
- **Arrêter** : actif si `running` ;
- **Actualiser** : actif si `!running`.
Infobulles : Archiver = « Copier les titres cochés dans l'archive, tels quels — aucun réencodage, rien n'est perdu » ou « Aucune archive configurée : ouvrez le volet « Archive » et indiquez le fichier où ranger les titres. » ; Restaurer = « Redescendre du seau les titres qui ne sont plus sur le disque. Rien n'est écrasé : seuls les fichiers absents sont écrits. » (voir défaut D1 en §6.6 pour le cas non configuré).

**Barre d'actions** (ordre gauche→droite, `:288-295`) : `Tout cocher` · `Tout décocher` · case `Titres absents du disque` · (espace) · `Restaurer` · `Actualiser` (pilule accent) · `Arrêter` · `Archiver` (pilule pleine, seule action qui écrit). Barre de progression globale sous la barre, masquée hors opération.

### 2.9 États d'archive d'un titre (`library_page.py:655-674`, `library_mirror.py:151-157`)

Pour chaque entrée, à chaque rendu :
- `planned_bitrate = entry.bitrate or mirror.bitrate or 0` (vestigial, sans effet) ; `as_is = True` (vestigial) ;
- `state = state_of(mirror, entry.stamp)` :

```
si mirror absent ou mirror.key vide            → ABSENT ("absent")
si stamp_courant non vide ET mirror.stamp non vide ET mirror.stamp ≠ stamp_courant → STALE ("stale")
sinon                                           → FRESH ("fresh")
```

Donc : fichier absent du disque (stamp courant vide) + ligne avec clé → **FRESH** ; ligne sans `stamp` → FRESH.

### 2.10 Archiver (« Envoyer au nuage ») (`library_page.py:1234-1324`, `library_mirror.py:192-318`)

**Déclenchement** : bouton **Archiver** (`_start_mirror`) ou menu « Envoyer ce titre au nuage » (un titre).

**Séquence UI** :
1. Titres = cochés-affichés ; si aucun → question « Archiver » : `"Aucun titre coché. Envoyer les %d titres affichés ?"` ; Oui → toutes les lignes affichées ; sinon abandon.
2. `_mirror_paths` : si une opération tourne → rien. Si non configuré → message d'information (texte `_unconfigured_text`, `:1077-1081` : « Aucune archive configurée. Ouvrez le volet « Archive » et indiquez le fichier où ranger les titres, puis « Enregistrer ». ») et ouverture du volet.
3. `todo` = titres avec `exists` ; si vide → « Aucun des titres retenus n'est présent sur le disque : il n'y a rien à réencoder. »
4. `known = {path: ligne d'archive}` pour ceux qui en ont une.
5. Chaque ligne concernée : cellule NUAGE `en attente…`. Barre de progression `0/len(todo)` visible. État « Envoi de %d titres en cours… ».
6. Fil `MirrorThread(items=[(path, bitrate)], known, credentials=load_config())`.

**Traitement d'un fichier** (`_mirror_one`, `library_mirror.py:262-318`), séquentiel, un fichier à la fois :
```
si !exists(path)                    → FAILED, "Fichier absent du disque."
current = stamp(path)
known   = known[canonical_path(path)]
si state_of(known, current) == FRESH → SKIPPED, "Déjà dans l'archive."        # aucune lecture
sha1 = SHA-1(contenu complet, lu par tranches de 1 Mio)     # toujours relu, jamais déduit du stamp
  erreur → FAILED, "Lecture impossible : <exc>"
ext  = extension du fichier, en minuscules (avec le point)
key  = prefix + "audio/source/" + sha1[0:2] + "/" + sha1 + ext
row  = {path: canonical_path(path), sha1, stamp: current, key,
        bitrate: int(source_rate) si non nul sinon probe_bitrate(path),
        source_bytes: taille du fichier, uploaded_at: now (epoch, secondes flottantes)}
existing = store.head(key)          erreur → FAILED, message
si existing → row.bytes = existing.bytes ; REUSED, "Déjà dans l'archive."    # dédoublonnage / reprise
si arrêt demandé → FAILED, "Copie interrompue."
data = lecture complète du fichier en mémoire       erreur → FAILED, "Lecture impossible : <exc>"
store.put(key, data, content_type = CONTENT_TYPES[ext] ou "application/octet-stream")
  erreur → FAILED, message
row.bytes = len(data) ; SENT, ""
```
Le fil émet `done(path, state, row|None, message)` puis `progress(index, total)` après chaque fichier ; à la fin `completed(sent, reused+skipped, failed, octets écrits par SENT)`. Arrêt : drapeau testé avant chaque fichier et avant l'écriture ; le fichier en cours de hachage/écriture se termine.
Erreur d'ouverture du magasin : chaque fichier reçoit FAILED avec le message, puis `completed(0, 0, total, 0)`.

**Retour UI par fichier** (`_on_mirror_done`, `:1295-1312`) : si `row` non nul (SENT ou REUSED) → **écriture en base** `local_db.upsert("archive", row)` depuis le fil d'interface (met aussi la ligne en file de synchronisation `sync_outbox`, cf. `local_db.py:146-199`), `entry.mirror = row`. Recalcul de l'état. FAILED → cellule `échec` + statut `"<nom de fichier> — <message>"` ; sinon cellule NUAGE recalculée.

**Fin** (`:1314-1324`) : barre masquée ; état = `"%d titres envoyés (%s)"` + `" · %d déjà à jour"` si >0 + `" · %d en échec"` si >0, en ERROR si (échecs et aucun envoyé) ; `_refilter()`.

**Écritures** : archive SQLite (`objects`, voir §3.9) ; cache `archive` (upsert : `updated_at = now`, `deleted = 0`, `h = row_hash`) ; `sync_outbox`.

### 2.11 Écoute depuis la Bibliothèque (`library_page.py:1128-1179`)

- Clic dans ÉCOUTE, double-clic n'importe où sur la ligne, ou menu « Écouter dans le lecteur » → `_play(row)`.
- Construction de la file : pour **chaque ligne affichée** (ordre affiché), `_track_for(entry)` ; les lignes sans piste sont **sautées** ; l'index de départ est la position de la ligne cliquée dans la file compactée.
- Si la file est vide → rien. Si la ligne cliquée n'a pas de piste → message « Ce titre n'est ni sur le disque ni dans le seau : il n'y a rien à écouter. »
- `_track_for` :
  - sous-titre = `" · ".join([format, year, genre] non vides)` ;
  - présent → piste locale `{path, title, artist, album, subtitle}` ;
  - absent et non FRESH → aucune ;
  - absent et FRESH → `store.presigned_get(mirror.key)` : avec l'archive SQLite cela lève `BlobError` (« L'archive locale ne diffuse pas de flux : un titre doit être restauré sur le disque pour être écouté. », `sqlite_store.py:369-378`) → aucune piste. (Avec l'ancien S3 : URL signée 1 h, sous-titre suffixé `" · copie en ligne"`.)
- Puis `player.play_tracks(page, tracks, index)` (§2.15).

**Conséquence actuelle** : un titre absent est annoncé « Écouter la copie en ligne » mais ne joue pas (incohérence, cf. §6.6 D4).

### 2.12 Menu contextuel (`library_page.py:1184-1229`)

Sur la ligne sous le curseur :

| Entrée | Action | Active si |
|---|---|---|
| Écouter dans le lecteur | `_play(row)` | `exists` ou FRESH |
| Ouvrir le dossier | ouvre l'Explorateur, fichier sélectionné (`core/utils.py:121-146`) | `exists` |
| — | | |
| Envoyer ce titre au nuage | archivage de ce seul titre | `exists` et archive configurée |
| Restaurer ce titre depuis le nuage | restauration de ce seul titre | `_restorable(entry)` et configurée |
| Retirer du nuage | `_drop_from_mirror` | ligne d'archive présente (même FRESH/STALE) |
| — | | |
| Copier le chemin | presse-papiers = chemin | toujours |

**Retirer du nuage** (`:1213-1229`) — **sans confirmation** :
1. ouvrir le magasin (erreur → boîte « Archive » avec le message) ;
2. `store.delete(key)` (`DELETE FROM objects WHERE key=?` ; absent = pas une erreur) ; erreur → boîte d'avertissement ;
3. `local_db.mark_deleted("archive", [path])` : pierre tombale (toutes colonnes de contenu à NULL sauf `path`, `deleted = 1`, mise en file de synchro) ;
4. `entry.mirror = None` ; `_refilter()`.
Attention : l'objet est supprimé **même si d'autres lignes `archive` partagent la même clé** (fichiers identiques dédoublonnés) — cf. §6.6 D5.

### 2.13 Restaurer (`library_page.py:1329-1458`, `library_mirror.py:321-459`)

**Restaurable** (`_restorable`, `:1329-1348`) : la ligne d'archive a une `key` **et** la destination calculée par `restore_target(path, key)` **n'existe pas** sur le disque. L'état FRESH/STALE n'entre pas en compte.

**Séquence UI** (`_start_restore`) :
1. `chosen` = cochés-affichés ; `entries = chosen` ou toutes les lignes affichées ; `todo` = restaurables.
2. Si `todo` vide : `elsewhere` = nombre de restaurables parmi **toutes** les entrées ;
   - > 0 → « Aucun des titres affichés n'est à restaurer, mais %d le sont ailleurs dans la bibliothèque. Cochez « Titres absents du disque », ou videz la recherche, pour les faire apparaître. »
   - sinon → « Rien à restaurer : tous les titres que le seau détient sont déjà sur le disque. »
3. Si rien n'était coché → question « Restaurer » : `"Aucun titre coché. Restaurer les %d titres manquants parmi les %d affichés ?"`.
4. `_restore_entries` : mêmes garde-fous que l'archivage (occupé, non configuré) ; cellules `en attente…` ; progression ; « Restauration de %d titres en cours… » ; fil `RestoreThread(rows = [entry.mirror])`.

**Traitement d'un titre** (`_restore_one`, `library_mirror.py:394-459`) :
```
si path ou key vide          → FAILED, "Ligne de miroir incomplète."
(dest, identique) = restore_target(path, key)
si non identique (clé sans "audio/source/") → FAILED,
   "Cette ligne désigne une ancienne copie réduite, absente de l'archive : le titre est à recopier depuis le disque."
si exists(dest)              → SKIPPED, "Déjà sur le disque."         # rien n'est jamais écrasé
créer le dossier parent (récursif) si absent ; erreur → FAILED, "Dossier impossible à créer : <exc>"
size = store.download(key, dest)    # temporaire ".dk_*" dans le dossier de destination puis renommage atomique
   BlobError → FAILED, message ; OSError → FAILED, "Écriture impossible : <exc>"
expected = row.sha1 en minuscules
si expected non vide:
    actual = SHA-1(dest)  (erreur → actual = "", message "Relecture impossible : <exc>")
    si actual ≠ expected → supprimer dest ; FAILED,
       "L'empreinte du fichier rendu ne correspond pas à celle attendue : la copie a été écartée."
updated = copie de row ; updated.stamp = stamp(dest)     # nouvelle date de modification
→ RESTORED, updated, "", size
```
Signaux : `done(path, state, updated|None, message)`, `progress(i, n)`, `completed(restored, skipped, failed, octets lus)` (4 entiers).

**Retour UI** (`_on_restore_done`, `:1419-1439`) : si ligne mise à jour → `upsert("archive", row)` (nouvelle empreinte date+taille ; sinon tout titre restauré passerait « à renvoyer »), `entry.mirror = row`, `entry.stamp = row.stamp`. Cellule : FAILED → `échec` + statut `"<nom> — <message>"` ; DEGRADED → `converti` ; RESTORED → `rendu`.

**Fin** (`:1441-1458`) : `"%d titres rendus à l'identique (%s)"` + `" · %d convertis en MP3 depuis la copie Opus, l'original étant perdu"` + `" · %d déjà sur le disque"` + `" · %d en échec"` ; erreur si échecs et aucun rendu ; puis `reload()` complet.

> Défauts : la version actuelle référence `RestoreThread.DEGRADED` qui n'existe plus et attend 5 valeurs à la fin au lieu de 4 (§6.6 D2, D3). La restauration **écrit** correctement les fichiers, mais la suite UI échoue. Spécification cible : se conformer au comportement décrit ci-dessus.

L'**upsert** de restauration réécrit la ligne à partir de ce que `reload()` a lu, qui ne contient pas `uploaded_at` → `uploaded_at` passe à NULL (§6.6 D6).

### 2.14 Arrêter (`library_page.py:1283-1289`)

Positionne le drapeau d'arrêt du fil qui tourne (archivage ou restauration) et affiche « Arrêt demandé : le titre en cours est abandonné. ». En pratique : le titre en cours **se termine** (sauf, côté archivage, si l'arrêt survient entre `head` et `put` → « Copie interrompue. ») ; les suivants ne sont pas traités ; le signal de fin est émis avec les compteurs partiels. Archivage et restauration sont **mutuellement exclusifs** (même barre de progression).

### 2.15 Lecteur (`player.py:701-1218`)

**Une seule fenêtre** pour toute l'application (`_PLAYER`, `:1150`). `play_tracks(parent, tracks, index)` (`:1170-1200`) :
1. file vide → rien ; Qt Multimedia absent → avertissement « Lecture indisponible : le module Qt Multimedia (PyQt6-Multimedia) est absent de cette installation. » ;
2. **coupe l'extrait 30 s en cours** (`stop_preview`) ;
3. si la fenêtre existe → `set_playlist(tracks, index)` (remplace file et titre) ; sinon création ;
4. affiche, met au premier plan, donne le focus.

**Format d'une piste** (`make_track`, `:687-698`) : `{path, url, title (défaut : nom du fichier), artist, album, cover (octets image ou null), subtitle}`.

**Fenêtre** : titre « Lecteur DeeKeep », largeur min 560, non modale (la bibliothèque reste utilisable). De haut en bas :
- en-tête : jaquette 76×76 (sinon « 🎵 »), titre (17 px gras cyan ; « Sans titre » à défaut), ligne `artiste — album`, ligne d'information = `subtitle` sinon `path` sinon `url` (infobulle = path), à droite `i / n` (seulement si n > 1) ;
- égaliseur (hauteur min 170) ;
- position : temps écoulé (`00:00`), curseur, durée (`--:--`) ;
- transport : précédent (34 px), lecture/pause (46 px), arrêt (34 px), suivant (34 px), icône volume 🔊/🔇, curseur volume 0-100 (110 px), bouton « Ouvrir le dossier » ;
- ligne d'état (messages d'erreur, pause pour extrait).

**Chargement d'un titre** (`load`, `:874-918`) : index borné ; relance la minuterie d'affichage ; arrête l'analyse précédente et la lecture ; vide les trames ; remet à zéro égaliseur, durée, curseur, temps, statut ; met à jour l'en-tête et la file ; puis :
- `path` non vide : s'il n'existe pas → échec « Fichier introuvable : <path> » ; sinon source = fichier local ;
- sinon `url` non vide → source = URL ;
- sinon échec « Aucun fichier associé à ce titre. ».
Échec (`_fail`) : message, bouton lecture/pause et curseur désactivés, « Ouvrir le dossier » désactivé ; la fenêtre reste ouverte. En succès : « Ouvrir le dossier » actif seulement pour un fichier local ; démarrage de l'analyse spectrale puis lecture.

**Jaquette** (`_refresh_header`, `:940-948` ; `embedded_cover`, `:655-672`) : `track.cover` sinon pochette intégrée : `.mp3` → première trame ID3 dont la clé commence par `APIC` ; `.flac` → `pictures[0]` ; autres formats → aucune. Mise à l'échelle 76×76 en conservant les proportions.

**Transport** :
- lecture/pause (`toggle`, `:995-1004`) : si joue → pause ; sinon **coupe l'extrait 30 s**, efface le message de pause-pour-extrait s'il y en avait un, lecture ;
- arrêt (`:1006-1008`) : stop + égaliseur éteint ;
- précédent / suivant : seulement si l'index le permet (boutons désactivés aux bornes) ;
- fin de média (`:1043-1047`) : égaliseur éteint, passage **automatique** au suivant s'il existe (pas de boucle, pas d'aléatoire) ;
- icône lecture/pause suit l'état réel du lecteur (`:1033-1035`) ;
- erreur de lecture (`:1049-1053`) : « Lecture impossible : <message ou « format non pris en charge »> ».

**Volume** (`:1055-1059`, `:1152-1157`) : curseur 0-100, valeur initiale = dernière valeur de la session (défaut **85**, non persistée entre lancements) ; gain appliqué = `clamp((v/100)², 0, 1)` ; icône 🔇 si 0.

**Barre de position** (`SeekSlider`, `:470-504` ; `:1070-1085`) : plage `0..durée (ms)` ; un **clic dans la rainure** place directement le curseur à la position cliquée (pas de saut par page) et démarre un glisser ; pendant le glisser seul le temps affiché change ; au relâchement → `seek(valeur)`. `seek(p)` : `p = max(0, min(p, durée si connue sinon p))`, positionne le lecteur et **réinitialise l'égaliseur**. Pas de page = 5 000 ms.

**Rafraîchissement** (`_tick`, toutes les `TICK_MS = 40` ms, `:1090-1108`) : si pas de glisser → temps écoulé `format_clock(position)` et curseur ; `frame = int(position_ms × 20 / 1000)` ; transmet `frame` au fil d'analyse (bridage) ; si lecture en cours et trame disponible → pousse les niveaux de la trame ; sinon pousse « rien » (les barres retombent).

`format_clock(ms)` (`:675-684`) : `--:--` si inconnu ou négatif ; `hh:mm:ss` (`%d:%02d:%02d`) au-delà d'une heure, sinon `mm:ss` (`%02d:%02d`).

**Raccourcis clavier** (`keyPressEvent`, `:1113-1132`) :

| Touche | Action |
|---|---|
| Espace | lecture / pause |
| → | `seek(position + 5000)` |
| ← | `seek(position − 5000)` |
| ↑ | volume + 5 (max 100) |
| ↓ | volume − 5 (min 0) |
| N ou « piste suivante » multimédia | titre suivant |
| P ou « piste précédente » multimédia | titre précédent |

**Fermeture** : arrête minuterie, lecture et analyse ; la fenêtre (masquée) est réutilisée au prochain appel. `close_player()` (fermeture de l'application) coupe aussi l'extrait.

### 2.16 Égaliseur d'analyse (`player.py:68-467`)

Qt ne donnant pas accès aux échantillons joués, le fichier (ou l'URL) est **décodé en parallèle** par ffmpeg en PCM mono 32 kHz ; une trame de 10 niveaux est calculée **20 fois par seconde de musique** et rangée à son indice ; l'affichage prend la trame `int(position × 20)`. Algorithme complet en §3.10. Sans ffmpeg : message « FFmpeg introuvable : égaliseur indisponible. », la lecture fonctionne.

Rendu (`SpectrumView`) : 10 colonnes × 18 diodes, montée instantanée, descente amortie, crête maintenue — paramètres en §3.10.

### 2.17 Extrait Deezer 30 s et règle « un seul son à la fois » (`player.py:1220-1307`)

Lecteur **distinct**, sans fenêtre ni égaliseur, partagé par la page Musique et la fenêtre de recherche (pas utilisé par la page Bibliothèque).

`play_preview(url, on_stop)` (`:1239-1264`) :
1. si multimédia absent ou URL vide → `false` ;
2. `pause_player()` : si la fenêtre du lecteur **joue**, elle se met en pause et affiche « En pause : un extrait de 30 secondes est en cours d'écoute. » (`pause_for_preview`, `:1010-1023`) ;
3. `stop_preview()` : arrête l'extrait précédent et appelle **son** rappel de fin ;
4. crée au besoin le lecteur d'extrait (volume = gain du volume courant **à la création seulement**) ;
5. mémorise `(url, on_stop)`, joue ; renvoie `true`.

`stop_preview()` (`:1278-1288`) : remet l'état à vide **avant** d'arrêter (évite un double rappel), stoppe, puis appelle le rappel mémorisé.
Fin naturelle (état « arrêté ») ou erreur de lecture (URL périmée, réseau) → `stop_preview()`.

**Matrice de la règle « un seul son »** :

| Événement | Fenêtre du lecteur | Extrait |
|---|---|---|
| Lancer un extrait | mise en pause (si elle jouait) + message | l'extrait précédent s'arrête, le nouveau joue |
| Ouvrir le lecteur / changer de file (`play_tracks`) | joue le titre demandé | coupé |
| Relancer la lecture dans la fenêtre (`toggle`) | reprend où elle était, message effacé | coupé |
| 2ᵉ clic sur la même ligne | — | arrêté |

**Usage par la page Musique** (`main_window.py:2649-2785`) — hors périmètre mais lié : cellule ÉCOUTE à deux sources (fichier extrait sinon URL `preview` Deezer) ; clic = extrait sans fenêtre (glyphe ▶ ↔ ■ via `set_item_playing`) ; double-clic ou menu « Écouter dans le lecteur » sur un titre non extrait = lecteur complet sur l'URL de l'extrait, sous-titre « Extrait de 30 secondes — le titre n'est pas encore extrait » ; menu « Écouter l'extrait de 30 s (sans fenêtre) » actif seulement si extrait et pas de fichier ; « Arrêter l'extrait » si c'est la ligne qui joue. Fenêtre de recherche (`add_track_dialog.py:879-937`) : bouton rond ▶/⏸ par ligne, désactivé si pas d'extrait (« Aucun extrait disponible pour ce titre »).

Glyphes (`:555-563`) : lecture `▶`, arrêt `■` (choisi pour éviter le rendu emoji de U+23F8), couleur éteinte `#4a5c60`.

### 2.18 Index de présence (pastilles ✓ ≈ ○ ⏳) (`library_index.py`)

**Où** : page Musique (pastille **informative**, la case reste libre) et fenêtre de recherche (une case **✓ est décochée et désactivée**, une case **≈ est décochée mais libre** ; « Tout sélectionner » les ignore ; bilan `✓ n déjà sur le disque · ≈ n doublon(s) possible(s)`, bouton « Voir les doublons (n) », « Dédoublonner… ») — `add_track_dialog.py:782-862`, `main_window.py:259-283`.

**États et apparence** (`:61-89`) :

| État | Glyphe | Couleur | Motif par défaut |
|---|---|---|---|
| `present` | ✓ | #5eeb7f | (selon règle) |
| `similar` | ≈ | #ffcf7a | (selon règle) |
| `absent` | ○ | #3b494c | « Absent du dossier de dépôt » |
| `unknown` | ⏳ | #5f7d84 | « Lecture du dossier de dépôt en cours… » |

Pastille : 18 px de large fixe, centrée, 13 px gras, fond transparent. Infobulle : motif, puis jusqu'à `TOOLTIP_PATHS = 5` lignes `"• <chemin>"`, puis `"… et N autre(s)"` s'il en reste.

**Cycle de vie** (`main_window.py:146-199`, `:3436-3448`) :
- construit en tâche de fond au démarrage, à chaque changement de dossier de dépôt, après un rangement (`organize_page.libraryChanged`), à l'ouverture de la recherche (README) ; un parcours déjà en cours n'est pas doublé ;
- tant qu'il n'est pas prêt : ⏳ et **rien n'est décoché** ;
- échec de construction → index vide (tout « absent »), sans message ;
- extraction réussie → `index.add(path, title, artist, album)` sans relire le disque, puis rediffusion ;
- suppression via la déduplication → `index.remove(path)` pour chaque fichier, puis rediffusion ;
- à chaque index prêt : réparation des chemins de l'historique (§3.3).

Algorithmes complets en §3.1-3.3.

---

## 3. Algorithmes à porter

### 3.1 Normalisation `canonical` et clés (`library_index.py:95-212`)

#### Expressions régulières (verbatim Python, syntaxe `re`)

```python
_FEAT_RE = re.compile(r"(?i)\s*\b(?:feat\.?|featuring|ft\.?|avec|with|vs\.?|versus)\s+")

_BRACKETS_RE = re.compile(r"[\(\[\{][^\)\]\}]*[\)\]\}]")

_VARIANT_RE = re.compile(
    r"(?i)\b(?:remix(?:es|ed)?|remaster(?:ed|isation|ise|isé|isée)?|live"
    r"|version|edit|mix|instrumental|acoustic|acoustique|unplugged|extended"
    r"|radio|single|album|original|explicit|clean|cover|karaoke|karaoké|demo"
    r"|mono|stereo|stéréo|bonus|deluxe|edition|édition|maxi|club|dub"
    r"|bootleg|reprise|inédit|inedit|remake|rework|slowed)\b"
)

_ARTIST_NOISE = {"and", "et", "the", "les", "los", "feat", "featuring", "ft", "with",
                 "avec", "vs", "versus", "presents", "present", "presente", "dj"}
```

Équivalents .NET : options `RegexOptions.IgnoreCase | RegexOptions.CultureInvariant` ; Python `\b`, `\s`, `\d` sont **Unicode** (comme .NET par défaut — ne pas utiliser `RegexOptions.ECMAScript`). Dans `_VARIANT_RE`, les mots accentués (`isé`, `stéréo`, `édition`, `inédit`, `karaoké`) sont appliqués sur le texte **brut** (avant `canonical`), donc sur des accents précomposés : en C#, normaliser l'entrée en **NFC** avant d'appliquer ces regex.

#### `canonical(text)` — ordre exact (`:123-140`)

```
1. t = NFKD(str(text or "")).casefold()
2. supprimer tous les caractères « combining » (catégorie Mn/Mc/Me ; Python unicodedata.combining(c) != 0)
3. t = t.replace("&", " and ").replace("$", "s")
4. t = re.sub(r"[\(\[]\s*(?:copy|copie|\d+)\s*[\)\]]", " ", t)      # « (1) », « [copy] », « (copie) »
5. t = re.sub(r"^\s*\d{1,3}\s*[-\._]\s*", " ", t)                   # numéro de piste en tête « 03 - », « 1. », « 12_ »
6. t = re.sub(r"[^a-z0-9]+", " ", t)                                 # TOUT le reste (y compris lettres non latines) → espace
7. retourner re.sub(r"\s+", " ", t).strip()
```

Portage C# : `string.Normalize(NormalizationForm.FormKD)`, puis `ToLowerInvariant()` **et** remplacement `ß`→`ss`, `ẞ`→`ss` (Python `casefold` le fait, .NET non ; les autres différences casefold/lower n'ont pas d'effet après l'étape 6), puis suppression des `UnicodeCategory.NonSpacingMark` / `SpacingCombiningMark` / `EnclosingMark`.

**Vecteurs de test** (obtenus en exécutant le code Python) :

| Entrée | `canonical` |
|---|---|
| `Ain’t Nobody` | `ain t nobody` |
| `Beyoncé & Jay-Z` | `beyonce and jay z` |
| `Ke$ha` | `kesha` |
| `03 - Get Lucky` | `get lucky` |
| `Titanium (feat. Sia)` | `titanium feat sia` |
| `Song (1)` / `Song [copy]` | `song` |
| `Cœur de pirate` | `c ur de pirate` (œ n'a pas de décomposition : perdu) |
| `Straße` | `strasse` |
| `ﬁnal` (ligature) | `final` |
| `Mötley Crüe` | `motley crue` |
| `Ørjan Nilsen` | `rjan nilsen` (Ø perdu) |
| `Œuvre` | `uvre` |
| `2Pac - 1. Changes` | `2pac 1 changes` |
| `  007 ` | `007` |
| `100% Pure` | `100 pure` |
| `Björk` | `bjork` |

Les pertes (`œ`, `æ`, `ø`, `ł`, `đ`, alphabets non latins) sont le comportement **actuel** ; les reproduire à l'identique garantit les mêmes rapprochements. Les améliorer (translittération) est possible car ces clés ne sont **jamais persistées** (calculées en mémoire), mais changerait les résultats.

#### Clés dérivées

```
title_key(title):
    cleaned = _BRACKETS_RE.sub(lambda m: " " si _FEAT_RE.search(m) sinon m, title)   # groupe entre crochets contenant un invité → supprimé en entier
    head = _FEAT_RE.split(cleaned, maxsplit=1)[0]  ou cleaned si vide                 # coupe au premier marqueur d'invité
    return canonical(head)

base_key(title):
    text = _BRACKETS_RE.sub(lambda m: " " si (_VARIANT_RE.search(m) ou _FEAT_RE.search(m)) sinon m, title)
    parts = re.split(r"\s+-\s+", text)
    tant que len(parts) > 1 et _VARIANT_RE.search(parts[-1]): parts.pop()             # retire les suffixes « - Live », « - Remastered 2011 »…
    return canonical(" - ".join(parts))
    # NB : base_key ne coupe PAS sur un « feat. » hors crochets (asymétrie avec title_key)

artist_tokens(a) = ensemble { mot de canonical(a).split() si mot ∉ _ARTIST_NOISE }
artist_key(a)    = " ".join(sorted(artist_tokens(a)))           # tri ordinal
main_artist_key(a) = artist_key(_FEAT_RE.split(a, maxsplit=1)[0])

_overlap(A, B) = 0 si A ou B vide, sinon |A ∩ B| / min(|A|, |B|)
_ratio(a, b)   = difflib.SequenceMatcher(None, a, b).ratio()    # 2·M / (|a|+|b|), Ratcliff/Obershelp
```

`_ratio` : porter l'algorithme de `difflib.SequenceMatcher` (plus longue sous-chaîne commune récursive à gauche et à droite, avec le départage de `find_longest_match` : sous-chaîne la plus longue, à égalité celle qui commence le plus tôt dans `a`, puis dans `b`). L'heuristique « autojunk » ne s'active qu'à partir de 200 caractères dans `b` : sans effet sur des titres, mais à neutraliser explicitement pour l'équivalence.

**Seuils** (`:210-212`) : `TITLE_OVERLAP_MIN = 0.5`, `TITLE_RATIO_MIN = 0.85`, `ARTIST_OVERLAP_MIN = 0.5`.

**Vecteurs** :

| Titre | `title_key` | `base_key` |
|---|---|---|
| `Titanium (feat. Sia)` | `titanium` | `titanium` |
| `Titanium (Radio Edit)` | `titanium radio edit` | `titanium` |
| `Get Lucky (Radio Edit - feat. Pharrell Williams and Nile Rodgers)` | `get lucky` | `get lucky` |
| `Right Round feat. Ke$ha` | `right round` | `right round feat kesha` |
| `Bohemian Rhapsody - Remastered 2011` | `bohemian rhapsody remastered 2011` | `bohemian rhapsody` |
| `Song - Live at Wembley - 1986 Remaster` | `song live at wembley 1986 remaster` | `song` |
| `One (Version 2) [Live]` | `one version 2 live` | `one` |
| `Hello - Part 2` | `hello part 2` | `hello part 2` |
| `Smells Like Teen Spirit (with Strings)` | `smells like teen spirit` | `smells like teen spirit` |

Effet de bord à connaître : « with »/« avec » sont des marqueurs d'invité, donc `I Wanna Dance With Somebody` → `title_key = "i wanna dance"`.

| Artiste | `artist_key` | `main_artist_key` |
|---|---|---|
| `Flo Rida Feat. Ke$ha` | `flo kesha rida` | `flo rida` |
| `Ke$ha, Flo Rida` | `flo kesha rida` | `flo kesha rida` |
| `The Beatles` | `beatles` | `beatles` |
| `DJ Snake & Lil Jon` | `jon lil snake` | `jon lil snake` |
| `Earth, Wind & Fire With the Emotions` | `earth emotions fire wind` | `earth fire wind` |
| `David Guetta vs. Showtek` | `david guetta showtek` | `david guetta` |

### 3.2 Construction de l'index (`library_index.py:216-450`)

**Sources de métadonnées par fichier**, clé de rapprochement `_match_key(path) = normcase(normpath(path))` (Windows : minuscules + `\`) :
1. cache : `SELECT path, title, artist, album FROM tracks WHERE deleted = 0` (lignes avec titre ou artiste) ; toute erreur (base absente, verrouillée) → source ignorée ;
2. historique de téléchargement (`downloaded_history`, entrées avec `path` et titre ou artiste) — **écrase** le cache (métadonnées Deezer) ;
3. sinon : déduction depuis le chemin.

**Parcours** : `reencode_scan.iter_audio_files(root)` — récursif, pile, `scandir`, sans suivre les liens symboliques, fichiers triés par nom insensible à la casse, extensions `(".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus", ".wma")` (insensible à la casse), dossiers illisibles ignorés. Si `root` vide ou inexistant → index vide.
(Note : `db_init.AUDIO_EXTENSIONS` en a 11 : + `.alac`, `.aiff`, `.aif` — divergence existante.)

**Lecture depuis le chemin** (`_from_path`, `_split_stem`) :
```
stem = nom de fichier sans extension
parts = relpath(path, root).split(séparateur)   (ValueError → [])
folder_artist = parts[0] si len(parts) >= 2 et canonical(parts[0]) != "divers"
folder_album  = parts[1] si len(parts) >= 3
morceaux = re.split(r"\s+-\s+", stem)
si 1 morceau : lecture unique (stem, folder_artist)
sinon left = " - ".join(morceaux[:-1]).strip(), right = morceaux[-1].strip()
    si folder_artist :
        si canonical(right) == canonical(folder_artist) → (titre=left, artiste=right) seule
        si canonical(left)  == canonical(folder_artist) → (titre=right, artiste=left) seule
    sinon / à défaut : (left, right) PUIS lecture inverse (right, left) également indexée
```
Exemples : `Yellow - Coldplay` sous `Coldplay\` → (Yellow, Coldplay) ; `Coldplay - Yellow` sous `Coldplay\` → (Yellow, Coldplay) ; `Get Lucky - Daft Punk` à la racine → deux lectures ; `A - B - C` → (`A - B`, `C`) et (`C`, `A - B`).

**Ingestion** d'une lecture (`_ingest`) : calcule `title_key`, `base_key`, `album_key = canonical(album)`, `artist_tokens`, `artist_key`, `main_key`, `stem_key = canonical(stem)` ; **ignorée si `title_key` vide**. Tables :
- `by_pair[artist_key + "|" + title_key]`
- `by_main[main_key + "|" + title_key]`
- `by_title[title_key]`
- `by_base[base_key]` (si non vide)
- `by_stem[stem_key]` (si non vide)
- `by_token[mot]` pour chaque mot distinct de `title_key`
- plus `paths` (clés de rapprochement) et `by_filename[nom de fichier en minuscules] → chemins`.

`add(path, title, artist, album)` : utilise les métadonnées si titre ou artiste, sinon le chemin. `remove(path)` : retire le chemin, filtre les entrées et **reconstruit** toutes les tables ; renvoie vrai si le chemin était indexé.

### 3.3 Verdict `lookup(title, artist, album)` (`library_index.py:474-587`)

Premier test qui aboutit gagne :

```
tkey = title_key(title) ; si vide ou index vide → ABSENT
akey = artist_key(artist) ; qtok = artist_tokens(artist) ; mkey = main_artist_key(artist) ; alkey = canonical(album)

1. by_pair[akey|tkey]                                   → PRESENT "Déjà présent (même titre, même artiste)"
2. pour stem in (canonical(f"{title} - {artist}"), canonical(f"{artist} - {title}")) :
       by_stem[stem]                                    → PRESENT "Déjà présent (fichier du même nom)"
3. si mkey : by_main[mkey|tkey]                         → SIMILAR "Doublon probable : même titre, même artiste principal (l'invité diffère)"
4. same = by_title[tkey]
   close = [e ∈ same | overlap(qtok, e.artist_tokens) >= 0.5]   → SIMILAR "Doublon possible : même titre, artiste similaire"
   si alkey : [e ∈ same | e.album_key == alkey]                  → SIMILAR "Doublon possible : même titre, même album"
5. bkey = base_key(title) ; si bkey :
   [e ∈ by_base[bkey] | (mkey et e.main_key == mkey) ou overlap(qtok, e.artist_tokens) >= 0.5]
                                                        → SIMILAR "Doublon possible : autre version du même titre"
6. approximatif :
   words = set(tkey.split())
   candidats = entrées (dédoublonnées) de by_token[w] pour w ∈ words, avec overlap(words, mots(e.title_key)) >= 0.5
   retenus = candidats avec [(mkey et e.main_key == mkey) ou overlap(qtok, e.artist_tokens) >= 0.5]
             ET ratio(tkey, e.title_key) >= 0.85
                                                        → SIMILAR "Doublon possible : titre proche, artiste compatible"
7. → ABSENT
```

Résultat : `(state, reason, paths)` où `paths` = chemins distincts des entrées retenues, dans l'ordre. `lookup(None, …)` (index non prêt) → `UNKNOWN`.

Vecteurs (index contenant `Daft Punk\Discovery\One More Time - Daft Punk.mp3`, `Divers\Titanium (Radio Edit) - David Guetta Feat. Sia.mp3`, et `Flo Rida\Right Round - Flo Rida Feat. Ke$ha.mp3` ajouté avec tags `Right Round` / `Flo Rida Feat. Ke$ha` / album `R.O.O.T.S.`) :

| Requête (titre, artiste, album) | État | Motif |
|---|---|---|
| One More Time, Daft Punk | present | même titre, même artiste |
| Titanium, David Guetta | similar | autre version du même titre |
| Titanium (feat. Sia), David Guetta | similar | autre version du même titre |
| Right Round, Flo Rida | similar | même artiste principal |
| Right Round (Remix), Flo Rida | similar | autre version |
| One More Tim, Daft Punk | similar | titre proche, artiste compatible |
| One More Time, Shakira | absent | — |
| Right Round, Unknown, R.O.O.T.S. | similar | même titre, même album |

**Réparation de l'historique** (`main_window.py:201-257`) : pour chaque entrée d'historique dont le chemin n'existe plus et n'est pas indexé, chercher les fichiers indexés de **même nom** (insensible à la casse) non déjà « réclamés » ; si **exactement un** → réaffecter le chemin (deux entrées d'historique portant le même ancien chemin sont réaffectées au même fichier) ; sauvegarder la configuration si au moins une réparation.

### 3.4 Empreintes, états et clés d'objet (`library_mirror.py:41-157`)

```
stamp(path)        = f"{int(st_mtime)}:{st_size}"   ("" si stat échoue)    # int() = troncature vers 0 des secondes
file_sha1(path)    = SHA-1 hexadécimal minuscule du contenu, lu par tranches de 1 048 576 octets
canonical_path(p)  = normpath(p.replace("/", os.sep))    # casse conservée ; clé PK de tracks/archive
SOURCE_SHELF       = "audio/source/"
object_key(prefix, sha1, ext) = f"{prefix}audio/source/{sha1[:2]}/{sha1}{ext.lower()}"
   ex. "deekeep/audio/source/eb/eb6daf46789277f1091e4488d796624afd07d2a2.mp3"
is_source_copy(key) = "audio/source/" ∈ key
restore_target(path, key) = (path, True) si is_source_copy(key)
                            sinon (path si ext == ".mp3" sinon base + ".mp3", False)
state_of(row, current) : cf. §2.9
```

Le stamp utilise l'heure de modification **en secondes Unix tronquées** (Windows : `st_mtime` flottant). Le stamp est aussi celui de la table `quality` (même forme).

**Types de contenu** (`CONTENT_TYPES`, identiques dans les deux modules) :
`.mp3 audio/mpeg`, `.flac audio/flac`, `.m4a audio/mp4`, `.aac audio/aac`, `.ogg audio/ogg`, `.opus audio/ogg`, `.wav audio/wav`, `.wma audio/x-ms-wma`, `.aiff audio/aiff`, `.aif audio/aiff`, `.alac audio/mp4` ; défaut `application/octet-stream`. (L'archive SQLite ne le stocke pas ; utile pour servir en HTTP.)

### 3.5 `probe_bitrate(path)` (`library_mirror.py:79-106`)

```
si mutagen indisponible → 0
info = mutagen.File(path).info ; si absent → 0
si info.bitrate > 0 → round(info.bitrate / 1000)            # kb/s
si info.length > 0  → round(taille_fichier × 8 / length / 1000)
toute exception → 0
```
Équivalent .NET : TagLib# (`TagLib.File.Create(path).Properties.AudioBitrate` en kb/s, `Duration`) avec le même repli taille/durée. Arrondi Python `round` = **arrondi bancaire** (au pair) : utiliser `Math.Round(x, MidpointRounding.ToEven)`.

### 3.6 Plan de débit (miroir Opus, mort) (`opus_mirror.py:197-224`)

```
plan(source_bitrate, target, source_only=False):
    rate = int(source_bitrate or 0) ; target = int(target or 128)
    si source_only ou rate == 0 ou rate <= target → (rate or target, copie_telle_quelle=True)
    sinon → (target, False)
```
Débits proposés `BITRATES = (64, 80, 96, 128, 160, 192)`, défaut 128 ; réglages `mirror_bitrate`, `mirror_source_only` (retirés de `settings.py` depuis). Clé : copie telle quelle → rayon `audio/source/` (comme ci-dessus) ; Opus → `f"{prefix}audio/{bitrate}/{sha1[:2]}/{sha1}.opus"` (le débit fait partie de la clé). Saut d'un fichier si FRESH **et** `known.bitrate == rate`.

### 3.7 Commandes ffmpeg (verbatim)

**Réencodage Opus** (`opus_mirror.py:299-309`, mort) — délai `TRANSCODE_TIMEOUT = 300` s, sortie dans un temporaire `deekeep-*.opus` :
```
ffmpeg -hide_banner -nostdin -loglevel error -y -i <source> -vn -map_metadata 0 -c:a libopus -b:a <bitrate>k -vbr on -application audio <destination.opus>
```

**Restauration Opus → MP3** (`opus_mirror.py:356-364`, mort) — `RESTORE_MP3_BITRATE = 192` :
```
ffmpeg -hide_banner -nostdin -loglevel error -y -i <source.opus> -vn -map_metadata 0 -c:a libmp3lame -b:a 192k -id3v2_version 3 <destination.mp3>
```
Contrôles communs : code retour ≠ 0 → échec avec la **dernière ligne** de stderr (« Réencodage refusé : … » / « Conversion refusée : … », « cause inconnue » si vide) ; fichier produit absent ou vide → « … sans résultat : fichier produit vide. » ; délai dépassé → processus tué, « … interrompu(e) : ffmpeg ne répond plus. » ; l'arrêt demandé tue le processus en cours.

**Décodage pour l'égaliseur** (`player.py:234-238`, vivant) :
```
ffmpeg -hide_banner -loglevel error -nostdin -i <fichier ou URL> -vn -ac 1 -ar 32000 -f s16le -
```
(stdout lu en flux, stderr ignoré.)

**Détection de ffmpeg** (`audio_probe.py:56-86`) : d'abord le ffmpeg livré avec l'application, puis le PATH ; un candidat est retenu si `ffmpeg -hide_banner -version` rend 0 (délai 30 s) ; résultat mémorisé.

### 3.8 Signature AWS SigV4 et URL pré-signées (`blob_store.py:163-451`, mort)

Adressage **par chemin** : `https://<hôte>/<seau>/<clé>`. Constantes : `ALGORITHM = "AWS4-HMAC-SHA256"`, `SERVICE = "s3"`, `DEFAULT_TIMEOUT = 180` s (HEAD/DELETE/check 30 s, listage 60 s), `PRESIGNED_SECONDS = 3600`, `DEFAULT_PREFIX = "deekeep/"`, `FREE_TIER_BYTES = 10 × 1000³`.

Encodages : chemin = `quote(key, safe="/~")` (les `/` restent) ; valeur de paramètre = `quote(value, safe="~")` (RFC 3986 : seuls `A-Za-z0-9-_.~` non encodés).

**Requête signée** (`_request`) :
```
amz_date = UTC "%Y%m%dT%H%M%SZ" ; datestamp = "%Y%m%d"
payload_hash = hex(SHA256(body ou ""))
path = "/" + quote_key(bucket) + ("/" + quote_key(key) si key)
signed = {"host": host, "x-amz-content-sha256": payload_hash, "x-amz-date": amz_date} ∪ {en-têtes additionnels en minuscules}
canonical_headers = concat(f"{nom}:{valeur.strip()}\n" pour nom trié)
signed_headers    = ";".join(noms triés)
canonical_query   = "&".join(f"{quote_value(n)}={quote_value(v)}" trié par (n, v) encodés)
canonical_request = "\n".join([METHOD, path, canonical_query, canonical_headers, signed_headers, payload_hash])
scope   = f"{datestamp}/{region}/s3/aws4_request"
to_sign = "\n".join(["AWS4-HMAC-SHA256", amz_date, scope, hex(SHA256(canonical_request))])
k = HMAC("AWS4"+secret, datestamp) → HMAC(k, region) → HMAC(k, "s3") → HMAC(k, "aws4_request")
signature = hex(HMAC(k, to_sign))
Authorization: AWS4-HMAC-SHA256 Credential={access_key}/{scope}, SignedHeaders={signed_headers}, Signature={signature}
```
(`host` est signé mais posé par le client HTTP ; `PUT` signe aussi `content-type` et `content-length`.)

**Opérations** :
| Opération | Requête | Succès | Notes |
|---|---|---|---|
| `put(key, data, content_type)` | `PUT /bucket/key` | 200/201 → ETag sans guillemets | corps entier en mémoire |
| `head(key)` | `HEAD` | 200 → `{bytes: Content-Length, etag}` ; 404 → absent | |
| `get(key)` | `GET` | 200 → octets | |
| `download(key, dest)` | `GET` en flux, tranches 1 Mio | 200 | temporaire `.dk_*` dans le dossier cible puis `replace` ; tout échec supprime le temporaire |
| `delete(key)` | `DELETE` | 200/204/404 | absent ≠ erreur |
| `iter_objects(prefix)` | `GET /bucket?list-type=2&max-keys=1000&prefix=…&continuation-token=…` | 200 | XML : `Contents/Key`, `Contents/Size`, `NextContinuationToken` (espaces de noms ignorés) |
| `usage()` | listage complet | (nombre, Σ tailles) | |
| `check()` | `GET ?list-type=2&max-keys=1` | 200 | n'écrit rien |

Erreur HTTP → `BlobError("<action> : HTTP <code> (<Code> — <Message>)")` lus dans le XML d'erreur, sinon 200 premiers caractères du corps. Erreur réseau → « Seau injoignable : <exc> ».

**URL pré-signée** (`presigned_get`, `:422-451`) :
```
params = {X-Amz-Algorithm: AWS4-HMAC-SHA256, X-Amz-Credential: f"{access_key}/{scope}",
          X-Amz-Date: amz_date, X-Amz-Expires: "3600", X-Amz-SignedHeaders: "host"}
query = canonical_query(params)
canonical_request = "\n".join(["GET", path, query, f"host:{host}\n", "host", "UNSIGNED-PAYLOAD"])
url = f"{endpoint}{path}?{query}&X-Amz-Signature={signature}"
```

Normalisation : `normalize_endpoint` → `https://hôte` (ajoute `https://` s'il manque le schéma, garde `http` s'il est explicite, retire tout chemin et `/` final) ; `normalize_prefix` → sans `/` de tête, avec `/` final, `""` si vide.

En .NET : `AWSSDK.S3` (avec `ForcePathStyle = true`, `ServiceURL` = point d'entrée, `AuthenticationRegion` = région) remplace avantageusement cette implémentation ; `GetPreSignedURL` pour les URL.

### 3.9 Archive SQLite (`sqlite_store.py`)

**Réponse à la question « archive locale sans perte dans un fichier SQLite ? »** : oui. L'archive est **un unique fichier SQLite** (par défaut `deekeep-archive.db`), dans lequel **chaque fichier audio est stocké intégralement comme BLOB**, octet pour octet, sans réencodage, dédoublonné par SHA-1 du contenu (la clé d'objet dérive du SHA-1). Ce fichier est **distinct** du cache `deekeep_cache.db` : ce dernier ne contient que l'**index** (table `archive`, quelques dizaines d'octets par titre) qui relie chaque chemin de la bibliothèque à sa clé d'objet. Raisons données : transaction (un titre entre entièrement ou pas), comptage exact sans parcours de disque, déménagement par simple copie de fichier ; prix : il faut l'application pour en sortir un titre. Il doit vivre sur un volume NTFS/ext4 (FAT32 refuse > 4 Go). Constat sur la machine de référence : `C:\temp\deekeep-archive.db`, **14 101 053 440 octets**, **3 581 objets**, Σ `bytes` = **14 084 022 859**, tous sous `deekeep/audio/source/…` en `.mp3` ; `archive_meta` = `schema_version 1`, `created_at`, `checked_at`.

**Schéma** (verbatim, `:77-90`) :
```sql
CREATE TABLE IF NOT EXISTS objects (
    key    TEXT PRIMARY KEY,
    bytes  INTEGER NOT NULL,
    stored_at REAL NOT NULL,
    data   BLOB NOT NULL
);
CREATE TABLE IF NOT EXISTS archive_meta (
    key TEXT PRIMARY KEY,
    value TEXT
);
```

**Ouverture** (`_db`, `:191-222`), paresseuse, connexion conservée : crée le dossier parent ; `connect(timeout=30)` ; `PRAGMA journal_mode=WAL` (lecture pendant écriture) ; `PRAGMA synchronous=NORMAL` ; création du schéma ; `INSERT OR IGNORE` de `schema_version = "1"` et `created_at = <epoch>`. Erreurs → « Archive inaccessible : … » / « Archive illisible : … ».

**Résolution du chemin** (`__init__`, `:171-186`) : `root` (ou `path`) de la configuration ; vide → `NotConfigured` « Aucune archive locale n'est configurée : indiquez le fichier où ranger les titres. » ; si c'est un **dossier existant** ou un chemin **sans extension** → `<chemin>/deekeep-archive.db` ; rendu absolu. Préfixe = `normalize_prefix(prefix or "deekeep/")`.

**Opérations** :
| Méthode | SQL / effet | Retour / erreurs |
|---|---|---|
| `put(key, data)` | `INSERT OR REPLACE INTO objects(key, bytes, stored_at, data) VALUES(?, len, now, ?)` en transaction | `str(len)` ; « Écriture de {key} impossible : … » |
| `head(key)` | `SELECT bytes FROM objects WHERE key=?` | `{bytes, etag: ""}` ou `None` |
| `get(key)` | `SELECT data …` | octets ; absent → « Lecture de {key} refusée : objet absent de l'archive. » |
| `download(key, dest)` | `SELECT rowid, bytes …` puis lecture **incrémentale** du BLOB (`blobopen`, tranches 1 Mio) vers un temporaire `.dk_*` du dossier de destination, `os.replace` atomique ; repli lecture d'un bloc ; tout échec supprime le temporaire | octets écrits |
| `delete(key)` | `DELETE FROM objects WHERE key=?` | absent ≠ erreur |
| `iter_objects(prefix)` | `SELECT key, bytes FROM objects WHERE key LIKE ? ESCAPE '\' ORDER BY key` (préfixe avec `\`, `%`, `_` échappés + `%`) | (clé, taille) |
| `usage()` | somme sur `iter_objects` | (nombre, octets) |
| `check()` | `INSERT OR REPLACE INTO archive_meta VALUES('checked_at', now)` | « Archive inutilisable : … » |
| `presigned_get()` | — | lève toujours « L'archive locale ne diffuse pas de flux : un titre doit être restauré sur le disque pour être écouté. » |
| `file_bytes()` | taille fichier + `-wal` + `-shm` | |
| `vacuum()` | `VACUUM` | octets gagnés (non appelé par l'UI) |

**Configuration** (`:95-136`) : `load_config()` lit `<data_dir>/archive.json` à **chaque appel** (`{"root": "", "prefix": "deekeep/"}` par défaut, valeurs non vides du fichier retenues, erreur de lecture imprimée et ignorée), puis la variable `DEEKEEP_ARCHIVE` **remplace** `root` si définie. `save_config(root)` : écriture atomique (`.tmp` puis `replace`), `root` rendu absolu, `prefix` normalisé, JSON indenté 2, UTF-8 non échappé. `is_configured()` = `root` non vide après `strip`.

**Utilitaires** (`:139-165`) : `free_bytes(path)` remonte jusqu'au premier dossier existant puis `disk_usage(...).free` (0 si injoignable) ; `same_device_as(a, b)` compare les **lettres de lecteur** en majuscules (`splitdrive`) — toujours faux sur Linux (pas de lettre).

`data_dir()` (`core/settings.py:42-52`) : Windows `%APPDATA%\DeeKeep` ; autres `$XDG_CONFIG_HOME/DeeKeep` ou `~/.config/DeeKeep` ; créé si absent.

### 3.10 Analyse spectrale (égaliseur) (`player.py:68-467`)

**Constantes** :
```
RATE = 32000 Hz ; WINDOW = 1024 échantillons ; FPS = 20 trames/s ; HOP = RATE // FPS = 1600
READ_FRAMES = 10 (lecture du tube par 1600×2×10 = 32 000 octets = 0,5 s)
BAND_LABELS  = ("31","62","125","250","500","1K","2K","4K","8K","16K")
BAND_CENTERS = (31.25, 62.5, 125, 250, 500, 1000, 2000, 4000, 8000, 16000) Hz
DB_FLOOR = -66.0 ; DB_CEIL = -6.0 ; BAND_TILT = 2.0 dB par rang de bande
FULL_SCALE = 32768 × 1024 / 4 = 8 388 608
LEAD_SECONDS = 25 (avance maximale de l'analyse sur la lecture → 500 trames)
```

**Raies par bande** : `low = floor(c/√2 × 1024/32000)`, `high = ceil(c×√2 × 1024/32000)`, `low ∈ [1, 511]`, `high ∈ [low+1, 512]` ; raies `low ≤ k < high`. Valeurs calculées :

| Bande | (low, high) | Plage approx. |
|---|---|---|
| 31 | (1, 2) | 31 Hz |
| 62 | (1, 3) | 31–62 Hz |
| 125 | (2, 6) | 62–156 Hz |
| 250 | (5, 12) | 156–344 Hz |
| 500 | (11, 23) | 344–688 Hz |
| 1K | (22, 46) | 688–1 406 Hz |
| 2K | (45, 91) | 1,4–2,8 kHz |
| 4K | (90, 182) | 2,8–5,7 kHz |
| 8K | (181, 363) | 5,7–11,3 kHz |
| 16K | (362, 512) | 11,3–16 kHz |

**Par trame** : prendre les 1 024 **premiers** échantillons s16le des 1 600 disponibles (les 576 suivants ne sont pas analysés), appliquer la fenêtre de Hann symétrique `w[i] = 0.5 − 0.5·cos(2πi/(N−1))`, FFT radix-2 complexe 1 024 points, puis pour chaque bande de rang `r` :
```
energy    = Σ_{k=low}^{high-1} |X[k]|²
magnitude = sqrt(energy / (high − low))                      # moyenne quadratique
dB        = 20·log10(magnitude / 8 388 608 + 1e-12) + r × 2.0
niveau    = clamp((dB − (−66)) / (−6 − (−66)), 0, 1)
```
Trame `i` ⇔ instant `i / 20` s. Les paquets arrivent dans l'ordre ; un paquet dont l'indice de départ ne vaut pas le nombre de trames déjà reçues, ou venant d'un fil précédent, est ignoré.

**Bridage** : le fil dort par pas de 100 ms tant que `index_produit − trame_jouée > 500`.

**Affichage** (`SpectrumView`) : `COLUMNS = 10`, `SEGMENTS = 18`, rafraîchi toutes les 40 ms :
```
pour chaque colonne : cible = niveau (ou 0 si pas de trame)
  si cible >= courant : courant = cible  (attaque instantanée)
  sinon courant = max(cible, courant − 0.055)               # DECAY par tick
  si courant >= crête : crête = courant ; maintien = 12 ticks (PEAK_HOLD)
  sinon si maintien > 0 : maintien −= 1
  sinon crête = max(courant, crête − 0.02)                  # PEAK_FALL
diodes allumées = round(courant × 18) ; diode de crête = round(crête × 18) (index crête−1)
```
Couleurs : fond `#080a0c`, bord blanc α 18, diode allumée en dégradé selon la hauteur `ratio = i/17` : `#22d366`→`#7dff9b` sur [0 ; 0,55[, `#7dff9b`→`#ffcf7a` sur [0,55 ; 0,82[, `#ffcf7a`→`#ff8a80` au-delà ; crête cyan `#00e5ff` α 200 ; diode éteinte = teinte de la colonne α 26. Marges : 30 px latérales (échelle « 0 / 5 / 10 » et points toutes les 2 diodes), 10 px haut, 20 px bas (libellés de fréquence + « Hz »). Barre = 66 % de la largeur de colonne, espace inter-diodes = max(1, 30 % de la hauteur de diode).
Changement de titre, arrêt, déplacement → égaliseur remis à zéro.

---

## 4. Intégrations externes

### 4.1 ffmpeg

Usage vivant : décodage parallèle pour l'égaliseur (§3.7), sur fichier local **ou URL** (extrait Deezer ouvert dans le lecteur). Usage mort : libopus / libmp3lame. Exécutable livré (`ffmpeg\` à la racine) puis PATH ; processus sans fenêtre sous Windows (`CREATE_NO_WINDOW`).

### 4.2 Stockage objet S3 (Cloudflare R2, Backblaze B2) — mort

Points d'entrée : R2 `https://<compte>.r2.cloudflarestorage.com`, région `auto` ; B2 `https://s3.<région>.backblazeb2.com`. Offres gratuites 10 Go (R2 sortie gratuite illimitée ; B2 sortie gratuite jusqu'à 3× le stocké). Opérations et signature en §3.8. Identifiants hors base (`bucket.json`, droits 0600 tentés), variables `DEEKEEP_S3_ENDPOINT`, `DEEKEEP_S3_REGION`, `DEEKEEP_S3_BUCKET`, `DEEKEEP_S3_KEY`, `DEEKEEP_S3_SECRET`, `DEEKEEP_S3_PREFIX` prioritaires. Le lecteur jouait directement l'URL pré-signée (requêtes `Range` servies par l'hébergeur).

### 4.3 Deezer (extraits 30 s)

L'URL d'extrait est le champ `preview` des pistes renvoyées par l'API publique Deezer (récupérée par la recherche, hors périmètre). Le lecteur la joue telle quelle. Ces URL (CDN `cdn*-preview*.dzcdn.net`) sont **signées et expirent** : une erreur de lecture coupe l'extrait (rappel de fin), sans message.

### 4.4 Explorateur Windows

« Ouvrir le dossier » : sélection du fichier via l'API Shell, repli `explorer /select,"<chemin>"`, puis ouverture du dossier parent (`core/utils.py:121-146`). Sans équivalent web.

### 4.5 Synchronisation Turso

Les écritures de la table `archive` passent par `local_db.upsert`/`mark_deleted` et entrent dans `sync_outbox` : elles sont synchronisées vers la base en ligne comme les autres tables (constat : 3 581 entrées `archive` en file). Le flux audio, lui, ne quitte jamais la machine.

### 4.6 mutagen

Lecture du débit (`probe_bitrate`) et des pochettes intégrées (ID3 `APIC*`, FLAC `pictures`).

---

## 5. Données

### 5.1 Table `archive` (cache `deekeep_cache.db`, ex-`opus_mirror`) — `db_schema.py:176-202`

DDL effectif (constaté) :
```sql
CREATE TABLE "archive" (
    path TEXT PRIMARY KEY,
    sha1 TEXT,
    stamp TEXT,
    key TEXT,
    bytes INTEGER,
    bitrate INTEGER,
    source_bytes INTEGER,
    uploaded_at REAL,
    updated_at REAL NOT NULL DEFAULT 0,
    deleted INTEGER NOT NULL DEFAULT 0,
    h INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX idx_mirror_sha1 ON archive (sha1);
CREATE INDEX idx_mirror_updated ON archive (updated_at);
```

| Colonne | Sémantique exacte | Écrite par |
|---|---|---|
| `path` | Chemin absolu du fichier source, forme `canonical_path` (séparateurs natifs, **casse conservée**), même valeur que `tracks.path`. Clé primaire. Ex. `D:\a-ha\Cast In Steel - a-ha.mp3` | archivage |
| `sha1` | SHA-1 hexadécimal minuscule du **contenu** du fichier au moment de l'archivage. Sert à dériver `key` et à **vérifier** la restauration | archivage |
| `stamp` | Empreinte `"<mtime entier>:<taille>"` du fichier au moment de l'archivage — ou de la **restauration** (remise à jour pour ne pas paraître « à renvoyer »). Comparée au stamp courant pour FRESH/STALE | archivage, restauration |
| `key` | Clé de l'objet dans le magasin : `deekeep/audio/source/<sha1[0:2]>/<sha1><ext>`. Plusieurs lignes peuvent partager la même clé (fichiers identiques). Historique : `deekeep/audio/<débit>/<xx>/<sha1>.opus` pour une réduction Opus | archivage |
| `bytes` | Taille de l'objet stocké (= taille du fichier pour une copie telle quelle ; taille de l'Opus sinon). Pour REUSED : taille lue par `head` | archivage |
| `bitrate` | Débit du fichier source en kb/s (de `tracks.bitrate`, sinon `probe_bitrate`, 0 si inconnu). Informatif aujourd'hui ; dans le miroir Opus : débit cible, participait au test de fraîcheur | archivage |
| `source_bytes` | Taille du fichier source au moment de l'archivage (sert au « % du poids d'origine ») | archivage |
| `uploaded_at` | Horodatage Unix (secondes flottantes) de l'archivage. **Remis à NULL** par une restauration (défaut D6) | archivage |
| `updated_at` | Horodatage de la dernière écriture de la ligne (technique, synchro) | `local_db` |
| `deleted` | 0/1, pierre tombale (« Retirer du nuage ») : colonnes de contenu à NULL | `local_db` |
| `h` | Empreinte 30 bits du contenu + `deleted` (blake2b, `db_schema.row_hash`), pour la synchro | `local_db` |

État constaté : 3 581 lignes vivantes, 0 supprimée, toutes en `audio/source/…mp3`, Σ `bytes` = Σ `source_bytes` = 14 084 022 859, aucun SHA-1 partagé, aucune clé `.opus` ; 211 titres vivants de `tracks` sans ligne d'archive ; aucune ligne d'archive orpheline.

### 5.2 Fichiers de configuration (`%APPDATA%\DeeKeep\`)

| Fichier | Lu par | Forme constatée (secrets non recopiés) | Rôle |
|---|---|---|---|
| `archive.json` | `sqlite_store.load_config` | `{"root": "C:\\temp", "prefix": "deekeep/"}` | Emplacement de l'archive SQLite. `root` dossier → fichier `C:\temp\deekeep-archive.db`. Surcharge : `DEEKEEP_ARCHIVE` |
| `store.json` | **aucun code actuel** | `{"root": "D:\\000_Logiciel\\DeezerMusic\\DeeKeep - miroir", "prefix": "deekeep/"}` | Vestige d'une étape intermédiaire non versionnée (magasin « dossier ») : le dossier `DeeKeep - miroir\deekeep\audio\source\<xx>\` existe avec des sous-dossiers **vides**. À ignorer |
| `bucket.json` | `blob_store.load_credentials` (mort) | **absent** ; forme attendue : `{"endpoint", "region" (défaut "auto"), "bucket", "access_key", "secret_key", "prefix" (défaut "deekeep/")}` | Identifiants S3 |
| `cloud.json(.disabled)` | `cloud_db` (hors périmètre) | clés `url`, `token` | Turso (renommé `.disabled` : synchro désactivée) |
| `deekeep_cache.db` | `local_db` | SQLite WAL, ~125 Mo | Cache : `tracks`, `archive`, etc. |

### 5.3 Données lues de `tracks`

`path, title, artist, album, album_artist, genre, year (TEXT), duration (REAL, s), bitrate (INTEGER kb/s), codec, ext, file_size, source ('library'|'history'|'both')` avec `deleted = 0`. Constat : 3 792 titres vivants, tous sous `D:\`, 3 586 `mp3` + 206 sans `ext`, 207 sans débit.

### 5.4 État en mémoire d'une entrée de la page (pour concevoir le DTO GraphQL)

`path, title, artist, album, album_artist, genre, year, duration, bitrate, codec, ext, file_size, source, exists, stamp, format, mirror{path, sha1, stamp, key, bytes, bitrate, source_bytes}, state (absent|fresh|stale), cloud_order (0|1|2), _haystack`.

---

## 6. Points d'attention pour la version web / serveur Linux ARM64

### 6.1 Chemins

- Les clés primaires `tracks.path` et `archive.path` sont des **chemins Windows absolus** (`D:\…`, `\`). Sur le serveur, la bibliothèque aura un autre point de montage : prévoir une **migration** des clés (racine Windows → racine Linux, `\` → `/`) dans `tracks`, `archive`, `quality`, appliquée **atomiquement** et des deux côtés de la synchro si Turso est conservé — sinon toutes les lignes `archive` deviennent orphelines et tous les titres « absents ». Recommandation : stocker un chemin **relatif** à une racine de bibliothèque déclarée.
- Casse : le desktop compare les chemins avec `normcase` (insensible) pour l'index de présence, mais **exactement** pour la jointure `tracks`↔`archive`. Sur ext4, décider explicitement (recommandé : comparaison sensible à la casse partout, après migration).
- `same_device_as` (lettre de lecteur) est inopérant sous Linux : utiliser `stat().st_dev` (ou `Mono.Unix`/`statvfs`) pour l'avertissement « même volume ».
- `free_bytes` : `DriveInfo` .NET sur le point de montage ou `statvfs`.

### 6.2 Empreinte date+taille

- Format à conserver strictement : `"{floor(mtime_unix_s)}:{size}"`. En .NET : `new DateTimeOffset(File.GetLastWriteTimeUtc(p)).ToUnixTimeSeconds()` (troncature) et `FileInfo.Length`.
- Copier la bibliothèque vers le serveur **en préservant les dates** (`rsync -t`, `robocopy /COPY:DAT`) ; sinon tous les titres passeront « à renvoyer » (STALE) — sans danger (le SHA-1 retrouvera les objets : REUSED), mais au prix d'un hachage complet de ~14 Go sur une machine ARM lente.

### 6.3 Archive SQLite sur petite machine

- Ne **pas** charger un fichier entier en mémoire comme `put` le fait (`data = handle.read()`) : en .NET, `INSERT … zeroblob(n)` puis écriture par flux `SqliteBlob` (Microsoft.Data.Sqlite) ; lecture par `SqliteBlob` en flux pour la restauration **et pour servir l'écoute** (requêtes HTTP `Range` sur un BLOB : possible avec `SqliteBlob.Seek`), ce qui lèverait la limite « l'archive ne diffuse pas de flux » du desktop.
- Calculer le SHA-1 pendant la copie (un seul passage disque) au lieu de deux lectures ; vérifier que le fichier n'a pas changé entre hachage et écriture (stamp avant/après) — le desktop ne le fait pas.
- Limite SQLite : un BLOB ≤ `SQLITE_MAX_LENGTH` (1 Go par défaut) : refuser proprement un fichier plus gros.
- Une seule connexion en écriture (WAL) : centraliser l'archive dans **un** microservice ; l'opération d'archivage/restauration doit être un **travail de fond** (file de tâches) avec progression poussée au client (subscription GraphQL ou SSE), arrêt coopératif, un seul travail à la fois (règle d'exclusion archivage/restauration).
- `VACUUM` après suppressions : non exposé en desktop ; prévoir une action d'entretien (attention : nécessite ~2× l'espace temporairement).

### 6.4 Lecture audio et égaliseur dans le navigateur

- Servir les fichiers par un point HTTP authentifié avec **Range** (`PhysicalFile(..., enableRangeProcessing: true)`), types MIME de §3.4.
- L'égaliseur ne doit **pas** être calculé côté serveur (FFT Python + ffmpeg par écoute = trop coûteux sur ARM) : utiliser Web Audio `AnalyserNode` (`fftSize` 2048 à 44,1/48 kHz, `smoothingTimeConstant = 0`), regrouper les raies sur les **mêmes bornes en Hz** que §3.10 (`c/√2 … c·√2`, en excluant le continu), moyenne quadratique, même pente +2 dB/bande, même fenêtre −66…−6 dB **après étalonnage** (l'AnalyserNode utilise une fenêtre de Blackman et une autre référence de dB : calibrer avec une sinusoïde pleine échelle pour retrouver 0 dB), même dynamique d'affichage (attaque instantanée, −0,055/40 ms, crête 12 ticks puis −0,02).
- Extraits Deezer : CORS des CDN Deezer non garanti → l'`AnalyserNode` lirait du silence et `crossOrigin` peut bloquer la lecture ; prévoir un proxy serveur ou désactiver l'égaliseur pour les extraits. Rafraîchir l'URL `preview` expirée via l'API Deezer.
- « Un seul son à la fois » : un **service Angular unique** possédant deux éléments audio (lecteur, extrait) et appliquant la matrice de §2.17.
- Raccourcis : limiter au composant lecteur ayant le focus (Espace fait défiler la page / active les boutons dans un navigateur) ; touches média via `navigator.mediaSession` (`nexttrack`, `previoustrack`, `play`, `pause`, `seekforward`, `seekbackward`).
- Volume : appliquer la courbe `(v/100)²` ; persister au moins pour la session (le desktop : 85 par défaut, non persisté).
- Jaquette : la table `covers` (dédoublonnée par SHA-1) est la source naturelle côté web ; la lecture `APIC`/FLAC peut se faire côté serveur avec TagLib#.

### 6.5 Recherche, propositions, liste

- 3 500 lignes : tout peut se faire **côté client** (même algorithme `canonical`, à porter aussi en TypeScript, avec les mêmes vecteurs de test) ; ou côté serveur avec un `contains` par token sur une colonne `haystack` précalculée. Conserver : différé 220 ms, 3 caractères, 5 propositions, priorités de §2.3.
- Le `stat` de 3 500 fichiers et la relecture des débits à **chaque** affichage sont acceptables en local mais à éviter à chaque requête web : mettre en cache présence/stamp avec un `FileSystemWatcher` (inotify) ou un rafraîchissement explicite, et **persister** le débit sondé dans `tracks`.
- Virtualiser le tableau (CDK virtual scroll).
- « Ouvrir le dossier » n'a pas de sens dans un navigateur : remplacer par « Copier le chemin » / « Télécharger le fichier ».

### 6.6 Défauts constatés dans la version actuelle (à ne pas reproduire)

| # | Où | Constat | Comportement attendu |
|---|---|---|---|
| D1 | `library_page.py:1075` | Nom `unconfigured` non défini : si l'archive n'est pas configurée, chaque rendu lève une `NameError` dans un slot Qt (PyQt6 sans `excepthook` → arrêt de l'application) | Infobulle de Restaurer = texte « non configuré » |
| D2 | `library_page.py:1436` | `library_mirror.RestoreThread.DEGRADED` n'existe plus → `AttributeError` pour tout titre non échoué | Gérer RESTORED / SKIPPED / FAILED uniquement |
| D3 | `library_page.py:1441` | Le slot de fin attend 5 valeurs, le signal en émet 4 (`restored, skipped, failed, received`) → la fin de restauration n'est jamais traitée (barre visible, pas de rechargement) | Bilan à 4 compteurs puis rechargement |
| D4 | `library_page.py:950-956`, `:1158-1179` | Un titre absent mais archivé est annoncé « Écouter la copie en ligne » et affiché comme jouable, mais `presigned_get` lève toujours → message « ni sur le disque ni dans le seau » | Soit diffuser depuis l'archive (cf. §6.3), soit présenter « Restaurer pour écouter » |
| D5 | `library_page.py:1213-1229` | « Retirer du nuage » supprime l'objet **sans confirmation** et sans vérifier que d'autres lignes `archive` référencent la même clé (dédoublonnage) → ces lignes pointent vers un objet disparu | Confirmer ; supprimer l'objet seulement quand plus aucune ligne vivante ne référence la clé |
| D6 | `library_page.py:604-606` + `:1429` | La ligne réécrite après restauration ne contient pas `uploaded_at` (non lu) → mis à NULL | Préserver `uploaded_at` |
| D7 | `library_page.py:434-439`, `:565`, `:996`, `:1260` | Textes résiduels (bucket.json, « Seau joignable », « n'est pas dans le seau », « rien à réencoder ») | Vocabulaire « archive » cohérent |
| D8 | `library_mirror.py:275-317` | Le fichier est lu deux fois (SHA-1, puis contenu) sans contrôle de modification entre les deux | Un seul passage, contrôle du stamp |
| D9 | `library_page.py:488-527` | La jauge additionne la taille de **tous** les titres présents, y compris déjà archivés | Afficher aussi le reste à archiver (Σ des non-FRESH) |
| D10 | `library_index.py` vs `db_init.py:46` | Deux listes d'extensions audio (8 vs 11) | Une seule liste partagée |

### 6.7 Proposition de surface d'API (indicative)

- Requêtes : `libraryTracks(filter, includeMissing, sort)` → entrées §5.4 ; `librarySuggestions(text)` ; `archiveConfig` ; `archiveGauge` ; `presence(title, artist, album)` (ou index servi en bloc au client).
- Mutations : `saveArchiveConfig(root)`, `testArchive(root)`, `measureArchive`, `startArchive(paths[])`, `startRestore(paths[])`, `stopArchiveJob`, `removeFromArchive(path)`.
- Abonnements : `archiveJobProgress` → `{path, state (sent|reused|skipped|failed|restored), message, done, total}` puis bilan `{sent, skippedOrReused, failed, bytes}` / `{restored, skipped, failed, bytes}`.
- REST hors GraphQL : `GET /audio/{trackId}` (Range) depuis le disque ou l'archive ; `GET /covers/{sha1}`.
