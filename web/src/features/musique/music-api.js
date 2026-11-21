import { api } from '../../core/api/api';
import { endpoint, serverEvent } from '../../core/api/endpoints';

const JOBS_Q = endpoint('DownloadJobs', 'downloadJobs', 'GET /api/downloads/jobs?type=download');
const PENDING_Q = endpoint('PendingDownloads', 'pendingDownloads', 'GET /api/downloads/pending');
const PRESENCE_Q = endpoint('Presence', 'presence', 'POST /api/library/presence', { body: v => v.items });
const ADD_PENDING = endpoint('AddPending', 'addPendingDownloads', 'POST /api/downloads/pending');
const REMOVE_PENDING = endpoint('RemovePending', 'removePendingDownloads', 'POST /api/downloads/pending/remove');
const ENQUEUE = endpoint('Enqueue', 'enqueueDownloads', 'POST /api/downloads/jobs');
const RETRY = endpoint('RetryDownloads', 'retryDownloadJobs', 'POST /api/downloads/jobs/retry');
const REMOVE_JOBS = endpoint('RemoveJobs', 'removeDownloadJobs', 'POST /api/downloads/jobs/remove');
const PURGE = endpoint('Purge', 'purgeDownloads', 'POST /api/downloads/jobs/purge', {
  body: () => ({ type: 'download', succeededOnly: true }),
});
const QUEUE_CHANGED = serverEvent('DownloadQueueChanged', 'downloadQueueChanged', 'downloads.changed');

/** La file telle que le serveur la connaît : tâches « download » et lignes en attente. */
export async function fetchQueue() {
  const [jobsData, pendingData] = await Promise.all([api.query(JOBS_Q), api.query(PENDING_Q)]);
  return { jobs: jobsData.downloadJobs ?? [], pending: pendingData.pendingDownloads ?? [] };
}

/** Pastilles de présence des lignes, par identifiant de ligne. */
export async function fetchPresence(rows) {
  const data = await api.query(PRESENCE_Q, {
    items: rows.map(r => ({ key: r.id, title: r.track.title, artist: r.track.artist, album: r.track.album })),
  });
  return new Map((data.presence ?? []).map(v => [v.key, { state: v.state, reason: v.reason }]));
}

/** Changements de la file faits ailleurs (autre onglet, autre appareil, fin de tâche). */
export function queueChanges() {
  return api.subscribe(QUEUE_CHANGED);
}

export function addPending(tracks) {
  return api.mutate(ADD_PENDING, { tracks }, { label: `Ajouter ${tracks.length} titre(s) à la liste` });
}

/** Lignes en attente → tâches « download » (un même lot). */
export function enqueueDownloads(tracks, pendingIds) {
  return api.mutate(
    ENQUEUE,
    { tracks, batchId: crypto.randomUUID().replaceAll('-', ''), pendingIds },
    { label: `Extraire ${pendingIds.length} titre(s)` },
  );
}

export function retryDownloads(ids) {
  return api.mutate(RETRY, { ids }, { label: 'Réessayer les téléchargements en échec' });
}

export function removePending(ids) {
  return api.mutate(REMOVE_PENDING, { ids }, { label: 'Retirer des lignes de la liste' });
}

export function removeJobs(ids) {
  return api.mutate(REMOVE_JOBS, { ids }, { label: 'Retirer des lignes de la liste' });
}

export function purgeDownloads() {
  return api.mutate(PURGE, {}, { label: 'Purger les téléchargés' });
}
