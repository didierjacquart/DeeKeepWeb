/**
 * Options de rangement (volet « Filtres ») : valeurs par défaut, cases du volet et
 * forme envoyée au serveur.
 */

export const DEFAULT_OPTIONS = {
  artist: true,
  album: true,
  rename: false,
  pattern: '%titre% - %artiste%',
  divers: false,
  diversMin: 3,
  keepAlbums: true,
  dedup: true,
  removeEmpty: true,
  reportUntagged: true,
  filtersOpen: false,
};

/** Options telles que le serveur les attend : le seuil « Divers » est un entier entre 2 et 10. */
export function optionsInput(o) {
  return { ...o, diversMin: Math.min(10, Math.max(2, Math.round(o.diversMin || 3))) };
}

/**
 * Options affichées au chargement. Sur téléphone le volet Filtres, ouvert, repousse la liste hors de l'écran :
 * il s'ouvre à la demande (l'affichage seul est replié, le réglage enregistré n'est pas touché).
 */
export function displayedOptions(saved, compact) {
  return { ...saved, filtersOpen: compact ? false : saved.filtersOpen };
}

/** Cases du volet Filtres, dans l'ordre d'affichage (la case « Divers » a sa propre ligne). */
export const OPTION_CHECKS = [
  { key: 'artist', label: 'Sous-dossier par Artiste' },
  { key: 'album', label: 'Sous-dossier par Album' },
  { key: 'rename', label: 'Renommer les fichiers' },
  {
    key: 'keepAlbums',
    label: 'Préserver musiques de film et compilations',
    tip: "Un dossier dont le nom est celui de l'album de ses fichiers (« Footloose », « Dirty Dancing », « Zen 2 ») est laissé intact dès que cet album est une musique de film, ou qu'il réunit au moins trois artistes dont aucun ne domine : le ranger par artiste l'éparpillerait en autant de dossiers d'un seul titre. Un dossier de genre ou un fourre-tout (« CD1 », « Various »), dont les albums diffèrent, reste rangé par artiste — de même que le best-of d'un artiste unique, invités compris.",
  },
  {
    key: 'dedup',
    label: "Ne garder qu'un exemplaire",
    tip: "Quand plusieurs copies d'un même titre visent le même emplacement, une seule est conservée — le meilleur débit, puis le fichier le plus complet — et rangée selon les règles habituelles (dossier de l'artiste, ou « Divers » si son artiste n'atteint pas le seuil). Les autres sont placées dans la corbeille de la bibliothèque. Les albums préservés ne sont pas concernés : un titre peut figurer à la fois dans une bande originale et chez son artiste.",
  },
  { key: 'removeEmpty', label: 'Supprimer les dossiers vides' },
  { key: 'reportUntagged', label: 'Signaler les fichiers sans tags' },
];
