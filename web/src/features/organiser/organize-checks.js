/**
 * Cases de l'arbre et lots de fichiers : calculs purs de la page Organiser.
 */

/** Fusionne un lot de fichiers reçu pendant l'analyse (un index déjà connu est remplacé), triés par index. */
export function mergeEntries(list, batch) {
  const byIndex = new Map(list.map(e => [e.index, e]));
  for (const e of batch) byIndex.set(e.index, e);
  return [...byIndex.values()].sort((a, b) => a.index - b.index);
}

/** Cases posées à la main (index → coché), telles que le serveur les garde. */
export function checksToMap(checks) {
  const map = new Map();
  for (const i of checks.checked) map.set(i, true);
  for (const i of checks.unchecked) map.set(i, false);
  return map;
}

/** Copie des cases posées à la main, avec `indices` mis à `value`. */
export function withChecks(touched, indices, value) {
  const next = new Map(touched);
  for (const i of indices) next.set(i, value);
  return next;
}

/** Case effective d'un fichier : celle de l'utilisateur, sinon la sélection par défaut du plan. */
export function effectiveChecked(entries, touched, rowByIndex) {
  const set = new Set();
  for (const e of entries) {
    const t = touched.get(e.index);
    if (t ?? rowByIndex.get(e.index)?.defaultChecked ?? true) set.add(e.index);
  }
  return set;
}

/** Fichiers cochés auxquels il manque des tags (« Compléter les tags »). */
export function lookupIndicesOf(rows, checkedSet) {
  return rows.filter(r => r.needsLookup && checkedSet.has(r.index)).map(r => r.index);
}

/** Opérations du plan retenues : déplacements et doublons cochés. */
export function applyOperations(rows, checked) {
  return rows.filter(r => (r.status === 'move' || r.status === 'duplicate') && checked.has(r.index));
}

/** Question posée avant le rangement. */
export function applyConfirmText(ops, root, removeEmpty) {
  const moves = ops.filter(r => r.status === 'move').length;
  const dups = ops.length - moves;
  let text = `Déplacer ${moves} fichier(s) dans :\n${root}`;
  if (dups)
    text += `\nSupprimer ${dups} doublon(s) du disque (corbeille de la bibliothèque), dont un autre exemplaire est conservé.`;
  if (removeEmpty) text += '\nLes dossiers devenus vides seront supprimés.';
  return text;
}
