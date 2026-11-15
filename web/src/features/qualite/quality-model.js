import { mediaUrl } from '../../shared/urls';
import { isActive } from '../../core/jobs/jobs';

/** DEFECT_ORDER et DEFECT_LABELS de l'original. */
export const DEFECTS = [
  { id: 'hiss', label: 'Souffle' },
  { id: 'crackle', label: 'Craquements' },
  { id: 'clipping', label: 'Saturation' },
  { id: 'gaps', label: 'Coupures' },
  { id: 'duration', label: 'Durée' },
  { id: 'tail', label: 'Blanc de fin' },
  { id: 'head', label: 'Blanc de début' },
];

/** Défauts que seule l'analyse approfondie sait voir. */
export const DEEP_DEFECTS = ['crackle'];

/** Filtre « Afficher » initial : tous les défauts. */
export const ALL_SHOWN = {
  hiss: true,
  crackle: true,
  clipping: true,
  gaps: true,
  duration: true,
  tail: true,
  head: true,
};

export function scoreClass(row) {
  if (row.error) return '';
  return row.score >= 100 ? 'ok' : row.score < 75 ? 'bad' : 'warn';
}

export function hasDefect(row, id) {
  return row.defects.some(d => d.id === id);
}

/** Couleur de la colonne Défauts : illisible, défauts (selon la note) ou sain. */
export function defectsClass(row) {
  return row.error ? 'bad' : row.defects.length ? scoreClass(row) : 'ok';
}

/** Titre du lecteur pour une ligne. */
export function toTrack(r) {
  return {
    id: r.path,
    title: r.title || r.path,
    artist: r.artist,
    album: r.album,
    src: mediaUrl(r.path),
    duration: r.duration,
    // Format, débit, note et défauts : ce qu'il faut écouter.
    context: r.cells['context'] ?? null,
  };
}

/** Tâche de retéléchargement de la ligne, si elle est connue. */
export function rowJob(row, redownloadById) {
  return (row.redownloadJobId ? redownloadById.get(row.redownloadJobId) : null) ?? null;
}

/** Un retéléchargement ou une réanalyse est-il en cours pour cette ligne ? */
export function rowBusy(row, redownloadById) {
  if (row.status === 'reanalyzing') return true;
  const job = rowJob(row, redownloadById);
  return !!job && isActive(job);
}

/** Colonne STATUT : avancement de la tâche d'extraction, puis état tenu par le serveur. */
export function rowStatus(row, job) {
  if (job && job.status === 'FAILED') return { text: 'Échec', tip: job.error ?? row.statusDetail, cls: 'bad' };
  if (job && job.status === 'CANCELLED') return { text: 'Échec', tip: 'Retéléchargement annulé', cls: 'bad' };
  if (job && isActive(job)) {
    if (job.status === 'QUEUED') return { text: 'En attente…', tip: null, cls: '' };
    const pct = Math.round(job.progress * 100);
    return { text: `${job.message ?? 'Initialisation…'} — ${pct}%`, tip: null, cls: '' };
  }
  if (job && job.status === 'SUCCEEDED' && row.status !== 'replaced' && row.status !== 'replaced_error')
    return { text: 'Réanalyse…', tip: null, cls: '' };
  const text = row.cells['status'] ?? '';
  return {
    text,
    tip: row.tooltips['status'] ?? null,
    cls: text.includes('persistant') ? 'warn' : text.includes('corrigé') ? 'ok' : '',
  };
}

/** Lignes reçues par l'abonnement : ajoutées ou remplacées, puis retirées. */
export function mergeEntries(rows, update) {
  const next = new Map(rows);
  for (const e of update.entries) next.set(e.path, e);
  for (const p of update.removed ?? []) next.delete(p);
  return next;
}
