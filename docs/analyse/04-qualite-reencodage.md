# 04 — Qualité sonore, durées de référence, analyse locale (Réencoder), utilitaires

> Spécification fonctionnelle et technique destinée à la réécriture web (Angular + GraphQL + microservices .NET, serveur Linux ARM64 peu puissant, base SQLite `deekeep_cache.db` conservée).
> Source analysée : application desktop PyQt6 `D:\000_Logiciel\DeezerMusic`, paquet `deekeep/`. Les références `fichier:ligne` renvoient à ce code (état du 26/10/2026).
> **Le code fait foi** : quand un commentaire Python contredit le code, c'est signalé (§6).

Périmètre :

| Module | Rôle |
|---|---|
| `deekeep/ui/quality_page.py` (1 574 l.) | Page « Qualité » (outil → Qualité) |
| `deekeep/audio/quality_scan.py` (983 l.) | Mesure ffmpeg, verdict (6 défauts + blancs), note, cache des mesures, threads d'analyse / de référence / de réanalyse |
| `deekeep/audio/reference_duration.py` (287 l.) | Durée de référence d'un titre (Deezer puis MusicBrainz), cache |
| `deekeep/audio/audio_probe.py` (130 l.) | Détection de ffmpeg, exécution de commandes, durée utile (silence final retranché) |
| `deekeep/audio/reencode_scan.py` (401 l.) | Parcours de dossiers + lecture des tags (pool de threads) ; utilisé par Réencoder, Organiser, Qualité |
| `deekeep/core/utils.py`, `deekeep/core/config.py` | Ressources embarquées (ffmpeg, deno), utilitaires, icône ; dépendances optionnelles (yt-dlp, mutagen) |
| (connexe) `deekeep/ui/main_window.py:1543-2201` | Page Réencoder : ingestion du scan local, filtre « Masquer ceux déjà au format cible » |
| (connexe) `deekeep/db/db_init.py:585-670`, `deekeep/db/db_schema.py:162-213` | Alimentation des tables `quality` et `reference_durations` |
| (connexe) `deekeep/audio/acoustid_lookup.py:57-96` | Détection de fpcalc |

---

## 1. Inventaire

### 1.1 Fonctionnalités visibles par l'utilisateur

| # | Fonctionnalité | Où | Résumé |
|---|---|---|---|
| F1 | Choisir le dossier analysé | Qualité | Par défaut = dossier de dépôt (`download_dir`) ; bouton « Modifier le dossier » |
| F2 | Régler les seuils de détection | Qualité, volet « ▸ Seuils » | 8 seuils numériques + case « Détecter les craquements » ; persistés ; **aucune relecture de fichier** |
| F3 | Lancer l'analyse | Qualité, « Analyser » | Parcours récursif, 1 ffmpeg par fichier, pool de threads, résultats par lots de 8 |
| F4 | Arrêter l'analyse | Qualité, « Arrêter » | Tue les ffmpeg en cours, conserve tout ce qui est déjà mesuré |
| F5 | Voir la progression et le temps restant | Qualité, barre + ligne d'état | « Analyse en cours… n / N fichiers (k signalés) — environ X min restantes » |
| F6 | Filtrer par défaut | Qualité, ligne « Afficher : » | 7 cases (Souffle, Craquements, Saturation, Coupures, Durée, Blanc de fin, Blanc de début) + « Titres sans défaut » |
| F7 | Trier | Clic sur en-tête | Tri par note croissante par défaut (pire d'abord) |
| F8 | Lire les mesures et infobulles explicatives | Colonnes SOUFFLE, COUPURES, DURÉE, BLANCS, DÉFAUTS | Infobulles calculées |
| F9 | Durée de référence en ligne | Automatique, en tâche de fond | Deezer puis MusicBrainz, cache disque, 5 req/s puis 1 req/s |
| F10 | Cocher / Tout cocher / Tout décocher | Qualité | Les fichiers illisibles ne sont jamais cochés par « Tout cocher » |
| F11 | Retélécharger la sélection | Qualité, bouton plein | 5 extractions simultanées, rangement selon règles « Organiser », remplacement du fichier abîmé |
| F12 | Réanalyse automatique après remplacement | Automatique | Statut « Remplacé ✓ défaut corrigé / persistant » |
| F13 | Écouter | Colonne ÉCOUTE (clic), double-clic ligne, menu contextuel | Ouvre le lecteur avec la liste affichée comme file d'attente |
| F14 | Ouvrir avec le lecteur système / Ouvrir le dossier | Menu contextuel | Spécifique desktop |
| F15 | Vider la liste | Qualité | Avec confirmation ; refusé si analyse ou retéléchargement en cours |
| F16 | Ajouter fichiers / dossier à réencoder | Réencoder | Scan local (tags + débit) via pool de 8 threads |
| F17 | Masquer ceux déjà au format cible | Réencoder | Bascule ; masque les lignes déjà au format/qualité des Paramètres |

### 1.2 Inventaire du code (fonctions et classes)

**`quality_scan.py`**

| Élément | Lignes | Rôle |
|---|---|---|
| `HISS…HEAD`, `DEFECT_ORDER`, `DEFECT_LABELS`, `REFERENCE_DEFECTS`, `DEEP_DEFECTS` | 100-126 | Identifiants et libellés des défauts |
| `DEFAULT_THRESHOLDS` | 133-171 | Seuils par défaut |
| Constantes d'analyse | 177-220 | Fréquence passe-haut, seuils silencedetect, adeclick, timeouts, pool |
| `_ASTATS_RE`, `_SILENCE_RE`, `_DECLICK_RE`, `_DURATION_RE`, `_RATE_RE` | 222-230 | Regex de parsing |
| `_filtergraph(deep)` | 233-252 | Graphe `-filter_complex` |
| `_to_float(text)` | 255-263 | Parse tolérant (`-inf`, `nan`) |
| `_run_ffmpeg(args, registry)` | 266-292 | Lance ffmpeg, enregistre le processus pour pouvoir le tuer |
| `_parse(log)` | 295-377 | Extraction des mesures |
| `_gap_stats(starts, ends, duration)` | 380-415 | Coupures : nombre, durée, densité max sur 10 s |
| `measure(path, deep, registry)` | 418-456 | Mesure d'un fichier (jamais `None`) |
| `_error_line(log)` | 459-465 | Message d'erreur exploitable |
| `_severity`, `trough_gap`, `hiss_measurable`, `_is_broadband`, `duration_excess` | 471-522 | Aides au verdict |
| `evaluate(measures, thresholds, reference)` | 525-602 | Défauts, gravités, note /100 |
| `HISS_DISPLAY_FLOOR_DB`, `describe(measures, defect)` | 608-632 | Texte court des cellules |
| `CACHE_VERSION`, `CACHE_FILENAME`, `MeasureCache` | 640-739 | Cache des mesures (JSON) |
| `_encode_measures`, `_decode_measures` | 744-768 | Sérialisation ±inf / NaN |
| `QualityScanThread` | 774-861 | Analyse d'un dossier (pool) |
| `build_entry(path, measures)` | 864-882 | Ligne d'écran (tags + mesures + verdict par défaut) |
| `ReferenceLookupThread` | 885-954 | File de recherches de durée de référence |
| `SingleAnalysisThread` | 957-982 | Réanalyse d'un fichier |

**`reference_duration.py`** : constantes 40-53 ; `_throttle` 59-66 ; `_canonical` 69-72 ; `_related` 75-85 ; `_from_deezer` 91-113 ; `_musicbrainz_recordings` 116-147 ; `_from_musicbrainz` 150-168 ; `ReferenceCache` 174-237 ; `lookup` 243-273 ; `closest` 276-286.

**`audio_probe.py`** : constantes 25-34 ; `run` 44-53 ; `ffmpeg_candidates` 56-62 ; `ffmpeg_exe` 65-86 ; `trailing_silence_start` 89-116 ; `effective_duration` 119-129.

**`reencode_scan.py`** : `AUDIO_EXTENSIONS` 34-36 ; `SCAN_WORKERS` 41 ; `_WANTED_ID3` 45-48 ; `_KNOWN_ID3_FRAMES` 50-66 ; cache tags 74-89 ; `_info_bitrate` 95-109 ; `_text`, `_split_num`, `_first` 112-145 ; `_empty_tags` 148-158 ; `_read_id3` 161-168 ; `_read_mp3` 171-207 ; `_read_vorbis` 210-227 ; `read_tags` 230-277 ; `read_audio_tags` 280-287 ; `iter_audio_files` 290-316 ; `LocalScanThread` 319-400.

**`utils.py`** : `APP_BADGE_*` 19-23 ; `PROJECT_ROOT` 29 ; `resource_path` 33-40 ; `ffmpeg_location` 43-56 ; `deno_location` 59-71 ; `ensure_deno_on_path` 74-87 ; `_select_via_shell` 90-118 ; `reveal_in_explorer` 121-147 ; `sanitize_filename` 150-152 ; `format_duration` 155-163 ; `create_app_icon` 166-178 ; `app_icon_image` 181-191 ; `_icon_file_image` 194-225 ; `_draw_app_icon(_image)` 228-275 ; `stamp_app_icon` 278-320.

**`config.py`** : drapeaux `YTDLP_AVAILABLE` (11-16), `MUTAGEN_AVAILABLE` + classes mutagen (24-36).

### 1.3 Tableau récapitulatif des constantes à porter (verbatim)

| Constante | Valeur | Source |
|---|---|---|
| `HISS_HIGHPASS_HZ` | `8000` | quality_scan.py:177 |
| `HISS_TROUGH_GAP_DB` | `12.0` | :183 |
| `DECLICK_THRESHOLD` | `12` | :186 |
| `SILENCE_NOISE_DB` | `-50` | :191 |
| `SILENCE_MIN_SECONDS` | `2` | :192 |
| `GAP_NOISE_DB` | `-80` | :198 |
| `GAP_MIN_SECONDS` | `0.02` | :199 |
| `GAP_EDGE_MARGIN` | `1.0` s | :203 |
| `GAP_WINDOW_SECONDS` | `10.0` s | :208 |
| `SILENCE_END_MARGIN` (qualité) | `1.5` s | :212 |
| `PROBE_TIMEOUT` | `900` s | :216 |
| `SCAN_WORKERS` (qualité) | `max(2, min(12, (os.cpu_count() or 4) - 2))` | :220 |
| `QualityScanThread.BATCH_SIZE` | `8` | :789 |
| `ReferenceLookupThread.BATCH_SIZE` | `10` | :905 |
| `ReferenceLookupThread.SAVE_EVERY` | `50` | :908 |
| Attente file de références | `timeout=0.3` s | :933 |
| `HISS_DISPLAY_FLOOR_DB` | `-120.0` | :608 |
| `CACHE_VERSION` (mesures) | `5` | :640 |
| `CACHE_FILENAME` (mesures) | `"quality_cache.json"` | :641 |
| `_INF_TOKEN` | `"-inf"` | :744 |
| Seuil d'écrêtage effectif | `peak_db >= -0.5` | :357 |
| Début de silence initial toléré | `silence_starts[0] <= 0.2` | :334 |
| Fichier entièrement muet | `tail >= duration - 0.5` | :346 |
| Pénalité de note | `12 + 20 * gravité` par défaut | :600 |
| Seuils page : `SCORE_GOOD` / `SCORE_SEVERE` | `100` / `75` | quality_page.py:71-73 |
| `MAX_CONCURRENT` (retéléchargements) | `5` | quality_page.py:81 |
| Délai de rendu différé | `500` ms (single-shot) | quality_page.py:139-142 |
| Attente arrêt thread références | `12000` ms | quality_page.py:752 |
| Attente arrêt scan (fermeture) | `5000` ms | quality_page.py:1553 |
| Attente extraction / réanalyse (fermeture) | `3000` ms chacune | quality_page.py:1562,1565 |
| `reference_duration.CACHE_FILENAME` | `"reference_cache.json"` | reference_duration.py:40 |
| `reference_duration.CACHE_VERSION` | `1` | :41 |
| `NEGATIVE_TTL` | `30 * 24 * 3600` s (30 jours) | :44 |
| `USER_AGENT` | `"DeeKeep/1.0 (bibliotheque musicale personnelle)"` | :48 |
| `DEEZER_INTERVAL` | `0.2` s | :49 |
| `MUSICBRAINZ_INTERVAL` | `1.1` s | :50 |
| `MUSICBRAINZ_RETRIES` | `2` | :51 |
| `TIMEOUT` (HTTP) | `8` s | :53 |
| Score MusicBrainz minimal | `90` | :161 |
| `audio_probe.SILENCE_NOISE` | `"-45dB"` | audio_probe.py:25 |
| `audio_probe.SILENCE_MIN_SECONDS` | `3` | :26 |
| `audio_probe.SILENCE_END_MARGIN` | `1.5` | :30 |
| Timeout `run()` par défaut | `180` s | :44 |
| Timeout détection ffmpeg | `30` s | :80 |
| `AUDIO_EXTENSIONS` | `(".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus", ".wma")` | reencode_scan.py:34-36 |
| `reencode_scan.SCAN_WORKERS` | `8` | :41 |
| `_TAG_CACHE_MAX` | `100000` | :75 |
| `LocalScanThread.BATCH_SIZE` | `200` | :338 |
| `REENCODE_AUTOMATCH_LIMIT` | `150` | main_window.py:141 |
| Tolérance « format cible » | `abs(bitrate - cible) <= 5` kbps | main_window.py:2186 |
| `APP_BADGE_RATIO` / `APP_BADGE_MARGIN` / `APP_BADGE_MIN_COVER` | `0.18` / `0.04` / `96` px | utils.py:19-23 |

---

## 2. Détail par fonction

### 2.1 Page « Qualité » — composition de l'écran

Ordre vertical (quality_page.py:151-325) :

1. **Titre** « Qualité des titres » (26 px, gras, `#00e5ff`).
2. **Sous-titre** (12 px, `#849396`), texte verbatim (l. 163-174) :
   « Écoute le signal de chaque fichier — souffle continu, craquements, saturation, coupures au milieu du morceau — et compare sa durée à celle du morceau chez Deezer ou MusicBrainz : un fichier plus court ou plus long n'est pas la version attendue, et c'est le seul indice qu'un rip radio propre laisse derrière lui. Les résultats s'exploitent sans attendre la fin de l'analyse : cochez et retéléchargez au fil de l'eau. Les titres réextraits remplacent la version défectueuse — rangés selon les règles de l'écran « Organiser » — puis sont réanalysés pour vérifier que le défaut a bien disparu. »
3. **Ligne dossier** : libellé « Dossier analysé : {root} » (infobulle = chemin) ; bouton bascule « ▸ Seuils » / « ▾ Seuils » (infobulle « Afficher ou masquer les seuils de détection ») ; bouton « Modifier le dossier » (`control-btn`).
4. **Volet des seuils** (repliable, masqué par défaut — l'état ouvert/fermé n'est pas persisté) : voir §2.8.
5. **Barre d'actions** (gauche → droite) : « Tout cocher », « Tout décocher », « Vider la liste » (infobulle « Retirer tous les résultats affichés (les fichiers du disque sont conservés) »), *espace*, « Arrêter » (`control-btn`, désactivé au départ), « Analyser » (`accent-btn`, infobulle « Mesurer la qualité sonore de tous les fichiers du dossier »), « Retélécharger la sélection » (`primary-btn`, infobulle « Réextraire les titres cochés et remplacer le fichier abîmé dans son dossier d'origine »).
6. **Barre de progression d'analyse** (8 px, masquée hors analyse ; indéterminée tant que le total n'est pas connu).
7. **Barre de progression des retéléchargements** (`global_progress_bar`, masquée hors retéléchargement).
8. **Ligne de filtres** : « Afficher : » + 7 cases cochées par défaut dans l'ordre `DEFECT_ORDER` + case « Titres sans défaut » (décochée, infobulle « Afficher aussi les titres qu'aucune mesure ne met en cause »). État non persisté.
9. **Tableau** (15 colonnes, hauteur de ligne fixe 36 px, non éditable, menu contextuel, en-têtes cliquables avec indicateur de tri).
10. **Ligne d'état** (12 px, `#5f7d84`), message initial « Lancez l'analyse pour repérer les titres à la qualité médiocre. ».

