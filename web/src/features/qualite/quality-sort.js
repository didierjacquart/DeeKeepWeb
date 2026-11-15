import { DEEP_DEFECTS, DEFECTS } from './quality-model';

const text = s => (s ?? '').toLowerCase();
const finite = n => (Number.isFinite(n) ? n : -Infinity);
const num = cell => {
  const m = /-?\d+(\.\d+)?/.exec(cell ?? '');
  return m ? parseFloat(m[0]) : null;
};

/** Clés de tri de l'original (quality_page.py:836-869), sur les textes déjà calculés par le serveur. */
export function sortKey(row, key) {
  switch (key) {
    case 'score':
      return [row.error ? -1 : row.score, text(row.title)];
    case 'title':
      return [text(row.title)];
    case 'artist':
      return [text(row.artist), text(row.title)];
    case 'defects':
      return [row.defects.length, row.score];
    case 'hiss':
      return [finite(num(row.cells['hiss']) ?? -Infinity)];
    case 'crackle':
      return [row.cells['crackle'] === 'non mesuré' ? -1 : (num(row.cells['crackle']) ?? -1)];
    case 'clipping':
      return [num(row.cells['clipping']) ?? 0];
    case 'gaps':
      return [num((row.cells['gaps'] ?? '').split('·')[1]) ?? 0];
    case 'duration':
      return [
        row.referenceDuration != null && row.duration != null ? Math.abs(row.duration - row.referenceDuration) : -1,
      ];
    case 'blanks':
      return [Math.max(blank(row, 'tail'), blank(row, 'head'))];
    case 'format':
      return [row.bitrate ?? 0, text(row.format)];
    case 'path':
      return [text(row.path)];
    case 'status':
      return [text(row.cells['status'])];
  }
}

function blank(row, which) {
  // La cellule « blanks » ne porte que les défauts retenus ; le détail complet est dans l'infobulle « Début : x s — Fin : y s ».
  const m = /Début : ([\d.]+) s — Fin : ([\d.]+) s/.exec(row.tooltips['blanks'] ?? '');
  return m ? parseFloat(m[which === 'head' ? 1 : 2]) : 0;
}

export function compareKeys(a, b) {
  for (let i = 0; i < Math.min(a.length, b.length); i++) {
    if (a[i] < b[i]) return -1;
    if (a[i] > b[i]) return 1;
  }
  return 0;
}

/**
 * keep(e) : un défaut visible, ou illisible (toujours montré), ou « Titres sans défaut » cochée et aucun défaut.
 * Un défaut « profond » n'est visible que si la case « Détecter les craquements » est cochée.
 */
export function keepRow(row, visible, includeClean) {
  if (row.error) return true;
  if (row.defects.length === 0) return includeClean;
  return row.defects.some(d => visible.has(d.id));
}

/** Défauts affichés : cochés dans « Afficher », sans les défauts profonds en analyse rapide. */
export function visibleDefects(shown, deep) {
  return new Set(DEFECTS.filter(d => shown[d.id] && (deep || !DEEP_DEFECTS.includes(d.id))).map(d => d.id));
}

/** Lignes affichées : filtrées puis triées (à égalité, dans l'ordre d'arrivée). */
export function filterAndSort(rows, visible, includeClean, sort) {
  const { key, asc } = sort;
  const keyed = rows.filter(r => keepRow(r, visible, includeClean)).map(r => ({ r, k: sortKey(r, key) }));
  // Pas de retri pendant l'analyse : les lignes arrivent en fin de liste, le tri définitif a lieu à la fin.
  keyed.sort((a, b) => (asc ? 1 : -1) * compareKeys(a.k, b.k) || a.r.uid - b.r.uid);
  return keyed.map(x => x.r);
}

/** Clic sur un en-tête : même colonne = sens inversé, autre colonne = croissant. */
export function nextSort(current, key) {
  if (!key) return current;
  return current.key === key ? { key, asc: !current.asc } : { key, asc: true };
}

/** Flèche du sens de tri, ajoutée au libellé de la colonne triée. */
export function sortMark(sort, key) {
  return key && sort.key === key ? (sort.asc ? ' ▲' : ' ▼') : '';
}
