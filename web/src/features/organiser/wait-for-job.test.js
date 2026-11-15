import { afterEach, describe, expect, it } from 'vitest';
import { jobs } from '../../core/jobs/jobs';
import { makeJob } from '../../test/render';
import { waitForJob } from './wait-for-job';

describe('waitForJob', () => {
  afterEach(() => jobs.forget(['job-w']));

  it('rend aussitôt une tâche déjà terminée', async () => {
    const done = await waitForJob(makeJob({ id: 'job-w', status: 'SUCCEEDED' }), 10);
    expect(done.status).toBe('SUCCEEDED');
  });

  it('attend la fin d’une tâche suivie par jobs', async () => {
    const waiting = waitForJob(makeJob({ id: 'job-w', status: 'RUNNING' }), 10);
    setTimeout(() => jobs.upsert(makeJob({ id: 'job-w', status: 'FAILED', updatedAt: 2 })), 25);
    expect((await waiting).status).toBe('FAILED');
  });
});
