import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { gql } from '@apollo/client';
import { of } from 'rxjs';
import { connectivity } from '../offline/connectivity';
import { offlineQueue } from '../offline/offline-queue';

const client = { query: vi.fn(), mutate: vi.fn(), watchQuery: vi.fn(), subscribe: vi.fn(), refetchQueries: vi.fn() };
vi.mock('./apollo', () => ({ getApollo: () => client }));

const { GraphQLApi } = await import('./api');

const Q = gql`
  query Tracks {
    tracks
  }
`;
const M = gql`
  mutation Save($v: Int!) {
    save(v: $v)
  }
`;

describe('GraphQLApi', () => {
  let api;
  let enqueue;

  beforeEach(() => {
    Object.values(client).forEach(fn => fn.mockReset());
    enqueue = vi.spyOn(offlineQueue, 'enqueue').mockResolvedValue({});
    connectivity.setState({ browserOnline: true });
    api = new GraphQLApi();
  });

  afterEach(() => connectivity.setState({ browserOnline: true }));

  describe('query', () => {
    it('rend les données du réseau', async () => {
      client.query.mockResolvedValue({ data: { tracks: [1] } });
      await expect(api.query(Q)).resolves.toEqual({ tracks: [1] });
      expect(client.query.mock.calls[0][0].fetchPolicy).toBe('network-only');
    });

    it('se replie sur le cache quand le réseau échoue', async () => {
      client.query.mockRejectedValueOnce(new Error('Failed to fetch')).mockResolvedValueOnce({ data: { tracks: [] } });
      await expect(api.query(Q)).resolves.toEqual({ tracks: [] });
      expect(client.query.mock.calls[1][0].fetchPolicy).toBe('cache-only');
    });

    it('lève l’erreur du serveur quand il n’y a aucune donnée', async () => {
      client.query.mockResolvedValue({ data: undefined, error: { message: 'Interdit' } });
      await expect(api.query(Q)).rejects.toThrow('Interdit');
    });
  });

  describe('mutate', () => {
    it('envoie une clé d’idempotence et rend les données', async () => {
      client.mutate.mockResolvedValue({ data: { save: true } });
      const r = await api.mutate(M, { v: 1 }, { label: 'Enregistrer' });
      expect(r).toEqual({ data: { save: true }, queued: false, error: undefined });
      expect(client.mutate.mock.calls[0][0].context.headers['X-Idempotency-Key']).toMatch(/.{8,}/);
    });

    it('met l’action en file quand le navigateur est hors ligne', async () => {
      connectivity.setState({ browserOnline: false });
      const r = await api.mutate(M, { v: 2 }, { label: 'Enregistrer' });
      expect(r).toEqual({ data: null, queued: true });
      expect(client.mutate).not.toHaveBeenCalled();
      expect(enqueue).toHaveBeenCalledWith(
        'Enregistrer',
        expect.stringContaining('mutation Save'),
        { v: 2 },
        expect.any(String),
      );
    });

    it('met l’action en file quand le serveur est injoignable', async () => {
      client.mutate.mockRejectedValue(new Error('Failed to fetch'));
      const r = await api.mutate(M, { v: 3 });
      expect(r.queued).toBe(true);
    });

    it('rend l’erreur sans mettre en file une action non différable', async () => {
      client.mutate.mockRejectedValue(new Error('Failed to fetch'));
      const r = await api.mutate(M, { v: 4 }, { queueable: false });
      expect(r).toEqual({ data: null, queued: false, error: 'Failed to fetch' });
      expect(enqueue).not.toHaveBeenCalled();
    });

    it('met en file quand un service répond SERVICE_UNAVAILABLE', async () => {
      client.mutate.mockResolvedValue({
        error: { message: 'indisponible', extensions: { code: 'SERVICE_UNAVAILABLE' } },
      });
      expect((await api.mutate(M, { v: 5 })).queued).toBe(true);
    });
  });

  it('traduit les résultats observés en état d’affichage', () => {
    client.watchQuery.mockReturnValue(of({ data: { tracks: [] }, loading: false, error: { message: 'hors ligne' } }));
    const states = [];
    api.watch(Q).subscribe(s => states.push(s));
    expect(states).toEqual([{ data: { tracks: [] }, loading: false, error: 'hors ligne', stale: true }]);
  });

  it('rafraîchit les requêtes affichées à chaque reconnexion', () => {
    connectivity.connected();
    connectivity.connected();
    expect(client.refetchQueries).toHaveBeenCalledWith({ include: 'active' });
  });
});
