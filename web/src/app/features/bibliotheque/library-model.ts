import { gql } from '@apollo/client';

/** Titre de la bibliothèque tel que le serveur le décrit (état disque et archive compris). */
export interface Track {
  path: string;
  title: string | null;
  artist: string | null;
  album: string | null;
  albumArtist: string | null;
  genre: string | null;
  year: string | null;
  duration: number | null;
  bitrate: number | null;
  codec: string | null;
  ext: string | null;
  fileSize: number | null;
  coverSha1: string | null;
  inHistory: boolean;
  onDisk: boolean;
  archiveState: 'none' | 'ok' | 'stale';
  archiveBytes: number | null;
  archiveSourceBytes: number | null;
  format: string | null;
}

export interface TrackPage {
  items: Track[];
  total: number;
  totalOnDisk: number;
  totalMissing: number;
}

export const TRACK_FIELDS = `
  path title artist album albumArtist genre year duration bitrate codec ext fileSize coverSha1 inHistory onDisk
  archiveState archiveBytes archiveSourceBytes format
`;

/** Toute la bibliothèque (limite 0 = sans limite) : filtre et tri se font sur le client, depuis le cache. */
export const TRACKS_QUERY = gql`
  query LibraryTracks {
    tracks(query: { includeMissing: true, limit: 0 }) { total totalOnDisk totalMissing items { ${TRACK_FIELDS} } }
  }
`;

export const TRACKS_CHANGED = gql`subscription LibraryTracksChanged { tracksChanged { paths reason } }`;
export const TRACKS_REMOVED = gql`subscription LibraryTracksRemoved { tracksRemoved { paths reason } }`;
export const ARCHIVE_CHANGED = gql`subscription LibraryArchiveChanged { archiveUpdated { paths reason } }`;

/** SEARCH_FIELDS de l'original : champ, libellé ; l'ordre sert de priorité aux propositions. */
export const SEARCH_FIELDS: ReadonlyArray<readonly [keyof Track, string]> = [
  ['artist', 'Artiste'],
  ['album', 'Album'],
  ['title', 'Titre'],
  ['albumArtist', 'Album de'],
  ['genre', 'Genre'],
  ['year', 'Année'],
  ['format', 'Format'],
];

export const MIN_SUGGEST_CHARS = 3;
export const MAX_SUGGESTIONS = 5;
export const FILTER_DELAY_MS = 220;

/**
 * Port de <c>library_index.canonical</c> : minuscules sans accent ni ponctuation, « & » → « and »,
 * « $ » → « s », marques de copie et numéro de piste de tête retirés. Les lettres hors a-z0-9
 * disparaissent comme dans l'original (mêmes rapprochements que le serveur).
 */
export function canonical(text: string | null | undefined): string {
  let t = (text ?? '').normalize('NFKD').toLowerCase().replace(/ß/g, 'ss').replace(/\p{M}/gu, '');
  t = t.replace(/&/g, ' and ').replace(/\$/g, 's');
  t = t.replace(/[(\[]\s*(?:copy|copie|\d+)\s*[)\]]/g, ' ');
  t = t.replace(/^\s*\d{1,3}\s*[-._]\s*/, ' ');
  return t.replace(/[^a-z0-9]+/g, ' ').replace(/\s+/g, ' ').trim();
}

export function basename(path: string): string {
  return path.split(/[\\/]/).pop() ?? path;
}

export interface LibraryRow {
  track: Track;
  /** Botte de foin canonique : tous les tags + nom de fichier. */
  hay: string;
  /** Tri NUAGE : à jour 0, à renvoyer 1, absent 2. */
  cloud: number;
}

export function toRow(track: Track): LibraryRow {
  const parts = SEARCH_FIELDS.map(([f]) => String(track[f] ?? ''));
  parts.push(basename(track.path));
  return { track, hay: canonical(parts.join(' ')), cloud: { ok: 0, stale: 1, none: 2 }[track.archiveState] };
}

/** `_selectable` : présent, ou case « absents » cochée, ou copie d'archive à jour. */
export function selectable(t: Track, includeMissing: boolean): boolean {
  return t.onDisk || includeMissing || t.archiveState === 'ok';
}

export interface Suggestion {
  label: string;
  value: string;
  key: string;
  count: number;
  priority: number;
}

/** `_build_value_index` : une valeur compte une fois par titre, sous sa nature la plus prioritaire. */
export function buildValueIndex(rows: LibraryRow[], includeMissing: boolean): Suggestion[] {
  const counts = new Map<string, Suggestion>();
  for (const { track } of rows) {
    if (!selectable(track, includeMissing)) continue;
    const best = new Map<string, { p: number; raw: string; label: string }>();
    SEARCH_FIELDS.forEach(([field, label], p) => {
      const raw = String(track[field] ?? '').trim();
      const key = raw ? canonical(raw) : '';
      if (!key) return;
      const cur = best.get(key);
      if (!cur || p < cur.p) best.set(key, { p, raw, label });
    });
    for (const [key, b] of best) {
      const slot = counts.get(key);
      if (!slot) counts.set(key, { label: b.label, value: b.raw, key, count: 1, priority: b.p });
      else if (b.p < slot.priority) Object.assign(slot, { label: b.label, value: b.raw, priority: b.p, count: slot.count + 1 });
      else slot.count++;
    }
  }
  return [...counts.values()].sort((a, b) => b.count - a.count || a.priority - b.priority || (a.key < b.key ? -1 : a.key > b.key ? 1 : 0));
}

/** `_suggestions_for` : celles qui commencent par la frappe d'abord, puis celles qui la contiennent. */
export function suggestionsFor(values: Suggestion[], text: string): Suggestion[] {
  const needle = canonical(text.trim());
  if (!needle) return [];
  const starts: Suggestion[] = [];
  const contains: Suggestion[] = [];
  for (const item of values) {
    if (item.key.startsWith(needle)) starts.push(item);
    else if (item.key.includes(needle)) contains.push(item);
    if (starts.length >= MAX_SUGGESTIONS) break;
  }
  return [...starts, ...contains].slice(0, MAX_SUGGESTIONS);
}

export function suggestionText(s: Suggestion): string {
  return `${s.label} · ${s.value}   (${s.count} titre${s.count > 1 ? 's' : ''})`;
}

export type SortColumn = 'title' | 'artist' | 'album' | 'genre' | 'year' | 'duration' | 'format' | 'cloud' | 'path';

/** Tri stable : durée et NUAGE numériques, le reste en forme canonique, vides en fin (ascendant). */
export function sortRows(rows: LibraryRow[], column: SortColumn, desc: boolean): LibraryRow[] {
  const numeric = column === 'duration' || column === 'cloud';
  const keyOf = (r: LibraryRow): number | string => {
    if (column === 'cloud') return r.cloud;
    if (column === 'duration') return r.track.duration ?? 0;
    const value = column === 'path' ? r.track.path : String(r.track[column] ?? '');
    return canonical(value) || '￿';
  };
  const keys = new Map(rows.map(r => [r, keyOf(r)] as const));
  return [...rows].sort((a, b) => {
    const ka = keys.get(a)!;
    const kb = keys.get(b)!;
    const c = numeric ? (ka as number) - (kb as number) : ka < kb ? -1 : ka > kb ? 1 : 0;
    return desc ? -c : c;
  });
}