### 2.2 Colonnes du tableau

Constantes `QC_*` = 0..14 (quality_page.py:54-62). Largeurs initiales (l. 312-318).

| # | En-tête | Largeur | Contenu | Couleur | Infobulle | Clé de tri (l. 836-869) |
|---|---|---|---|---|---|---|
| 0 | *(vide)* | auto | Case à cocher = `entry.selected` | — | — | non triable (clic ignoré) |
| 1 | ÉCOUTE | 66 fixe | Glyphe ▶ (item, pas un widget) ; infobulle « Écouter ce titre dans le lecteur (égaliseur, déplacement) » | — | idem | défaut `(score,)` |
| 2 | NOTE | 60 | `str(score)` ; « — » si erreur | vert si ≥100, rouge si <75, orange sinon ; pas de couleur si erreur | — | `(score, title.lower())` |
| 3 | TITRE | 190 | `title` (tag, sinon nom de fichier sans extension) | — | — | `(title.lower(),)` |
| 4 | ARTISTE | 150 | `artist` | — | — | `(artist.lower(), title.lower())` |
| 5 | DÉFAUTS | 190 | Libellés joints par « , » dans l'ordre d'évaluation, ou « Aucun » ; « Illisible » si erreur | « Aucun » vert ; sinon couleur de la note ; « Illisible » rouge | message d'erreur si illisible | `(len(defects), score)` |
| 6 | SOUFFLE | 80 | `describe(HISS)` | rouge si défaut HISS, sinon gris | §2.2.1 | `(_finite(hiss_db),)` (NaN/None → −∞) |
| 7 | CRAQUEMENTS | 100 | `describe(CRACKLE)` ; « non mesuré » si `crackle_pct is None` | rouge/gris ; « non mesuré » gris | « Cochez « Détecter les craquements » puis relancez l'analyse. » (si non mesuré) | `(crackle_pct or -1.0,)` |
| 8 | SATURATION | 90 | `describe(CLIPPING)` | rouge/gris | — | `(clipping_pct or 0.0,)` |
| 9 | COUPURES | 90 | `describe(GAPS)` | rouge/gris | §2.2.2 | `(gap_density or 0.0,)` |
| 10 | DURÉE | 120 | §2.2.3 | rouge si défaut DURATION, sinon gris | §2.2.3 | `abs(duration - reference)` ou `-1.0` si pas de référence |
| 11 | BLANCS | 130 | §2.2.4 | rouge si HEAD/TAIL, sinon gris | §2.2.4 | `max(tail_seconds, head_seconds)` |
| 12 | FORMAT / DÉBIT | 120 | `"{FMT} · {bitrate} kbps"` ou `"{FMT}"` si débit 0 ; « ? » si format vide | — | — | `(bitrate, format)` |
| 13 | FICHIER | étirée | chemin complet | — | chemin | `(path.lower(),)` |
| 14 | STATUT | 190 | statut de retéléchargement (voir §2.16) | — | — | `(status,)` |

Si la ligne est en erreur (`measures.error` non vide), les colonnes 6-9 affichent « — » (l. 978-979).

Couleurs (l. 65-68) : `COLOR_BAD #ff8a80`, `COLOR_WARN #ffcf7a`, `COLOR_OK #5eeb7f`, `COLOR_MUTED #849396`.

**Tri** (l. 1169-1180) : clic sur la colonne courante = inversion du sens ; clic sur une autre = ascendant. Colonne 0 ignorée. Tri initial : `(QC_SCORE, Ascendant)` → pire note en tête. Le tri est stable (Python `sort`) ; sens descendant = `reverse=True` sur la même clé.

#### 2.2.1 Infobulle SOUFFLE (`_hiss_tooltip`, l. 1011-1028)

1. Si `!hiss_measurable(m)` : « Fichier échantillonné à {sample_rate} Hz : il ne contient rien au-dessus de 8 kHz,\noù se mesure le souffle. »
2. Sinon `gap = trough_gap(m)` ; si `gap is None` : « Plancher de bruit au-dessus de 8 kHz.\nLe fichier comporte du silence numérique franc. »
3. Sinon : « Plancher de bruit au-dessus de 8 kHz.\nAu moment le plus calme, la bande haute est {gap:.0f} dB sous le signal entier\n({verdict}). » avec verdict = « bruit large bande : c'est un souffle » si `gap <= 12.0`, sinon « aigus absents des passages calmes : c'est de la musique, pas un souffle ».

#### 2.2.2 Infobulle COUPURES (`_gaps_tooltip`, l. 997-1009)

- `gap_count == 0` : « Aucun silence numérique au milieu du titre.\nLes blancs de début et de fin ont leur propre colonne. »
- sinon : « {count} coupure(s) au milieu du titre, {gap_seconds:.2f} s en tout.\nPassage de dix secondes le plus troué : {100*gap_density:.0f} % de silence.\nC'est cette densité qui fait le verdict — un morceau bâti sur\ndes silences en compte beaucoup sans en être abîmé. »

#### 2.2.3 Cellule et infobulle DURÉE (l. 1036-1086)

`mmss(s)` = `total = round(s or 0)` puis `"%d:%02d" % (total // 60, total % 60)` (ex. « 3:36 »).

| Cas | Texte | Couleur |
|---|---|---|
| erreur ou durée 0 | « — » | gris |
| `reference == "?"` (pas encore cherchée) | « {mmss} — référence… » | gris |
| `reference is None` (rien trouvé / réseau) | « {mmss} — sans référence » | gris |
| référence numérique | « {mmss}  ({gap:+.0f} s) » (deux espaces) avec `gap = duration - reference` | rouge si `DURATION` ∈ défauts, sinon gris |

Infobulle :
- `"?"` : « Durée du fichier : {mmss}.\nDurée du morceau en cours de recherche… »
- `None` : « Durée du fichier : {mmss}.\nNi Deezer ni MusicBrainz ne connaissent ce titre : sa\ndurée n'est comparée à rien. »
- numérique : « Durée du fichier : {mmss} — morceau : {mmss(ref)} ({gap:+.0f} s).\n{verdict}\nLa référence retenue est, parmi les versions connues, celle\ndont la durée est la plus proche. » ; verdict = si `abs(gap) > seuil duration_gap` : « Le fichier est plus {court|long} que le morceau : ce n'est pas la\nversion attendue — montage écourté, édition radio, ou\nextraction d'une mauvaise source. » (court si gap<0) ; sinon « Le fichier dure ce que dure le morceau. »

#### 2.2.4 Cellule et infobulle BLANCS (l. 1088-1154)

Texte :
1. Si HEAD et/ou TAIL ∈ défauts : parties « {head:.0f} s début » puis « {tail:.0f} s fin », jointes par « / », en rouge.
2. Sinon si erreur : « — » gris.
3. Sinon `silence = max(head, tail)` ; `measured = tail > seuil tail_seconds OR head > seuil head_seconds` :
   - pas `measured` → « ok »
   - `reference == "?"` → « {silence:.0f} s — référence… »
   - `reference is None` → « {silence:.0f} s — sans référence »
   - sinon → « {silence:.0f} s — dans le master »
   (gris dans tous ces cas)

Infobulle (toujours) : ligne 1 « Début : {head:.1f} s — Fin : {tail:.1f} s », puis :
- `"?"` : « Durée de référence en cours de recherche… »
- `None` : « Durée du fichier : {duration:.0f} s.\nNi Deezer ni MusicBrainz ne connaissent ce titre : sans\ndurée de référence, rien ne dit si ce silence appartient au\nmorceau. Aucun blanc n'est donc signalé. »
- numérique : « Durée du fichier : {duration:.0f} s — référence : {reference:d} s ({excess:+.0f} s).\n{verdict} » avec verdict = « le fichier dépasse le morceau : le silence a été ajouté » si `excess > seuil duration_excess`, sinon « le silence appartient au master, il n'y a rien à corriger ».

### 2.3 États de la page et activation des contrôles

Variables d'état (l. 94-134) : `_entries` (toutes les lignes analysées), `_rows` (lignes affichées après filtre, dans l'ordre affiché), `_known` (chemins normalisés déjà adoptés), `_uid` (compteur), `_by_uid`, `_scan_total`, `_scanning`, `_scan_complete`, `_scan_clock`, `_workers` (uid → extraction), `_queue` (uid en attente), `_batch` (uid du lot), `_tally`, `_sort`, `_ref_thread`, `_ref_queue`, `_ref_asked` (uid confiés), `_ref_done`.

`busy = _scanning OR _workers non vide OR _queue non vide` (l. 514-515).

Règles d'activation (`_refresh_controls`, l. 1517-1543) — `downloading = _workers ou _queue non vides` :

