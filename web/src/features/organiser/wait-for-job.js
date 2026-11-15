import { jobs } from '../../core/jobs/jobs';
import { isTerminal } from './organize-labels';

/** Attend la fin d'une tâche (suivie par jobs, rattrapée à la reconnexion), relue toutes les `ms` millisecondes. */
export async function waitForJob(job, ms) {
  jobs.upsert(job);
  for (;;) {
    const current = jobs.get(job.id) ?? job;
    if (isTerminal(current.status)) return current;
    await new Promise(res => setTimeout(res, ms));
  }
}
