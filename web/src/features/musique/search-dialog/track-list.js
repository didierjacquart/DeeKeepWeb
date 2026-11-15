import { isDuplicate } from './search-model';

/** Calculs purs de la liste de pistes : filtre, tri, sélection (pistes cochées par identifiant). */

/** Pistes affichées : filtre sur le titre, « doublons seulement », puis tri par colonne. */
export function visibleTracks(tracks, { filter, duplicatesOnly, sortColumn, sortAsc }) {
  const text = filter.trim().toLowerCase();
  const list = tracks.filter(
    t => (!text || t.title.toLowerCase().includes(text)) && (!duplicatesOnly || isDuplicate(t)),
  );
  if (!sortColumn) return list;
  const dir = sortAsc ? 1 : -1;
  const key = t =>
    sortColumn === 'duration'
      ? t.duration
      : sortColumn === 'rank'
        ? (t.rank ?? 0)
        : (t[sortColumn] ?? '').toString().toLowerCase();
  return [...list].sort((a, b) => {
    const x = key(a),
      y = key(b);
    return (x < y ? -1 : x > y ? 1 : 0) * dir;
  });
}

/** Entités triées par colonne (nom ou détails), sans casse. */
export function sortedEntities(entities, entitySort) {
  if (!entitySort) return entities;
  const dir = entitySort.asc ? 1 : -1;
  return [...entities].sort(
    (a, b) =>
      (a[entitySort.column] ?? '').toLowerCase().localeCompare((b[entitySort.column] ?? '').toLowerCase()) * dir,
  );
}

/** « Tout sélectionner » est coché : toutes les pistes visibles cochables le sont. */
export function allVisibleChecked(visible, picked) {
  const eligible = visible.filter(t => !isDuplicate(t));
  return eligible.length > 0 && eligible.every(t => picked.has(t.id));
}

/** Coche ou décoche une piste ; une piste déjà sur le disque ne se coche pas. */
export function togglePick(picked, t, on) {
  if (t.presence.state === 'PRESENT') return picked;
  const next = new Map(picked);
  if (on) next.set(t.id, t);
  else next.delete(t.id);
  return next;
}

/** « Tout sélectionner » : n'agit que sur les lignes visibles ; en cochant, ignore ✓ et ≈. */
export function pickVisible(picked, visible, on) {
  const next = new Map(picked);
  for (const t of visible) {
    if (on) {
      if (!isDuplicate(t)) next.set(t.id, t);
    } else next.delete(t.id);
  }
  return next;
}

/** Pastilles reçues : ce qui est déjà sur le disque est décoché. */
export function uncheckDuplicates(picked, list) {
  const next = new Map(picked);
  for (const t of list) if (isDuplicate(t)) next.delete(t.id);
  return next;
}

/** Pistes affichées avec les pastilles fraîchement recalculées (la liste elle-même ne change pas). */
export function mergePresence(tracks, fresh) {
  const byId = new Map(fresh.map(x => [x.id, x.presence]));
  return tracks.map(x => (byId.has(x.id) ? { ...x, presence: byId.get(x.id) } : x));
}

/** Seules les lignes dont l'état vient de changer sont décochées : le choix de l'utilisateur est respecté. */
export function uncheckChanged(picked, before, updated) {
  const was = new Map(before.map(x => [x.id, x.presence.state]));
  const next = new Map(picked);
  for (const x of updated) {
    if (was.get(x.id) !== x.presence.state && isDuplicate(x)) next.delete(x.id);
  }
  return next;
}

/** Pistes cochées rendues à l'appelant, sans leur pastille. */
export function pickedTracks(picked) {
  return [...picked.values()].filter(t => t.id).map(({ presence: _presence, ...lite }) => lite);
}
