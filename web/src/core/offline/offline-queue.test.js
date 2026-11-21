import { beforeEach, describe, expect, it, vi } from 'vitest';
import { get, del } from 'idb-keyval';
import { notify } from '../ui/notify';
import { ApiError } from '../api/http';

const send = vi.fn();
vi.mock('../api/http', async importOriginal => ({ ...(await importOriginal()), send: (...args) => send(...args) }));

const { OfflineQueueService } = await import('./offline-queue');

const ADD = x => ({ method: 'POST', url: '/api/add', body: { x } });

describe('OfflineQueueService', () => {
  beforeEach(async () => {
    send.mockReset();
    await del('dk-offline-queue-v2');
  });

  it('garde l’action dans IndexedDB et prévient l’utilisateur', async () => {
    const info = vi.spyOn(notify, 'info');
    const queue = new OfflineQueueService();
    await queue.enqueue('Ajouter', ADD(1), 'cle-1');
    expect(queue.state.items).toMatchObject([{ id: 'cle-1', label: 'Ajouter', attempts: 0, request: ADD(1) }]);
    expect(await get('dk-offline-queue-v2')).toHaveLength(1);
    expect(info).toHaveBeenCalledWith('Hors ligne : « Ajouter » sera envoyé dès le retour de la connexion.');
  });

  it('retrouve la file d’une session précédente', async () => {
    const first = new OfflineQueueService();
    await first.enqueue('Ajouter', ADD(1), 'cle-1');
    const second = new OfflineQueueService();
    await vi.waitFor(() => expect(second.state.items).toHaveLength(1));
  });

  it('rejoue dans l’ordre avec la clé d’idempotence, puis vide la file', async () => {
    send.mockResolvedValue({ ok: true });
    const success = vi.spyOn(notify, 'success');
    const queue = new OfflineQueueService();
    await queue.enqueue('Un', ADD(1), 'k1');
    await queue.enqueue('Deux', ADD(2), 'k2');
    await queue.replay();
    expect(send.mock.calls).toEqual([
      [ADD(1), { 'X-Idempotency-Key': 'k1' }],
      [ADD(2), { 'X-Idempotency-Key': 'k2' }],
    ]);
    expect(queue.state.items).toEqual([]);
    expect(success).toHaveBeenLastCalledWith('« Deux » envoyé.');
  });

  it('s’arrête au premier échec réseau et compte la tentative', async () => {
    send.mockRejectedValue(new ApiError('Serveur injoignable', 0, 'NETWORK'));
    const queue = new OfflineQueueService();
    await queue.enqueue('Un', ADD(1), 'k1');
    await queue.enqueue('Deux', ADD(2), 'k2');
    await queue.replay();
    expect(send).toHaveBeenCalledTimes(1);
    expect(queue.state.items[0]).toMatchObject({ id: 'k1', attempts: 1, lastError: 'Serveur injoignable' });
    expect(queue.state.replaying).toBe(false);
  });

  it('garde l’action quand un service est momentanément indisponible', async () => {
    send.mockRejectedValue(new ApiError('Service « library » momentanément indisponible.', 503, 'SERVICE_UNAVAILABLE'));
    const queue = new OfflineQueueService();
    await queue.enqueue('Un', ADD(1), 'k1');
    await queue.replay();
    expect(queue.state.items).toHaveLength(1);
  });

  it('retire une action refusée par le serveur et l’annonce, puis passe à la suivante', async () => {
    send.mockRejectedValueOnce(new ApiError('Titre inconnu', 400, 'INVALID_ARGUMENT')).mockResolvedValueOnce({});
    const error = vi.spyOn(notify, 'error');
    const queue = new OfflineQueueService();
    await queue.enqueue('Un', ADD(1), 'k1');
    await queue.enqueue('Deux', ADD(2), 'k2');
    await queue.replay();
    expect(queue.state.items).toEqual([]);
    expect(error).toHaveBeenCalledWith('« Un » a échoué : Titre inconnu');
  });
});
