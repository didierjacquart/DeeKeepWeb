/** Valeur de tri d'une ligne pour une colonne (textes sans casse). */
function sortValue(r, key) {
  switch (key) {
    case 'id':
      return r.track.id || 0;
    case 'duration':
      return r.track.duration || 0;
    case 'title':
      return r.track.title.toLowerCase();
    case 'artist':
      return r.track.artist.toLowerCase();
    case 'album':
      return (r.track.album ?? '').toLowerCase();
    default:
      return r.status.toLowerCase();
  }
}

/** Lignes triées par colonne ; à égalité, l'ordre d'arrivée. Sans colonne : l'ordre d'origine. */
export function sortRows(rows, sortKey, sortAsc) {
  if (!sortKey) return rows;
  const dir = sortAsc ? 1 : -1;
  return [...rows].sort((a, b) => {
    const x = sortValue(a, sortKey),
      y = sortValue(b, sortKey);
    return (x < y ? -1 : x > y ? 1 : 0) * dir || a.seq - b.seq;
  });
}

/** Flèche d'en-tête de la colonne triée. */
export function sortArrow(column, sortKey, sortAsc) {
  return sortKey === column ? (sortAsc ? ' ▲' : ' ▼') : '';
}