| Contrôle | Actif si |
|---|---|
| Analyser | `!scanning && !downloading` |
| Arrêter | `scanning` |
| Tout cocher / Tout décocher | toujours |
| Retélécharger la sélection | `!downloading` (**actif pendant l'analyse**) |
| Vider la liste | `!scanning && !downloading` |
| Case « Détecter les craquements » | `!scanning` |
| Seuils | toujours |
| Modifier le dossier | toujours affiché ; le clic ne fait rien si `busy` (l. 503-505) |

Machine d'états simplifiée :

```
Vide ──Analyser──▶ Analyse ──fin──▶ Résultats ──Analyser──▶ Analyse (efface tout)
                     │  ▲             │
                Arrêter│  │             ├─Retélécharger──▶ Retéléchargement (5 en parallèle) ──fin──▶ Résultats
                     ▼  │             │                     (possible aussi pendant Analyse)
               Résultats(interrompue) └─Vider──▶ Vide
Recherche de références : thread indépendant, vit de la 1re ligne ingérée jusqu'à Vider / nouvelle Analyse / changement de dossier / fermeture.
```

### 2.4 Lancer l'analyse (`_start_scan`, l. 565-594)

Préconditions (ordre) :
1. si `busy` → rien.
2. si le dossier n'existe pas → avertissement « Dossier introuvable » : « Le dossier suivant n'existe pas :\n{root} ».
3. si `ffmpeg_exe()` est `None` → avertissement « FFmpeg indisponible » : « L'analyse du signal repose sur FFmpeg, qui est introuvable.\nConsultez la page Diagnostic pour connaître son état. »

Déroulé :
1. `deep = case « Détecter les craquements »`.
2. `_clear("Analyse en cours…")` : arrête la recherche de références (attente ≤ 12 s, sauvegarde du cache), vide toutes les structures et le tableau.
3. Démarre le chronomètre, `_scanning = True`, rafraîchit les contrôles, barre de progression visible et indéterminée.
4. Lance `QualityScanThread([root], deep, cache)`.

Côté thread (`QualityScanThread.run`, quality_scan.py:835-861) :
1. `_collect()` : pour chaque entrée de `paths` : dossier → `iter_audio_files` (récursif) ; fichier avec extension audio → ajouté. Arrêt possible pendant la collecte.
2. émet `total(N)`.
3. `ThreadPoolExecutor(max_workers=SCAN_WORKERS)` ; `executor.map(_analyse_one, files)` — **les résultats sont consommés dans l'ordre des fichiers** (un fichier lent retarde l'émission des suivants).
4. `_analyse_one(path)` (l. 821-833) : si arrêt → `None` ; sinon `cache.get(path, deep)` ; si absent → `measure(path, deep, registry=_procs)` ; si arrêt survenu pendant la mesure → `None` (la mesure n'est **pas** mise en cache) ; sinon `cache.put(path, measures)` ; renvoie `build_entry(path, measures)`.
5. Chaque entrée non nulle est ajoutée à `results` et `pending` ; à 8 entrées : émet `batch(pending)` puis `progress(len(results))`.
6. `finally` : `executor.shutdown(wait=False, cancel_futures=True)`.
7. Fin : si `pending` non vide **et pas d'arrêt** → `batch(pending)` ; `progress(len(results))` ; `cache.save()` ; `finished_scan(results, complete=_running)`.

`build_entry` (l. 864-882) : lit les tags (`reencode_scan.read_tags(path, need_bitrate=True)`), construit `{path, tags, title, artist, album, bitrate, format (extension en majuscules sans point), measures, status:""}` et évalue avec les **seuils par défaut et sans référence** (réévalué par la page dans `_ingest`).

Côté page :
- `_on_scan_total(N)` (l. 608-613) : barre 0..N ; état « Analyse en cours… {N} fichier(s) à lire ».
- `_on_scan_progress(n)` (l. 615-623) : barre = n ; état « Analyse en cours… {n} / {N} fichiers ({k} signalés) » + « — {temps restant} » si disponible, où k = nombre d'entrées ayant au moins un défaut.
- Temps restant (`_remaining_text`, l. 625-636) : voir §3.13.

### 2.5 Ingestion des lots (`_ingest`, l. 638-660 ; `_on_scan_batch`, l. 757-760 ; `_append_rows`, l. 891-913)

Pour chaque entrée :
1. clé = `normcase(abspath(path))` ; si déjà dans `_known` → ignorée (anti-doublon : `finished_scan` renvoie aussi des entrées déjà reçues par lots).
2. `_uid += 1` ; `entry._uid`, `entry.selected = False`, `entry.reference = "?"` (si absent).
3. évaluation `evaluate(measures, seuils courants, référence numérique ou None)`.
4. indexation, ajout à `_entries`, mise en file de la recherche de référence (§2.15).
5. si `_keep(entry)` (filtres) → la ligne est **ajoutée à la fin** du tableau (pas de retri pendant l'analyse : coût quadratique évité). Le tri définitif a lieu à la fin de l'analyse ou à toute action sur filtre/tri/seuil.
6. après la boucle : `_pump_reference()`.

### 2.6 Arrêter (`_stop_scan`, l. 596-606 ; `QualityScanThread.stop`, quality_scan.py:799-806)

- État « Arrêt de l'analyse… », bouton Arrêter désactivé.
- `_running = False` et **kill** de tous les processus ffmpeg enregistrés dans `_procs` (pas d'attente de leur fin).
- Les tâches non commencées sont annulées (`cancel_futures`).
- Les entrées déjà calculées mais non encore émises sont transmises par `finished_scan(results, False)` et ingérées (dédupliquées).
- La recherche de références **continue** (elle n'est pas liée à l'analyse).

### 2.7 Fin d'analyse et bilan (`_on_scan_finished`, l. 762-772 ; `_refresh_summary`, l. 774-798)

- Ingestion de la liste complète, `_scanning = False`, `_scan_complete = complete`, barre masquée, contrôles rafraîchis, **réévaluation complète + rendu trié** (`_reevaluate`), bilan.
- Bilan (seulement si pas d'analyse en cours et au moins une entrée), segments joints par « — » et terminés par « . » :
  1. « {Analyse terminée|Analyse interrompue} : {len(entries)} fichier(s) lus, {flagged} titre(s) à la qualité douteuse »
  2. si erreurs : « {errors} illisible(s) »
  3. si `waiting = len(_ref_asked) - _ref_done > 0` : « durée du morceau en cours de recherche pour {waiting} titre(s) »
  4. si case craquements décochée : « craquements non mesurés (analyse rapide) »
- Le bilan est recalculé à chaque arrivée de références (le nombre de titres douteux évolue après la fin de ffmpeg).

### 2.8 Seuils réglables (`_build_options_panel`, l. 327-437)

Case « Détecter les craquements (analyse ~4× plus lente) » — infobulle : « Les craquements se repèrent en prédisant chaque échantillon à\npartir des précédents : c'est la mesure la plus sûre, et de loin\nla plus coûteuse. Décochez-la pour une analyse rapide limitée au\nsouffle, à la saturation et aux blancs. » — défaut **cochée** (`qual_deep` = `true`).

Grille de 8 champs numériques (2 par ligne, ordre ci-dessous ; `row, col = divmod(index, 2)`) :

| Clé | Libellé | Min | Max | Pas | Déc. | Suffixe | Défaut |
|---|---|---|---|---|---|---|---|
| `hiss_db` | Souffle au-dessus de | -90.0 | -30.0 | 1.0 | 0 | « dB » | -65.0 |
| `crackle_pct` | Craquements au-delà de | 0.0005 | 1.0 | 0.0005 | 4 | « % » | 0.0025 |
| `clipping_pct` | Saturation au-delà de | 0.005 | 5.0 | 0.005 | 3 | « % » | 0.20 |
| `gap_density_pct` | Coupures occupant plus de | 5.0 | 90.0 | 5.0 | 0 | « % » | 30.0 |
| `tail_seconds` | Blanc de fin au-delà de | 1.0 | 60.0 | 1.0 | 0 | « s » | 5.0 |
| `head_seconds` | Blanc de début au-delà de | 0.5 | 30.0 | 0.5 | 1 | « s » | 3.0 |
| `duration_gap` | Durée s'écartant de la référence de plus de | 2.0 | 120.0 | 1.0 | 0 | « s » | 10.0 |
| `duration_excess` | Blancs signalés si la durée dépasse de | 1.0 | 60.0 | 1.0 | 0 | « s » | 3.0 |

Infobulles verbatim (libellé et champ portent la même) — à reprendre telles quelles (quality_page.py:355-411) :
- **hiss_db** : « Plancher de bruit mesuré au-dessus de 8 kHz. Un titre sain / reste sous −80 dB ; un souffle audible remonte ce plancher / à son propre niveau. / Un morceau que rien n'interrompt n'a pas de plancher à / mesurer : il n'est signalé que si le bruit relevé est bien / large bande, ce que la colonne « Souffle » explique au survol. »
- **crackle_pct** : « Part d'échantillons imprévisibles. Titres sains : moins de / 0,001 % ; deux craquements par seconde : 0,004 %. »
- **clipping_pct** : « Part d'échantillons collés à la butée du format. Une variété / récente en montre couramment 0,03 à 0,08 % : c'est le master / qui est ainsi, retélécharger n'y changerait rien. Au-delà de / 0,2 %, la distorsion s'entend. »
- **gap_density_pct** : « Part de silence numérique — des échantillons nuls, non du signal / faible — du passage de dix secondes le plus troué. / Les compter ne suffirait pas : « We Will Rock You » montre / soixante-neuf silences, ceux qui séparent ses frappes de pied, / et se porte très bien. Ce qui trahit une extraction qui hoquette, / c'est qu'elle blanchit une large part d'un passage entier : / 51 % là où un fichier sain ne dépasse pas 19 %, et reste le plus / souvent sous 5 %. / Les blancs de début et de fin sont exclus : ils ont leur propre / seuil. »
- **tail_seconds** : « Durée de la plage muette terminant le fichier. Une fin en / fondu en occupe deux ou trois. / Un blanc n'est signalé que si le fichier dépasse en outre la / durée du morceau (voir le seuil ci-dessous). »
- **head_seconds** : « Durée de la plage muette ouvrant le fichier. / Un blanc n'est signalé que si le fichier dépasse en outre la / durée du morceau (voir le seuil ci-dessous). »
- **duration_gap** : « Écart, dans un sens ou dans l'autre, entre la durée du fichier / et celle du morceau chez Deezer ou MusicBrainz. / Un fichier plus court ou plus long n'est pas la version / attendue : montage écourté, édition radio, extraction d'une / mauvaise source. C'est souvent le seul indice exploitable — un / rip radio propre se mesure comme un master propre. / Sur soixante-dix titres tirés au hasard, l'écart médian est de / 1,6 s et les trois quarts tiennent sous 3,4 s ; au-delà de dix / secondes commence une autre famille. »
- **duration_excess** : « Un silence n'est un défaut que s'il a été ajouté : beaucoup de / morceaux se terminent sur un fondu ou des applaudissements que / leur master contient bel et bien. / La durée du morceau est cherchée chez Deezer, puis chez / MusicBrainz, pour les seuls fichiers portant un blanc — et la / plus proche de celle du fichier est retenue, pour ne pas / accuser un titre de ne pas durer autant qu'un remix. / Sur des fichiers sains, l'écart mesuré ne dépasse pas 3 s. / Sans durée de référence, aucun blanc n'est signalé. »

(« / » = saut de ligne `\n` dans l'original.)

Note sous la grille (11 px) : « Modifier un seuil réévalue aussitôt l'analyse déjà faite : aucun fichier n'est relu. »

**Comportement** :
- Toute modification d'un champ (`_on_threshold_changed`, l. 473-477) : enregistre **tous** les réglages (`qual_deep` + `qual_<clé>` pour les 8 clés, puis sauvegarde de la configuration) et `_reevaluate()` : réévaluation de toutes les entrées, mise en file de références éventuelles, rendu complet.
- Changement de la case craquements (`_on_deep_changed`, l. 479-485) : enregistre les réglages et re-rend (filtrage des défauts « profonds »), **sans** relancer d'analyse.
- `sync_from_settings` (l. 446-460) est appelé **à chaque affichage de la page** (main_window.py:1335) : relit `qual_deep` (défaut `True`) et les 8 seuils (défaut = `DEFAULT_THRESHOLDS`), sans déclencher les gestionnaires ; puis réévalue si des entrées existent.
- Persistance : clés `app_settings` `qual_deep`, `qual_hiss_db`, `qual_crackle_pct`, `qual_clipping_pct`, `qual_gap_density_pct`, `qual_tail_seconds`, `qual_head_seconds`, `qual_duration_gap`, `qual_duration_excess` (valeurs JSON : nombres flottants, booléen).

### 2.9 Filtres d'affichage (`_visible_defects`, l. 816-820 ; `_keep`, l. 822-830)

```
visibles = { d ∈ cases cochées | deep_coché OU d ∉ DEEP_DEFECTS }   // DEEP_DEFECTS = {crackle}
keep(e) =  (e.defects ∩ visibles ≠ ∅)
        OU (e.measures.error non vide)                             // un illisible est toujours montré
        OU (case « Titres sans défaut » cochée ET e.defects = ∅)
```

Cas limite : une entrée dont **tous** les défauts sont filtrés n'apparaît pas, même avec « Titres sans défaut » coché (elle n'est pas « sans défaut »). Chaque changement de case → rendu complet.

### 2.10 Rendu (`_render`, l. 874-889)

Garde anti-réentrance `_rendering` ; `rows = trier(filtrer(_entries))` ; `table.rowCount = len(rows)` ; chaque ligne est remplie (`_fill_row`, l. 915-973) ; l'indicateur de tri est replacé. Le rendu est **différé de 500 ms** (timer mono-coup) lorsqu'il est déclenché par des références qui font entrer/sortir des lignes ; sinon seules les lignes touchées sont remises à jour.

### 2.11 Sélection

- Cocher une case met à jour `entry.selected` (l. 1185-1190), sauf pendant un rendu programmatique.
- « Tout cocher » / « Tout décocher » (l. 1192-1206) n'agissent que sur les **lignes visibles** ; « Tout cocher » ne coche **pas** les lignes en erreur (`allowed = checked && !error`).
- La sélection survit aux re-rendus (stockée dans l'entrée).

### 2.12 Écoute et menu contextuel (l. 1220-1295)

- Clic simple en colonne ÉCOUTE, ou double-clic n'importe où sur la ligne → `_play(uid)`.
- `_play` : file d'attente = **toutes les lignes affichées dans l'ordre du tableau**, position = la ligne cliquée ; si la ligne n'est plus affichée → le titre seul. Chaque piste : `{path, title, artist, subtitle}`.
- Sous-titre lecteur (`_player_subtitle`, l. 1270-1284) : segments joints par « `  ·  ` » : « {FMT} · {bitrate} kbps » (ou « {FMT} ») ; puis « fichier illisible » si erreur, sinon « note {score} » et, s'il y a des défauts, leurs libellés joints par « , » **en minuscules**.
- L'existence du fichier n'est pas vérifiée au rendu (c'est le lecteur qui le signale).
- Menu contextuel : « Écouter dans DeeKeep », « Ouvrir avec le lecteur du système » (ouvre si le fichier existe), « Ouvrir le dossier » (`reveal_in_explorer`).

### 2.13 Vider la liste / changer de dossier

`_clear_results` (l. 530-563) : si `busy` → information « Vidage indisponible » : « Attendez la fin de l'analyse et des retéléchargements en cours avant de vider la liste. » ; si aucune entrée → rien ; sinon question « Vider la liste » : « Retirer les {n} résultat(s) d'analyse ?\nLes fichiers du disque sont conservés ; il faudra relancer l'analyse pour les revoir. » (bouton par défaut **Non**) ; Oui → `_clear("Écran vidé : relancez l'analyse.")`, total remis à 0, barres masquées.

`set_root(root)` (l. 494-501) : si même chemin normalisé → rien ; sinon met à jour le libellé et `_clear("Dossier modifié : relancez l'analyse.")`. Appelé par « Modifier le dossier » (sélecteur de dossier, titre « Choisir le dossier à analyser ») et **par la fenêtre principale quand le dossier de dépôt change** (main_window.py:3262). Les **mesures en cache ne sont jamais effacées** par ces actions (seule la liste à l'écran).

### 2.14 Cycle de vie d'une ligne

1. `build_entry` (thread d'analyse) : tags + mesures, verdict provisoire (seuils par défaut, sans référence).
2. `_ingest` (page) : `_uid`, `selected=False`, `reference="?"`, verdict avec seuils courants ; file de référence.
3. Arrivée de la référence : `reference = entier | None`, nouveau verdict ; la ligne peut apparaître/disparaître selon les filtres.
4. Changement de seuil ou réaffichage de la page : nouveau verdict pour toutes les lignes (aucune relecture).
5. Retéléchargement : statuts successifs, puis `path`/titre/artiste/album mis à jour.
6. Réanalyse : `measures`, `bitrate`, `format`, `tags` remplacés, nouveau verdict, statut final.
7. Vider / nouvelle analyse / changement de dossier : la ligne disparaît (les mesures restent en cache).

### 2.15 Durée de référence — flux côté page (l. 665-755)

- `entry.reference` vaut `"?"` (pas encore cherchée), `None` (cherchée sans résultat **ou** réseau indisponible) ou un entier de secondes.
- `_reference_of(entry)` = valeur numérique, sinon `None` (pour `evaluate`).
- Une entrée a besoin d'une référence (`_needs_reference`, l. 677-690) si : `reference == "?"` ET pas d'erreur ET `duration > 0` ET `artist.strip()` non vide ET `title.strip()` non vide. (Attention : `title` retombe sur le nom de fichier s'il n'y a pas de tag ; c'est donc surtout l'artiste qui conditionne la recherche.)
- `_queue_reference` : si non déjà demandée (`_ref_asked`) → ajoute `(uid, artist, title, duration)` à `_ref_queue`.
- `_pump_reference` : démarre `ReferenceLookupThread` s'il ne tourne pas, lui soumet toute la file, vide la file.
- `_on_reference_found(results)` (lot de couples `(uid, référence|None)`) : pour chaque entrée : mémorise `was_kept`, pose la référence, réévalue, `_ref_done += 1` ; si la visibilité d'au moins une entrée change → rendu complet différé (500 ms) ; sinon mise à jour des seules lignes touchées ; puis bilan.
- `_stop_reference_lookup` : `stop()`, attente ≤ 12 s, destruction, `ReferenceCache.save()`.

Côté thread (`ReferenceLookupThread.run`, quality_scan.py:929-954) : boucle tant que `_running` : `get(timeout=0.3)` ; si file vide → émet ce qui est en attente et reboucle ; sinon `durations = lookup(artist, title, cache)` (exception → `None`, journalisée) ; ajoute `(uid, closest(durations, measured))` ; émet par paquets de 10 ; `cache.save()` tous les 50 titres ; à l'arrêt : émet le reste et sauvegarde. Le thread **ne s'arrête pas** quand la file est vide : il attend de nouveaux titres (soumis par une réévaluation, une réanalyse…). Les titres encore en file au moment de l'arrêt sont abandonnés.

### 2.16 Retéléchargement de la sélection (`_redownload`, l. 1300-1371)

Préconditions (ordre) :
1. si `_workers` ou `_queue` non vides → information « Téléchargement en cours » : « Des retéléchargements sont déjà en cours. »
2. si yt-dlp absent → avertissement « Extraction indisponible » : « yt-dlp n'est pas installé. Installez-le via : pip install yt-dlp »
3. `selected` = lignes **affichées** (`_rows`) cochées et sans erreur ; vide → « Aucune sélection » : « Cochez au moins un titre à retélécharger. »
4. `targets` = celles dont `artist.strip()` et `title.strip()` sont non vides ; `skipped = |selected| - |targets|` ; si aucune → avertissement « Tags insuffisants » : « Les titres cochés n'ont ni artiste ni titre dans leurs tags : la recherche n'aurait rien à chercher, et le fichier trouvé remplacerait l'existant à l'aveugle.\n\nRenseignez d'abord les tags — l'écran « Organiser » sait les retrouver à partir du signal. »
5. Confirmation « Retélécharger » (défaut **Non**) : « {n} titre(s) vont être réextraits et remplaceront la version abîmée.\n\nLe fichier produit est rangé selon les règles de l'écran « Organiser » (sous-dossiers, modèle de nom, « Divers ») : un titre mal placé peut donc changer de dossier au passage.\n\nChaque titre est ensuite réanalysé pour vérifier que le défaut a disparu. » + si `skipped` : « \n\n{skipped} titre(s) coché(s) sont écartés : leurs tags ne donnent pas l'artiste ou le titre nécessaires à la recherche. » + « \n\nContinuer ? »

Déroulé :
1. `_batch = _queue = [uid…]` ; `_tally = library_rules.Tally(root)` (une seule lecture du disque pour tout le lot ; **aucune réservation**, le fichier remplacé est déjà compté).
2. Chaque cible : statut « En attente… », `_pct = 0`.
3. Barre de retéléchargement à 0 et visible ; contrôles rafraîchis ; `_pump()` démarre jusqu'à **5** extractions simultanées.
4. `_launch(uid)` (l. 1389-1427) : options d'export lues **au moment du lancement** (`current_export_options` : `file_format`, `quality`, `cookies_browser` (`None` si « Aucun… », sinon nom en minuscules), `cookies_file` ; repli `{"MP3", "320 kbps (HQ)", None, ""}`) ; statut « Initialisation… », `_pct = 5` ; extraction avec :
   - identifiant = uid, `track_id = ""` (pas d'identifiant Deezer : **`resolve_metadata=True`**, la correspondance Deezer est résolue à partir des tags),
   - titre / artiste / album de l'entrée,
   - dossier de destination = **racine de la bibliothèque** (pas le dossier du fichier abîmé) ; rangement par `library_rules.rules_from_settings(settings)` et `tally`,
   - `extra_tags` = jeu complet des tags du fichier abîmé (ils priment sur ceux du catalogue),
   - `original_path` = chemin du fichier abîmé (supprimé après succès si le fichier produit a un autre chemin — download_thread.py:603-610),
   - `cover_bytes = None`, `max_retries` par défaut (10).
5. Progression (`_on_download_progress`, l. 1429-1438) : `_pct`, statut = texte ; cellule « {texte} — {pct}% » ; barre globale = moyenne des `_pct` du lot (entier tronqué).
6. Fin (`_on_download_finished`, l. 1447-1478) :
   - succès : `_pct = 100` ; `path = track_info.path` (ou ancien) ; titre/artiste/album repris de `track_info` s'ils sont non vides ; `cache.forget(new_path)` ; statut « Réanalyse… » ; lancement de la réanalyse ;
   - échec : `_pct = 0`, statut « Échec » (message seulement en console — dans la version web, l'exposer en infobulle) ;
   - re-remplissage de la ligne, barre, `_pump()` ; quand plus rien n'est actif ni en file : contrôles rafraîchis et, si aucune analyse ne tourne, état « Retéléchargement terminé : les titres remplacés sont réanalysés, la colonne « Défauts » dit ce qu'il en reste. »

Le contrat de l'extraction (yt-dlp, recherche YouTube, tags, jaquette, rangement) est décrit dans l'analyse du module de téléchargement ; ici on n'en consomme que : progression `(uid, pourcentage, texte)` et fin `(uid, succès, message, track_info{title, artist, album, path, cover_b64, tags})`.

### 2.17 Réanalyse après remplacement (`_reanalyse`, l. 1480-1487 ; `SingleAnalysisThread`, quality_scan.py:957-982 ; `_on_reanalysed`, l. 1492-1514)

- Un thread par fichier remplacé (pas de pool, jusqu'à 5 simultanés de fait), `deep` = état **courant** de la case.
- `measure(path, deep)` (sans registre : non interruptible), `cache.put` + `cache.save()`, `build_entry`. Exception → `None`.
- Retour page : `None` → statut « Remplacé ✓ (réanalyse impossible) » ; sinon remplace `measures`, `bitrate`, `format`, `tags`, réévalue avec **la référence existante** (non recalculée), remet en file la recherche de référence (n'a d'effet que si `reference == "?"`), statut « Remplacé ✓ défaut corrigé » si aucun défaut, sinon « Remplacé ✓ défaut persistant ».

### 2.18 Fermeture de l'application (`can_leave`, `stop_threads`, l. 1545-1566)

- `can_leave()` = aucune extraction active ni en file ; sinon la fenêtre principale demande confirmation (« Retéléchargement en cours »).
- `stop_threads()` : arrêt du scan (attente ≤ 5 s), vidage de la file de références et arrêt du thread (≤ 12 s, sauvegarde), vidage de la file d'extraction, attente ≤ 3 s par extraction et par réanalyse en vol (pour ne pas laisser un fichier à demi écrit), `MeasureCache.save()`.

### 2.19 Page Réencoder — analyse locale et filtre « format cible »

**Ajout** (main_window.py:1563-1602) : « Ajouter des fichiers » (sélection multiple, filtre « Fichiers audio (*.mp3 *.flac …) ;; Fichiers MP3 (*.mp3) ;; Tous les fichiers (*) ») ou « Ajouter un dossier » (« sous-dossiers inclus ») ; le dernier dossier utilisé est mémorisé en mémoire (`_reencode_last_dir`, repli `download_dir`). Refusé si des conversions sont en cours (« Attendez la fin des conversions avant d'ajouter des fichiers. ») ou si une analyse tourne (« Une analyse de fichiers est déjà en cours. »).

**Scan** = `LocalScanThread(paths)` avec `need_bitrate=True` (reencode_scan.py:319-400) :
1. `_collect` : dossiers → `iter_audio_files` ; fichiers isolés gardés s'ils ont une extension audio.
2. émet `total(N)`.
3. Pool de **8** threads (`ThreadPoolExecutor`, ordre conservé) : `_read_one(path)` = `read_tags(path, need_bitrate)` + `path` + `format` (extension en majuscules).
4. lots de 200 → `batch` + `progress` ; fin → `progress` et `scanned(liste complète)`. Arrêt : `_running=False`, `cancel_futures`.
5. La page Réencoder n'écoute que `total`, `progress` (« Analyse en cours… {n} / {N} fichier(s) analysé(s) ») et `scanned` (ingestion en une fois). La page Organiser l'utilise avec `need_bitrate=False` (organize_page.py:1434).

**Ingestion** (main_window.py:1634-1687) : dédoublonnage sur `abspath` ; chaque fichier devient `{path, title, artist, album, bitrate, format, match:None, cover_bytes:None, cover_url:None, status:"En attente", selected:False, _searched:False, _uid}` ; si plus de **150** nouveaux fichiers : pas de recherche automatique de correspondance Deezer, fichiers **présélectionnés** et message « Bibliothèque volumineuse » : « {n} fichiers ajoutés. La recherche automatique de correspondances Deezer (jaquettes) a été désactivée pour préserver la fluidité ; les fichiers sont présélectionnés et seront réencodés à partir de leurs métadonnées locales.\n\nAstuce : utilisez « Masquer ceux déjà au format cible » pour ne convertir que le nécessaire. » ; si des fichiers ont été trouvés mais aucun n'est nouveau : « Aucun ajout » / « Tous les fichiers trouvés sont déjà présents dans la liste. »

**« Masquer ceux déjà au format cible »** (main_window.py:1468-1475, 2160-2201) : bouton bascule (`control-btn`, infobulle « Masquer les fichiers déjà dans le format et la qualité définis dans les Paramètres (rien à réencoder) ») ; libellé « Afficher tous les fichiers » quand actif. Masque (sans retirer) les lignes pour lesquelles `_is_at_target` est vrai (§3.12). Format cible = combo Paramètres (`MP3`, `FLAC`, `WAV`, `M4A`) ; qualité = combo (`320 kbps (HQ)`, `128 kbps (Standard)`, `Lossless (Qualité Source)`). L'état du filtre n'est pas persisté. Les lignes masquées ne sont pas converties (seules les lignes « sélectionnées et visibles » le sont, main_window.py:1987).

### 2.20 `audio_probe` — durée utile (utilisée par la recherche de tags)

`effective_duration(path, declared)` (l. 119-129) : renvoie `int(round(début du silence final))` si un silence final existe et que ce nombre est > 0, sinon `None` (« la durée déclarée fait foi »). `trailing_silence_start` (l. 89-116) — commande §3.1.3 ; `None` si ffmpeg absent, durée déclarée nulle, fichier absent, code de sortie ≠ 0, ou aucun `silence_start`. Utilisée par `tag_lookup.py:892`.

### 2.21 `utils` / `config`

- `resource_path(rel)` : `sys._MEIPASS` (exécutable PyInstaller) sinon racine du projet + `rel`.
- `ffmpeg_location()` : dossier `ffmpeg/` livré s'il contient `ffmpeg.exe` (Windows) / `ffmpeg` (autres), sinon `None` (passé à yt-dlp comme `ffmpeg_location`).
- `deno_location()` / `ensure_deno_on_path()` : dossier `deno/` livré ; préfixé au `PATH` du processus au démarrage (main.py:19) pour que yt-dlp trouve un moteur JavaScript (exigé par YouTube).
- `reveal_in_explorer(path)` : Windows `SHOpenFolderAndSelectItems` (COM), repli `explorer /select,"chemin"`, repli ouverture du dossier parent. **Non transposable** côté web.
- `sanitize_filename(name)` : `re.sub(r'[\\/*?:"<>|]', "", name)`.
- `format_duration(s)` : `int(s)` → `"%02d:%02d"` (mm:ss, minutes non bornées) ; exception → `"--:--"`.
- Icône : `icon.ico` si présent, sinon dessin vectoriel 128×128 (carré arrondi rayon 30, fond `#00e5ff`, croche `#00363d` : hampe x=78 l=9 de y=32 à 90 coins 4, drapeau bézier, tête ellipse 40×28 centrée (62,84) rotation −20°).
- `stamp_app_icon(cover_bytes)` : appose l'icône en haut à droite d'une jaquette : côté badge = `max(16, round(min(w,h) × 0.18))`, marge = `max(1, round(min(w,h) × 0.04))`, ignoré si `min(w,h) < 96` ; conversion RGB32 puis JPEG qualité **92** ; en cas d'échec, octets d'origine.
- `config` : `YTDLP_AVAILABLE`, `MUTAGEN_AVAILABLE` (+ classes `MP3`, `ID3`, `APIC`, `TIT2`, `TPE1`, `TALB`, `TPE2`, `TCON`, `TDRC`, `TRCK`, `TPOS`, `TPUB`, `FLAC`, `Picture`).
- Détection de fpcalc (acoustid_lookup.py:67-96) : candidats `fpcalc/fpcalc(.exe)`, `chromaprint/fpcalc(.exe)`, puis `fpcalc` du PATH ; retenu si `fpcalc -version` rend 0 ou si sa sortie contient « fpcalc » ; sinon un ffmpeg dont `ffmpeg -hide_banner -muxers` contient « chromaprint » ; résultat mémoïsé.

---

## 3. Algorithmes à porter

### 3.1 Commandes ffmpeg exactes

#### 3.1.1 Analyse qualité — mode approfondi (`deep = true`, défaut)

Arguments (liste, **pas** de shell) — quality_scan.py:442-445 :

```
<ffmpeg> -hide_banner -nostats -i <chemin> -filter_complex <GRAPHE> -map [out] -f null -
```

`<GRAPHE>` (une seule chaîne, sans espaces, concaténée — `_filtergraph(True)`, l. 233-252) :

```
[0:a:0]asplit=4[a][b][c][d];[a]silencedetect@long=noise=-50dB:d=2,aformat=sample_fmts=s16,astats@full=metadata=0:measure_perchannel=none[s1];[b]highpass=f=8000,astats@hf=metadata=0:measure_perchannel=none[s2];[c]silencedetect@gap=noise=-80dB:d=0.02[s3];[d]adeclick=t=12[s4];[s1][s2][s3][s4]amix=inputs=4[out]
```

#### 3.1.2 Analyse qualité — mode rapide (`deep = false`)

```
[0:a:0]asplit=3[a][b][c];[a]silencedetect@long=noise=-50dB:d=2,aformat=sample_fmts=s16,astats@full=metadata=0:measure_perchannel=none[s1];[b]highpass=f=8000,astats@hf=metadata=0:measure_perchannel=none[s2];[c]silencedetect@gap=noise=-80dB:d=0.02[s3];[s1][s2][s3]amix=inputs=3[out]
```

Points clés :
- `GAP_MIN_SECONDS` est formaté par `%s` → `0.02` ; `SILENCE_MIN_SECONDS` par `%d` → `2`.
- Les instances sont **nommées** (`@long`, `@gap`, `@full`, `@hf`) : c'est le seul moyen de distinguer leurs lignes dans le journal. `adeclick` n'est pas nommé (préfixe `[Parsed_adeclick_N @ …]`).
- La sortie `amix` est envoyée au muxer `null` : rien n'est écrit.
- stdout et stderr sont capturés et **concaténés** (`out + err`) ; le journal utile est sur stderr.
- Délai maximal : 900 s ; au-delà : kill, code −1, journal + « \nAnalyse interrompue (délai dépassé). ».
- Échec de lancement (OSError) → code −1, journal = message d'exception.
- Le processus est enregistré dans un ensemble partagé pendant son exécution pour pouvoir être tué par « Arrêter ».
- Sous Windows : `CREATE_NO_WINDOW` (sans objet sous Linux).

#### 3.1.3 Silence final (`audio_probe.trailing_silence_start`, l. 100-104)

```
<ffmpeg> -hide_banner -nostats -i <chemin> -af silencedetect=noise=-45dB:d=3 -f null -
```

Timeout 180 s (`run`). Journal = `stderr + stdout`. Regex : `silence_start:\s*([0-9.]+)` et `silence_end:\s*([0-9.]+)` (findall, **sans** signe moins). Règle : s'il y a des débuts et (`#starts > #ends` ou `|ends[-1] - declared| <= 1.5`) → `starts[-1]`, sinon `None`.

#### 3.1.4 Détection de ffmpeg (`ffmpeg_exe`, audio_probe.py:65-86)

Candidats dans l'ordre : `<dossier ffmpeg livré>/ffmpeg(.exe)` puis `ffmpeg(.exe)` (PATH). Retenu = premier pour lequel `<exe> -hide_banner -version` rend 0 (timeout 30 s). Résultat mémoïsé (verrou : les appels concurrents attendent la fin de la détection). `refresh=True` force une nouvelle détection.

### 3.2 Format réel du journal (ffmpeg 8.1.2, build gyan.dev, vérifié le 26/10/2026)

Extrait réel sur `D:\Ace of Base\Beautiful Life - Ace of Base.mp3` (mode approfondi) :

```
  Duration: 00:03:41.29, start: 0.023021, bitrate: 128 kb/s
  Stream #0:0: Audio: mp3 (mp3float), 48000 Hz, stereo, fltp, 128 kb/s, start 0.023021
[silencedetect@gap @ 00000214cbd255c0] silence_start: 0
  Stream #0:0: Audio: pcm_s16le, 48000 Hz, stereo, s16, 1536 kb/s
[silencedetect@gap @ 00000214cbd255c0] silence_end: 0.751104 | silence_duration: 0.751104
[silencedetect@gap @ 00000214cbd255c0] silence_start: 218.366708
[silencedetect@long @ 00000214cbd26380] silence_start: 217.777646
[Parsed_adeclick_7 @ 00000214cbd256c0] Detected clicks in 3 of 21243474 samples (1.4122e-05%).
[silencedetect@gap @ 00000214cbd255c0] silence_end: 221.286187 | silence_duration: 2.919479
[astats@hf @ 00000214cbd26140] Peak level dB: -7.859703
[astats@hf @ 00000214cbd26140] RMS through dB: -113.376532
[astats@hf @ 00000214cbd26140] Peak count: 2.000000
[astats@hf @ 00000214cbd26140] Noise floor dB: -129.159566
[astats@hf @ 00000214cbd26140] Number of samples: 10621737
[astats@full @ 00000214cbd247c0] Peak level dB: 0.000265
[astats@full @ 00000214cbd247c0] RMS through dB: -110.211159
[astats@full @ 00000214cbd247c0] Flat factor: 3.621094
[astats@full @ 00000214cbd247c0] Peak count: 58.000000
[astats@full @ 00000214cbd247c0] Noise floor dB: -inf
[astats@full @ 00000214cbd247c0] Number of samples: 10621737
[silencedetect@long @ 00000214cbd26380] silence_end: 221.286187 | silence_duration: 3.508542
```

Observations indispensables au portage :
- La clé astats s'écrit **`RMS through dB`** (coquille historique de ffmpeg pour « trough ») : c'est cette chaîne qui est recherchée.
- astats émet un bloc « Overall » (et pas de blocs par canal, `measure_perchannel=none`) ; la ligne « Overall » seule ne matche pas la regex (pas de « : »).
- `Number of samples` est un nombre d'échantillons **par canal** (221,29 s × 48 000 ≈ 10,62 M), alors que `Peak count` est cumulé sur les canaux et qu'adeclick compte sur tous les canaux (21,2 M). La formule de saturation (`100 × Peak count / Number of samples`) est conservée **telle quelle** pour rester comparable aux mesures existantes (elle peut théoriquement dépasser 100 % en stéréo).
- Les valeurs peuvent être `-inf`, `inf`, `nan`, ou absurdes (`Noise floor dB: -897.069387` sur silence numérique — observé en base).
- Avec ffmpeg 8.x, **silencedetect ferme la plage encore ouverte à la fin du flux** (ligne `silence_end` émise à la fin, valeur = fin du flux) : la branche « plage restée ouverte » (`#starts > #ends`) ne joue presque plus, c'est la branche « close sur la dernière trame » (`|ends[-1] - duration| <= 1.5`) qui détecte le blanc final. Les deux branches doivent être portées.
- Plusieurs lignes `Audio:` existent (entrée puis sortie) : seule la **première** correspondance est retenue (débit d'échantillonnage de l'entrée). Idem pour `Duration:` (la première = celle du fichier).

### 3.3 Regex (verbatim, quality_scan.py:222-230) et parsing

```python
_ASTATS_RE   = r"^\[astats@(\w+) @ [^\]]*\] ([A-Za-z][^:]*): (.+)$"
_SILENCE_RE  = r"^\[silencedetect@(\w+) @ [^\]]*\] silence_(start|end):\s*(-?[\d.]+)"
_DECLICK_RE  = r"Detected clicks in (\d+) of (\d+) samples"
_DURATION_RE = r"Duration: (\d+):(\d+):([\d.]+)"
_RATE_RE     = r"Audio: .*?, (\d+) Hz"
```

`_ASTATS_RE` et `_SILENCE_RE` sont **ancrées en début de ligne** (`match`) ; `_DECLICK_RE`, `_DURATION_RE`, `_RATE_RE` sont cherchées n'importe où (`search`). En .NET : `RegexOptions.CultureInvariant`, appliquer `^…$` ligne par ligne (découper le journal sur `\r?\n`), lazy `.*?` identique.

Algorithme `_parse(log)` (l. 295-377), ligne par ligne, **dans cet ordre de priorité** (`continue` après chaque correspondance) :

```
stats = {} ; silence_starts, silence_ends, gap_starts, gap_ends = [], [], [], []
clicks = null ; duration = 0.0 ; rate = 0
pour chaque ligne :
  si ASTATS.match : stats[g1 + "|" + g2] = to_float(g3) ; continuer        // ex. "full|Peak count"
  si SILENCE.match : nom=g1, type=g2, v=float(g3)
       si nom == "gap" : (type=="start" ? gap_starts : gap_ends).add(v)
       sinon           : (type=="start" ? silence_starts : silence_ends).add(v)   // "long" et tout autre nom
       continuer
  si DECLICK.search : total = int(g2) ou 1 si 0 ; clicks = 100.0 * int(g1) / total ; continuer
  si duration == 0 et DURATION.search : duration = h*3600 + m*60 + s
  si rate == 0 et RATE.search : rate = int(g1)
```

`to_float(text)` (l. 255-263) : `float(text)` ; en cas d'échec : `t = text.strip().lower()` ; si `t` se termine par `inf` → `-inf` si commence par `-`, sinon `+inf` ; sinon `NaN`. En C# : `double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, …)` puis traitement explicite de `inf`, `-inf`, `nan` (Python accepte nativement `"-inf"`, `"inf"`, `"nan"`, `"infinity"`).

Résultat de `_parse` (clés exactes) :

| Clé | Calcul |
|---|---|
| `duration` | ligne `Duration:` (0.0 si absente, ex. `Duration: N/A`) |
| `sample_rate` | première ligne `Audio: …, N Hz` |
| `peak_db` | `stats["full|Peak level dB"]`, défaut NaN |
| `clipping_pct` | §3.5 |
| `flat_factor` | `stats["full|Flat factor"]`, défaut 0.0 (informatif, non utilisé par le verdict) |
| `hiss_db` | `stats["hf|Noise floor dB"]`, défaut −∞ |
| `hiss_trough_db` | `stats["hf|RMS through dB"]`, défaut −∞ |
| `full_trough_db` | `stats["full|RMS through dB"]`, défaut −∞ |
| `crackle_pct` | `clicks` (null en mode rapide) |
| `gap_count`, `gap_seconds`, `gap_density` | §3.6 |
| `head_seconds`, `tail_seconds` | §3.4 |

`measure()` ajoute `deep` (booléen) et `error` (chaîne).

### 3.4 Blancs de début et de fin (l. 332-347)

```
head = 0.0
si silence_starts non vide ET silence_starts[0] <= 0.2 ET silence_ends non vide :
    head = max(0.0, silence_ends[0])

tail = 0.0
si silence_starts non vide ET duration != 0 :
    closed_on_end = silence_ends non vide ET |silence_ends[-1] - duration| <= 1.5
    si len(silence_starts) > len(silence_ends) OU closed_on_end :
        tail = max(0.0, duration - silence_starts[-1])
    si head != 0 ET tail >= duration - 0.5 :      // fichier entièrement muet
        tail = 0.0
```

Seules les listes `@long` (−50 dB, ≥ 2 s) servent ici.

### 3.5 Saturation (l. 349-358)

```
samples    = stats["full|Number of samples"] ou 0 si absent
peak_count = stats["full|Peak count"] ou 0
peak_db    = stats["full|Peak level dB"] ou NaN
clipping   = 0.0
si samples != 0 ET peak_db n'est pas NaN ET peak_db >= -0.5 :
    clipping = 100.0 * peak_count / samples
```

Attention à la sémantique Python `x or 0` : `NaN` est « vrai » en Python, donc un `NaN` resterait `NaN` ; seul `0`/absent donne 0. Reproduire : si la clé est absente ou vaut 0 → 0.

### 3.6 Coupures : densité sur fenêtre de 10 s (`_gap_stats`, l. 380-415)

Entrées : listes `gap_starts`, `gap_ends` (silencedetect `@gap`, −80 dB, ≥ 0,02 s), `duration`.

```
si duration == 0 : renvoyer (0, 0.0, 0.0)
segments = [(s, e) pour (s, e) dans zip(starts, ends)            // zip : tronqué à la plus courte liste ;
            si s >= 1.0 ET e <= duration - 1.0]                    // une plage sans fin est ignorée
si segments vide : renvoyer (0, 0.0, 0.0)
total = Σ (e - s)
density = 0.0
pour chaque (origin, _) dans segments :                           // fenêtre ancrée sur chaque début de coupure
    covered = Σ_{(s,e) ∈ segments} max(0.0, min(e, origin + 10.0) - max(s, origin))
    density = max(density, covered / 10.0)
renvoyer (len(segments), total, min(1.0, density))
```

- `gap_density` est une **fraction** (0..1) ; le seuil utilisateur est en %, d'où `100 × gap_density` dans `evaluate`.
- Complexité O(n²) sur le nombre de coupures (n reste petit, quelques dizaines à quelques centaines).
- Appariement par position (`zip`) : la k-ième fin est associée au k-ième début (ffmpeg les émet en alternance).

### 3.7 `measure(path, deep)` — gestion d'erreur (l. 418-456)

Valeurs neutres (« empty ») :

```json
{"duration":0.0,"sample_rate":0,"peak_db":NaN,"clipping_pct":0.0,"flat_factor":0.0,
 "hiss_db":-inf,"hiss_trough_db":-inf,"full_trough_db":-inf,"crackle_pct":null,
 "gap_count":0,"gap_seconds":0.0,"gap_density":0.0,"head_seconds":0.0,"tail_seconds":0.0,
 "deep":<deep>,"error":""}
```

1. ffmpeg introuvable → `empty` + `error = "FFmpeg introuvable"`.
2. fichier absent → `empty` + `error = "Fichier introuvable"`.
3. exécution ; `result = empty ∪ _parse(log)` ; `result.deep = deep`.
4. si code ≠ 0 **et** `duration == 0` → `empty` + `error = _error_line(log) or "Analyse impossible"`. Si code ≠ 0 mais une durée a été lue, les mesures sont **conservées** (ffmpeg a travaillé, seule la fin a échoué) — y compris en cas de kill par « Arrêter » (mais alors le résultat est jeté par l'appelant).

`_error_line(log)` (l. 459-465) : parcourt les lignes **à rebours**, renvoie la première ligne non vide (après `strip`) qui ne commence ni par `frame=`, ni par `size=`, ni par deux espaces — tronquée à 160 caractères ; sinon `""`. (Le test des deux espaces porte sur la ligne **déjà strippée**, il ne filtre donc rien en pratique.)

### 3.8 Verdict, gravités et note (`evaluate`, l. 525-602)

```
limits = DEFAULT_THRESHOLDS ⊕ thresholds (fusion, les clés fournies écrasent)
defects = [] (ordre d'ajout = ordre ci-dessous) ; severities = {}

severity(v, seuil, span) = span <= 0 ? 1.0 : clamp((v - seuil) / span, 0, 1)

1. HISS     : hiss = m.hiss_db (défaut −∞)
              si hiss n'est pas NaN ET hiss > limits.hiss_db ET hiss_measurable(m) ET is_broadband(m)
                → "hiss", severity(hiss, limits.hiss_db, 15.0)
2. CRACKLE  : c = m.crackle_pct ; si c != null ET c > limits.crackle_pct
                → "crackle", severity(c, limits.crackle_pct, limits.crackle_pct * 8)
3. CLIPPING : k = m.clipping_pct (défaut 0.0) ; si k > limits.clipping_pct
                → "clipping", severity(k, limits.clipping_pct, limits.clipping_pct * 8)
4. GAPS     : d = 100.0 * (m.gap_density or 0.0) ; si d > limits.gap_density_pct
                → "gaps", severity(d, limits.gap_density_pct, 25.0)
   excess = duration_excess(m, reference)      // null si reference null ou duration 0 ; sinon duration - reference
5. DURATION : si excess != null ET |excess| > limits.duration_gap
                → "duration", severity(|excess|, limits.duration_gap, 30.0)
6. si excess != null ET excess > limits.duration_excess :
     TAIL   : si m.tail_seconds > limits.tail_seconds → "tail", severity(tail, limits.tail_seconds, 20.0)
     HEAD   : si m.head_seconds > limits.head_seconds → "head", severity(head, limits.head_seconds, 8.0)

penalty = Σ_{d ∈ defects} (12 + 20 * severities[d])
score   = max(0, int(round(100 - penalty)))      // round Python = arrondi au pair (banker's) → Math.Round(x, MidpointRounding.ToEven)
renvoyer (defects, severities, score)
```

Conséquences : note 100 = aucun défaut ; un défaut coûte entre 12 et 32 points ; la note sert surtout au tri. L'ordre des défauts dans la liste (donc dans la colonne DÉFAUTS et la colonne `defects` en base) est : hiss, crackle, clipping, gaps, duration, tail, head.

Aides :
- `trough_gap(m)` (l. 478-489) : `full = m.full_trough_db`, `high = m.hiss_trough_db` ; si l'un est null, NaN ou ±∞ → `null` ; sinon `full - high`.
- `hiss_measurable(m)` (l. 492-500) : `rate = m.sample_rate or 0` ; vrai si `rate == 0` ou `rate >= 2.5 × 8000` (= 20 000 Hz).
- `is_broadband(m)` (l. 503-512) : `gap = trough_gap(m)` ; vrai si `gap == null` ou `gap <= 12.0`.

### 3.9 Texte des cellules (`describe`, l. 611-632)

| Défaut | Texte |
|---|---|
| hiss | « — » si NaN ou `<= -120.0` (−∞ compris) ; sinon `"%.0f dB"` (ex. « -60 dB ») |
| crackle | `"%.4f %%"` (ex. « 0.0006 % ») ; « — » si null |
| clipping | `"%.3f %%"` (ex. « 0.101 % ») |
| gaps | « — » si `gap_count == 0` ; sinon `"%d · %.0f %%"` (nombre · densité %) |
| tail | `"%.0f s"` |
| head | `"%.0f s"` |
| autre (duration) | `""` |

Séparateur décimal : **point** (formatage C, culture invariante).

### 3.10 Sérialisation des mesures (`_encode_measures` / `_decode_measures`, l. 747-768)

- Écriture : flottant ±∞ → chaîne `"-inf"` / `"inf"` ; NaN → `null` ; le reste tel quel.
- Lecture : `"-inf"` → −∞, `"inf"` → +∞ (sur **toutes** les clés) ; si `peak_db` est `null` → NaN.
- En base (`quality.measures`), le JSON est compact, clés triées (`json_dump`, db_schema.py:335-338).

### 3.11 Cache des mesures (`MeasureCache`, l. 644-739)

- Clé d'entrée : `os.path.normcase(os.path.abspath(path))` → sous Windows, **minuscules + antislashs** (ex. `d:\\addicted to you - shakira.mp3`).
- Empreinte (« stamp ») : `"%d:%d" % (st_mtime_ns, st_size)` (ex. `1784567362000000000:2372162`) ; `None` si `stat` échoue.
- `get(path, deep)` : stamp différent ou absent → `None` ; si `deep` demandé et mesure non approfondie → `None` (une mesure approfondie sert aussi une demande rapide) ; sinon mesures décodées.
- `put(path, measures)` : ignoré si stamp introuvable ou `measures.error` non vide (**les erreurs ne sont jamais mises en cache**) ; marque « sale ».
- `forget(path)` : supprime l'entrée ; `clear()` : vide et sauvegarde.
- `save()` : seulement si « sale » ; écriture atomique `fichier.tmp` puis remplacement ; deux verrous (données / disque) ; en cas d'échec, l'entrée redevient « sale ».
- `load()` : ignoré si version ≠ 5.
- Version web : remplacer par la table `quality` (§5.2), même `stamp`.

### 3.12 Réencoder : « déjà au format cible » (main_window.py:2161-2186)

```
target_bitrate(qualité) = 128 si "128" ∈ qualité ; 320 si "320" ∈ qualité ; sinon 0
is_at_target(item) :
    fmt = format_cible.ToUpper()
    si item.format.ToUpper() != fmt : faux
    si fmt ∈ {FLAC, WAV} : vrai                       // sans perte : le format suffit
    t = target_bitrate(qualité) ; si t == 0 : vrai     // « Lossless (Qualité Source) »
    renvoyer |item.bitrate (0 si absent) - t| <= 5
```

Un 320 kbps avec cible 128 n'est **pas** masqué (il reste à convertir).

### 3.13 Temps restant (`_remaining_text`, l. 625-636)

```
si done < 5 ou total == 0 ou chrono invalide : ""
elapsed = secondes écoulées depuis le début ; si <= 0 : ""
seconds = (total - done) * elapsed / done
si seconds < 45 : "moins d'une minute restante"
minutes = round(seconds / 60)
"environ {minutes} min restante" + ("s" si minutes > 1)
```

### 3.14 Durée de référence (reference_duration.py)

**Normalisation** (`_canonical`, l. 69-72) :
```
t = (text or "").lower()
t = Regex.Replace(t, "[^a-z0-9]+", " ")
t = Regex.Replace(t, "\s+", " ").Trim()
```
Les lettres accentuées et non latines sont **supprimées** (ex. « Michel Teló » → `michel tel`). La clé de cache est `canonical(artist) + "|" + canonical(title)` (ex. `madcon feat ray dalton|don t worry radio version`). Pour réutiliser les lignes existantes de `reference_durations`, la normalisation .NET doit produire **exactement** les mêmes clés (`ToLowerInvariant` puis les deux remplacements ; tester sur un échantillon de la base).

**Parenté de titres** (`_related`, l. 75-85) : faux si l'un est vide ; sinon `candidate.StartsWith(wanted) || wanted.StartsWith(candidate)` (après normalisation, ordinal).

**Cadence** (`_throttle`, l. 59-66) : par service, verrou global ; attend `précédent + intervalle - maintenant` si positif ; puis note l'instant (horloge monotone). Deezer 0,2 s (≈ 5 req/s), MusicBrainz 1,1 s (< 1 req/s).

**Deezer** (`_from_deezer`, l. 91-113) :
- `GET https://api.deezer.com/search?q=artist:"{artist}" track:"{title}"&limit=25` (paramètres encodés en URL par la bibliothèque HTTP ; les guillemets internes ne sont **pas** échappés), délai 8 s, sans en-tête particulier.
- Exception (réseau, JSON invalide) → `null` (« ne rien conclure, ne rien mettre en cache »).
- Statut ≠ 200 → liste vide (⇒ MusicBrainz sera interrogé).
- Pour chaque `data[]` : `seconds = duration` (entier) ; ignoré si ≤ 0 ; ajouté à `all_found` ; ajouté à `close` si `related(canonical(track.title), canonical(title))`.
- Renvoie `close` s'il est non vide, **sinon `all_found`** (toutes les durées, même sans parenté de titre).

**MusicBrainz** (`_musicbrainz_recordings` + `_from_musicbrainz`, l. 116-168) :
- `GET https://musicbrainz.org/ws/2/recording?query=artist:"{artist}" AND recording:"{title}"&fmt=json&limit=25`, en-tête `User-Agent: DeeKeep/1.0 (bibliotheque musicale personnelle)`, délai 8 s.
- Jusqu'à 2 tentatives : chaque tentative passe par la cadence ; exception → `null` ; statut 503 et tentative restante → pause `1,1 × (tentative + 2)` s (2,2 s) puis nouvelle tentative ; autre statut ≠ 200 → `null` ; JSON invalide → `null` ; sinon `recordings`.
- Filtre : `int(score or 0) >= 90`, `length` (ms) > 0, `related(canonical(recording.title), canonical(title))` ; durée = `int(round(length / 1000.0))` (arrondi au pair).

**`lookup(artist, title, cache)`** (l. 243-273) :
```
artist, title = Trim ; si l'un est vide : []
si cache : known = cache.get(artist, title) ; si known != null : renvoyer known
d = deezer(artist, title) ; si d == null : renvoyer null
si d vide : mb = musicbrainz(artist, title) ; si mb == null : renvoyer null ; d = mb
si cache : cache.put(artist, title, d)            // y compris liste vide (recherche négative)
renvoyer d
```

**Cache** (`ReferenceCache`, l. 174-237) : entrée `{"durations": [int…], "time": epoch}` ; `get` → `null` si absent, si `durations` n'est pas une liste, ou si la liste est **vide et plus vieille que 30 jours** (recherche négative expirée) ; `put` horodate ; sauvegarde atomique identique au cache des mesures.

**`closest(durations, measured)`** (l. 276-286) : `null` si liste vide/nulle ou `measured` nul ; sinon l'élément minimisant `|d - measured|` (premier en cas d'égalité). C'est cette valeur (entière) qui devient `entry.reference`.

### 3.15 Lecture des tags (`reencode_scan.read_tags`, l. 230-277)

Clés renvoyées : `title, artist, album, albumartist, track, tracktotal, disc, disctotal, date, genre, composer, publisher, comment, compilation` (chaînes), `bitrate` (entier kbps), `title_tagged` (booléen).

- Cache mémoire par chemin : `(mtime_ns, taille) → (need_bitrate, tags)` ; réutilisé si même empreinte et (débit déjà lu ou non demandé) ; vidé entièrement à 100 000 entrées ; `clear_tag_cache()` après rangement.
- **MP3** : si débit demandé (ou filtrage de trames indisponible) → lecture complète `MP3(path)` (tags + en-tête MPEG pour le débit) ; en cas d'échec, repli sur la lecture ID3 seule. Sinon lecture ID3 restreinte aux trames `TIT2, TPE1, TPE2, TALB, TRCK, TPOS, TDRC, TYER, TDAT, TIME, TCON, TCOM, TPUB, TCMP, COMM` (+ équivalents ID3v2.2), sans ID3v1, **sans décoder la pochette**. Correspondances : title=TIT2, artist=TPE1, album=TALB, albumartist=TPE2, track/tracktotal=TRCK découpé sur « / », disc/disctotal=TPOS, date=TDRC sinon TYER, genre=TCON, composer=TCOM, publisher=TPUB, compilation=TCMP, comment = premier `COMM` non vide (toutes langues).
- **FLAC** : champs Vorbis ; débit via `info`.
- **Autres** (M4A, OGG, Opus, WMA, WAV, AAC) : `mutagen.File(path, easy=True)` → champs nommés.
- Champs nommés (`_read_vorbis`) : title ; artist ; album ; albumartist ← `albumartist` | `album artist` ; track ← `tracknumber` | `track` (découpé « n/total »), tracktotal ← reste ou `tracktotal` | `totaltracks` ; disc ← `discnumber` | `disc`, disctotal ← reste ou `disctotal` | `totaldiscs` ; date ← `date` | `year` | `originaldate` ; genre ; composer ; publisher ← `organization` | `label` | `publisher` ; comment ← `comment` | `description` ; compilation. Valeur = premier élément non vide, `strip`.
- Débit (`_info_bitrate`) : `round(info.bitrate / 1000)` si > 0, sinon `round(taille × 8 / durée / 1000)` si durée > 0, sinon 0.
- Toute exception de lecture est avalée (tags vides).
- `title_tagged = title non vide` ; si vide, `title = nom de fichier sans extension`.

### 3.16 Parcours de dossier (`iter_audio_files`, l. 290-316)

Parcours itératif en profondeur avec pile : pour chaque dossier, `scandir` ; sous-dossiers (sans suivre les liens symboliques) et fichiers dont le nom (en minuscules) se termine par une extension audio ; erreurs d'accès ignorées ; les fichiers d'un dossier sont émis triés **insensiblement à la casse**, puis les sous-dossiers sont empilés en ordre inverse pour être visités alphabétiquement. Ordre résultant : fichiers du dossier courant, puis sous-dossiers récursivement (ordre alphabétique). **Aucun dossier n'est exclu** (`$RECYCLE.BIN`, `System Volume Information`, dossiers cachés…).

### 3.17 Jeux d'essai (vérifiés sur la base réelle)

| Cas | Mesures (extrait) | Référence | Attendu |
|---|---|---|---|
| « Beautiful Life - Ace of Base.mp3 » | duration 221.29 ; sample_rate 48000 ; peak_db 0.000265 ; Peak count 58 ; samples 10621737 → clipping 0.000546 % ; hiss −129.16 ; troughs −110.21 / −113.38 ; crackle 1.412e-05 ; gaps : (0 ; 0.751) exclu (début < 1 s), (218.37 ; 221.29) exclu (fin > durée − 1) → 0 ; head 0 ; tail = 221.29 − 217.777646 = 3.512354 | durées `[221,179,227,…]` → closest = 221 | défauts `[]`, note 100 |
| « Fighter - Christina Aguilera.mp3 » | hiss_db −59.787714 ; full_trough −59.812481 ; hiss_trough −71.05837 → gap 11.25 ≤ 12 (large bande) ; sample_rate 48000 ; clipping 0.1008 ; crackle 8.9e-05 ; duration 246.05 | `[245,266]` → 245, excès +1.05 | `["hiss"]`, gravité (−59.79+65)/15 = 0.3475, note round(100 − 18.95) = **81** |
| Silence numérique | hiss_db −897.07 | — | « — » en colonne SOUFFLE (≤ −120) |

---

## 4. Intégrations externes

### 4.1 FFmpeg / FFprobe

- Version de référence actuelle : **ffmpeg 8.1.2** (build « essentials » gyan.dev, Windows), livré dans `ffmpeg/` à la racine de l'application.
- Filtres requis : `asplit`, `silencedetect`, `aformat`, `astats`, `highpass`, `adeclick`, `amix`, muxer `null`. Tous font partie de libavfilter standard (présents dans les paquets Debian/Ubuntu ARM64 et les builds statiques arm64).
- ffprobe n'est pas utilisé par ce périmètre.
- La sortie journal est en anglais et non localisée ; le format des lignes (`[nom@instance @ 0x…] clé: valeur`) est stable depuis des années mais **doit être vérifié** sur la version installée côté serveur (§6).

### 4.2 API Deezer (publique, sans clé)

| Élément | Valeur |
|---|---|
| URL | `https://api.deezer.com/search` |
| Paramètres | `q=artist:"<artiste>" track:"<titre>"`, `limit=25` |
| Champs lus | `data[].duration` (s), `data[].title` |
| Délai | 8 s |
| Cadence | ≥ 0,2 s entre deux appels (tous appelants confondus dans le processus) |
| Erreurs | exception → aucune conclusion ; statut ≠ 200 → « aucun résultat » (bascule MusicBrainz) |

### 4.3 MusicBrainz

| Élément | Valeur |
|---|---|
| URL | `https://musicbrainz.org/ws/2/recording` |
| Paramètres | `query=artist:"<artiste>" AND recording:"<titre>"`, `fmt=json`, `limit=25` |
| En-tête | `User-Agent: DeeKeep/1.0 (bibliotheque musicale personnelle)` |
| Champs lus | `recordings[].score`, `recordings[].length` (ms), `recordings[].title` |
| Cadence | ≥ 1,1 s ; 503 → une 2e tentative après 2,2 s |
| Délai | 8 s |

Une bibliothèque de 3 000 titres demande environ un quart d'heure la première fois (5/s Deezer, 1/s pour les titres absents de Deezer), puis rien (cache).

### 4.4 mutagen (lecture des tags)

Équivalent .NET : **TagLibSharp** (MP3/ID3v2.2-2.4, FLAC, MP4/M4A, Ogg/Opus, WMA, WAV). Exiger : lecture sans décoder les images (TagLib# charge les images paresseusement), débit (`Properties.AudioBitrate`), durée. Vérifier la cohérence des débits (VBR) avec mutagen sur un échantillon : le filtre « format cible » tolère ±5 kbps.

### 4.5 Extraction (yt-dlp) — consommée par « Retélécharger »

Voir l'analyse du module de téléchargement. Paramètres transmis par la page Qualité : identifiant de ligne, `track_id=""`, titre, artiste, album, destination = racine bibliothèque, format et qualité d'export courants, `cover_bytes=None`, règles de rangement, décompte partagé, tags du fichier abîmé, cookies (navigateur ou fichier), `original_path`, `resolve_metadata=True`. Événements : progression `(id, %, texte)`, fin `(id, succès, message, {title, artist, album, path, cover_b64, tags})`.

### 4.6 Deno, fpcalc

- Deno : uniquement pour yt-dlp (moteur JS YouTube) ; côté serveur ARM64, installer `deno` (binaire aarch64 officiel) et le mettre dans le `PATH` du service d'extraction.
- fpcalc (Chromaprint) : hors périmètre Qualité ; détection décrite §2.21 ; paquet `libchromaprint-tools` sur Debian ARM64.

---

## 5. Données

### 5.1 Fichiers de cache de l'application desktop (`%APPDATA%\DeeKeep\`)

**À l'exécution, l'application desktop ne lit ni n'écrit les tables `quality` et `reference_durations`** : elle utilise deux fichiers JSON ; les tables ne sont remplies que par la collecte `db_init.build_local` (bouton « Base de données » / ligne de commande), qui recopie ces fichiers.

`quality_cache.json` (≈ 2,2 Mo) :
```json
{"version": 5, "entries": {
  "d:\\addicted to you - shakira.mp3": {
    "stamp": "1784567362000000000:2372162",
    "measures": {"duration": 147.06, "sample_rate": 48000, "peak_db": 0.000265,
      "clipping_pct": 0.0455…, "flat_factor": 9.324583, "hiss_db": -897.069387,
      "hiss_trough_db": -442.507419, "full_trough_db": "-inf", "crackle_pct": 0.000226…,
      "gap_count": 0, "gap_seconds": 0.0, "gap_density": 0.0, "head_seconds": 0.0,
      "tail_seconds": 0.0, "deep": true, "error": ""}}}}
```

`reference_cache.json` (≈ 350 Ko) :
```json
{"version": 1, "entries": {
  "cutting crew|i just died in your arms": {"durations": [280, 329, 286, 406, 265, 490, 490, 325, 265, 325, 119, 121], "time": 1786982712.7150493}}}
```

### 5.2 Table `quality`

DDL réel (base `deekeep_cache.db`) :
```sql
CREATE TABLE quality (
    path TEXT PRIMARY KEY,
    stamp TEXT,
    measures TEXT,
    defects TEXT,
    updated_at REAL NOT NULL DEFAULT 0,
    deleted INTEGER NOT NULL DEFAULT 0,
    h INTEGER NOT NULL DEFAULT 0
)
-- index : CREATE INDEX IF NOT EXISTS idx_quality_updated ON quality (updated_at)
```

| Colonne | Contenu |
|---|---|
| `path` | Chemin **avec la casse réelle** quand le fichier est connu de `tracks` (rapprochement insensible à la casse), sinon la clé du cache (minuscules) normalisée (`os.path.normpath`) |
| `stamp` | `"<mtime_ns>:<taille>"` |
| `measures` | JSON compact, clés triées, ±∞ en chaînes `"-inf"`/`"inf"`, NaN en `null` (schéma §3.3 + `deep`, `error`) |
| `defects` | JSON : liste ordonnée d'identifiants parmi `hiss, crackle, clipping, gaps, duration, tail, head` |
| `updated_at` | horodatage Unix de la dernière écriture |
| `deleted` | 0/1 (pierre tombale : `stamp`, `measures`, `defects` à `NULL`) |
| `h` | empreinte 30 bits (BLAKE2b) du contenu (voir analyse du module base de données) |

Constat sur la base réelle (lecture seule, 26/10/2026) :
- 4 935 lignes dont 365 pierres tombales ; 4 570 vivantes, **toutes `deep = true`**, aucune en erreur, `crackle_pct` toujours renseigné.
- Types : `gap_count`, `sample_rate` entiers ; `deep` booléen ; `error` chaîne ; `full_trough_db`, `hiss_db`, `hiss_trough_db` flottant **ou** `"-inf"` ; tous les autres flottants.
- Répartition des `defects` : `[]` 3 480 ; `["duration"]` 751 ; `["clipping"]` 99 ; `["crackle"]` 59 ; `["gaps"]` 47 ; `["tail"]` 32 ; `["duration","tail"]` 32 ; …
- 54 lignes concernent `D:\$RECYCLE.BIN\…` (corbeille analysée faute d'exclusion).
- 3 359 lignes vivantes se joignent à `tracks` sur `path`.
- Exemple :
```
path     = D:\Christina Aguilera\Fighter - Christina Aguilera.mp3
measures = {"clipping_pct":0.10076701811926629,"crackle_pct":8.890460383600504e-05,"deep":true,"duration":246.05,"error":"","flat_factor":8.721454,"full_trough_db":-59.812481,"gap_count":0,"gap_density":0.0,"gap_seconds":0.0,"head_seconds":0.0,"hiss_db":-59.787714,"hiss_trough_db":-71.05837,"peak_db":0.000265,"sample_rate":48000,"tail_seconds":0.0}
defects  = ["hiss","duration"]
```

**Attention : la colonne `defects` existante n'est pas fiable** (voir §6.3) : elle a été calculée par `db_init.collect_quality` avec une référence **médiane** et une clé de référence différente. Pour « Fighter », l'écran desktop affiche `["hiss"]` (référence la plus proche 245 s) alors que la base contient `["hiss","duration"]` (médiane de `[245, 266]` = 266 s). La version web doit **recalculer** le verdict à partir de `measures` + seuils + référence (c'est d'ailleurs le principe : le verdict ne se stocke pas, il se recalcule).

### 5.3 Table `reference_durations`

```sql
CREATE TABLE reference_durations (
    key TEXT PRIMARY KEY,          -- canonical(artiste) + "|" + canonical(titre)
    durations TEXT,                -- JSON : liste d'entiers (secondes), éventuellement vide
    fetched_at REAL,               -- horodatage de la recherche (arrondi à 3 décimales)
    updated_at REAL NOT NULL DEFAULT 0,
    deleted INTEGER NOT NULL DEFAULT 0,
    h INTEGER NOT NULL DEFAULT 0
)
```

Constat : 3 381 lignes, aucune pierre tombale, 148 recherches négatives (`[]`). Exemples : `('taylor swift|22', '[242,232,303,233,243,232,158,291,280,231,260,63,230,231]', 1786982716.796)`, `('madcon feat ray dalton|don t worry radio version', '[]', 1786982756.744)`. La règle d'expiration (liste vide de plus de 30 jours → à rechercher de nouveau) s'applique sur `fetched_at`.

### 5.4 Paramètres (`app_settings`, valeur JSON)

| Clé | Valeur en base | Rôle |
|---|---|---|
| `qual_deep` | `true` | Analyse approfondie |
| `qual_hiss_db` | `-65.0` | |
| `qual_crackle_pct` | `0.0025` | |
| `qual_clipping_pct` | `0.2` | |
| `qual_gap_density_pct` | `30.0` | |
| `qual_tail_seconds` | `5.0` | |
| `qual_head_seconds` | `3.0` | |
| `qual_duration_excess` | `3.0` | |
| `qual_duration_gap` | `10.0` | |
| `download_dir` | | Dossier analysé par défaut |
| `file_format`, `quality` | ex. `"MP3"`, `"128 kbps (Standard)"` | Format/qualité du retéléchargement et du filtre « format cible » |
| `youtube_browser`, `cookies_file` | | Identification YouTube pour l'extraction |
| `org_*` | | Règles de rangement appliquées au fichier retéléchargé |

### 5.5 Modèle de ligne de l'écran (à exposer en GraphQL)

```
QualityEntry {
  uid: Int!                      # identifiant de session (stable pendant la vie de la liste)
  path: String!
  title, artist, album: String   # title = nom de fichier si pas de tag
  tags: TagSet                   # jeu complet (§3.15), transmis au retéléchargement
  format: String                 # extension en majuscules
  bitrate: Int                   # kbps, 0 si inconnu
  measures: QualityMeasures      # §3.3 (+ deep, error) ; ±inf / NaN à représenter (ex. null + drapeau, ou chaînes)
  reference: ReferenceState      # PENDING ("?") | NONE (null) | VALUE(secondes)
  defects: [DefectId!]!          # ordre d'évaluation
  severities: { DefectId: Float } 
  score: Int                     # 0..100
  selected: Boolean
  status: String                 # « En attente… », « Initialisation… », « {texte} — {n}% », « Échec », « Réanalyse… », « Remplacé ✓ … »
  progressPct: Int
}
```

### 5.6 Lectures / écritures disque et base (résumé)

| Opération | Lit | Écrit |
|---|---|---|
| Analyse | arborescence du dossier ; chaque fichier audio (décodage complet par ffmpeg) ; tags ; cache des mesures | cache des mesures (fin d'analyse) |
| Recherche de référence | cache des références ; réseau | cache des références (tous les 50, à l'arrêt) |
| Seuils | — | configuration (`qual_*`) |
| Retéléchargement | tags, règles de rangement, arborescence (décompte) | nouveau fichier dans la bibliothèque ; suppression de l'ancien si chemin différent ; historique (module téléchargement) |
| Réanalyse | nouveau fichier | cache des mesures (immédiatement) |
| Réencoder (scan) | arborescence, tags + en-têtes audio | — |

---

## 6. Points d'attention pour la version web (Linux ARM64 peu puissant)

### 6.1 Coût CPU et parallélisme

- **Mesure réelle** (i5-1240P, 1 processus) sur un MP3 de 3 min 41 : **4,1 s en mode approfondi, 1,4 s en mode rapide** (adeclick ≈ ×3). Le code annonce « ~4× ». Sur un Cortex-A72/A76 (Raspberry Pi 4/5), compter grossièrement ×2 à ×4 : ~10-16 s par titre en approfondi, ~3-6 s en rapide. Pour une bibliothèque de ~3 500 titres (239 h de musique) : de l'ordre de **10 à 15 h** d'analyse approfondie avec 1 processus, 3-5 h en rapide. La première analyse doit donc être une **tâche de fond longue, reprenable** (le cache par `stamp` rend la reprise gratuite), jamais liée à une session navigateur.
- `SCAN_WORKERS = max(2, min(12, cpu-2))` est calibré pour un poste de travail : **ne pas le porter tel quel**. Recommandation : 1 analyse simultanée par défaut sur ARM (conforme à ARCHITECTURE.md : « 1 analyse qualité sur un Raspberry Pi »), paramétrable (1..cœurs−1), processus ffmpeg lancés avec priorité basse (`nice -n 10`, `ionice -c3`) et `-threads 1` éventuel pour borner la mémoire/CPU, file persistée.
- Consommer les résultats **dans l'ordre d'achèvement** (pas comme `executor.map`, qui bloque derrière un fichier lent).
- « Arrêter » doit tuer les processus (signal `SIGKILL` sur le processus ffmpeg, idéalement groupe de processus) et ne pas mettre en cache les mesures interrompues.
- Le délai de 900 s par fichier peut être trop court sur ARM pour un mix d'une heure en mode approfondi : le rendre proportionnel à la durée (ex. `max(900, 0,5 × durée)`).
- Proposer le mode rapide par défaut sur ARM, ou une analyse « rapide d'abord, approfondie la nuit » ; le cache distingue déjà les deux (une mesure approfondie sert une demande rapide, pas l'inverse).
- Retéléchargement : 5 extractions simultanées sur desktop ; sur ARM, s'aligner sur la limite globale du service d'extraction (2 selon ARCHITECTURE.md) — chaque extraction réencode via ffmpeg puis déclenche une réanalyse (ffmpeg à nouveau).
- Lecture des tags : pool de 8 threads I/O ; sur carte SD/disque USB, 2-4 suffisent.

### 6.2 Compatibilité ffmpeg et parsing

- Épingler la version de ffmpeg du serveur (paquet Debian 5.1/6.x/7.x ou build statique arm64 8.x) et **rejouer les jeux d'essai §3.17** : noms de champs astats (`RMS through dB`, `Noise floor dB`, `Peak count`, `Number of samples`, `Peak level dB`, `Flat factor`), message adeclick (`Detected clicks in N of M samples`), comportement de silencedetect en fin de flux (§3.2). Les mesures existantes en base viennent de ffmpeg 8.1.2.
- Parser en culture invariante ; gérer `inf`, `-inf`, `nan` ; reproduire l'arrondi au pair (`Math.Round(…, MidpointRounding.ToEven)`) pour la note et les durées MusicBrainz ; les formats `%.0f` côté affichage peuvent diverger d'une unité sur des demi-valeurs exactes (sans incidence fonctionnelle).
- Journal ffmpeg : le lire en flux (stderr) ligne par ligne plutôt que de tout accumuler ; il reste petit grâce à `-nostats`.
- Les valeurs absurdes (`-897 dB`, `-3202 dB`) sont normales sur du silence numérique : ne pas les « nettoyer » (elles participent à `trough_gap`).

### 6.3 Incohérences et pièges relevés dans le code existant

1. **Clés et règle de référence différentes entre l'écran et la collecte en base** : `db_init._reference_for` (db_init.py:638-648) cherche la référence avec la clé `artist.strip().lower() + "|" + title.strip().lower()` (non normalisée — elle ne correspond donc qu'aux titres sans ponctuation ni accent) et prend la **médiane** des durées, alors que l'écran utilise `canonical()` et la **plus proche** (`closest`). La colonne `quality.defects` en base est par conséquent souvent fausse (751 lignes `["duration"]`, dont « Beautiful Life » : médiane 341 s contre une référence la plus proche à 221 s). Version web : une seule implémentation (`closest` + `canonical`) et recalcul du verdict.
2. Commentaire trompeur : quality_page.py:99-100 et l'infobulle de `duration_excess` affirment que seuls les fichiers portant un blanc sont recherchés ; le code (`_needs_reference`, l. 677-690) recherche **tout fichier ayant artiste et titre**. Le code fait foi.
3. Après un retéléchargement réussi, `cache.forget(new_path)` oublie le **nouveau** chemin ; l'entrée de l'**ancien** chemin (supprimé si le fichier a été renommé/déplacé) reste orpheline dans le cache. Prévoir la suppression de la ligne `quality` de l'ancien chemin.
4. La réanalyse réutilise la référence calculée pour l'ancienne durée (`closest` n'est pas rejoué) ; si le nouveau fichier a une durée très différente, le verdict « durée » peut être faux. Recalculer `closest` à partir des durées en cache.
5. `set_root` appelé par la fenêtre principale pendant une analyse ne stoppe pas le scan en cours : ses lots continuent d'arriver dans une liste censée être vide. En web : un changement de dossier doit annuler la tâche d'analyse en cours.
6. Mode rapide avec mesures approfondies en cache : `crackle_pct` est présent, le défaut « crackle » peut entrer dans la note alors que la case de filtre est neutralisée (`_visible_defects`) ; la note d'un titre peut donc baisser sans défaut visible. Décider explicitement (ignorer `crackle` dans `evaluate` quand l'analyse demandée est rapide, ou l'afficher).
7. `iter_audio_files` n'exclut aucun dossier système : 54 fichiers de la corbeille sont en base. Exclure au minimum `$RECYCLE.BIN`, `System Volume Information`, `.Trash-*`, `lost+found`, dossiers cachés (`.`).
8. Les guillemets internes de l'artiste/du titre ne sont pas échappés dans les requêtes Deezer/MusicBrainz (`artist:"…"`) ; un titre contenant `"` fausse la requête. Échapper (`\"`) ou retirer.
9. `_error_line` ne filtre pas réellement les lignes indentées (test après `strip`) — sans incidence, à reproduire ou corriger sciemment.
10. Une panne réseau (Deezer `null`) donne `reference = None` à l'écran, **indiscernable** de « titre inconnu » (« sans référence ») ; elle n'est pas mise en cache et sera retentée à la prochaine analyse. En web, distinguer les deux états (ex. `NETWORK_ERROR` réessayable).
11. Saturation : `Peak count` (tous canaux) rapporté à `Number of samples` (par canal) — conserver la formule pour comparer aux mesures historiques, mais le documenter dans l'interface si besoin.
12. Le message d'échec d'extraction n'est visible qu'en console (`print`) : l'exposer en infobulle de la cellule STATUT comme sur les autres écrans.

### 6.4 Chemins, casse et empreintes sur Linux

- Les chemins en base sont des chemins **Windows** (`D:\Artiste\Titre.mp3`), certaines lignes `quality` en minuscules (`d:\$recycle.bin\…`). Le serveur Linux verra la bibliothèque sous un autre point de montage (`/srv/musique/…`) avec une **casse significative**. Il faut une couche de traduction de chemins (préfixe Windows ↔ préfixe Linux, séparateurs) et une politique de clé : soit conserver la clé historique `path` pour rester compatible avec l'application desktop et la synchronisation Turso, soit migrer. Ne jamais appliquer `normcase` (minuscules) sous Linux.
- `stamp = mtime_ns:taille` : le `mtime` d'un même fichier vu par Windows (NTFS/exFAT) et par Linux (montage NTFS/exFAT/SMB) peut différer (précision 2 s en FAT, arrondi à la seconde observé ici : `…000000000`). Si le stamp diffère, tout sera remesuré (coûteux sur ARM). Prévoir une comparaison tolérante (même taille et |Δmtime| ≤ 2 s) ou une recalibration unique des stamps au premier passage.
- Écrire dans la table `quality` en respectant le contrat de synchronisation (`updated_at`, `deleted`, `h`, file `sync_outbox`) décrit dans l'analyse « base de données » : la table est partagée avec l'application desktop via Turso.

### 6.5 Réseau et cadence côté serveur

- La cadence Deezer/MusicBrainz doit être **globale au serveur** (un seul limiteur partagé par tous les utilisateurs et services, persistant éventuellement entre redémarrages), pas par requête GraphQL.
- MusicBrainz recommande un User-Agent avec contact (`Application/version ( contact )`) : conserver la valeur actuelle pour la compatibilité, idéalement y ajouter une URL/adresse de contact.
- La recherche de références doit survivre à l'arrêt de l'analyse et à la fermeture du navigateur (tâche serveur), et sauvegarder régulièrement (aujourd'hui tous les 50 titres).

### 6.6 Interface web

- Volume : plusieurs milliers de lignes ; filtrage/tri **côté serveur** ou client avec virtualisation (CDK virtual scroll) ; pousser les résultats par lots (abonnement GraphQL, lots de 8 analyses / 10 références) ; regrouper les rendus (équivalent du délai de 500 ms) ; ne pas retrier pendant l'analyse (ajout en fin de liste) mais retrier à la fin.
- « Écouter » : remplacer le lecteur Qt par un flux HTTP avec `Range` servi par le serveur, la liste affichée devenant la file d'attente ; « Ouvrir avec le lecteur du système » et « Ouvrir le dossier » n'ont pas d'équivalent (proposer « Télécharger le fichier » et « Copier le chemin »).
- Les états « référence… », « sans référence », « dans le master », « non mesuré », « Illisible » doivent être conservés : ils expliquent pourquoi un silence ou une durée n'est pas mis en cause.
- La page doit permettre de quitter et revenir : l'analyse, la recherche de références et les retéléchargements sont des tâches serveur dont l'état (progression, temps restant, bilan) est relu à l'ouverture.
- Persister éventuellement l'état des filtres et du volet « Seuils » (non persistés aujourd'hui) — amélioration facultative.
