import { describe, expect, it, vi } from 'vitest';
import { makeJob } from '../../test/render';
import { mockApi } from '../../test/mock-api';
import { jobs } from '../../core/jobs/jobs';
import { dialogs } from '../../shared/dialog/dialog-stack';
import { AUTOMATCH_LIMIT, ReencodeStore } from './reencode-store';
import { STORAGE_KEY } from './reencode-storage';

let seq = 0;
const scanId = () => `scan-${++seq}`;

/** Fichier trouvé par une analyse (événement jobItem). */
const found = (n, fields = {}) => ({
  localPath: `D:/m/${n}.mp3`,
  dbPath: `m/${n}.mp3`,
  title: `Titre ${n}`,
  artist: 'X',
  bitrate: 128,
  ext: '.mp3',
  ...fields,
});

/** Magasin démarré, avec une analyse `id` en cours qui a déjà rendu `files`. */
function startedStore(server, id, files) {
  const store = new ReencodeStore();
  store.start();
  store.setState({ scanJobIds: [id] });
  for (const data of files) server.emit('JobItem', { jobItem: { jobId: id, status: 'ok', data } });
  return store;
}

const openDialogs = () => dialogs.state.stack.length;

describe('ReencodeStore', () => {
  it('ajoute les fichiers à la fin de l’analyse et cherche leur correspondance Deezer', async () => {
    const server = mockApi({
      ScanLocalFiles: { scanLocalFiles: { id: 'scan-a' } },
      BestMatch: ({ title }) => ({ bestMatch: title === 'Titre 1' ? { id: 11, title, artist: 'X' } : null }),
    });
    const store = new ReencodeStore();
    store.start();
    await store.scan(['D:/m']);
    expect(server.mutations('ScanLocalFiles')).toEqual([{ paths: ['D:/m'] }]);
    expect(store.state.scanJobIds).toEqual(['scan-a']);

    server.emit('JobItem', { jobItem: { jobId: 'scan-a', status: 'ok', data: found(1) } });
    server.emit('JobItem', { jobItem: { jobId: 'scan-a', status: 'error', data: found(9) } });
    server.emit('JobItem', { jobItem: { jobId: 'scan-a', status: 'ok', data: found(2) } });
    jobs.upsert(makeJob({ id: 'scan-a', type: 'files.scan', status: 'SUCCEEDED' }));

    expect(store.state.scanJobIds).toEqual([]);
    expect(store.state.items.map(i => i.path)).toEqual(['D:/m/1.mp3', 'D:/m/2.mp3']);
    await vi.waitFor(() => expect(store.state.matching).toBe(0));
    expect(store.state.items.map(i => [i.matchState, i.selected])).toEqual([
      ['found', true],
      ['none', false],
    ]);
    expect(server.queries('BestMatch')).toEqual([
      { artist: 'X', title: 'Titre 1' },
      { artist: 'X', title: 'Titre 2' },
    ]);
  });

  it('refuse une analyse que le serveur ne lance pas', async () => {
    mockApi({ ScanLocalFiles: new Error('Dossier interdit') });
    await expect(new ReencodeStore().scan(['C:/'])).rejects.toThrow('Dossier interdit');
  });

  it(`cherche les correspondances jusqu'à ${AUTOMATCH_LIMIT} fichiers`, async () => {
    const server = mockApi({ BestMatch: { bestMatch: null } });
    const id = scanId();
    const store = startedStore(
      server,
      id,
      Array.from({ length: AUTOMATCH_LIMIT }, (_, n) => found(n)),
    );
    jobs.upsert(makeJob({ id, type: 'files.scan', status: 'SUCCEEDED' }));
    await vi.waitFor(() => expect(store.state.matching).toBe(0));
    expect(server.queries('BestMatch')).toHaveLength(AUTOMATCH_LIMIT);
    expect(openDialogs()).toBe(0);
  });

  it('au-delà de la limite, présélectionne les fichiers sans chercher de correspondance', () => {
    const server = mockApi({});
    const id = scanId();
    const store = startedStore(
      server,
      id,
      Array.from({ length: AUTOMATCH_LIMIT + 1 }, (_, n) => found(n)),
    );
    jobs.upsert(makeJob({ id, type: 'files.scan', status: 'SUCCEEDED' }));
    expect(store.state.items).toHaveLength(AUTOMATCH_LIMIT + 1);
    expect(store.state.items.every(i => i.matchState === 'skipped' && i.selected)).toBe(true);
    expect(store.state.matching).toBe(0);
    expect(openDialogs()).toBe(1);
  });

  it('prévient quand une analyse ne trouve rien de nouveau', () => {
    const server = mockApi({ BestMatch: { bestMatch: null } });
    const empty = scanId();
    const store = startedStore(server, empty, []);
    jobs.upsert(makeJob({ id: empty, type: 'files.scan', status: 'SUCCEEDED' }));
    expect(openDialogs()).toBe(1);
    expect(store.state.items).toEqual([]);
  });

  it('ignore les doublons déjà dans la liste', () => {
    const server = mockApi({});
    const id = scanId();
    const store = startedStore(server, id, [found(1, { localPath: 'D:/M/1.MP3' })]);
    store.updateItems(() => [{ path: 'd:/m/1.mp3', matchState: 'none' }]);
    jobs.upsert(makeJob({ id, type: 'files.scan', status: 'SUCCEEDED' }));
    expect(store.state.items).toHaveLength(1);
    expect(openDialogs()).toBe(1);
  });

  it('sauvegarde la liste dans le navigateur et la restaure', () => {
    mockApi({});
    const store = new ReencodeStore();
    store.start();
    store.updateItems(() => [{ path: 'D:/m/a.mp3', matchState: 'pending', selected: true }]);
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY))).toEqual([
      { path: 'D:/m/a.mp3', matchState: 'pending', selected: true },
    ]);
    expect(new ReencodeStore().state.items).toEqual([{ path: 'D:/m/a.mp3', matchState: 'none', selected: true }]);
  });

  it('met les conversions en file puis applique leur résultat', async () => {
    const server = mockApi({ EnqueueConversions: { enqueueConversions: [{ id: 'conv-1' }, { id: 'conv-2' }] } });
    const store = new ReencodeStore();
    store.start();
    const a = {
      path: 'D:/m/a.mp3',
      dbPath: 'm/a.mp3',
      title: 'A',
      artist: 'X',
      album: '',
      format: 'MP3',
      bitrate: 128,
      match: { id: 5 },
      selected: true,
      converted: false,
      jobId: null,
    };
    const b = { ...a, path: 'D:/m/b.mp3', match: null };
    store.updateItems(() => [a, b]);

    await store.convert([a, b]);
    const [vars] = server.mutations('EnqueueConversions');
    expect(vars.items).toEqual([
      { localPath: 'D:/m/a.mp3', deezerId: 5 },
      { localPath: 'D:/m/b.mp3', deezerId: null },
    ]);
    expect(vars.batchId).toMatch(/^[0-9a-f]{32}$/);
    expect(store.state.batchIds).toEqual(['conv-1', 'conv-2']);
    expect(store.state.items.map(i => i.jobId)).toEqual(['conv-1', 'conv-2']);

    jobs.upsert(
      makeJob({
        id: 'conv-1',
        status: 'SUCCEEDED',
        result: { localPath: 'D:/m/a.mp3', dbPath: 'm/a.mp3', format: 'mp3', bitrate: 320, title: 'A (HQ)' },
      }),
    );
    expect(store.state.items[0]).toMatchObject({ converted: true, selected: false, bitrate: 320, title: 'A (HQ)' });
    expect(store.state.items[1].converted).toBe(false);
  });

  it('signale une conversion refusée', async () => {
    mockApi({ EnqueueConversions: new Error('Serveur occupé') });
    await expect(new ReencodeStore().convert([{ path: 'a', match: null }])).rejects.toThrow('Serveur occupé');
  });
});
