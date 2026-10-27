# 03 — Organiser, compléter les tags, rechercher les doublons

> Spécification fonctionnelle et technique destinée à la réécriture web
> (Angular + GraphQL + microservices .NET, serveur Linux ARM64, base SQLite conservée).
> Source analysée : application desktop DeeKeep (Python / PyQt6), dossier
> `D:\000_Logiciel\DeezerMusic`, paquet `deekeep/`. Toutes les références
> `fichier:ligne` renvoient à ce code source.
>
> Ce document doit suffire à réimplémenter le périmètre **sans lire le Python**.
> Les regex, constantes et listes de mots sont recopiées **verbatim** : elles
> doivent être portées telles quelles en C# (voir les remarques de portage en §3.0).

---

## Sommaire

1. [Inventaire](#1-inventaire)
2. [Détail par fonction](#2-détail-par-fonction)
3. [Algorithmes à porter](#3-algorithmes-à-porter)
4. [Intégrations externes](#4-intégrations-externes)
5. [Données](#5-données)
6. [Points d'attention pour la version web / serveur Linux](#6-points-dattention-pour-la-version-web--serveur-linux)

---

## 1. Inventaire

### 1.1 Fichiers du périmètre

| Fichier | Lignes | Rôle |
|---|---|---|
| `deekeep/ui/organize_page.py` | 2 321 | Page « Organiser la bibliothèque » : analyse, arborescence à cases, options, calcul du plan (`build_plan`), application (`OrganizeApplyThread`), suppression des dossiers vides, signalement des fichiers non tagués, « Ne garder qu'un exemplaire », lancement de « Compléter les tags » et de « Rechercher les doublons ». Contient aussi la logique métier pure (index d'artistes, albums préservés, choix du meilleur exemplaire). |
| `deekeep/library/library_rules.py` | 415 | Règles de rangement **partagées** par l'écran Organiser et par toutes les extractions (téléchargement, retéléchargement Qualité) : nom de dossier sûr, artiste principal, « Divers », décompte `Tally`, destination d'un fichier extrait. |
| `deekeep/library/name_pattern.py` | 246 | Balises de nommage `%titre%`…, alias historiques, menu « Insérer une balise », motifs prédéfinis, formatage et « recollage » d'un nom. |
| `deekeep/ui/dedup_dialog.py` | 571 | Rapprochement approximatif des fichiers (union-find), écran **fichier/fichier** (`DeduplicationDialog`) et écran **piste Deezer/fichiers** (`LibraryDuplicateDialog`). |
| `deekeep/ui/tag_lookup_dialog.py` | 491 | Écran de revue « Compléter les tags » (propositions, cases, correction des tags contredits, arrêt). |
| `deekeep/audio/tag_lookup.py` | 950 | Moteur `find_tags` : requêtes Deezer, MusicBrainz, AcoustID ; notation des candidats ; second appel `/track` et `/album` ; cache d'albums ; thread de recherche. |
| `deekeep/audio/acoustid_lookup.py` | 206 | Empreinte Chromaprint (fpcalc ou ffmpeg) et appel de l'API AcoustID. |
| `deekeep/audio/tag_write.py` | 211 | Écriture des tags (ID3 / FLAC Vorbis / interface « easy » MP4, OGG, WMA…), jaquette. |

Dépendances hors périmètre lues pour comprendre (décrites ici seulement dans ce qui touche le périmètre) :
`deekeep/audio/reencode_scan.py` (lecture des tags, parcours disque, cache),
`deekeep/audio/audio_probe.py` (durée hors silence final via ffmpeg),
`deekeep/core/utils.py` (`sanitize_filename`), `deekeep/core/settings.py` (valeurs par défaut),
`deekeep/library/library_index.py` (objet `Match` de l'écran piste/fichiers),
`deekeep/ui/add_track_dialog.py` (appelant de `LibraryDuplicateDialog`),
`deekeep/ui/main_window.py` (intégration de la page, paramètres dupliqués),
`deekeep/workers/download_thread.py` (consommateur de `library_rules`).

### 1.2 Fonctionnalités utilisateur

| # | Fonctionnalité | Point d'entrée | Écrit sur le disque ? |
|---|---|---|---|
| F1 | Choisir le dossier analysé | bouton « Modifier le dossier » | non |
| F2 | Analyser (lecture des tags, arbre au fil de l'eau) | bouton « Analyser » | non |
| F3 | Régler les options de rangement (volet « Filtres ») | cases, motif, seuil Divers | paramètres seulement |
| F4 | Motif de nommage + menu « Insérer une balise » + aperçu | volet Filtres | non |
| F5 | Calcul du plan de rangement (déplacer / renommer / déjà en place / album préservé / doublon / non tagué) | automatique après analyse et à chaque option | non |
| F6 | Cases à cocher (fichiers, dossiers, tri-état), « Tout cocher », « Tout décocher », « Cocher ce qui est à faire » | arbre + boutons | non |
| F7 | Filtre d'affichage « Masquer ce qui est déjà en place », « Tout déplier / replier » | case + bouton | non |
| F8 | Appliquer le plan (déplacements, renommages, suppression des doublons sûrs, suppression des dossiers vides) | bouton « Organiser » | **oui** (move, rename, delete, rmdir) |
| F9 | Rapport des fichiers non tagués | option « Signaler les fichiers sans tags » | non |
| F10 | « Ne garder qu'un exemplaire » (doublons certains) | option du plan | **oui** (delete à l'application) |
| F11 | Compléter les tags (recherche en ligne + revue + écriture) | bouton « Compléter les tags » | **oui** (tags) |
| F12 | Rechercher les doublons (approximatif, écran fichier/fichier) | bouton « Rechercher les doublons » | **oui** (delete) |
| F13 | Doublons piste Deezer ↔ fichiers (écran de la recherche) | page Musique / fenêtre de recherche, bouton « Dédoublonner… » | **oui** (delete) |
| F14 | Écouter un fichier de l'arbre / ouvrir son dossier | double-clic, menu contextuel | non |
| F15 | Règles de rangement appliquées aux extractions (`library_rules.destination`, `Tally`) | téléchargement, retéléchargement | **oui** (dossier/nom du fichier produit) |

---

## 2. Détail par fonction

### 2.0 Disposition générale de la page « Organiser »

Page de la fenêtre principale (pas une modale) : l'utilisateur peut naviguer
ailleurs pendant une analyse (`organize_page.py:9-11`). De haut en bas
(`_build_ui`, `organize_page.py:811-1172`) :

1. Titre « Organiser la bibliothèque » + sous-titre :
   « Range les fichiers déjà présents selon leurs tags, comme le fait le téléchargement, en réunissant le plus de titres possible sous un même artiste. L'analyse ne modifie rien : le plan ci-dessous n'est appliqué qu'après validation, dossier par dossier ou fichier par fichier. »
2. Ligne dossier : « Dossier analysé : {root} » (infobulle = chemin), bouton bascule « Filtres ▾ / Filtres ▴ », bouton « Modifier le dossier ».
3. Résumé des filtres actifs (visible **seulement volet replié**) : « Filtres : » + énumération (voir F3).
4. Volet des filtres (carte) : grille 3 colonnes de cases, ligne motif de nommage, aperçu, ligne « Divers », ligne « Cocher ce qui est à faire ».
5. Barre d'actions : à gauche « Tout cocher », « Tout décocher », « Tout déplier » ; à droite « Compléter les tags », « Rechercher les doublons », « Analyser » (style accent), « Organiser » (style primaire, seul bouton plein).
6. Ligne d'état (`lbl_status`) + case « Masquer ce qui est déjà en place » à droite.
7. Barre de progression (masquée hors traitement).
8. Arborescence 4 colonnes : « DOSSIER / FICHIER », « ARTISTE », « ALBUM », « ACTION ».

Couleurs d'état de la colonne « Action » (`organize_page.py:115-120`) :

| Constante | Couleur | Usage |
|---|---|---|
| `COLOR_MOVE` | `#00e5ff` | déplacement / renommage |
| `COLOR_INPLACE` | `#5f7d84` | déjà en place, album conservé (texte du nom aussi) |
| `COLOR_WARN` | `#ffcf7a` | tag manquant |
| `COLOR_DIR` | `#bac9cc` | lignes de dossier |
| `COLOR_ALBUM` | `#8fb7c4` | dossier d'album préservé |
| `COLOR_DUPLICATE` | `#ff8a80` | doublon à supprimer |

États internes de la page : `_scanning` (analyse), `_applying` (rangement) ;
`_busy() = _scanning or _applying` bloque Analyser, Organiser, Compléter les tags,
Rechercher les doublons et le changement de dossier (`organize_page.py:1404-1411`).

### F1 — Dossier analysé

- Valeur initiale : le dossier de dépôt de l'application (`download_dir`, `main_window.py:1185-1189`). Quand le dossier de dépôt change dans les Paramètres, la page suit (`set_root`, `main_window.py:3260`).
- « Modifier le dossier » : refusé si `_busy()`. Sélecteur de dossier (titre « Choisir le dossier à organiser », départ = dossier courant).
- `set_root(root)` (`organize_page.py:1384-1390`) : si le chemin est le même (comparaison insensible à la casse, chemins absolus) → rien. Sinon : nouveau libellé et **plan entièrement vidé** avec le message « Dossier modifié : relancez l'analyse. »
- Le dossier choisi **n'est pas persisté** comme réglage propre de la page (il n'est pas dans les clés `org_*`).

### F2 — Analyse

Déclenchement : bouton « Analyser » (`_start_scan`, `organize_page.py:1413-1439`).

1. Refus si `_busy()`.
2. Si le dossier n'existe pas : boîte « Dossier introuvable » — « Le dossier suivant n'existe pas :\n{root} ».
3. Annule un recalcul de plan différé en attente, vide le plan (« Analyse en cours… »), barre de progression **indéterminée**.
4. Lance le parcours (`LocalScanThread([root], need_bitrate=False)`) : le **débit n'est pas lu** (gain ×2 sur l'analyse, il ne sert pas au rangement).
5. Événements :
   - `total(n)` : la barre devient déterminée 0..n ; état « Analyse en cours… {n} fichier(s) à lire ».
   - `batch(entries)` : lots de **200** fichiers (`reencode_scan.py:338`) ajoutés à l'arbre (cases cochées par défaut, pas de recalcul des dossiers pendant le remplissage).
   - `progress(count)` : « Analyse en cours… {count} / {total} fichiers lus ».
   - `scanned(entries)` : fin. Liste finale (fait foi, même partielle en cas d'arrêt). Si vide → « Aucun fichier audio trouvé dans ce dossier. » ; sinon aperçu du motif rafraîchi puis **calcul du plan** (F5).

Parcours disque (`reencode_scan.iter_audio_files`, `reencode_scan.py:290-316`) :
- récursif, pile ; ne suit pas les liens symboliques de dossiers (`is_dir(follow_symlinks=False)`) ;
- extensions retenues (comparaison en minuscules, suffixe) :
  `AUDIO_EXTENSIONS = (".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus", ".wma")` (`reencode_scan.py:34-36`) ;
- ordre : dans chaque dossier, fichiers triés par nom en minuscules, puis sous-dossiers triés (parcours en profondeur, ordre alphabétique insensible à la casse) ;
- erreurs d'accès ignorées silencieusement.

Lecture des tags : pool de **8** lectures simultanées (`SCAN_WORKERS = 8`), ordre des résultats = ordre du parcours. Structure d'une **entrée** : voir §5.1. Points métier essentiels :
- si le fichier n'a pas de titre, `title` = nom du fichier sans extension et `title_tagged = false` (`reencode_scan.py:269-271`) ;
- `format` = extension en majuscules sans point (`MP3`, `FLAC`…) ;
- `track`/`tracktotal` et `disc`/`disctotal` sont découpés depuis « 3/12 » ;
- `date` = TDRC, sinon TYER (MP3) ; `date`, `year`, `originaldate` (Vorbis) ;
- cache mémoire des tags par chemin, invalidé par (mtime ns, taille) ; vidé en bloc après rangement / suppression / écriture de tags (`clear_tag_cache`).

Arborescence construite pendant l'analyse (`_dir_item`, `_add_file_item`, `organize_page.py:1484-1541`) :
- la racine visible est le dossier analysé (texte = chemin complet), dépliée ;
- les dossiers intermédiaires sont créés de proche en proche ; un chemin hors racine est rattaché à la racine ;
- ligne fichier : colonnes nom de fichier, artiste (tag brut), album (tag brut), action vide ; **case cochée à la naissance** ; le chemin complet est porté par la ligne (c'est ce qui distingue une ligne fichier d'une ligne dossier).

### F3 — Options de rangement (volet « Filtres »)

| Case / contrôle | Clé de réglage | Défaut | Effet |
|---|---|---|---|
| « Sous-dossier par Artiste » | `org_artist` | vrai | dossier `Artiste` |
| « Sous-dossier par Album » | `org_album` | vrai | dossier `Album` (sous l'artiste si coché) |
| « Renommer les fichiers » | `org_rename` | faux | applique le motif |
| Motif (liste éditable) | `org_pattern` | `%titre% - %artiste%` | voir F4 |
| « Préserver musiques de film et compilations » | `org_keep_albums` | vrai | albums à part entière laissés intacts |
| « Ne garder qu'un exemplaire » | `org_dedup` | vrai | doublons certains supprimés |
| « Supprimer les dossiers vides » | `org_remove_empty` | vrai | après application |
| « Signaler les fichiers sans tags » | `org_report_untagged` | vrai | affichage des non tagués |
| « Regrouper dans « Divers » les artistes de moins de » + compteur | `org_divers`, `org_divers_min` | faux, 3 | règle Divers |
| bouton « Filtres » déplié | `org_filters_open` | faux | état du volet |

Disposition de la grille (`organize_page.py:911-915`) : ligne 1 Artiste / Album / Renommer ; ligne 2 Préserver / Un exemplaire / Dossiers vides ; ligne 3 Signaler.

Compteur Divers : plage **2 à 10**, suffixe « titres » ; mention « (option enregistrée dans les Paramètres) ». Les cases Artiste, Album, Divers et le seuil existent **aussi** dans la page Paramètres (`main_window.py:1098-1133`) et partagent les mêmes clés : la page Organiser relit les réglages **à chaque affichage** (`sync_from_settings`, `main_window.py:1326`). Les anciens noms de balises du motif enregistré sont réécrits à l'affichage (`name_pattern.modernize`).

Infobulles verbatim à reprendre :
- Préserver : « Un dossier dont le nom est celui de l'album de ses fichiers (« Footloose », « Dirty Dancing », « Zen 2 ») est laissé intact dès que cet album est une musique de film, ou qu'il réunit au moins trois artistes dont aucun ne domine : le ranger par artiste l'éparpillerait en autant de dossiers d'un seul titre. Un dossier de genre ou un fourre-tout (« CD1 », « Various »), dont les albums diffèrent, reste rangé par artiste — de même que le best-of d'un artiste unique, invités compris. »
- Un exemplaire : « Quand plusieurs copies d'un même titre visent le même emplacement, une seule est conservée — le meilleur débit, puis le fichier le plus complet — et rangée selon les règles habituelles (dossier de l'artiste, ou « Divers » si son artiste n'atteint pas le seuil). Les autres sont supprimées du disque. Les albums préservés ne sont pas concernés : un titre peut figurer à la fois dans une bande originale et chez son artiste. »
- Divers : « Le décompte porte sur l'artiste principal : « Inna » et « Inna Feat. Flo Rida » comptent — et se rangent — ensemble. La règle joue dans les deux sens : le dossier « Divers » est analysé comme le reste de la bibliothèque, et un titre qui s'y trouve en ressort dès que son artiste atteint le seuil, en comptant les titres que contient déjà son dossier. »

Règles d'activation (`_refresh_option_widgets`, `organize_page.py:1302-1317`) :
- la ligne motif + aperçu reste **visible** mais grisée si « Renommer » est décoché ;
- « Divers » grisé si « Sous-dossier par Artiste » est décoché ; le compteur grisé sauf si Artiste **et** Divers cochés.

Résumé volet replié (`_filters_summary`, `organize_page.py:1209-1230`), fragments joints par « · » ; « aucun » si vide :
`dossier par artiste`, `dossier par album`, `renommage « {motif} »`, `« Divers » sous {N} titres` (seulement si Divers **et** Artiste), `musiques de film et compilations préservées`, `un seul exemplaire des doublons`, `dossiers vides supprimés`, `fichiers sans tags signalés`.

Comportement au changement d'option (`organize_page.py:1365-1375`) :
- l'aperçu se met à jour immédiatement ;
- un minuteur **mono-coup de 350 ms** (relancé à chaque frappe) enregistre les réglages **puis** recalcule le plan à partir des entrées déjà lues (aucune relecture disque) ;
- **les cases cochées à la main ne sont pas touchées** par un recalcul (voir F6).

### F4 — Motif de nommage, menu des balises, aperçu

- Liste éditable proposant `name_pattern.PRESETS` (voir §3.2), saisie libre possible, largeur minimale ~24 caractères.
- Infobulle : « Motif de nommage. Les balises %…% sont remplacées par les tags du fichier ; une balise vide disparaît du nom. Les balises « _00 » forcent deux chiffres : %piste_00% donne 07. »
- Bouton « Insérer une balise ▾ » : menu hiérarchique catégorie → balise (`TAG_CATEGORIES`), chaque entrée affichée « libellé⇥balise » ; l'insertion se fait **à la position du curseur** du champ, puis focus sur le champ.
- Aperçu (`_refresh_preview`, `organize_page.py:1319-1363`), par ordre de priorité :
  1. renommage désactivé : « Renommage désactivé : les fichiers gardent leur nom actuel. Cochez « Renommer les fichiers » pour appliquer ce modèle. »
  2. balises inconnues (§3.2 `unknown_tokens`) : « Balise inconnue : %x%, %y% — elle sera recopiée telle quelle. » (couleur `#ffcf7a`)
  3. pas encore d'analyse : « Aperçu disponible après l'analyse. Exemple de balises : %titre%, %artiste%, %album%, %piste_00%, %annee%. »
  4. sur le **premier fichier analysé** : si un tag requis manque (`missing_required_tags`) : « Aperçu : {nom actuel}  →  inchangé ({titre} absent des tags : fichier signalé) » ; sinon « Aperçu : {nom actuel}  →  {nouveau nom + extension} » ou « (nom vide : fichier signalé) ».

### F5 — Calcul du plan de rangement

Fonction pure `build_plan(entries, root, options)` (`organize_page.py:483-663`), algorithme détaillé en §3.4. Résultat : une ligne par entrée, avec un **statut** :

| Statut | Signification | Texte de la colonne « Action » | Couleur |
|---|---|---|---|
| `move` | à déplacer et/ou renommer | si même dossier : « Renommer  →  {nouveau nom} » ; sinon chemin de destination relatif à la racine (absolu si hors racine). Infobulle : destination absolue | `#00e5ff` |
| `inplace` | déjà exactement au bon endroit | « Déjà en place » (nom de fichier grisé aussi) | `#5f7d84` |
| `album` | appartient à un album préservé | « Album conservé » ; la ligne **dossier parent** reçoit « Musique de film / compilation — laissée intacte » (couleur `#8fb7c4`) si elle n'a pas déjà un texte | `#5f7d84` |
| `duplicate` | copie en trop d'un titre, à supprimer | « Doublon à supprimer  ·  exemplaire gardé : {emplacement FINAL de l'exemplaire gardé, relatif} » ; infobulle « Ce fichier sera supprimé du disque.\nExemplaire conservé :\n{chemin} » | `#ff8a80` |
| `untagged` | tag indispensable manquant | « Tag manquant : {liste} » ; infobulle « Complétez les tags de ce fichier pour pouvoir le ranger. » | `#ffcf7a` |

« Emplacement final » de l'exemplaire gardé : destination du plan pour ce fichier s'il en a une (`dst`), sinon son chemin actuel (`_final_path`, `organize_page.py:1617-1622`).

Ligne d'état après calcul (`organize_page.py:1839-1856`), fragments joints par « · » (les optionnels seulement si > 0) :
`{n} fichiers analysés` · `{move} à ranger` · `{inplace} déjà en place` · `{duplicate} doublon(s) à supprimer` · `{divers} vers « Divers »` · `{from_divers} sortent de « Divers »` · `{album} dans {album_dirs} album(s) préservé(s)` · `{untagged} sans tags exploitables`.

Boutons après calcul : « Organiser » actif s'il existe au moins une ligne `move` ou `duplicate` (indépendamment des cases) ; « Cocher ce qui est à faire » actif ; « Compléter les tags » actif s'il reste au moins un fichier coché à compléter (F11) ; « Rechercher les doublons » actif si ≥ 2 entrées.

### F6 — Cases à cocher

Principe (`organize_page.py:1515-1528`, `1636-1708`, `1915-2006`) : **toute** ligne fichier porte une case, même « déjà en place ». La case désigne la ligne pour **toutes** les actions de l'écran (application du plan, recherche des tags) ; décocher un dossier l'écarte de tout.

- Case de dossier : calculée à partir des **seuls fichiers** de sa branche (pas des sous-dossiers comme tels) : cochée si tous cochés, décochée si aucun, **partielle** sinon ; un dossier sans fichier n'a **pas de case**.
- Cocher/décocher un dossier propage l'état à tous les fichiers de la branche (l'état partiel ne se propage pas) ; les ancêtres sont recalculés.
- **Mémoire des cases touchées** (`_touched_checks`) : tout fichier dont la case est posée par l'utilisateur (directement ou via son dossier, ou via Tout cocher / Tout décocher) est mémorisé ; un recalcul du plan **ne modifie plus** sa case. La mémoire est vidée à chaque nouvelle analyse, au changement de dossier, et par « Cocher ce qui est à faire ».
- Sélection par défaut posée après chaque calcul de plan, pour les fichiers non touchés (`_default_checked`, `organize_page.py:1956-1969`) :
  - `move`, `duplicate`, `untagged` → **cochés** ;
  - `inplace`, `album` → cochés **seulement si** `needs_extras(entry)` (il manque albumartist, genre, date ou track — §3.6.1), sinon décochés.
- « Tout cocher » / « Tout décocher » : tous les fichiers analysés, marqués comme touchés.
- « Cocher ce qui est à faire » (dans le volet) : oublie les cases touchées et repose la sélection par défaut. Libellé annexe : « décoche les fichiers déjà en place dont les tags sont complets ».
- Après tout changement de case, l'activation de « Compléter les tags » est recalculée.

### F7 — Affichage

- « Masquer ce qui est déjà en place » (`_apply_visibility`, `organize_page.py:1765-1787`) : masque les fichiers de statut `inplace` et `album`, puis tout dossier dont aucun descendant n'est visible. Les fichiers sans statut (non tagués non signalés, voir F9) restent visibles. Ce filtre n'est **pas** persisté.
- « Tout déplier » ↔ « Tout replier » : bouton bascule ; replier laisse la racine dépliée. Réinitialisé à « Tout déplier » quand le plan est vidé.

### F8 — Appliquer le plan (bouton « Organiser »)

`_confirm_apply` (`organize_page.py:2211-2256`) puis `OrganizeApplyThread` (`organize_page.py:686-752`).

1. Refus si `_busy()`.
2. Opérations = lignes `move` et `duplicate` **cochées**. Aucune → « Aucune sélection » / « Cochez au moins un fichier à ranger. »
3. Confirmation (bouton par défaut : **Non**) :
   « Déplacer {nb move} fichier(s) dans :\n{root} » + si doublons « \nSupprimer définitivement {nb} doublon(s) du disque, dont un autre exemplaire est conservé. » + si dossiers vides « \nLes dossiers devenus vides seront supprimés. »
4. Pendant l'application : tous les contrôles de la page désactivés, progression 0..N, état « Rangement… {n} — {nom du fichier} ».
5. Ordre d'exécution : **toutes les suppressions de doublons d'abord**, puis les déplacements (tri stable : `duplicate` = 0, autres = 1). Raison : l'exemplaire conservé vise parfois l'emplacement qu'occupe encore un doublon.
6. Suppression : `os.remove` — **définitive** (pas de corbeille). Erreur → échec consigné.
7. Déplacement `_move(src, dst)` :
   - si `src` et `dst` désignent le même emplacement (comparaison insensible à la casse) : si les chemins sont strictement identiques → succès sans rien faire ; sinon (changement de **casse** seul) → simple `rename` ;
   - sinon si `dst` existe déjà → échec « un fichier porte déjà ce nom à destination » (**jamais d'écrasement**) ;
   - sinon création des dossiers intermédiaires puis déplacement **inter-volumes possible** (copie + suppression si besoin).
8. Puis, si l'option est cochée, **suppression des dossiers vides** sous la racine (§3.9).
9. Bilan (boîte « Rangement terminé ») :
   « {déplacés} fichier(s) rangé(s) sur {à déplacer}. » ; « {supprimés} doublon(s) supprimé(s) sur {à supprimer}. » (si > 0) ; « {k} dossier(s) vide(s) supprimé(s). » (si > 0) ; « \n{e} échec(s) : » puis au plus **10** lignes « • {nom} — {message} » et « … et {e-10} autre(s). ».
10. Cache de tags vidé, plan **vidé** (« Rangement terminé : relancez l'analyse pour vérifier. ») — aucune relance automatique.
11. Si au moins un fichier a été déplacé ou supprimé : signal `libraryChanged` → la fenêtre principale relit l'index du dossier de dépôt (pastilles de présence) (`main_window.py:1193`).

Fermeture de l'application pendant une application : demande « Un rangement de la bibliothèque est en cours. Quitter maintenant attendra la fin du fichier en cours de déplacement. Continuer ? » ; l'arrêt attend jusqu'à 10 s la fin de l'opération en cours (`organize_page.py:2309-2321`, `main_window.py:551-562`). Il n'y a **pas** de bouton d'annulation du rangement.

### F9 — Rapport des fichiers non tagués

- Le statut `untagged` est **toujours** calculé et compté (« {n} sans tags exploitables ») ; l'option « Signaler les fichiers sans tags » ne gouverne que l'annotation (texte « Tag manquant : … »). Option décochée : la ligne reste sans texte d'action, sans couleur, visible même avec « Masquer ce qui est déjà en place », **cochée par défaut**, et reste candidate à « Compléter les tags ».
- Libellés possibles de la liste des manques (ordre) : `artiste` ; `titre` (motif de renommage réclamant `%titre%` alors que le titre ne vient que du nom de fichier) ; `album` ; `motif de nommage sans valeur` (seulement si rien d'autre ne manque).
- Un fichier non tagué n'est **jamais** déplacé ni renommé, et ne sert **jamais** de référence à la déduplication.

### F10 — « Ne garder qu'un exemplaire »

Doublons **certains** uniquement : fichiers que le plan enverrait sur le **même fichier de destination** (même artiste principal, même album si l'option Album est active, même nom de fichier visé, casse ignorée). Le meilleur exemplaire est gardé (débit, puis taille… voir §3.5), les autres passent en `duplicate`. Exclus : fichiers d'albums préservés, fichiers bloqués (non rangeables, y compris album manquant quand l'option Album est active). Les doublons sont retirés **avant** le décompte des artistes (un titre en trois exemplaires ne vaut pas trois titres pour la règle Divers).

### F11 — Compléter les tags

**Sélection des fichiers** (`_lookup_entries`, `organize_page.py:2089-2115`) : dans l'ordre de l'analyse, les fichiers **cochés** (un fichier sans ligne encore affichée est tenu pour coché) qui sont :
- soit `untagged` dans le plan courant (quel que soit l'affichage) ;
- soit `needs_extras(entry)` (il manque `albumartist`, `genre`, `date` ou `track`).

Le bouton est actif seulement si cette liste est non vide. S'il est cliqué sans rien à faire : « Rien à compléter » — « Aucun fichier coché ne manque de tag : ils portent tous leur titre, leur artiste et leur album, ainsi que leur artiste d'album, leur genre, leur année et leur numéro de piste. »

**Avertissement de durée** : au-delà de `TAGS_LOOKUP_WARN = 50` fichiers, confirmation (défaut Oui) annonçant `max(1, round(n × SECONDS_PER_LOOKUP / 60))` minute(s), avec `SECONDS_PER_LOOKUP = 1.2` (`organize_page.py:105-112`, `2154-2171`). Texte : « {n} fichiers cochés n'ont pas tous leurs tags.\n\nLe catalogue est interrogé fichier par fichier : comptez de l'ordre de {m} minute(s). La recherche s'interrompt à tout moment — les correspondances déjà trouvées restent écrivables, les fichiers non encore interrogés sont laissés tels quels et un passage suivant les reprendra. Rien n'est écrit sans validation.\n\nLancer la recherche ? »

**Écran de revue** « Compléter les tags » (`tag_lookup_dialog.py`) — modal, ~80 % de l'écran, min 1000×560 :
- Titre « Tags manquants » ; texte d'explication (`tag_lookup_dialog.py:85-99`) commençant par « {n} fichier(s) ne portent pas tous leurs tags : … ».
- Ligne d'état AcoustID (calculée à l'ouverture, §4.4) : « Empreinte acoustique disponible en dernier recours — empreinte calculée par fpcalc|ffmpeg (muxer chromaprint) » (gris `#849396`) ou « Empreinte acoustique indisponible — {raison} » (orange `#ffcf7a`).
- État « Recherche en cours… {k} / {n} fichiers », barre de progression.
- Tableau 7 colonnes : « ÉCRIRE », « FICHIER », « TAGS ACTUELS », « TAGS TROUVÉS », « CHAMPS COMBLÉS », « SOURCE », « CORRESPONDANCE ». Une ligne est ajoutée **pour chaque fichier examiné**, dans l'ordre, au fil de l'eau.
  - FICHIER : chemin relatif à la racine (absolu si hors racine) ; infobulle chemin complet.
  - TAGS ACTUELS : titre (seulement si `title_tagged`), artiste, album joints par « · », ou « (aucun) ».
  - Sans proposition : pas de case ; « aucune correspondance trouvée » ; « — » ; SOURCE = « Deezer + MusicBrainz » (+ « + AcoustID » si disponible) si un tag **indispensable** manquait, sinon « Deezer » ; CORRESPONDANCE = « tags, nom de fichier et dossier trop pauvres » (indispensable manquant) ou « le catalogue ne reconnaît pas cet enregistrement ». Couleur `#5f7d84`.
  - Avec proposition : case cochée **si et seulement si** `sure` ; TAGS TROUVÉS « {titre} · {artiste} · {album} » (couleur `#00e5ff` si sûre, `#ffcf7a` si probable), infobulle « Titre : …\nArtiste : …\nAlbum : …\nDurée du fichier : {d} s — durée trouvée : {s} s\nSource : …\nRecherche : {requête} » ; SOURCE = `Deezer` / `MusicBrainz` / `AcoustID` ; CORRESPONDANCE = « sûre|probable ({score:.2f}) — {raison} ».
  - CHAMPS COMBLÉS : libellés des champs `fills` joints par « · » ; pour les tags complémentaires la valeur est montrée (« genre = Pop ») ; libellés : `title`→titre, `artist`→artiste, `album`→album, `albumartist`→artiste album, `genre`→genre, `date`→année, `track`→piste, `tracktotal`→total pistes, `disc`→disque. S'il y a des champs contredits (`corrects`) : « ⟳ {liste} » si l'option de correction est cochée (couleur orange), sinon « (⟳ {liste} ignoré) » ; groupes séparés par **trois espaces**. Infobulle : « Tags écrits : » puis « • … » par ligne, et « Tags corrigés : … » ou « La correspondance contredit : …\nCochez « Corriger aussi les tags… » pour les remplacer. »
- Case « Corriger aussi les tags que la correspondance contredit » (décochée par défaut) — infobulle : « Par défaut, seuls les champs vides sont renseignés. Cochez pour remplacer aussi les tags qui contredisent la correspondance retenue — un artiste erroné, par exemple. Les champs concernés sont signalés « ⟳ » dans le tableau. »
- Boutons : « Tout cocher », « Tout décocher », « Arrêter la recherche », « Annuler », « Écrire les tags cochés » (primaire, actif **dès la première proposition**).
- « Arrêter la recherche » : désactive le bouton, demande l'arrêt au fil (effectif **après le fichier en cours**), état « Arrêt de la recherche… {k} / {n} fichiers examinés » ; l'écran reste ouvert. La progression n'écrase plus ce libellé.
- Fin : barre et bouton d'arrêt masqués ; état « {sûres} correspondance(s) sûre(s) · {probables} probable(s) · {examinés − propositions} sans réponse » + « · {non examinés} non examiné(s) — recherche arrêtée » s'il y en a.
- « Écrire les tags cochés » : si rien de coché → « Cochez au moins une correspondance à écrire. » ; sinon confirmation (défaut **Non**) : « Écrire {nb champs} tag(s) dans {nb fichiers} fichier(s) ? » + « \nLa recherche n'a pas parcouru toute la liste : {k} fichier(s) n'ont pas été interrogés et resteront tels quels. » si arrêt + soit « \nDont {c} tag(s) existant(s) remplacé(s) par une valeur différente — l'ancienne valeur sera perdue. » soit « \nSeuls des champs vides sont renseignés ; aucun tag existant n'est remplacé. ». Oui → la recherche est arrêtée (attente ≤ 3 s) et l'écran se ferme en validant.
- Annuler / fermeture : arrêt de la recherche (attente ≤ 3 s), rien n'est écrit.

**Valeurs retenues** (`get_accepted`, `tag_lookup_dialog.py:422-436`) : pour chaque ligne cochée, `values` de la proposition (champs vides comblés) + `replaces` si l'option de correction est cochée.

**Écriture** (de retour dans la page, `organize_page.py:2178-2200`) : `tag_write.write(path, values)` par fichier (§3.8) ; bilan « {écrits} fichier(s) complété(s) sur {acceptés}. » + échecs (10 max, « … et {k} autre(s). »). Si au moins un fichier écrit : cache de tags vidé et **nouvelle analyse automatique**. Aucune jaquette n'est écrite par ce flux.

Le moteur de recherche (`find_tags`) est décrit en §3.6.

### F12 — Rechercher les doublons (fichier/fichier)

`_find_duplicates` (`organize_page.py:2011-2046`) :
1. Si analyse/rangement en cours : « Analyse en cours » — « Attendez la fin de l'analyse avant de chercher les doublons. » ; si pas d'analyse : « Lancez d'abord l'analyse du dossier. »
2. Index d'artistes du plan (`ArtistIndex`, §3.3) ; groupes = `duplicate_groups(entries, artist_key=index.entry_key)` (§3.7) — **tous** les fichiers analysés (cases ignorées).
3. Aucun groupe : « Aucun fichier ne semble être la copie d'un autre. »
4. Lecture du **débit** des seuls fichiers des groupes (non lu à l'analyse).
5. Écran `DeduplicationDialog(groups, target_format = réglage file_format)` :
   - titre « Fichiers en double » ; texte « {n} fichiers dans {g} groupe(s). Cochez ceux à supprimer. Par défaut, chaque groupe conserve (décoché) l'exemplaire au format cible ({FMT}), puis celui rangé dans le dossier de l'artiste, puis l'emplacement le moins profond, puis le meilleur débit. » ;
   - tableau 6 colonnes « SUPPR. », « GROUPE », « TITRE », « ARTISTE », « FORMAT / DÉBIT », « FICHIER » ; une ligne par fichier, groupes numérotés à partir de 1 ; l'exemplaire gardé porte « {n} ★ » (infobulle « Exemplaire conservé par défaut (meilleure qualité) ») ; **toutes les autres lignes cochées** ; FORMAT / DÉBIT = « MP3 · 320 kbps » ou format seul (« ? » si inconnu) ;
   - choix de l'exemplaire gardé : §3.7.2 (critères **différents** de F10 !) ;
   - boutons « Annuler », « Supprimer les fichiers cochés » (style danger) → si rien : « Cochez au moins un fichier à supprimer. » ; sinon confirmation (défaut Non) « Supprimer définitivement {n} fichier(s) du disque ?\nCette action est irréversible. ».
6. Suppression (`_delete_duplicates`, `organize_page.py:2048-2084`) : `os.remove` si le fichier existe (un fichier déjà absent compte comme supprimé) ; les entrées supprimées sont retirées de l'analyse et de l'arbre, cache vidé, **plan recalculé** sans relecture ; bilan « {n} fichier(s) supprimé(s). » + « \n\nErreurs :\n » (10 max).

### F13 — Doublons piste Deezer ↔ fichiers du disque (`LibraryDuplicateDialog`)

Appelé depuis la fenêtre de recherche (`add_track_dialog.py:1183-1251`) par « Dédoublonner… ». Entrée : liste `{track, match}` pour chaque piste affichée dont la pastille est **verte** (`present`) ou **orange** (`similar`) et dont `match.paths` est non vide. `match` = `Match(state, reason, paths)` produit par `library_index` (hors périmètre ; `reason` est un texte déjà formulé).

- Construction des groupes (`dedup_dialog.py:468-485`) : un groupe par piste ; un fichier (clé = chemin normalisé, casse ignorée) n'est montré **que sous la première piste** qui le cite ; piste sans fichier restant → pas de groupe.
- Écran « Doublons de la bibliothèque » (~80 %×75 %, min 900×520) ; texte « {g} piste(s) de la recherche répondent à {n} fichier(s) déjà sur le disque. Cochez les fichiers à supprimer : la piste redevient alors sélectionnable dans la recherche, prête à être extraite de nouveau. Les pistes dont un seul fichier répond ne sont pas cochées d'avance — une ressemblance n'est pas une preuve. »
- Arbre 3 colonnes « PISTE DEEZER / FICHIERS DU DISQUE », « QUALITÉ », « MOTIF / EMPLACEMENT » :
  - ligne piste (non cochable, dépliée) : « {glyphe}  {titre} » (glyphes ✓ vert `#5eeb7f`, ≈ orange `#ffcf7a`), « {artiste} — {album} », `match.reason` ;
  - lignes fichiers : nom, qualité (« MP3 · 320 kbps · 8,4 Mo », ou « fichier introuvable »), dossier ; infobulle chemin.
- Pré-cochage : **un seul fichier** → décoché ; **plusieurs** → tous cochés sauf le meilleur (`file_score`, §3.7.3), dont l'infobulle qualité dit « Meilleur exemplaire du groupe : conservé par défaut ».
- Boutons « Tout cocher », « Tout décocher », « Fermer », « Supprimer les {n} fichier(s) cochés » (danger, désactivé si 0, libellé recalculé à chaque case). Confirmation identique à F12.
- Suppression par l'appelant : `os.remove` ; chemins effectivement disparus retirés de l'index (`LibraryIndex.remove`) **sans relire le dossier** ; la fenêtre principale est prévenue (`on_library_files_deleted`) ; pastilles recalculées ; bilan « {n} fichier(s) supprimé(s). » + erreurs.

### F14 — Écoute et dossier depuis l'arbre

- Double-clic sur une ligne fichier, ou menu contextuel « Écouter dans DeeKeep » (grisé sur un dossier) : ouvre le lecteur commun ; la **file d'attente** = fichiers **visibles** du même dossier parent, dans l'ordre de l'arbre, en démarrant sur le fichier cliqué (titre = nom de fichier, artiste/album = colonnes, sous-titre = chemin) (`organize_page.py:1731-1758`).
- « Ouvrir le dossier » : ouvre l'explorateur Windows sur le fichier sélectionné (sur une ligne dossier, l'action ne fait rien — chemin absent).

### F15 — Règles de rangement des extractions (`library_rules`)

- `rules_from_settings(settings)` (`library_rules.py:129-144`) lit les clés `org_*` : `artist` (défaut vrai), `album` (vrai), `rename` (faux), `pattern` (défaut `%titre% - %artiste%`), `divers` (faux), `divers_min = max(1, int(org_divers_min or 3))`.
- `download_pattern(rules)` : motif de l'utilisateur **seulement si** `rename` ; sinon la convention historique `%titre% - %artiste%` (un fichier extrait doit bien recevoir un nom).
- `destination(root, tags, rules, ext, exclude, tally)` = `target_dir(...)` / `target_stem(...)` + extension (§3.4.6).
- Un objet `Tally` est partagé par tous les fils d'un **lot** d'extraction ; chaque titre mis en file est **réservé** (`tally.reserve(artiste)`) avant tout démarrage (`main_window.py:3344`). Un lot neuf (plus rien en file ni en cours, ou dossier de dépôt changé) repart d'un `Tally` neuf (`main_window.py:3329-3332`). La page Qualité crée un `Tally` par retéléchargement groupé (`quality_page.py:1361`).
- Le réencodage sur place (sans `layout`) garde le dossier imposé et nomme selon la convention historique (`download_thread.py:226-229`).

---

## 3. Algorithmes à porter

### 3.0 Remarques générales de portage C#

- **Regex** : recopier verbatim ; `(?i)` → `RegexOptions.IgnoreCase` ou conserver l'inline. Python `\w`, `\s`, `\b`, `\d` sont **Unicode** (comme .NET par défaut, sauf `\d` qui en .NET accepte aussi les chiffres Unicode — identique à Python 3). Ne **pas** utiliser `RegexOptions.ECMAScript`. Python `re.split(pattern, s, 1)` = un seul découpage (`Regex.Split` avec `count = 2`). `re.sub` remplace toutes les occurrences.
- **`casefold()`** Python ≠ `ToLowerInvariant()` (ex. « ß » → « ss »). Pour une compatibilité stricte avec les clés existantes, implémenter `CaseFold` = `ToLowerInvariant()` + table des repliements complets Unicode (au minimum ß→ss, ẞ→ss, ς→σ, ﬁ→fi…) ; à défaut, documenter l'écart (impact faible : regroupement d'artistes et comparaison de noms).
- **`.lower()`** Python = `ToLowerInvariant()`.
- **Tri** Python de chaînes = ordre **ordinal** par point de code (`StringComparer.Ordinal`). `sorted(..., key=str.lower)` = ordinal sur la version minuscule.
- **`max(iterable, key=f)`** renvoie le **premier** maximum ; **`min`** le premier minimum. Comparaison de tuples = lexicographique.
- **`round()`** Python = arrondi **bancaire** (au pair) : `Math.Round(x, MidpointRounding.ToEven)` (c'est le défaut .NET).
- **`os.path.normcase`** : sous Windows met en minuscules et remplace `/` par `\` ; sous Linux ne fait **rien**. Toute la logique actuelle suppose des comparaisons de chemins **insensibles à la casse** (voir §6.2).
- **`str.isdigit()`** : vrai pour tout chiffre Unicode (y compris « ² ») ; `char.IsDigit` .NET est plus restrictif (Nd seulement). Écart négligeable.
- **`str.strip(" -_.")`** retire, aux deux bouts, tout caractère de l'ensemble (pas la sous-chaîne) → `Trim(new[]{' ','-','_','.'})`.

### 3.1 Nettoyage des noms de fichiers/dossiers

`sanitize_filename(name)` (`core/utils.py:150-152`) :

```
re.sub(r'[\\/*?:"<>|]', "", name)
```

`safe_component(name)` (`library_rules.py:64-81`) :

```
cleaned = re.sub(r"[\x00-\x1f]+", " ", sanitize_filename(name or ""))
return cleaned.strip().rstrip(". ")
```

1. supprime `\ / * ? : " < > |` (sans remplacement : « AC/DC » → « ACDC ») ;
2. chaque suite de caractères de contrôle U+0000–U+001F → **un** espace ;
3. supprime les espaces (blancs Unicode) en tête et en fin, puis les **points et espaces finaux** (interdits sous Windows) ;
4. **ne compacte pas** les espaces internes : « Whip / Nae Nae » → « Whip  Nae Nae » (deux espaces), volontairement, pour coïncider avec ce que produit le téléchargement.

Non traité : noms réservés Windows (CON, NUL, COM1…), longueur maximale, points de tête.

`artist_key(name) = safe_component(name or "").casefold()` (`library_rules.py:84-86`).

`equivalent_name(a, b)` (`organize_page.py:128-136`) : `re.sub(r"\s+", " ", a) == re.sub(r"\s+", " ", b)` — comparaison **sensible à la casse** après compactage des blancs.

### 3.2 Balises de nommage (`name_pattern.py`)

#### 3.2.1 Table complète des balises (`TOKENS`, `name_pattern.py:67-93`)

Regex de reconnaissance d'une balise : `_TOKEN_RE = re.compile(r"%[a-z0-9_]+%")` — **minuscules uniquement** (« %Titre% » n'est pas une balise et reste tel quel).

| Balise | Valeur produite | Source (clé d'entrée) | Exemple |
|---|---|---|---|
| `%titre%` | titre | `title` (attention : nom de fichier si non tagué) | Africa |
| `%artiste%` | artiste | `artist` | Toto |
| `%artiste_album%` | artiste de l'album | `albumartist` | Toto |
| `%artiste_album_ou_artiste%` | artiste de l'album, à défaut artiste | `albumartist` ‖ `artist` (chacun `strip`) | Toto |
| `%album%` | album | `album` | Toto IV |
| `%piste%` | numéro de piste tel quel | `track` | 7 |
| `%piste_00%` | numéro de piste sur 2 chiffres | `_pad(track, 2)` | 07 |
| `%total_pistes%` | nombre de pistes de l'album | `tracktotal` | 12 |
| `%disque%` | numéro de disque | `disc` | 1 |
| `%disque_00%` | numéro de disque sur 2 chiffres | `_pad(disc, 2)` | 01 |
| `%total_disques%` | nombre de disques | `disctotal` | 2 |
| `%date%` | date complète telle quelle | `date` | 1982-04-08 |
| `%annee%` | **premier groupe de 4 chiffres** de la date (`re.search(r"\d{4}", date)`) | `date` | 1982 |
| `%decennie%` | `année[:3] + "0"` | `date` | 1980 |
| `%genre%` | genre | `genre` | Pop |
| `%compositeur%` | compositeur | `composer` | |
| `%editeur%` | éditeur / label | `publisher` | |
| `%commentaire%` | commentaire | `comment` | |
| `%compilation%` | drapeau compilation brut | `compilation` | 1 |
| `%nom_fichier%` | nom du fichier actuel sans extension | `path` | 03 - Africa |
| `%dossier_parent%` | nom du dossier qui contient le fichier | `path` | Toto IV |
| `%format%` | format (extension majuscule) | `format` | MP3 |
| `%debit%` | débit en kbps, vide si 0 | `bitrate` | 320 |

Valeur simple = `str(entry.get(clé) or "").strip()`.

`_pad(value, width)` (`name_pattern.py:27-32`) : valeur `strip` ; vide → vide ; si **entièrement composée de chiffres** → complétée de zéros à gauche jusqu'à `width` (`"3"` → `"03"`, `"123"` → `"123"`) ; sinon renvoyée telle quelle (`"3a"` → `"3a"`). **Suffixe `_00` = forcer deux chiffres.**

Attention : dans l'écran Organiser l'analyse ne lit pas le débit → `%debit%` y est **toujours vide** (cas limite à corriger ou documenter).

#### 3.2.2 Alias historiques (`ALIASES`, `name_pattern.py:98-103`)

| Ancien | Nouveau |
|---|---|
| `%piste2%` | `%piste_00%` |
| `%disque2%` | `%disque_00%` |
| `%nb_pistes%` | `%total_pistes%` |
| `%nb_disques%` | `%total_disques%` |

Toujours interprétés au formatage ; `modernize(pattern)` réécrit le motif enregistré à l'affichage (remplacements de sous-chaînes, dans l'ordre ci-dessus).

#### 3.2.3 Menu « Insérer une balise » (`TAG_CATEGORIES`, `name_pattern.py:108-142`) — libellés verbatim

- **Tags de base** : Titre `%titre%` ; Artiste `%artiste%` ; Artiste de l'album `%artiste_album%` ; Artiste de l'album ou artiste `%artiste_album_ou_artiste%` ; Album `%album%` ; Genre `%genre%`.
- **Numéros de piste et de disque** : « Numéro de la piste   →  7 » `%piste%` ; « Numéro de la piste sur 2 chiffres   →  07 » `%piste_00%` ; « Nombre total de pistes de l'album   →  12 » `%total_pistes%` ; « Numéro du disque   →  1 » `%disque%` ; « Numéro du disque sur 2 chiffres   →  01 » `%disque_00%` ; « Nombre total de disques   →  2 » `%total_disques%`.
- **Date de sortie** : « Date complète   →  1982-04-08 » `%date%` ; « Année seule   →  1982 » `%annee%` ; « Décennie   →  1980 » `%decennie%`.
- **Tags supplémentaires** : Compositeur `%compositeur%` ; « Éditeur / label » `%editeur%` ; Commentaire `%commentaire%` ; « Partie d'une compilation » `%compilation%`.
- **Fichier** : « Nom de fichier actuel » `%nom_fichier%` ; « Dossier parent » `%dossier_parent%` ; « Format   →  MP3 » `%format%` ; « Débit   →  320 » `%debit%`.

#### 3.2.4 Motifs prédéfinis (`PRESETS`, `name_pattern.py:150-161`) et défaut

```
"%titre% - %artiste%"
"%artiste% - %titre%"
"%artiste% - %album% - %titre%"
"%album% - %titre%"
"%artiste% - %titre% (%annee%)"
"%artiste% - %album% (%annee%) - %titre%"
"%artiste_album_ou_artiste% - %titre%"
"%titre% (%artiste%)"
"%titre%"
"%nom_fichier%"
```

`DEFAULT_PATTERN = "%titre% - %artiste%"` (convention historique, partagée avec le téléchargement).

#### 3.2.5 Fonctions

- `unknown_tokens(pattern)` : balises du motif (regex `_TOKEN_RE`) absentes de `TOKENS` **et** de `ALIASES`.
- `missing_required_tags(pattern, entry)` (`name_pattern.py:191-214`) : table `_FILENAME_BACKED = (("%titre%", "title_tagged", "titre"),)`. Si le motif (alias résolus) contient `%titre%` et que `entry.title_tagged` est faux → renvoie `["titre"]`. But : éviter « Joan Baez.mp3 » → « Joan Baez - Joan Baez.mp3 » → … (renommage auto-référent). `%nom_fichier%` n'est pas concerné.
- `format_name(pattern, entry)` : remplace chaque balise reconnue (alias résolus) par sa valeur ; **une balise inconnue est recopiée telle quelle** ; puis `_tidy`.
- `_tidy(name)` (`name_pattern.py:217-231`), à porter **exactement** :

```python
name = re.sub(r"\s+", " ", name)
name = re.sub(r"\(\s*\)|\[\s*\]|\{\s*\}", "", name)
previous = None
while previous != name:
    previous = name
    name = re.sub(r"\s*([-–_,])\s*\1*\s*(?=[-–_,])", " ", name)
name = re.sub(r"\s+", " ", name)
return name.strip(" -–_,.")
```

  (le tiret `–` est U+2013). Exemples : `"03 - Africa"` ; piste vide → `" - Africa"` → `"Africa"` ; `"Toto -  - Africa"` → `"Toto - Africa"` ; `"Africa ()"` → `"Africa"` ; `"R.E.M."` → `"R.E.M"` (le point final est retiré).
- Le nom final d'un fichier = `safe_component(format_name(...))` ; chaîne vide → statut « motif de nommage sans valeur ».

### 3.3 Artiste principal et index des artistes

#### 3.3.1 Regex (`library_rules.py:50-61`) — verbatim

```python
_FEAT_RE = re.compile(
    r"(?i)\s+(?:feat\.?|featuring|ft\.?|vs\.?|versus|with|avec"
    r"|présente|presente|presents?)\s+"
)
_JOIN_RE = re.compile(r"(?i)\s*(?:&|\+|,|/|\bx\b|\band\b|\bet\b)\s*")
_STEM_SPLIT_RE = re.compile(r"\s+-\s+")
```

- `_FEAT_RE` : marqueurs **explicites** d'invité (entourés d'au moins un blanc) : `feat`, `feat.`, `featuring`, `ft`, `ft.`, `vs`, `vs.`, `versus`, `with`, `avec`, `présente`, `presente`, `present`, `presents`.
- `_JOIN_RE` : séparateurs **ambigus** : `&`, `+`, `,`, `/`, `x` (mot), `and` (mot), `et` (mot).

#### 3.3.2 `main_artist(name, known)` (`library_rules.py:89-112`)

```
base = (partie avant le PREMIER match de _FEAT_RE).strip()  ;  si vide → name.strip()
pour chaque match m de _JOIN_RE dans base (gauche → droite) :
    prefix = base[:m.start].strip()
    key = artist_key(prefix)
    si key non vide ET key ∈ known ET key != artist_key(base) : renvoyer prefix
renvoyer base
```

- « Madonna Featuring Justin Timberlake and Timbaland » → « Madonna » (feat).
- « Calvin Harris & Sam Smith » → « Calvin Harris » **si** « calvin harris » ∈ `known`.
- « Earth, Wind & Fire » → inchangé si « earth » ∉ `known`.
- `known` = côté Organiser : clés de **tous** les noms d'artistes bruts du lot analysé ; côté extraction : dossiers de premier niveau existants (`Tally.known`).

Ne pas confondre avec `tag_lookup.main_artist` (§3.6.2), règle différente utilisée pour les requêtes en ligne.

#### 3.3.3 `stem_artist(filename)` (`library_rules.py:115-123`)

Nom de fichier sans extension découpé par `_STEM_SPLIT_RE` (« espace(s) - espace(s) ») ; s'il y a au moins deux morceaux → **dernier** morceau `strip` (convention « Titre - Artiste ») ; sinon chaîne vide.

#### 3.3.4 Artiste d'une entrée (`entry_artist`, `organize_page.py:186-197`)

`artist` (strip) ; sinon `albumartist` (strip) **sauf** s'il désigne « personne » (`is_various`) ; sinon vide.

`words(value)` (version Organiser, `organize_page.py:150-157`) : `" ".join(re.findall(r"[^\W_]+", value.casefold()))` — mots Unicode, accents **conservés**, ponctuation et `_` supprimés.

`is_various(name)` : `words(name) ∈ _VARIOUS_NAMES` avec (`organize_page.py:95-99`) :

```
"various", "various artists", "va", "artistes varies", "artistes variés",
"divers", "compilation", "compilations", "multi interpretes",
"multi interprètes", "unknown artist", "artiste inconnu"
```

#### 3.3.5 `ArtistIndex(entries)` (`organize_page.py:200-252`)

1. `raw` = compteur des noms `entry_artist(e)` non vides (chaînes exactes).
2. `known` = { `artist_key(nom)` pour nom ∈ raw } sans la chaîne vide.
3. Pour chaque nom distinct (avec son effectif) : `base = main_artist(nom, known)` ; `key = artist_key(base)` ; si `key` vide → `base = nom`, `key = artist_key(nom)`. Mémoriser `keys[nom] = key` ; `spellings[key][base] += effectif`.
4. Nom de dossier de chaque clé = `safe_component(` écriture la plus fréquente `)` ; à égalité, la première dans l'ordre **ordinal** (`max(sorted(names), key=names.get)`).
5. `key(nom)` : `nom.strip()` ; vide → `""` ; sinon `keys[nom]` si connu, sinon `artist_key(nom)`. `entry_key(e) = key(entry_artist(e))`. `folder(key)` → nom de dossier ou `""`.

### 3.4 Plan de rangement (`build_plan`)

#### 3.4.1 Options

`artist`, `album`, `rename`, `pattern` (défaut `DEFAULT_PATTERN` si vide), `divers`, `divers_min` (= `max(1, int(x or 3))`), `keep_albums` (défaut vrai), `dedup` (défaut vrai). `use_divers = divers AND artist`. Défauts : `default_options()` (`organize_page.py:376-387`).

#### 3.4.2 Albums préservés (`collection_folders`, `organize_page.py:255-325`)

Constantes (`organize_page.py:83-92`) :

```python
COLLECTION_MIN_ARTISTS = 3       # compilation : artistes différents attendus
SOUNDTRACK_MIN_ARTISTS = 2       # musique de film : deux interprètes suffisent
COLLECTION_NAME_RATIO = 0.6      # part des titres dont l'album nomme le dossier
COLLECTION_DOMINANT_RATIO = 0.5  # au-delà, l'album est celui d'un seul artiste

SOUNDTRACK_RE = re.compile(
    r"(?i)(soundtrack|motion picture|original score|film score"
    r"|bande[\s-]*originale|musique (?:du|de|des|extraite))"
)
```

`name_matches(folder, album)` : `left = words(folder)`, `right = words(album)` (version Organiser) ; faux si l'un est vide ; vrai si `" "+left+" "` est contenu dans `" "+right+" "` **ou** l'inverse (inclusion de mots entiers, dans les deux sens).

`_is_compilation_flag(v)` : `str(v).strip().lower() ∈ {"1","true","yes","oui"}`.

Algorithme :
```
regrouper les entrées par dossier contenant (chaîne exacte de dirname)
pour chaque dossier D et ses fichiers items :
    ignorer si D == racine, D == racine/Divers, ou D est sous racine/Divers
    named = items dont name_matches(basename(D), item.album)
    si named vide : ignorer
    artists = compteur de index.entry_key(item) sur TOUS les items, clé "" retirée
    coherent = len(named) >= 0.6 * len(items)
    multi    = len(artists) >= 3
    si au moins un item de named a un album SOUNDTRACK_RE :
        protéger D si len(artists) >= 2 ET (coherent OU multi)
        passer au dossier suivant (dans tous les cas)
    si non coherent OU non multi : ignorer
    dominant = (effectif max de artists) / (somme des effectifs)
    flagged  = nb d'items avec compilation vraie OU albumartist « various »
    protéger D si dominant < 0.5 OU flagged > len(items) / 2
```
Les dossiers protégés sont mémorisés en chemin absolu normalisé (casse ignorée). Un fichier est « dans un album protégé » si son **dossier parent direct** est protégé.

#### 3.4.3 Première passe : cibles

Pour chaque entrée (ordre de l'analyse) :
- si protégée (option `keep_albums`) → cible `None` (statut final `album`) ;
- sinon :
  - `artist_id = index.entry_key(entry)`, `artist = index.folder(artist_id)`, `album = entry.album.strip()`, `name = basename(path)` (nom actuel) ;
  - `no_artist = artist_option AND artist == ""` ;
  - si `rename` et pas `no_artist` : `no_tags = missing_required_tags(pattern, entry)` ; si vide : `stem = safe_component(format_name(pattern, entry))` ; non vide → `name = stem + extension actuelle` (extension avec sa casse d'origine) ; vide → `no_pattern = true` ;
  - `blocked = no_artist OR no_tags non vide OR no_pattern OR (album_option AND album == "")`.

#### 3.4.4 Doublons certains (`duplicate_copies`, `organize_page.py:447-480`)

Si `dedup` : groupes des entrées **non bloquées** (cible non `None`) par clé
`(artist_id, safe_component(album).casefold() si album_option sinon "", name.casefold())` ;
pour chaque groupe ≥ 2 : `kept = best_copy(groupe, racine/Divers)` (§3.5) ; chaque autre membre → `extra[chemin normalisé] = chemin du gardé`.

#### 3.4.5 Décompte Divers (`effective_artist_counts`, `organize_page.py:390-444`)

- `movable` = entrées à cible non `None` et **non** marquées doublon (les bloquées en font partie).
- `analyzed` = chemins normalisés de **toutes** les entrées.
- `counts[key]` = nombre d'entrées `movable` par `index.entry_key` (clé vide exclue).
- Pour chaque clé dont `count < divers_min` : `counts[key] += artist_folder_count(root, index.folder(key), analyzed)` = nombre de fichiers audio présents **récursivement** sous `root/{dossier de l'artiste}` et **absents** du lot analysé (0 si le dossier n'existe pas). C'est ce qui permet à deux titres égarés dans Divers de rejoindre un dossier d'artiste existant même si l'analyse ne porte que sur « Divers ».
- Calculé seulement si `use_divers`.

#### 3.4.6 Seconde passe : statuts et destinations

Pour chaque entrée, dans l'ordre :
1. cible `None` → `album` (stats.album += 1).
2. marquée doublon → `duplicate`, `dst` = chemin du gardé.
3. `to_divers = use_divers AND artist_id != "" AND counts[artist_id] < divers_min`.
4. `missing` = [`artiste` si no_artist] + no_tags (`titre`) + [`album` si album_option ET album vide ET non to_divers] ; si `missing` vide et `no_pattern` → [`motif de nommage sans valeur`]. Non vide → `untagged`.
5. Dossier visé : `to_divers` → `root/Divers` (**plat** : ni artiste ni album) ; sinon `root` [+ `/{artist}` si artist_option] [+ `/{safe_component(album)}` si album_option]. **Remarque** : artiste décoché + album coché → `root/{Album}/`.
6. `dst = dossier/name`. Si `same_path(dirname(src), dossier)` (casse ignorée) **et** `equivalent_name(basename(src), name)` → `inplace` (un dossier qui ne diffère que par la casse n'est donc **pas** corrigé ; un nom qui diffère par la casse l'est).
7. Sinon `move` ; stats.divers += 1 si `to_divers`, sinon stats.from_divers += 1 si `src` est sous `root/Divers`.

Statistiques : `move, inplace, untagged, duplicate, divers, from_divers, album, album_dirs (= nb de dossiers protégés)`.

#### 3.4.7 Règles côté extraction (`library_rules.target_dir`, `target_stem`, `Tally`)

`target_dir(root, tags, rules, exclude, tally)` (`library_rules.py:349-382`) :
```
si rules absent ou rules.artist faux : renvoyer root          (⚠ pas de dossier album dans ce cas,
                                                               contrairement à Organiser)
name = tags.artist.strip() ; vide → root
tally = tally ou Tally(root)
artist = tally.folder(name) ; vide → root
si rules.divers ET tally.count(name, exclude) < rules.divers_min (défaut 3) : renvoyer root/Divers
path = root/artist ; album = safe_component(tags.album.strip())
si rules.album ET album : path = path/album
```

`target_stem(tags, rules)` (`library_rules.py:385-401`) : `safe_component(format_name(download_pattern(rules), tags))` ; à défaut `"{titre} - {artiste}"` (si les deux), puis titre seul, puis artiste seul (chacun passé par `safe_component`) ; à défaut **`"piste"`**.

`Tally(root)` (`library_rules.py:169-346`), verrou réentrant, disque lu **une seule fois** et à la demande :
- `_scan_folders()` : dossiers de premier niveau de `root` (sans suivre les liens), clé `artist_key(nom)` → nom réel (le premier rencontré), « Divers » exclu.
- `_scan_loose()` : fichiers audio **directement** dans `root` et dans `root/Divers` ; chacun compté pour `_key(stem_artist(nom))` s'il est non vide.
- `_key(artist) = artist_key(main_artist(artist.strip(), known = clés des dossiers))`.
- `_on_disk(key)` (mémoïsé) = loose[key] + nombre de fichiers audio récursifs sous `root/{dossier de key}`.
- `_excluded(key, exclude)` = 1 si le fichier remplacé (`exclude`) est compté pour cette clé : il est sous le dossier de l'artiste, ou il est directement dans `root`/`root/Divers` et `stem_artist` le rattache à la clé ; sinon 0.
- `known()` = ensemble des clés de dossiers.
- `folder(artist)` = nom du dossier **existant** de l'artiste principal s'il existe (écriture du disque conservée), sinon `safe_component(artiste principal)`.
- `reserve(artist, count=1)` : `pending[key] += count`.
- `count(artist, exclude)` = `max(0, on_disk − excluded) + max(pending[key], 1)` (le titre en cours compte pour 1, ou tout le lot réservé).

### 3.5 Meilleur exemplaire — « Ne garder qu'un exemplaire » (`best_copy`, `organize_page.py:328-373`)

```python
DUPLICATE_SIZE_RATIO = 0.97
quality(e) = (débit kbps, taille octets)  # (0,0) si fichier illisible ; débit lu à la demande (en-tête), 0 si inconnu
largest = max des tailles du groupe
rang(e) = (
    -débit,
    0 si taille >= 0.97 * largest sinon 1,
    -taille,
    1 si e est sous racine/Divers sinon 0,
    nombre de séparateurs de chemin dans path,
    len(path),
    path.casefold(),
)
gardé = élément de rang MINIMAL (premier en cas d'égalité totale)
```

⚠ Tel que codé, le critère « à 3 % près » est **sans effet** : la composante `-taille` qui suit ordonne déjà strictement par taille, si bien que « hors Divers / moins enfoui » ne départage que des fichiers de taille **identique**. L'intention documentée (à taille quasi égale, préférer le mieux rangé) n'est pas réalisée. À trancher : reproduire à l'identique ou corriger (déplacer `-taille` après les critères de rangement).

### 3.6 Recherche des tags (`tag_lookup.find_tags`)

#### 3.6.1 Constantes (`tag_lookup.py:71-135`)

```python
MIN_SCORE = 0.55        # en dessous, aucune proposition n'est faite
SURE_SCORE = 0.80       # au-dessus, la proposition est cochée d'office
DURATION_EXACT = 3      # s : deux durées à ce point proches valent preuve
DURATION_MAX = 30       # s : au-delà, ce n'est pas le même enregistrement
DURATION_AMBIGUOUS = 8  # s : rivale crédible
DURATION_ONLY_SCORE = 0.78
MUSICBRAINZ_AGENT = "DeeKeep/1.0 ( organiseur de bibliothèque musicale )"
MUSICBRAINZ_DELAY = 1.05
MUSICBRAINZ_MAX_QUERIES = 3
CORE_TITLE_WEIGHT = 0.9
EXTRA_FIELDS = ("albumartist", "genre", "date", "track", "tracktotal", "disc")
EXTRA_TRIGGER_FIELDS = ("albumartist", "genre", "date", "track")
```

`GENERIC_FOLDERS` (`tag_lookup.py:99-105`) :

```
"divers", "various", "va", "musique", "music", "mp3", "cd", "cd1", "cd2",
"disc", "disc1", "disc2", "disk", "disk1", "disk2", "itunes", "downloads",
"telechargements", "compil", "compils", "compilation", "compilations",
"electro", "dance", "pop", "rock", "pop rock", "pop dance", "ambiance",
"disco", "funk", "rap", "jazz", "classique", "varietes", "no title",
```

`_STOPWORDS` (`tag_lookup.py:148-151`) :

```
"la", "le", "les", "l", "un", "une", "des", "de", "du", "d", "et", "ou",
"au", "aux", "en", "the", "an", "of", "to", "in", "on", "and", "or",
```

#### 3.6.2 Regex et normalisation (verbatim)

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
```

Listes de mots qu'elles portent : mentions parasites `official…`, `lyric(s)…`, `audio`, `video`, `clip`, `hd`, `hq`, `full` (entre parenthèses), tout `[…]`, « official video/audio/lyric video », `lyric(s)`, débit final (« - 128 », « 320kbps ») ; qualificatifs de version `full`, `radio`, `extended`, `club`, `maxi`, `original`, `album`, `single`, `vf`, `vo` + `edit`, `mix`, `version`, `remix`, `remaster`, `remastered` (+ chiffres) ; invités `feat`, `feat.`, `featuring`, `ft`, `ft.`, `avec`, `with`, `vs`, `vs.`, `versus`.

Fonctions :
- `words(text)` (**version tag_lookup**, ≠ Organiser) : `casefold`, décomposition **NFKD**, suppression des caractères combinants, puis `re.findall(r"[a-z0-9]+")` → liste. Les lettres non latines ou non décomposables (ø, æ, cyrillique…) **disparaissent**. Port : `Normalize(FormKD)` + retrait des `UnicodeCategory.NonSpacingMark` (et `SpacingCombiningMark`, `EnclosingMark`), puis `[a-z0-9]+`.
- `_significant(items)` : mots hors stopwords ; si aucun → tous.
- `contained(needle, haystack)` : `target = _significant(words(needle))` ; vide → 0 ; sinon part des mots de `target` présents dans l'**ensemble** `words(haystack)` (asymétrique, doublons de `target` comptés).
- `clean_stem(name)` : basename sans extension → `_LEADING_TRACK_RE.sub("")` → `_NOISE_RE.sub(" ")` → blancs compactés → `strip(" -_.")`. Ex. « 03 - Losing My Religion (Official Video) [320kbps].mp3 » → « Losing My Religion ». ⚠ Effets de bord à conserver ou corriger consciemment : « 50 Cent - In Da Club » perd « 50 » (`^\s*\d{2}\s+`) ; « Summer of 69 » perd « 69 » (débit final supposé).
- `core_title(title)` : `_NOISE_RE` → `_QUALIFIER_RE` → `_VERSION_TAIL_RE` → compactage → `strip(" -_.")`, à défaut titre d'origine. Ex. « White Knuckle Ride Full Edit [Www Keepvid Com] » → « White Knuckle Ride ». (Utilisé par la recherche YouTube/Deezer des extractions, pas par `find_tags`.)
- `main_artist(artist)` (tag_lookup) : `_GUEST_RE.sub("")`, `strip(" -_,&")`, compactage ; à défaut l'original. « Mad Ice Feat. Irina » → « Mad Ice ». (Utilisé par les extractions.)
- `missing_fields(entry)` : `title` si **non** `title_tagged` ; `artist`, `album` si vides.
- `missing_extras(entry)` : champs de `EXTRA_FIELDS` vides. `needs_extras(entry)` : au moins un champ de `EXTRA_TRIGGER_FIELDS` vide (le nombre de pistes et le disque ne déclenchent **jamais** une recherche à eux seuls).
- `local_duration(path)` : durée de l'en-tête arrondie à la seconde (0 si illisible).
- `_clues(entry)` : (`clean_stem(path)`, artiste, album, dossier parent — vidé si `" ".join(words(parent)) ∈ GENERIC_FOLDERS`).
- `title_source(entry)` : `clean_stem(path) + " " + nom du dossier parent (non filtré) [+ " " + titre si title_tagged]` — l'album est volontairement exclu.

#### 3.6.3 Requêtes Deezer (`deezer_queries`, `tag_lookup.py:291-313`), dans l'ordre, dédoublonnées sans casse

1. si artiste ET album : `artist:"{artiste}" album:"{album}"`
2. si artiste ET stem ET `words(stem) != words(artiste)` : `{artiste} {stem}` ; sinon si artiste : `artist:"{artiste}"`
3. si stem : `{stem}`
4. si dossier utile : si stem ET `words(stem) != words(dossier)` : `{dossier} {stem}` ; sinon si artiste : `{dossier} {artiste}`

(Aucun échappement des guillemets.)

#### 3.6.4 Requêtes MusicBrainz (`musicbrainz_queries`, `tag_lookup.py:336-375`), **3 au plus**

Échappement Lucene : `re.sub(r'([+\-&|!(){}\[\]^"~*?:\\/])', r"\\\1", value)`.
`_without_artist(stem, artist)` : découpe le stem sur les blancs, retire chaque morceau dont l'ensemble de mots (`words`) est **inclus** dans les mots de l'artiste (un morceau sans mot, ex. « - », est donc retiré), rejoint, `strip(" -_.")`.

1. si artiste ET album : `artist:"{A}" AND release:"{B}"`
2. si artiste : titre = `_without_artist(stem, artiste)` ; non vide → `recording:"{T}" AND artist:"{A}"` ; vide → `artist:"{A}"`.
   sinon si stem : découpe sur le **premier** `" - "` : si partie droite non vide → `recording:"{gauche}" AND artist:"{droite}"` puis `recording:"{droite}" AND artist:"{gauche}"` ; sinon `recording:"{stem}"`.
3. si stem : `{stem échappé}` (texte libre)
4. si dossier utile ET stem ET `words(stem) != words(dossier)` : `recording:"{stem}" AND artist:"{dossier}"`

Dédoublonnage sans casse puis troncature à 3.

#### 3.6.5 Concordance de durée (`_duration_agreement`, `tag_lookup.py:578-591`)

```
si local == 0 ou distant == 0 : None (inconnue)
delta = |local - distant|
delta > 30  → -1.0
delta <= 3  →  1.0
sinon       →  1.0 - (delta - 3) / 27        (ex. 10 s → 0,7407)
```

#### 3.6.6 Notation d'un candidat (`evaluate`, `tag_lookup.py:610-704`)

Entrées : candidat {title, artist, album, seconds}, entrée, `duration` (s), `source = title_source(entry)`.

```
agreement = _duration_agreement(duration, cand.seconds)
delta     = |duration - cand.seconds| si les deux connus, sinon 0
title_score = contained(cand.title, source)
core = _QUALIFIER_RE.sub(" ", cand.title).strip()
si core non vide et core != cand.title : title_score = max(title_score, 0.9 * contained(core, source))
artist = entry.artist.strip() ; album = entry.album.strip()
artist_score = max(contained(cand.artist, artist) si artist sinon 0, contained(cand.artist, source))
album_score  = contained(cand.album, album) si album sinon 0
conclusive = title_score >= 0.9 ET artist_score >= 0.99

si agreement < 0 (connu) ET non conclusive : écarté (0)
duration_score = 0.5 si agreement inconnu
               = 0.05 * 30 / max(delta, 30) si agreement < 0
               = agreement sinon
score = 0.45 * title_score + 0.30 * artist_score + 0.25 * duration_score

si title_score < 0.5 ET album_score >= 0.8 ET agreement == 1.0 (connu) :
    renvoyer (max(score, 0.78), "durée identique dans l'album déjà connu")
si title_score < 0.5 : écarté
si artist_score < 0.5 :
    si artist (tag) non vide : écarté
    exact = words(cand.title) == words(clean_stem(path))
    si non exact OU agreement inconnu OU agreement < 1 : écarté
    renvoyer (0.78, "titre exact et durée identique — artiste non vérifiable dans le nom du fichier")
raison :
    agreement < 0  → "titre et artiste reconnus, mais durée très différente ({delta} s d'écart)"
    agreement >= 1 → "titre reconnu, durée identique"
    conclusive     → "titre et artiste reconnus dans le nom du fichier"
    inconnu        → "titre reconnu, durée inconnue"
    sinon          → "titre reconnu, durée proche ({delta} s d'écart)"
renvoyer (score, raison)
```

#### 3.6.7 Meilleur candidat textuel (`_best_from_text`, `tag_lookup.py:722-744`)

Pour chaque requête (dans l'ordre) : `limit = 25` si la requête contient `artist:`, `release:` ou `recording:`, sinon `8` ; noter chaque candidat ; garder le meilleur (score **strictement** supérieur) avec sa raison et sa requête ; **arrêter** les requêtes dès que le meilleur score ≥ **0,95**.
Garde-fou des rivales : si la raison du meilleur commence par « durée » (identification par durée seule), et qu'il existe parmi **tous** les candidats vus une autre piste avec `seconds` connu, `|seconds − duration| <= 8` et (titre **ou** artiste différent au sens `words`) → **aucun résultat**.

#### 3.6.8 Candidat par empreinte (`_best_from_fingerprint`, `tag_lookup.py:747-771`)

Candidats AcoustID triés par `match` décroissant (§4.4) ; premier candidat avec un titre non vide **et** dont la durée n'est pas en désaccord (> 30 s) : `score = match` si durée identique (≤ 3 s) ou inconnue, sinon `0.75 × match` ; raison « empreinte acoustique (concordance {match:.0%}) » + « , durée identique » le cas échéant ; requête = « empreinte ».

#### 3.6.9 Orchestration (`_propose`, `find_tags`, `tag_lookup.py:774-900`)

```
find_tags(entry, acoustid_key, extras=True côté UI) :
    absent = missing_fields(entry)
    extras = missing_extras(entry) si extras ET needs_extras(entry) sinon []
    si absent vide ET extras vide : None
    duration = local_duration(path) ; source = title_source(entry) ; album = entry.album
    p = _propose(entry, absent, extras, duration, …)
    si p ET p.sure : renvoyer p
    si absent vide : renvoyer p                       # complémentaires seuls : pas de 2e passe
    useful = durée hors silence final (ffmpeg, §4.5) ou None
    si useful ET |useful - duration| > 3 :
        r = _propose(…, useful, note=" (durée mesurée hors silence final)")
        si r ET (p absent OU r.score > p.score) : renvoyer r
    renvoyer p

_propose :
    services = [Deezer] + [MusicBrainz si absent non vide] + [AcoustID si absent non vide]
    pour chaque service dans cet ordre :
        best, score, raison, requête = service(entry)
        si best absent OU score < 0.55 : service suivant
        si album (tag) : parmi best.albums (MusicBrainz/AcoustID), prendre le premier titre t
            tel que contained(t, album) >= 0.8 ou contained(album, t) >= 0.8 → best.album = t
        values = { f : best[f] pour f ∈ absent si best[f] non vide }
        si absent non vide ET values vide : service suivant
        si extras non vide ET service == Deezer :
            details = deezer_details(best.id, best.album_id)      # §4.1
            values += { f : v ∈ details si f ∈ extras }
        si values vide : service suivant
        replaces = { f ∈ (title, artist, album) : best[f] si f ∉ values ET tag actuel non vide
                     ET best[f] non vide ET words(actuel) != words(best[f]) }
        renvoyer la proposition (§5.4) avec sure = score >= 0.80, score arrondi à 2 décimales
    renvoyer None
```

Conséquences : un fichier auquel ne manque qu'un tag complémentaire n'est soumis qu'à **Deezer** (jamais MusicBrainz ni empreinte) ; seuls les **champs vides** sont proposés (`values`) ; les tags contredits ne sont écrits qu'avec l'option de correction ; le numéro de disque et le nombre de pistes sont écrits **s'ils sont fournis** et absents.

#### 3.6.10 Thread de recherche (`TagLookupThread`, `tag_lookup.py:914-950`)

Séquentiel, un fichier à la fois (pas de parallélisme : API publique) ; toute exception → proposition `None` ; émet `found(index, proposition|None)` puis `progress(index+1)` ; drapeau d'arrêt testé avant et après chaque fichier (le résultat d'un fichier en vol au moment de l'arrêt est **jeté**) ; `done` en fin.

### 3.7 Rapprochement approximatif des doublons (`dedup_dialog.py`)

#### 3.7.1 Clés et union-find (`dedup_dialog.py:49-124`)

`canonical(text)` — verbatim :

```python
s = (text or "").lower()
s = re.sub(r'[\(\[]\s*(copy|copie|\d+)\s*[\)\]]', ' ', s)
s = re.sub(r'\b(copy|copie)\b', ' ', s)
s = re.sub(r'^\s*\d{1,3}\s*[-\._]\s*', ' ', s)
s = re.sub(r'[^a-z0-9]+', ' ', s)
return re.sub(r'\s+', ' ', s).strip()
```

⚠ Les lettres accentuées sont **supprimées** (« Été » → « t ») : conserver tel quel pour la compatibilité, ou décider d'un repli des accents.

Clés d'une entrée :
- `"f:" + canonical(nom de fichier sans extension)` (si non vide) ;
- `"m:" + canonical(artist) + "|" + canonical(title)` si titre et artiste non vides (le titre peut être le nom de fichier de repli) ;
- `"a:" + artist_key_fn(entry) + "|" + canonical(title)` si titre non vide et fonction fournie et résultat non vide — côté Organiser, `artist_key_fn = ArtistIndex.entry_key` (clé d'artiste **principal**, `safe_component` + casefold, accents conservés).

Union-find (avec compression de chemin) sur les indices : pour chaque entrée dans l'ordre, pour chaque clé, union avec la **première** entrée ayant porté cette clé. Groupes = classes de taille ≥ 2, dans l'ordre de leur représentant. Transitivité : A~B par le nom, B~C par les tags → {A,B,C}.

#### 3.7.2 Exemplaire gardé — écran fichier/fichier (`_keep_score`, `dedup_dialog.py:208-254`)

```
norm(t) = re.sub(r"[^a-z0-9]+"," ", t.lower()) compacté
in_artist_folder = un composant quelconque du chemin du dossier parent (séparateurs / ou \) a norm == norm(artiste)
score = ( 1 si format.upper() == format cible (réglage file_format, ex. "MP3") sinon 0,
          1 si in_artist_folder sinon 0,
          -profondeur (nombre de "/" dans le chemin, après \ → /),
          débit,
          -longueur du chemin )
gardé = premier élément de score MAXIMAL (comparaison stricte)
```

⚠ Diffère de §3.5 (qualité d'abord) et de §3.7.3 : à harmoniser ou à reproduire tel quel (le texte de l'écran décrit cet ordre).

#### 3.7.3 Exemplaire gardé — écran piste/fichiers (`file_score`, `dedup_dialog.py:344-361`)

`(débit, taille, -profondeur, -longueur du chemin)` ; gardé = `max` (premier à égalité). Taille et débit 0 si illisibles.

`human_size` : unités `o`, `Ko`, `Mo`, `Go` (base 1024), entier pour les octets, une décimale sinon, **virgule** décimale (« 8,4 Mo »).
`file_quality` : « {EXT} · {débit} kbps · {taille} » (débit omis si inconnu), « fichier introuvable » si la taille est illisible.

### 3.8 Écriture des tags (`tag_write.write`)

Champs écrivables : `FIELDS = ("title", "artist", "album", "albumartist", "genre", "date", "track", "tracktotal", "disc", "disctotal", "publisher")`. `clean_values` : ne garde que ces clés, valeur convertie en texte `strip`, vides ignorées.

```
si mutagen indisponible : (faux, "mutagen n'est pas disponible")
values = clean_values(values)
si values vide ET pas de jaquette : (vrai, "")      # fichier non ouvert
si fichier absent : (faux, "fichier introuvable")
.mp3  → _write_mp3   ; .flac → _write_flac   ; autre → _write_easy
exception → (faux, message)
```

**MP3 / ID3** (`tag_write.py:122-144`) :
- tags créés s'il n'y en a pas ; chaque champ fourni **remplace** la trame du même identifiant ; encodage texte **UTF-8** (`encoding=3`).
- correspondances : `title`→**TIT2**, `artist`→**TPE1**, `album`→**TALB**, `albumartist`→**TPE2**, `genre`→**TCON**, `date`→**TDRC**, `publisher`→**TPUB**.
- trames jumelées « numéro/total » : **TRCK** (`track`, `tracktotal`), **TPOS** (`disc`, `disctotal`) ; fusion avec la valeur existante : `numéro = nouveau ou ancien`, `total = nouveau ou ancien` ; écrit « n/t » ou « n » ; **un total sans numéro n'est jamais écrit** (« /12 » illisible).
- jaquette (si fournie) : trame **APIC** type 3 (couverture avant), MIME **toujours `image/jpeg`**, description « Cover », UTF-8 — remplace l'APIC de même description, les autres images restent.
- sauvegarde mutagen par défaut : **ID3v2.4** (`v2_version=4`), ID3v1 **mis à jour s'il existe déjà** (non créé), reste du fichier (autres trames, flux audio) intact.

**FLAC / Vorbis comments** (`tag_write.py:147-160`) : clés `title`, `artist`, `album`, `albumartist`, `genre`, `date`, `tracknumber`, `tracktotal`, `discnumber`, `disctotal`, `organization` (= publisher) ; chaque clé fournie remplace les valeurs existantes. Jaquette : **toutes** les images existantes sont effacées puis une image type 3, `image/jpeg`, « Cover » ajoutée.

**Autres formats** (`.m4a`, `.ogg`, `.opus`, `.wma`, `.wav`, `.aac`) via l'interface « easy » de mutagen (`tag_write.py:163-183`) : clés `title`, `artist`, `album`, `albumartist`, `genre`, `date`, `tracknumber`, `discnumber` uniquement — **`tracktotal`, `disctotal`, `publisher` ne sont pas écrits**, ni la jaquette. Champ refusé par le conteneur → ignoré silencieusement ; renvoie toujours `(vrai, "")` si le fichier s'ouvre (même si rien n'a été écrit) ; `(faux, "format non pris en charge")` si non reconnu.
Écarts constatés à connaître : OGG/Opus n'utilisent **pas** la table Vorbis complète malgré le commentaire du module ; en MP4, écrire `tracknumber` seul remplace l'atome `trkn` (le total existant est perdu) ; WMA/WAV/AAC : comportement dépendant du conteneur (champs souvent refusés ou non standard).

### 3.9 Suppression des dossiers vides (`prune_empty_dirs`, `organize_page.py:666-683`)

Parcours **ascendant** (enfants avant parents) de **toute** l'arborescence sous la racine (racine exclue) ; tout dossier dont le listing est vide est supprimé (un parent ne contenant que des dossiers vides le devient et part dans la même passe) ; erreurs ignorées. Remarques : concerne aussi des dossiers vides **sans rapport** avec le rangement ; un fichier caché (`desktop.ini`, `Thumbs.db`, `.DS_Store`, `.directory`) empêche la suppression.

---

## 4. Intégrations externes

### 4.1 Deezer (API publique, sans clé)

| Appel | URL | Paramètres | Timeout | Utilisation |
|---|---|---|---|---|
| Recherche | `GET https://api.deezer.com/search` | `q` (requête §3.6.3), `limit` (8 ou 25) | 10 s | candidats |
| Piste | `GET https://api.deezer.com/track/{id}` | — | 10 s | `track_position`, `disk_number`, `release_date`, `album.id` |
| Album | `GET https://api.deezer.com/album/{id}` | — | 10 s | `genres.data[0].name`, `artist.name`, `nb_tracks`, `release_date` |

- Pas d'en-tête particulier (User-Agent par défaut de `requests`), pas de limitation de débit côté client (le quota public Deezer est d'environ 50 requêtes / 5 s par IP — à faire respecter côté serveur).
- Réponse de recherche → candidat : `title`, `artist.name`, `album.title`, `duration` (s, entier), `album.cover_medium` ‖ `album.cover_small` (non utilisée pour l'écriture), `id`, `album.id` (en texte).
- `_deezer_get` : toute exception, code ≠ 200, JSON invalide, réponse non-objet ou contenant une clé `error` → `{}` (absence de réponse). Recherche : exception ou code ≠ 200 → liste vide.
- **Second appel** `deezer_details(track_id, album_id)` (`tag_lookup.py:474-502`), seulement si des tags complémentaires manquent et que le service retenu est Deezer :
  - `/track/{id}` (si id) : `track` = `track_position`, `disc` = `disk_number` (si non nuls) ; `date` = **année** de `release_date` de la **piste** (4 premiers caractères s'ils sont des chiffres) — préférée à celle de l'album (réédition) ; `album_id` complété depuis la piste s'il manquait ;
  - `/album/{id}` (via cache) : `genre` (premier genre), `albumartist`, `tracktotal` (`nb_tracks`) ; `date` = année de l'album seulement si la piste n'en donnait pas ;
  - les champs vides sont retirés du résultat.
- **Cache d'albums** : dictionnaire mémoire global `album_id → {genre, albumartist, tracktotal, date}`, protégé par verrou, durée de vie = processus, **sans éviction** ; un échec (réponse vide) **n'est pas** mis en cache.

### 4.2 MusicBrainz

- `GET https://musicbrainz.org/ws/2/recording` ; paramètres `query` (Lucene, §3.6.4), `fmt=json`, `limit` (8 ou 25) ; en-tête **`User-Agent: DeeKeep/1.0 ( organiseur de bibliothèque musicale )`** ; timeout 15 s.
- **Limitation** : au moins **1,05 s** entre deux appels, globalement pour le processus (verrou + horodatage monotone) (`_throttle`, `tag_lookup.py:509-520`).
- Code **503** → attente 1,05 s puis **un seul** nouvel essai ; autre code ≠ 200, exception ou JSON invalide → liste vide.
- Réponse → candidat : `title` ; `artist` = noms de `artist-credit[].name` joints par **une espace** (les `joinphrase` « feat. », « & » sont perdus) ; `album` = titre de la première `releases[]`, `albums` = tous les titres de parution ; `seconds` = `round(length/1000)` (0 si absent).
- Sollicité seulement si un tag **indispensable** manque et que Deezer n'a rien donné d'exploitable. Au plus 3 requêtes par fichier (et la relance hors silence final peut en refaire 3).

### 4.3 Outils d'empreinte (Chromaprint)

`fingerprint_tool()` (`acoustid_lookup.py:75-96`), détection **une fois** par processus (mémoïsée, non verrouillée) :
1. `fpcalc` (`fpcalc.exe` sous Windows) cherché dans `{ressources}/fpcalc/`, puis `{ressources}/chromaprint/`, puis le PATH ; accepté si `fpcalc -version` rend le code 0 **ou** si sa sortie contient « fpcalc » (timeout 20 s).
2. sinon chaque ffmpeg candidat (celui livré dans `{ressources}/ffmpeg/`, puis le PATH) : accepté si `ffmpeg -hide_banner -muxers` (timeout 30 s) réussit et sa sortie contient « chromaprint ».

Calcul (`compute_fingerprint`, `FINGERPRINT_SECONDS = 120`, timeout 120 s) :
- fpcalc : `fpcalc -json -length 120 {fichier}` → champ `fingerprint` du JSON ; repli pour anciennes versions : ligne `FINGERPRINT=…` de la sortie texte.
- ffmpeg : `ffmpeg -hide_banner -loglevel error -i {fichier} -t 120 -ac 1 -ar 22050 -f chromaprint -fp_format base64 -` → sortie standard.
- échec → chaîne vide (pas d'appel réseau).

`availability(key)` → (disponible, message) :
- pas d'outil : « aucun calculateur d'empreinte : installez fpcalc (Chromaprint) dans le dossier « fpcalc » de l'application, ou un ffmpeg compilé avec le muxer chromaprint » ;
- clé vide : « clé AcoustID absente : elle est gratuite sur acoustid.org/new-application, à renseigner dans les Paramètres » ;
- sinon « empreinte calculée par fpcalc » ou « empreinte calculée par ffmpeg (muxer chromaprint) ».

### 4.4 AcoustID

- `GET https://api.acoustid.org/v2/lookup` ; paramètres `client` = clé (réglage `acoustid_key`, strip), `meta=recordings+releasegroups`, `duration` = durée entière du fichier (s), `fingerprint` ; timeout 20 s ; pas de User-Agent dédié ; pas de limitation côté client (le service tolère ~3 requêtes/s).
- Réponse : si `status != "ok"` → rien. Pour chaque `results[]` de `score >= MIN_MATCH = 0.5`, pour chaque `recordings[]` : `title`, `artist` = noms `artists[].name` joints par une espace, `album` = premier `releasegroups[].title`, `albums` = tous, `seconds` = `duration` (entier), `match` = score. Tri par `match` décroissant. (`SURE_MATCH = 0.85` est défini mais **inutilisé**.)
- Sollicité **en dernier**, seulement si un tag indispensable manque, et si outil + clé sont présents.
- La clé est un **secret** (`SECRET_KEYS = {"arl", "acoustid_key"}`, `db/db_init.py:63`) : non synchronisée en ligne sauf `cloud_sync_secrets`.

### 4.5 ffmpeg — durée hors silence final (`audio_probe.effective_duration`)

Commande : `ffmpeg -hide_banner -nostats -i {fichier} -af silencedetect=noise=-45dB:d=3 -f null -` (timeout 180 s, décodage **complet** du fichier). Analyse de stderr+stdout : `silence_start: x` / `silence_end: y`. Le silence final est retenu si une plage reste ouverte (plus de débuts que de fins) ou si la dernière fin est à ≤ **1,5 s** de la durée déclarée ; durée utile = `round(début du dernier silence)` (None si ≤ 0 ou pas de silence final). ffmpeg détecté une fois (`-hide_banner -version`, code 0), le livré puis le PATH.

### 4.6 Système de fichiers et OS

- Lecture : parcours récursif, lecture des tags (mutagen), `getsize`, `stat`.
- Écriture : `os.remove` (doublons), `rename` / `shutil.move` (rangement), `makedirs`, `rmdir` (dossiers vides), écriture des tags en place.
- Explorateur Windows (« Ouvrir le dossier ») et lecteur audio local.

---

## 5. Données

### 5.1 Entrée d'analyse (fichier lu)

Produite par `LocalScanThread._read_one` (`reencode_scan.py:362-370`) :

| Clé | Type | Remarque |
|---|---|---|
| `path` | texte | chemin absolu |
| `format` | texte | extension majuscule sans point |
| `title` | texte | **nom de fichier sans extension si pas de titre** |
| `title_tagged` | booléen | vrai si le titre vient des tags |
| `artist`, `album`, `albumartist`, `genre`, `composer`, `publisher`, `comment`, `compilation` | texte | vides si absents |
| `track`, `tracktotal`, `disc`, `disctotal` | texte | découpés de « n/t » ; Vorbis : `tracktotal`/`totaltracks`, `disctotal`/`totaldiscs` en repli |
| `date` | texte | brute (« 1982 », « 1982-04-08 »…) |
| `bitrate` | entier kbps | **0** dans l'écran Organiser (non lu) ; lu à la demande pour les doublons |

Sources des tags lus : ID3 (TIT2, TPE1, TPE2, TALB, TRCK, TPOS, TDRC|TYER, TCON, TCOM, TPUB, TCMP, première COMM non vide), FLAC/Vorbis (`title`, `artist`, `album`, `albumartist`|`album artist`, `tracknumber`|`track`, `discnumber`|`disc`, `date`|`year`|`originaldate`, `genre`, `composer`, `organization`|`label`|`publisher`, `comment`|`description`, `compilation`), autres via « easy » avec les mêmes noms.

### 5.2 Ligne de plan (`build_plan`)

`{ entry, src, status ∈ {move, inplace, album, duplicate, untagged}, dst, missing: [texte] }` — `dst` = destination (move/inplace), chemin de l'exemplaire gardé (duplicate), vide sinon. Statistiques : `{move, inplace, untagged, duplicate, divers, from_divers, album, album_dirs}`.

### 5.3 Options de rangement et réglages persistés

Réglages stockés dans la table SQLite **`app_settings`** (une ligne par clé, valeur **JSON**), écrits à chaque changement d'option (après 350 ms) et à l'ouverture/fermeture du volet :

| Clé | Type | Défaut | Lu par |
|---|---|---|---|
| `org_artist` | bool | true | Organiser, extractions, Paramètres |
| `org_album` | bool | true | idem |
| `org_rename` | bool | false | Organiser, extractions |
| `org_pattern` | texte | `%titre% - %artiste%` | idem |
| `org_divers` | bool | false | Organiser, extractions, Paramètres |
| `org_divers_min` | int (UI 2–10, règle ≥ 1) | 3 | idem |
| `org_remove_empty` | bool | true | Organiser |
| `org_report_untagged` | bool | true | Organiser |
| `org_keep_albums` | bool | true | Organiser |
| `org_dedup` | bool | true | Organiser |
| `org_filters_open` | bool | false | Organiser (UI) |
| `acoustid_key` | texte (secret) | "" | Compléter les tags |
| `file_format` | texte | `MP3` | écran doublons (format cible) |
| `download_dir` | texte | `~/Music` | dossier analysé par défaut |

Les options de plan (`current_options`) reprennent `artist, album, rename, pattern, divers, divers_min, keep_albums, dedup`.

### 5.4 Proposition de tags (`find_tags`)

```
{
  title, artist, album, seconds,          // piste retenue
  cover_url,                              // Deezer seulement, non utilisée
  values:   { champ: valeur },            // champs vides comblés (indispensables + complémentaires)
  fills:    [champs de values],
  replaces: { champ: valeur },            // title/artist/album contredits (non vides et mots différents)
  corrects: [champs de replaces],
  score: 0..1 (2 décimales), sure: score >= 0.80,
  reason: texte (+ " (durée mesurée hors silence final)"),
  query: requête ou "empreinte",
  duration: durée utilisée (s), source: "Deezer" | "MusicBrainz" | "AcoustID"
}
```

### 5.5 Candidats de service

Deezer : `{title, artist, album, seconds, cover_url, id, album_id}` ; MusicBrainz : `{title, artist, album, albums[], seconds, cover_url:""}` ; AcoustID : idem MusicBrainz + `match`.

### 5.6 Écran piste/fichiers

Entrée `[{track: {title, artist, album, …}, match: {state: present|similar, reason: texte, paths: [chemins]}}]` ; sortie : liste de chemins à supprimer.

### 5.7 Base SQLite et caches

- Ce périmètre **ne lit ni n'écrit** la table `tracks` : l'analyse relit le disque, le rangement et l'écriture de tags ne mettent pas la base à jour (la collecte ultérieure s'en charge). La table `tracks` a pour clé primaire **`path`** et porte `title, artist, album, album_artist, genre, year, track_no, duration, bitrate, …` (`db/db_schema.py:128-160`) : un déplacement ou une écriture de tags la **périme**.
- Caches mémoire (perdus au redémarrage) : tags par chemin (mtime ns + taille, 100 000 entrées max puis purge totale), albums Deezer, outil d'empreinte, ffmpeg détecté, horodatage MusicBrainz.

---

## 6. Points d'attention pour la version web / serveur Linux

### 6.1 Architecture

- **Bibliothèque partagée** : `library_rules` + `name_pattern` + `safe_component` + `main_artist` doivent vivre dans **un seul** paquet .NET (NuGet interne) consommé par le service « Organiser » **et** par le service d'extraction/téléchargement — c'est une exigence métier explicite (un titre extrait doit se poser là où l'analyse l'aurait mis). Tests de non-régression communs (jeux d'exemples de §3).
- **Traitements longs côté serveur** (analyse, application du plan, recherche de tags, recherche de doublons, empreintes) : tâches de fond avec identifiant, progression poussée par **abonnement GraphQL** (ou SSE), arrêt coopératif (« Arrêter la recherche », arrêt d'analyse). Un seul traitement d'écriture à la fois **par bibliothèque** (équivalent `_busy`) — verrou serveur, pas seulement l'état du bouton, car plusieurs onglets/utilisateurs peuvent agir.
- **État du plan** : le plan, les cases et la mémoire des « cases touchées » doivent survivre à un rafraîchissement de page → les tenir côté serveur (session de rangement) ou les reconstruire déterministiquement ; le recalcul après changement d'option doit rester **sans relecture disque** (garder les entrées en mémoire / cache).
- **Arbre volumineux** (milliers de lignes) : rendu virtualisé (CDK virtual scroll / arbre plat), états tri-états calculés côté client à partir des seuls fichiers ; pagination ou chargement par branche si nécessaire.
- **Anti-rebond** de 350 ms sur la saisie du motif et des options, puis mutation GraphQL de recalcul.
- **Écran de revue des tags** : lignes ajoutées au fil des résultats (abonnement), bouton d'écriture actif dès la première proposition, écriture qui **arrête** la recherche.
- **Écoute** : remplacer le lecteur local par un point de lecture HTTP avec requêtes `Range` ; « Ouvrir le dossier » n'a pas d'équivalent web (proposer de copier le chemin ou de naviguer dans l'arbre).
- **Choix du dossier** : sélecteur de dossiers **côté serveur**, restreint aux racines autorisées (prévenir toute traversée de chemin `..`).

### 6.2 Linux et chemins

- **Sensibilité à la casse** : tout le code compare les chemins via `normcase` (insensible à la casse sous Windows, **identité** sous Linux). Décider explicitement :
  - comparaisons de chemins (`same_path`, `is_inside`, index par chemin) : conserver l'insensibilité (`StringComparer.OrdinalIgnoreCase`) pour reproduire le comportement, **mais** sur ext4 deux dossiers « Madonna » et « madonna » peuvent coexister : l'existence réelle doit être testée avec la casse exacte ;
  - `artist_folder_count(root, index.folder(key))` et `Tally._scan_folders` : sous Linux, chercher le dossier d'artiste **sans tenir compte de la casse** (lister la racine et comparer les clés), sinon le décompte Divers et la sortie de Divers se dégradent ;
  - renommage de casse seule (`_move`) : sous Linux c'est un `rename` ordinaire ; à l'inverse un dossier « madonna » existant ne sera pas réutilisé par un plan visant « Madonna » → doublon de dossier. Recommandation : résoudre la cible sur le disque de façon insensible à la casse avant de créer un dossier.
- **Caractères interdits** : conserver le jeu Windows (`\ / * ? : " < > |`, contrôles, points/espaces finaux) même sous Linux, pour que la bibliothèque reste copiable sur un disque NTFS/exFAT et que les noms restent identiques à ceux déjà produits.
- **Chemins existants dans la base** : `tracks.path` et l'historique contiennent des chemins Windows (`D:\…`). Prévoir une table de correspondance de racines (préfixe Windows → montage Linux) et une normalisation des séparateurs.
- **Déplacement inter-volumes** : `File.Move` .NET sous Unix gère le cas inter-périphériques (copie + suppression) ; conserver la règle « jamais d'écrasement » (`overwrite: false`) et le test préalable d'existence.
- **Suppression définitive** : le desktop supprime sans corbeille. En web, fortement recommandé : corbeille applicative (déplacement dans un dossier `.deekeep-trash/` daté, purge différée), au moins pour les doublons.
- **Dossiers vides** : sous Linux ignorer (ou traiter comme vides) les fichiers parasites selon une liste configurable ; ne supprimer idéalement que les dossiers **rendus vides par l'opération** pour éviter de toucher à l'existant.
- **Droits** : le service doit écrire dans la bibliothèque (utilisateur dédié, umask) ; gérer les erreurs `EACCES`/`EROFS` comme échecs par fichier.

### 6.3 Performance sur ARM64 peu puissant

- **Lecture des tags** : 8 lectures parallèles conviennent à un SSD ; sur carte SD / disque USB réduire à 2–4. Envisager d'utiliser la table `tracks` comme **cache persistant** (clé `path`, `file_mtime`, `file_size`) pour éviter de relire des milliers de fichiers — à condition de la **mettre à jour** après chaque déplacement (changement de clé primaire), suppression et écriture de tags.
- **Débit** : ne le lire qu'à la demande (doublons), comme aujourd'hui.
- **Empreinte acoustique** : 120 s de décodage par fichier + **décodage complet** pour la détection de silence final : coûteux sur ARM ; sérialiser (file de travail à 1 slot), timeouts conservés (120 s / 180 s).
- **Outils** : vérifier la disponibilité de `fpcalc` ARM64 (paquet Debian/Ubuntu `libchromaprint-tools`) et d'un ffmpeg avec muxer `chromaprint` (le ffmpeg des distributions est souvent compilé avec `--enable-chromaprint`, à vérifier) ; garder la **détection réelle** (exécution) plutôt que supposer.
- **Écriture des tags en C#** : TagLib# (managé, compatible ARM64) — attention : par défaut il peut écrire en ID3v2.3/2.4 selon la configuration ; le comportement actuel est **ID3v2.4, UTF-8**, ID3v1 mis à jour seulement s'il existe, trames remplacées individuellement, TRCK/TPOS fusionnés. Choisir et documenter la version ID3 (le miroir restauré, lui, est en v2.3 — hors périmètre). Profiter du portage pour écrire `tracktotal`/`disctotal`/`publisher` en OGG/Opus et préserver le total en MP4 (écarts §3.8), si l'on accepte de diverger.

### 6.4 Réseau et quotas

- **MusicBrainz** : limiteur **global au serveur** (1 requête / 1,05 s) partagé par tous les utilisateurs et tous les services (y compris `reference_duration` hors périmètre) ; User-Agent conforme à leur politique, idéalement avec une adresse de contact (`DeeKeep/1.0 ( contact@… )`).
- **Deezer** : ajouter un limiteur (≈ 50 requêtes / 5 s) et une relance sur quota (`error.code == 4`), absents aujourd'hui ; un fichier coûte jusqu'à 4 requêtes de recherche + 2 de détails.
- **AcoustID** : limiteur ≈ 3 requêtes/s ; une empreinte de 120 s fait plusieurs Ko → préférer **POST** (formulaire, éventuellement gzip) au GET actuel pour éviter les URL trop longues.
- **Cache d'albums Deezer** : le rendre partagé et borné (LRU / durée de vie) côté service ; envisager un cache persistant (SQLite) comme `reference_durations`.
- **Secrets** : la clé AcoustID reste un secret serveur (pas exposée au client Angular) ; ne pas la journaliser dans les URL de requêtes.

### 6.5 Cohérences et anomalies à arbitrer avant portage

1. Trois critères différents de « meilleur exemplaire » : plan (§3.5 : débit > taille > rangement), écran fichier/fichier (§3.7.2 : format cible > dossier artiste > profondeur > débit), écran piste/fichiers (§3.7.3 : débit > taille > profondeur). Le README décrit le troisième pour l'écran Musique.
2. `DUPLICATE_SIZE_RATIO` sans effet (§3.5).
3. Organiser crée `racine/Album` quand « Artiste » est décoché et « Album » coché ; l'extraction (`target_dir`) ne crée alors **aucun** sous-dossier → divergence de règle entre plan et extraction.
4. `%debit%` toujours vide dans Organiser (débit non lu).
5. Dossier dont seule la casse diffère : jamais corrigé par le plan (statut « déjà en place »).
6. `canonical` (doublons) supprime les lettres accentuées ; `words` de tag_lookup supprime les lettres non latines.
7. `clean_stem` retire les nombres finaux de 2–3 chiffres et les « NN » de tête (« Summer of 69 », « 50 Cent »).
8. Écriture « easy » : `tracktotal`, `disctotal`, `publisher` et la jaquette ignorés hors MP3/FLAC ; succès annoncé même si aucun champ n'a été accepté.
9. MusicBrainz/AcoustID : les phrases de liaison d'artistes (« feat. », « & ») sont remplacées par une espace dans la valeur proposée à l'écriture.
10. Le rangement et l'écriture de tags ne mettent pas à jour la table `tracks` (clé = chemin) : à faire dans la version web.
11. Pas d'annulation possible d'un rangement en cours ; la version web devrait au moins permettre un arrêt entre deux fichiers et produire un journal (source → destination) permettant une **annulation** ultérieure.
