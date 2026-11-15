const compareFr = (a, b) => a.localeCompare(b, 'fr', { sensitivity: 'base' });

/** Recherches dans l'ordre de l'historique, ou par requête (`'asc'` / `'desc'`). */
export function sortSearches(searches, dir) {
  const list = [...searches].sort((a, b) => a.position - b.position);
  return dir ? list.sort((a, b) => (dir === 'asc' ? 1 : -1) * compareFr(a.query, b.query)) : list;
}

/** Tri suivant de la colonne « Recherche » : croissant au premier clic, puis alterné. */
export const nextSearchSort = dir => (dir === 'asc' ? 'desc' : 'asc');

/** Flèche de la colonne « Recherche ». */
export const searchSortMark = dir => (dir === 'asc' ? ' ▴' : dir === 'desc' ? ' ▾' : '');

/**
 * Téléchargements, les plus récents en premier (l'historique est rangé du plus ancien
 * au plus récent), ou triés par colonne : `{ key, desc }`.
 */
export function sortDownloads(history, sort) {
  const list = [...history].sort((a, b) => b.position - a.position);
  if (!sort) return list;
  const val = e => (sort.key === 'path' ? e.path : (e[sort.key] ?? '')).toLowerCase();
  return list.sort((a, b) => (sort.desc ? -1 : 1) * compareFr(val(a), val(b)));
}

/** Clic sur une colonne des téléchargements : croissant, puis alterné. */
export const nextDownloadSort = (sort, key) => (sort?.key === key ? { key, desc: !sort.desc } : { key, desc: false });

/** Flèche d'une colonne des téléchargements. */
export const downloadSortMark = (sort, key) => (sort?.key === key ? (sort.desc ? ' ▾' : ' ▴') : '');
