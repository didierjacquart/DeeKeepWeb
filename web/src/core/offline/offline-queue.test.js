import { beforeEach, describe, expect, it, vi } from 'vitest';
import { gql } from '@apollo/client';
import { print } from 'graphql';
import { get, del } from 'idb-keyval';
import { notify } from '../ui/notify';

const mutate = vi.fn();
vi.mock('../graphql/apollo', () => ({ getApollo: () => ({ mutate }) }));

const { OfflineQueueService } = await import('./offline-queue');

const ADD = print(gql`
  mutation Add($x: Int!) {
    add(x: $x)
  }
`);

describe('OfflineQueueService', () => {
  beforeEach(async () => {
    mutate.mockReset();
    await del('dk-offline-queue-v1');
  });

  it('garde l’action dans IndexedDB et prévient l’utilisateur', async () => {
    const info = vi.spyOn(notify, 'info');
    const queue = new OfflineQueueService();
    await queue.enqueue('Ajouter', ADD, { x: 1 }, 'cle-1');
    expect(queue.state.items).toMatchObject([{ id: 'cle-1', label: 'Ajouter', attempts: 0 }]);
    expect(await get('dk-offline-queue-v1')).toHaveLength(1);
    expect(info).toHaveBeenCalledWith('Hors ligne : « Ajouter » sera envoyé dès le retour de la connexion.');
  });

  it('retrouve la file d’une session précédente', async () => {
    const first = new OfflineQueueService();
    await first.enqueue('Ajouter', ADD, { x: 1 }, 'cle-1');
    const second = new OfflineQueueService();
    await vi.waitFor(() => expect(second.state.items).toHaveLength(1));
  });

  it('rejoue dans l’ordre avec la clé d’idempotence, puis vide la file', async () => {
    mutate.mockResolvedValue({ data: { add: true } });
    const success = vi.spyOn(notify, 'success');
    const queue = new OfflineQueueService();
    await queue.enqueue('Un', ADD, { x: 1 }, 'k1');
    await queue.enqueue('Deux', ADD, { x: 2 }, 'k2');
    await queue.replay();
    expect(mutate.mock.calls.map(c => [c[0].variables, c[0].context.headers['X-Idempotency-Key']])).toEqual([
      [{ x: 1 }, 'k1'],
      [{ x: 2 }, 'k2'],
    ]);
    expect(queue.state.items).toEqual([]);
    expect(success).toHaveBeenLastCalledWith('« Deux » envoyé.');
  });

  it('s’arrête au premier échec réseau et compte la tentative', async () => {
    mutate.mockRejectedValue(new Error('Failed to fetch'));
    const queue = new OfflineQueueService();
    await queue.enqueue('Un', ADD, { x: 1 }, 'k1');
    await queue.enqueue('Deux', ADD, { x: 2 }, 'k2');
    await queue.replay();
    expect(mutate).toHaveBeenCalledTimes(1);
    expect(queue.state.items[0]).toMatchObject({ id: 'k1', attempts: 1, lastError: 'Failed to fetch' });
    expect(queue.state.replaying).toBe(false);
  });

  it('retire une action refusée par le serveur et l’annonce', async () => {
    mutate.mockResolvedValue({ error: { message: 'Titre inconnu' } });
    const error = vi.spyOn(notify, 'error');
    const queue = new OfflineQueueService();
    await queue.enqueue('Un', ADD, { x: 1 }, 'k1');
    await queue.replay();
    expect(queue.state.items).toEqual([]);
    expect(error).toHaveBeenCalledWith('« Un » a échoué : Titre inconnu');
  });
});
