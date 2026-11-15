import { gql } from '@apollo/client';
import { api } from '../../core/graphql/api';

/** Piste Deezer d'une ligne (charge utile de la tâche « download » ou ligne en attente). */
const TRACK_FIELDS =
  'id title artist artistId album albumId duration previewUrl coverSmall coverBig link trackPosition diskNumber';
const JOB_FIELDS = `id service type title status progress done failed total message error attempts groupKey payload result createdAt updatedAt startedAt finishedAt`;

const JOBS_Q = gql`query DownloadJobs { downloadJobs(type: "download") { ${JOB_FIELDS} } }`;
const PENDING_Q = gql`query PendingDownloads { pendingDownloads { id addedAt track { ${TRACK_FIELDS} } } }`;
const PRESENCE_Q = gql`
  query Presence($items: [PresenceQueryInput!]!) {
    presence(items: $items) {
      key
      state
      reason
      files {
        path
        reason
      }
    }
  }
`;
const ADD_PENDING = gql`
  mutation AddPending($tracks: [DownloadTrackInput!]!) {
    addPendingDownloads(tracks: $tracks) {
      id
    }
  }
`;
const REMOVE_PENDING = gql`
  mutation RemovePending($ids: [UUID!]!) {
    removePendingDownloads(ids: $ids)
  }
`;
const ENQUEUE = gql`
  mutation Enqueue($tracks: [DownloadTrackInput!]!, $batchId: String, $pendingIds: [UUID!]) {
    enqueueDownloads(tracks: $tracks, batchId: $batchId, pendingIds: $pendingIds) {
      id
    }
  }
`;
const RETRY = gql`
  mutation RetryDownloads($ids: [UUID!]!) {
    retryDownloadJobs(ids: $ids) {
      id
    }
  }
`;
const REMOVE_JOBS = gql`
  mutation RemoveJobs($ids: [UUID!]!) {
    removeDownloadJobs(ids: $ids)
  }
`;
const PURGE = gql`
  mutation Purge {
    purgeDownloads(type: "download", succeededOnly: true)
  }
`;
const QUEUE_CHANGED = gql`
  subscription DownloadQueueChanged {
    downloadQueueChanged {
      removedJobIds
      pendingChanged
    }
  }
`;

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
