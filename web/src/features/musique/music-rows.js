import { formatDuration } from '../../shared/format';

/**
 * Ligne de la file : une piste en attente (conservée sur le serveur) ou une tâche « download ».
 * @typedef {object} QueueRow
 * @property {string} id
 * @property {'pending' | 'job'} kind
 * @property {object | null} job
 * @property {object} track Piste Deezer de la ligne.
 * @property {number} seq Ordre d'arrivée (départage des tris).
 * @property {string} status
 * @property {'muted' | 'info' | 'ok' | 'err'} color
 * @property {string | null} tooltip
 * @property {number} progress 0..100
 * @property {boolean} busy
 * @property {boolean} canCancel
 * @property {boolean} failed
 * @property {boolean} succeeded
 * @property {string | null} dbPath Fichier extrait (chemin en base).
 */

export function emptyTrack(title) {
  return {
    id: 0,
    title: title ?? '?',
    artist: '',
    album: null,
    duration: 0,
    coverSmall: null,
    coverBig: null,
    previewUrl: null,
  };
}

/** Statut, couleur et progression d'une ligne (textes et couleurs du spec 01 §2.3.3). */
export function toRow(base) {
  const { job } = base;
  const row = {
    ...base,
    status: 'En attente',
    color: 'muted',
    tooltip: null,
    progress: 0,
    busy: false,
    canCancel: false,
    failed: false,
    succeeded: false,
    dbPath: null,
  };
  if (!job) return row;
  switch (job.status) {
    case 'QUEUED':
      row.status = job.attempts > 0 && job.error ? `Nouvelle tentative (${job.error})` : "En file d'attente";
      row.canCancel = true;
      break;
    case 'RUNNING':
      row.status = job.message || 'Initialisation...';
      row.color = 'info';
      row.progress = Math.round((job.progress ?? 0) * 100);
      row.busy = true;
      row.canCancel = true;
      break;
    case 'SUCCEEDED': {
      const n = job.result?.attempts ?? 1;
      row.status = n > 1 ? `Téléchargé ✓ (${n} essais)` : 'Téléchargé ✓';
      row.color = 'ok';
      row.progress = 100;
      row.succeeded = true;
      row.dbPath = job.result?.dbPath ?? null;
      row.tooltip = job.result?.videoTitle ? `Vidéo : ${job.result.videoTitle}` : null;
      break;
    }
    case 'FAILED':
      row.status = job.message && job.message !== 'Terminé' ? job.message : 'Erreur';
      row.color = 'err';
      row.tooltip = job.error;
      row.failed = true;
      break;
    default:
      row.status = 'Annulé';
      row.color = 'err';
      row.failed = true;
  }
  return row;
}

/**
 * Lignes affichées : les lignes en attente, puis les tâches par date de création. L'état
 * vivant d'une tâche (abonnement jobUpdated) l'emporte sur celui de la requête, mais la
 * charge utile (la piste) ne vient que de la requête.
 * @returns {QueueRow[]}
 */
export function buildRows(base, pending, live) {
  const liveMap = new Map(live.map(j => [j.id, j]));
  const pendingRows = pending.map((p, i) => toRow({ id: p.id, kind: 'pending', job: null, track: p.track, seq: i }));
  const jobRows = [...base]
    .sort((a, b) => a.createdAt - b.createdAt)
    .map((b, i) => {
      const l = liveMap.get(b.id);
      const job = l ? { ...b, ...l, payload: b.payload, result: l.result ?? b.result } : b;
      return toRow({
        id: b.id,
        kind: 'job',
        job,
        track: b.payload?.track ?? emptyTrack(b.title),
        seq: pendingRows.length + i,
      });
    });
  return [...pendingRows, ...jobRows];
}

/** Progression globale (0..1) : moyenne des progressions, arrondie au pour cent inférieur. */
export function globalProgress(rows) {
  return rows.length ? Math.floor(rows.reduce((s, r) => s + r.progress, 0) / rows.length) / 100 : 0;
}

/** Lignes à extraire : en attente, ou en échec / annulées ; seules les cochées si l'on en a coché. */
export function eligibleRows(rows, selected) {
  const all = rows.filter(r => r.kind === 'pending' || r.failed);
  const chosen = all.filter(r => selected.has(r.id));
  return chosen.length ? chosen : all;
}

/** « 3:25 », complété du pourcentage pendant et après l'extraction. */
export function progressLabel(r) {
  const duration = formatDuration(r.track.duration);
  return r.busy || r.succeeded ? `${duration} - ${Math.round(r.progress)}%` : duration;
}

/** Clé des pastilles : change quand l'ensemble des titres ou le nombre de fichiers extraits change. */
export function presenceKey(rows) {
  return rows.map(r => `${r.id}|${r.succeeded ? 1 : 0}`).join(',');
}

/** Tâches vivantes absentes de la requête (créées ailleurs), jointes en une clé. */
export function unknownLiveKey(base, live) {
  const known = new Set(base.map(j => j.id));
  return live
    .filter(j => !known.has(j.id))
    .map(j => j.id)
    .join(',');
}
