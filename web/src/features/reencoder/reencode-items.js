import { isActive } from '../../core/jobs/jobs';

/** Règle « déjà au format cible » (spec 01 §3.9) : le format suffit pour le sans perte, sinon ±5 kbps. */
export function isAtTarget(item, format, quality) {
  const target = format.toUpperCase();
  if (item.format.toUpperCase() !== target) return false;
  if (target === 'FLAC' || target === 'WAV') return true;
  const kbps = quality.includes('128') ? 128 : quality.includes('320') ? 320 : 0;
  if (kbps === 0) return true;
  return Math.abs((item.bitrate || 0) - kbps) <= 5;
}

/** Texte d'avancement de l'analyse locale (« Analyse en cours… n / total fichier(s) analysé(s) »). */
export function scanTextOf(all, scanJobIds) {
  const job = all.find(j => scanJobIds.includes(j.id) && isActive(j));
  return job ? job.message || 'Analyse en cours…' : null;
}

/** Tâche de conversion d'une ligne (null si aucune). */
export function jobOf(item, byId) {
  return item.jobId ? (byId.get(item.jobId) ?? null) : null;
}

/** Lignes nouvelles d'une analyse : les chemins déjà présents (casse ignorée) sont écartés. */
export function itemsFromScan(found, existing) {
  const known = new Set(existing.map(i => i.path.toLowerCase()));
  const fresh = [];
  for (const f of found) {
    if (known.has(f.localPath.toLowerCase())) continue;
    known.add(f.localPath.toLowerCase());
    fresh.push({
      path: f.localPath,
      dbPath: f.dbPath,
      title: f.title ?? '',
      artist: f.artist ?? '',
      album: f.album ?? '',
      bitrate: f.bitrate ?? 0,
      format: (f.ext || '').replace('.', '').toUpperCase(),
      matchState: 'pending',
      match: null,
      selected: false,
      jobId: null,
      converted: false,
    });
  }
  return fresh;
}

/** Résultat d'une correspondance Deezer : une ligne trouvée est cochée. */
export function applyMatch(items, path, match) {
  return items.map(i =>
    i.path === path && i.matchState === 'pending'
      ? { ...i, match, matchState: match ? 'found' : 'none', selected: match ? true : i.selected }
      : i,
  );
}

/**
 * Résultat des conversions réussies : fichier produit (chemin, format, débit) et tags retenus.
 * Rend la même liste si aucune conversion n'a abouti depuis le dernier passage.
 */
export function applyConversionResults(items, byId) {
  const succeeded = i => !i.converted && jobOf(i, byId)?.status === 'SUCCEEDED';
  if (!items.some(succeeded)) return items;
  return items.map(i => {
    if (!succeeded(i)) return i;
    const r = byId.get(i.jobId).result ?? {};
    return {
      ...i,
      converted: true,
      selected: false,
      path: r.localPath ?? i.path,
      dbPath: r.dbPath ?? i.dbPath,
      format: (r.format ?? i.format).toUpperCase(),
      bitrate: r.bitrate ?? i.bitrate,
      title: r.title || i.title,
      artist: r.artist || i.artist,
      album: r.album ?? i.album,
      matchState: 'found',
    };
  });
}

function sortValue(i, key) {
  switch (key) {
    case 'title':
      return i.title.toLowerCase();
    case 'artist':
      return i.artist.toLowerCase();
    case 'path':
      return i.path.toLowerCase();
    case 'format':
      return `${i.format}|${String(i.bitrate).padStart(5, '0')}`;
    default:
      return i.matchState === 'found' ? 1 : 0;
  }
}

/** Lignes affichées : filtre « déjà au format cible », puis tri par colonne (aucun si `key` est nul). */
export function visibleItems(items, { hideTarget, target, key, asc }) {
  const list = hideTarget ? items.filter(i => !isAtTarget(i, target.format, target.quality)) : items;
  if (!key) return list;
  const dir = asc ? 1 : -1;
  return [...list].sort((a, b) => {
    const va = sortValue(a, key);
    const vb = sortValue(b, key);
    return (va < vb ? -1 : va > vb ? 1 : 0) * dir;
  });
}

/** Progression globale d'un lot de conversions (0..1, arrondie au centième inférieur). */
export function batchProgressOf(batchJobs) {
  if (!batchJobs.length) return 0;
  const sum = batchJobs.reduce(
    (s, j) => s + (j.status === 'SUCCEEDED' ? 100 : j.status === 'RUNNING' ? j.progress * 100 : 0),
    0,
  );
  return Math.floor(sum / batchJobs.length) / 100;
}
