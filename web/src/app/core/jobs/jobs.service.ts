import { Injectable, computed, inject, signal } from '@angular/core';
import { gql } from '@apollo/client';
import { GqlService } from '../graphql/gql.service';
import { ConnectivityService } from '../offline/connectivity.service';

export type JobStatus = 'QUEUED' | 'RUNNING' | 'SUCCEEDED' | 'FAILED' | 'CANCELLED';

export interface Job {
  id: string;
  service: string;
  type: string;
  title: string | null;
  status: JobStatus;
  progress: number;
  done: number;
  failed: number;
  total: number;
  message: string | null;
  error: string | null;
  attempts: number;
  groupKey: string | null;
  result: unknown;
  createdAt: number;
  updatedAt: number;
  startedAt: number | null;
  finishedAt: number | null;
}

export interface JobItem {
  jobId: string;
  type: string;
  itemKey: string;
  status: string;
  message: string | null;
  data: unknown;
}

export const JOB_FIELDS = `
  id service type title status progress done failed total message error attempts groupKey result
  createdAt updatedAt startedAt finishedAt
`;

const JOBS_QUERY = gql`query Jobs($activeOnly: Boolean!) { jobs(activeOnly: $activeOnly) { ${JOB_FIELDS} } }`;
const JOB_UPDATED = gql`subscription JobUpdated { jobUpdated { ${JOB_FIELDS} } }`;
const JOB_ITEM = gql`subscription JobItem($jobId: UUID, $type: String) { jobItem(jobId: $jobId, type: $type) { jobId type itemKey status message data } }`;
const CANCEL = gql`mutation CancelJob($service: String!, $id: UUID!) { cancelJob(service: $service, id: $id) { ${JOB_FIELDS} } }`;
const RETRY = gql`mutation RetryJob($service: String!, $id: UUID!) { retryJob(service: $service, id: $id) { ${JOB_FIELDS} } }`;

export const isActive = (j: Job) => j.status === 'QUEUED' || j.status === 'RUNNING';

/**
 * Vue unique des tâches de fond de tous les services : téléchargements,
 * analyses, rangements, archivages… Alimentée par l'abonnement jobUpdated,
 * recalée par une requête à chaque reconnexion (rien n'est perdu pendant une
 * coupure : les tâches continuent sur le serveur).
 */
@Injectable({ providedIn: 'root' })
export class JobsService {
  private readonly gql = inject(GqlService);
  private readonly connectivity = inject(ConnectivityService);

  private readonly map = signal(new Map<string, Job>());
  readonly all = computed(() => [...this.map().values()].sort((a, b) => b.createdAt - a.createdAt));
  readonly active = computed(() => this.all().filter(isActive));

  constructor() {
    this.gql.subscribe<{ jobUpdated: Job }>(JOB_UPDATED).subscribe({
      next: d => d?.jobUpdated && this.upsert(d.jobUpdated),
      error: () => undefined,
    });
    this.connectivity.onReconnect(() => void this.load());
    void this.load();
  }

  async load(): Promise<void> {
    try {
      const data = await this.gql.query<{ jobs: Job[] }>(JOBS_QUERY, { activeOnly: false });
      const next = new Map<string, Job>();
      for (const job of data.jobs ?? []) next.set(job.id, job);
      this.map.set(next);
    } catch {
      // Hors ligne : on garde ce qu'on sait.
    }
  }

  upsert(job: Job): void {
    this.map.update(m => {
      const previous = m.get(job.id);
      if (previous && previous.updatedAt > job.updatedAt) return m;
      const next = new Map(m);
      next.set(job.id, { ...previous, ...job, result: job.result ?? previous?.result ?? null });
      return next;
    });
  }

  forget(ids: string[]): void {
    this.map.update(m => {
      const next = new Map(m);
      for (const id of ids) next.delete(id);
      return next;
    });
  }

  byType(...types: string[]) {
    return computed(() => this.all().filter(j => types.includes(j.type)));
  }

  byGroup(groupKey: string) {
    return computed(() => this.all().filter(j => j.groupKey === groupKey));
  }

  get(id: string | null | undefined) {
    return computed(() => (id ? this.map().get(id) ?? null : null));
  }

  /** Résultats élément par élément d'une tâche par lot (ou de toutes celles d'un type). */
  items(filter: { jobId?: string; type?: string }) {
    return this.gql.subscribe<{ jobItem: JobItem }>(JOB_ITEM, filter);
  }

  async cancel(job: Pick<Job, 'service' | 'id'>): Promise<void> {
    const r = await this.gql.mutate<{ cancelJob: Job }>(CANCEL, { service: job.service, id: job.id }, { label: 'Arrêter la tâche' });
    if (r.data?.cancelJob) this.upsert(r.data.cancelJob);
  }

  async retry(job: Pick<Job, 'service' | 'id'>): Promise<void> {
    const r = await this.gql.mutate<{ retryJob: Job }>(RETRY, { service: job.service, id: job.id }, { label: 'Relancer la tâche' });
    if (r.data?.retryJob) this.upsert(r.data.retryJob);
  }
}
