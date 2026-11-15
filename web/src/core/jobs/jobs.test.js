import { describe, expect, it, vi } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { JobsService, isActive, jobs } from './jobs';
import { useJob, useJobsByGroup, useJobsByType } from './useJobs';
import { makeJob } from '../../test/render';
import { mockApi } from '../../test/mock-api';

describe('JobsService', () => {
  it('classe les tâches des plus récentes aux plus anciennes et isole les actives', () => {
    const service = new JobsService();
    service.upsert(makeJob({ id: 'a', createdAt: 1, status: 'SUCCEEDED' }));
    service.upsert(makeJob({ id: 'b', createdAt: 3, status: 'RUNNING' }));
    service.upsert(makeJob({ id: 'c', createdAt: 2, status: 'QUEUED' }));
    expect(service.state.all.map(j => j.id)).toEqual(['b', 'c', 'a']);
    expect(service.state.active.map(j => j.id)).toEqual(['b', 'c']);
  });

  it('ignore une mise à jour plus ancienne que celle qu’elle connaît', () => {
    const service = new JobsService();
    service.upsert(makeJob({ id: 'a', updatedAt: 10, progress: 0.8 }));
    service.upsert(makeJob({ id: 'a', updatedAt: 5, progress: 0.2 }));
    expect(service.get('a').progress).toBe(0.8);
  });

  it('garde le résultat connu quand la mise à jour n’en porte pas', () => {
    const service = new JobsService();
    service.upsert(makeJob({ id: 'a', updatedAt: 1, result: { dbPath: 'x' } }));
    service.upsert(makeJob({ id: 'a', updatedAt: 2, result: null, status: 'SUCCEEDED' }));
    expect(service.get('a')).toMatchObject({ status: 'SUCCEEDED', result: { dbPath: 'x' } });
  });

  it('oublie les tâches retirées', () => {
    const service = new JobsService();
    service.upsert(makeJob({ id: 'a' }));
    service.forget(['a']);
    expect(service.get('a')).toBeNull();
    expect(service.state.all).toEqual([]);
  });

  it('charge toutes les tâches au démarrage et suit l’abonnement', async () => {
    const server = mockApi({ Jobs: { jobs: [makeJob({ id: 'srv', status: 'QUEUED' })] } });
    const service = new JobsService();
    service.start();
    await vi.waitFor(() => expect(service.get('srv')).not.toBeNull());
    expect(server.queries('Jobs')).toEqual([{ activeOnly: false }]);
    server.emit('JobUpdated', { jobUpdated: makeJob({ id: 'srv', status: 'RUNNING', updatedAt: 9 }) });
    expect(service.get('srv').status).toBe('RUNNING');
  });

  it('garde ce qu’elle sait quand le serveur ne répond pas', async () => {
    mockApi({ Jobs: new Error('hors ligne') });
    const service = new JobsService();
    service.upsert(makeJob({ id: 'a' }));
    await service.load();
    expect(service.get('a')).not.toBeNull();
  });

  it('arrête une tâche par le service qui la porte', async () => {
    const server = mockApi({ CancelJob: { cancelJob: makeJob({ id: 'a', status: 'CANCELLED', updatedAt: 5 }) } });
    const service = new JobsService();
    await service.cancel({ service: 'downloader', id: 'a' });
    expect(server.mutations('CancelJob')).toEqual([{ service: 'downloader', id: 'a' }]);
    expect(service.get('a').status).toBe('CANCELLED');
  });

  it('reconnaît les tâches actives', () => {
    expect(isActive(makeJob({ status: 'QUEUED' }))).toBe(true);
    expect(isActive(makeJob({ status: 'FAILED' }))).toBe(false);
  });
});

describe('hooks des tâches', () => {
  it('filtrent par type, par groupe et par identifiant', () => {
    const { result } = renderHook(() => ({
      downloads: useJobsByType('download', 'convert'),
      batch: useJobsByGroup('lot-1'),
      one: useJob('b'),
    }));
    act(() => {
      jobs.upsert(makeJob({ id: 'a', type: 'download', groupKey: 'lot-1' }));
      jobs.upsert(makeJob({ id: 'b', type: 'quality.scan', groupKey: 'lot-1' }));
      jobs.upsert(makeJob({ id: 'c', type: 'convert' }));
    });
    expect(result.current.downloads.map(j => j.id).sort()).toEqual(['a', 'c']);
    expect(result.current.batch.map(j => j.id).sort()).toEqual(['a', 'b']);
    expect(result.current.one.id).toBe('b');
  });
});
