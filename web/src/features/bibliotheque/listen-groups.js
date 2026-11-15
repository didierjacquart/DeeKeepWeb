import { canonical } from './library-model';
import { playable } from './track-info';

/**
 * Écoute d'un album ou d'un artiste entier depuis la Bibliothèque : la file est faite de
 * tous les titres écoutables du groupe, pas seulement de ceux que la recherche affiche.
 * Les noms se comparent sous leur forme canonique (casse, accents, ponctuation ignorés).
 */

const collator = new Intl.Collator('fr', { numeric: true, sensitivity: 'base' });

/** Ordre de lecture d'un album : le chemin (fichiers numérotés « 01 - … », sous-dossiers de disques). */
const byPath = (a, b) => collator.compare(a.path, b.path);

/** Ordre d'un artiste : albums par année puis par nom, pistes dans l'ordre de l'album. */
const byYearAlbumPath = (a, b) =>
  (Number(a.year) || 9999) - (Number(b.year) || 9999) ||
  collator.compare(canonical(a.album), canonical(b.album)) ||
  byPath(a, b);

const folder = path => path.replace(/[\\/][^\\/]*$/, '');

/** Artiste qui signe l'album : l'artiste de l'album, sinon celui du titre. */
export function albumOwner(t) {
  return (t.albumArtist || t.artist || '').trim();
}

/**
 * Titres du même album que `t` : même nom d'album, et même artiste d'album (s'il est renseigné)
 * ou, à défaut, même artiste ou même dossier — deux « Greatest Hits » d'artistes différents ne se
 * mélangent pas, une compilation sans artiste d'album reste entière.
 * @param {object[]} tracks Titres de la bibliothèque.
 * @param {object} t Titre choisi.
 */
export function albumTracks(tracks, t) {
  const album = canonical(t.album);
  if (!album) return [];
  const owner = canonical(t.albumArtist);
  const artist = canonical(t.artist);
  const dir = folder(t.path);
  return tracks
    .filter(x => playable(x) && canonical(x.album) === album)
    .filter(x =>
      owner ? canonical(x.albumArtist) === owner : canonical(x.artist) === artist || folder(x.path) === dir,
    )
    .sort(byPath);
}

/**
 * Titres d'un artiste, qu'il soit l'interprète ou l'artiste de l'album.
 * @param {object[]} tracks
 * @param {string} name
 */
export function artistTracks(tracks, name) {
  const key = canonical(name);
  if (!key) return [];
  return tracks
    .filter(x => playable(x) && (canonical(x.artist) === key || canonical(x.albumArtist) === key))
    .sort(byYearAlbumPath);
}

/**
 * Titres d'un album désigné par son seul nom (proposition de la recherche), tous artistes confondus :
 * regroupés par artiste d'album, puis dans l'ordre de chaque album.
 * @param {object[]} tracks
 * @param {string} name
 */
export function albumNamedTracks(tracks, name) {
  const key = canonical(name);
  if (!key) return [];
  return tracks
    .filter(x => playable(x) && canonical(x.album) === key)
    .sort((a, b) => collator.compare(canonical(albumOwner(a)), canonical(albumOwner(b))) || byPath(a, b));
}

/** Natures de proposition (libellés de SEARCH_FIELDS) qui désignent un groupe écoutable. */
const GROUP_OF_LABEL = { Artiste: 'artist', Album: 'album' };

/**
 * Groupe écoutable désigné par une proposition de la recherche (artiste ou album), sinon null.
 * @param {object[]} tracks
 * @param {{ label: string, value: string } | null} suggestion
 * @returns {{ kind: 'artist' | 'album', name: string, tracks: object[] } | null}
 */
export function groupOfSuggestion(tracks, suggestion) {
  const kind = suggestion && GROUP_OF_LABEL[suggestion.label];
  if (!kind) return null;
  const list = kind === 'artist' ? artistTracks(tracks, suggestion.value) : albumNamedTracks(tracks, suggestion.value);
  return list.length ? { kind, name: suggestion.value, tracks: list } : null;
}

/** « l'album « X » » / « l'artiste « Y » », pour les libellés des boutons et menus. */
export function groupLabel(kind, name) {
  return `${kind === 'album' ? 'l’album' : 'l’artiste'} « ${name} »`;
}

/** « 12 titres » */
export function countLabel(n) {
  return `${n} titre${n > 1 ? 's' : ''}`;
}
