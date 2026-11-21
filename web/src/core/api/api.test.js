import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { connectivity } from '../offline/connectivity';
import { offlineQueue } from '../offline/offline-queue';
import { buildRequest, combine, endpoint } from './endpoints';
import { ApiError } from './http';

const send = vi.fn();
vi.mock('./http', async importOriginal => ({ ...(await importOriginal()), send: (...args) => send(...args) }));

const { Api, responseCache } = await import('./api');

const TRACKS = endpoint('Tracks', 'tracks', 'GET /api/library/tracks');
const SAVE = endpoint('Save', 'save', 'POST /api/save');

describe('buildRequest', () => {
  const part = route => endpoint('X', 'x', route).parts[0];

  it('remplit le chemin et met le reste en paramètres d’URL pour une lecture', () => {
    expect(
      buildRequest(part('GET /api/catalog/browse/{kind}/{id}'), { kind: 'ALBUM', id: 12, page: 2, none: null }),
    ).toEqual({
      method: 'GET',
      url: '/api/catalog/browse/ALBUM/12?page=2',
    });
  });

  it('répète les listes en paramètres et garde les paramètres fixes', () => {
    expect(buildRequest(part('GET /api/quality/entries?limit=5'), { defects: ['hiss', 'gap'] }).url).toBe(
      '/api/quality/entries?defects=hiss&defects=gap&limit=5',
    );
  });

  it('envoie le reste des variables en corps JSON pour une action', () => {
    expect(
      buildRequest(part('POST /api/organize/sessions/{sessionId}/apply'), { sessionId: 's1', indices: [1] }),
    ).toEqual({
      method: 'POST',
      url: '/api/organize/sessions/s1/apply',
      body: { indices: [1] },
    });
  });

  it('accepte un corps construit à la main', () => {
    const p = endpoint('X', 'x', 'PUT /api/settings', { body: v => v.values }).parts[0];
    expect(buildRequest(p, { values: { a: 1 } }).body).toEqual({ a: 1 });
  });
});

describe('Api', () => {
  let api;
  let enqueue;

  beforeEach(() => {
    send.mockReset();
    responseCache.restore({});
    enqueue = vi.spyOn(offlineQueue, 'enqueue').mockResolvedValue({});
    connectivity.setState({ browserOnline: true });
    api = new Api();
  });

  afterEach(() => connectivity.setState({ browserOnline: true }));

  describe('query', () => {
    it('rend la réponse sous le nom du champ', async () => {
      send.mockResolvedValue([1]);
      await expect(api.query(TRACKS)).resolves.toEqual({ tracks: [1] });
      expect(send).toHaveBeenCalledWith({ method: 'GET', url: '/api/library/tracks' });
    });

    it('réunit les appels d’une opération combinée', async () => {
      send.mockImplementation(r => Promise.resolve(r.url.endsWith('history') ? ['h'] : ['s']));
      const op = combine(
        'History',
        endpoint('H', 'history', 'GET /api/library/history'),
        endpoint('H', 'searches', 'GET /api/catalog/searches'),
      );
      await expect(api.query(op)).resolves.toEqual({ history: ['h'], searches: ['s'] });
    });

    it('se replie sur le cache quand le serveur est injoignable', async () => {
      send.mockResolvedValueOnce([]).mockRejectedValueOnce(new ApiError('Serveur injoignable', 0, 'NETWORK'));
      await api.query(TRACKS);
      await expect(api.query(TRACKS)).resolves.toEqual({ tracks: [] });
    });

    it('lève l’erreur du serveur', async () => {
      send.mockRejectedValue(new ApiError('Interdit', 400, 'INVALID_ARGUMENT'));
      await expect(api.query(TRACKS)).rejects.toThrow('Interdit');
    });
  });

  describe('mutate', () => {
    it('envoie une clé d’idempotence et rend les données', async () => {
      send.mockResolvedValue(true);
      const r = await api.mutate(SAVE, { v: 1 }, { label: 'Enregistrer' });
      expect(r).toEqual({ data: { save: true }, queued: false });
      expect(send.mock.calls[0][0]).toEqual({ method: 'POST', url: '/api/save', body: { v: 1 } });
      expect(send.mock.calls[0][1]['X-Idempotency-Key']).toMatch(/.{8,}/);
    });

    it('met l’action en file quand le navigateur est hors ligne', async () => {
      connectivity.setState({ browserOnline: false });
      const r = await api.mutate(SAVE, { v: 2 }, { label: 'Enregistrer' });
      expect(r).toEqual({ data: null, queued: true });
      expect(send).not.toHaveBeenCalled();
      expect(enqueue).toHaveBeenCalledWith(
        'Enregistrer',
        { method: 'POST', url: '/api/save', body: { v: 2 } },
        expect.any(String),
      );
    });

    it('met l’action en file quand le serveur est injoignable ou un service indisponible', async () => {
      send.mockRejectedValueOnce(new ApiError('x', 0, 'NETWORK'));
      expect((await api.mutate(SAVE, { v: 3 })).queued).toBe(true);
      send.mockRejectedValueOnce(new ApiError('x', 503, 'SERVICE_UNAVAILABLE'));
      expect((await api.mutate(SAVE, { v: 4 })).queued).toBe(true);
    });

    it('rend l’erreur sans mettre en file une action non différable', async () => {
      send.mockRejectedValue(new ApiError('Serveur injoignable', 0, 'NETWORK'));
      const r = await api.mutate(SAVE, { v: 5 }, { queueable: false });
      expect(r).toEqual({ data: null, queued: false, error: 'Serveur injoignable' });
      expect(enqueue).not.toHaveBeenCalled();
    });
  });

  it('montre le cache puis la réponse du serveur, et recharge à la reconnexion', async () => {
    send.mockResolvedValueOnce([1]).mockResolvedValueOnce([2]);
    const states = [];
    const sub = api.watch(TRACKS).subscribe(s => states.push(s));
    await vi.waitFor(() =>
      expect(states.at(-1)).toEqual({ data: { tracks: [1] }, loading: false, error: null, stale: false }),
    );
    connectivity.connected();
    connectivity.connected();
    await vi.waitFor(() => expect(states.at(-1)?.data).toEqual({ tracks: [2] }));
    expect(states.at(-2)).toEqual({ data: { tracks: [1] }, loading: true, error: null, stale: false });
    sub.unsubscribe();
  });

  it('signale des données périmées quand le serveur ne répond plus', async () => {
    send.mockResolvedValueOnce([1]).mockRejectedValueOnce(new ApiError('Serveur injoignable', 0, 'NETWORK'));
    await api.query(TRACKS);
    const states = [];
    api.watch(TRACKS).subscribe(s => states.push(s));
    await vi.waitFor(() =>
      expect(states.at(-1)).toEqual({
        data: { tracks: [1] },
        loading: false,
        error: 'Serveur injoignable',
        stale: true,
      }),
    );
  });
});
