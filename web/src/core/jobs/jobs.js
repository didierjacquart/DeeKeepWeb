import { gql } from '@apollo/client';

import { Store } from '../../lib/store';
import { api } from '../graphql/api';
import { connectivity } from '../offline/connectivity';

/**
 * Tâche de fond d'un service (statuts QUEUED, RUNNING, SUCCEEDED, FAILED, CANCELLED).
 * @typedef {object} Job
 * @property {string} id
 * @property {string} service
 * @property {string} type ex. « download », « quality.scan »
 * @property {string | null} title
 * @property {'QUEUED' | 'RUNNING' | 'SUCCEEDED' | 'FAILED' | 'CANCELLED'} status
 * @property {number} progress 0..1
 * @property {number} done
 * @property {number} failed
 * @property {number} total
 * @property {string | null} message
 * @property {string | null} error
 * @property {number} attempts
 * @property {string | null} groupKey
 * @property {unknown} result
 * @property {number} createdAt
 * @property {number} updatedAt
 * @property {number | null} startedAt
 * @property {number | null} finishedAt
 */

/**
 * Résultat élément par élément d'une tâche par lot.
 * @typedef {object} JobItem
 * @property {string} jobId
 * @property {string} type
 * @property {string} itemKey
 * @property {string} status
 * @property {string | null} message
 * @property {unknown} data
 */

export const JOB_FIELDS = `
  id service type title status progress done failed total message error attempts groupKey result
  createdAt updatedAt startedAt finishedAt
`;

const JOBS_QUERY = gql`query Jobs($activeOnly: Boolean!) { jobs(activeOnly: $activeOnly) { ${JOB_FIELDS} } }`;
const JOB_UPDATED = gql`subscription JobUpdated { jobUpdated { ${JOB_FIELDS} } }`;
const JOB_ITEM = gql`
  subscription JobItem($jobId: UUID, $type: String) {
    jobItem(jobId: $jobId, type: $type) {
      jobId
      type
      itemKey
      status
      message
      data
    }
  }
`;
const CANCEL = gql`mutation CancelJob($service: String!, $id: UUID!) { cancelJob(service: $service, id: $id) { ${JOB_FIELDS} } }`;
const RETRY = gql`mutation RetryJob($service: String!, $id: UUID!) { retryJob(service: $service, id: $id) { ${JOB_FIELDS} } }`;

export const isActive = j => j.status === 'QUEUED' || j.status === 'RUNNING';

function derive(byId) {
  const all = [...byId.values()].sort((a, b) => b.createdAt - a.createdAt);
  return { byId, all, active: all.filter(isActive) };
}

/**
 * Vue unique des tâches de fond de tous les services : téléchargements,
 * analyses, rangements, archivages… Alimentée par l'abonnement jobUpdated,
 * recalée par une requête à chaque reconnexion (rien n'est perdu pendant une
 * coupure : les tâches continuent sur le serveur).
 */
export class JobsService extends Store {
  started = false;

  constructor() {
    super(derive(new Map()));
  }

  /** Démarre l'écoute (une fois la session ouverte). */
  start() {
    if (this.started) return;
    this.started = true;
    api.subscribe(JOB_UPDATED).subscribe({
      next: d => d?.jobUpdated && this.upsert(d.jobUpdated),
      error: () => undefined,
    });
    connectivity.onReconnect(() => void this.load());
    void this.load();
  }

  async load() {
    try {
      const data = await api.query(JOBS_QUERY, { activeOnly: false });
      const next = new Map();
      for (const job of data.jobs ?? []) next.set(job.id, job);
      this.setState(derive(next));
    } catch {
      // Hors ligne : on garde ce qu'on sait.
    }
  }

  upsert(job) {
    const m = this.state.byId;
    const previous = m.get(job.id);
    if (previous && previous.updatedAt > job.updatedAt) return;
    const next = new Map(m);
    next.set(job.id, { ...previous, ...job, result: job.result ?? previous?.result ?? null });
    this.setState(derive(next));
  }

  forget(ids) {
    if (!ids.length) return;
    const next = new Map(this.state.byId);
    for (const id of ids) next.delete(id);
    this.setState(derive(next));
  }

  get(id) {
    return id ? (this.state.byId.get(id) ?? null) : null;
  }

  /** Résultats élément par élément d'une tâche par lot (ou de toutes celles d'un type). */
  items(filter) {
    return api.subscribe(JOB_ITEM, filter);
  }

  async cancel(job) {
    const r = await api.mutate(CANCEL, { service: job.service, id: job.id }, { label: 'Arrêter la tâche' });
    if (r.data?.cancelJob) this.upsert(r.data.cancelJob);
  }

  async retry(job) {
    const r = await api.mutate(RETRY, { service: job.service, id: job.id }, { label: 'Relancer la tâche' });
    if (r.data?.retryJob) this.upsert(r.data.retryJob);
  }
}

export const jobs = new JobsService();
