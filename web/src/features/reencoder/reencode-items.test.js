import { describe, expect, it } from 'vitest';
import { makeJob } from '../../test/render';
import {
  applyConversionResults,
  applyMatch,
  batchProgressOf,
  isAtTarget,
  itemsFromScan,
  jobOf,
  scanTextOf,
  visibleItems,
} from './reencode-items';

const item = (fields = {}) => ({
  path: 'D:/m/a.mp3',
  dbPath: 'm/a.mp3',
  title: 'A',
  artist: 'X',
  album: '',
  bitrate: 128,
  format: 'MP3',
  matchState: 'found',
  match: null,
  selected: false,
  jobId: null,
  converted: false,
  ...fields,
});

describe('isAtTarget', () => {
  it('compare le format sans tenir compte de la casse', () => {
    expect(isAtTarget(item({ format: 'flac' }), 'FLAC', '')).toBe(true);
    expect(isAtTarget(item({ format: 'MP3' }), 'FLAC', '')).toBe(false);
  });

  it('accepte le sans perte quel que soit le débit', () => {
    expect(isAtTarget(item({ format: 'WAV', bitrate: 0 }), 'wav', '320 kbps')).toBe(true);
  });

  it('tolère ±5 kbps autour du débit cible', () => {
    expect(isAtTarget(item({ bitrate: 316 }), 'MP3', '320 kbps (HQ)')).toBe(true);
    expect(isAtTarget(item({ bitrate: 314 }), 'MP3', '320 kbps (HQ)')).toBe(false);
    expect(isAtTarget(item({ bitrate: 128 }), 'MP3', '128 kbps')).toBe(true);
    expect(isAtTarget(item({ bitrate: 0 }), 'MP3', '128 kbps')).toBe(false);
  });

  it('se contente du format quand la qualité ne donne pas de débit', () => {
    expect(isAtTarget(item({ bitrate: 192 }), 'MP3', 'Variable')).toBe(true);
  });
});

describe('scanTextOf', () => {
  it("rend le message de l'analyse active, sinon null", () => {
    const all = [
      makeJob({ id: 's1', status: 'RUNNING', message: '3 / 10 fichier(s) analysé(s)' }),
      makeJob({ id: 's2', status: 'SUCCEEDED' }),
    ];
    expect(scanTextOf(all, ['s1'])).toBe('3 / 10 fichier(s) analysé(s)');
    expect(scanTextOf(all, ['s2'])).toBeNull();
    expect(scanTextOf([makeJob({ id: 's3', status: 'QUEUED' })], ['s3'])).toBe('Analyse en cours…');
  });
});

describe('jobOf', () => {
  it('retrouve la tâche de la ligne', () => {
    const job = makeJob({ id: 'c1' });
    const byId = new Map([['c1', job]]);
    expect(jobOf(item({ jobId: 'c1' }), byId)).toBe(job);
    expect(jobOf(item({ jobId: 'c2' }), byId)).toBeNull();
    expect(jobOf(item(), byId)).toBeNull();
  });
});

describe('itemsFromScan', () => {
  it('convertit les fichiers trouvés et écarte les doublons (casse ignorée)', () => {
    const found = [
      { localPath: 'D:/m/A.MP3', dbPath: 'x', ext: '.mp3' },
      { localPath: 'D:/m/b.flac', dbPath: 'm/b.flac', title: 'B', artist: 'Y', album: 'Z', bitrate: 900, ext: '.flac' },
      { localPath: 'd:/m/B.flac', dbPath: 'dup', ext: '.flac' },
    ];
    expect(itemsFromScan(found, [item()])).toEqual([
      {
        path: 'D:/m/b.flac',
        dbPath: 'm/b.flac',
        title: 'B',
        artist: 'Y',
        album: 'Z',
        bitrate: 900,
        format: 'FLAC',
        matchState: 'pending',
        match: null,
        selected: false,
        jobId: null,
        converted: false,
      },
    ]);
  });

  it('met des valeurs neutres quand les tags manquent', () => {
    const [fresh] = itemsFromScan([{ localPath: 'D:/c', dbPath: 'c' }], []);
    expect(fresh).toMatchObject({ title: '', artist: '', album: '', bitrate: 0, format: '' });
  });
});

describe('applyMatch', () => {
  it('coche une ligne trouvée et marque « none » une ligne sans correspondance', () => {
    const match = { id: 7, title: 'A', artist: 'X' };
    const list = [item({ matchState: 'pending' }), item({ path: 'b', matchState: 'pending', selected: true })];
    expect(applyMatch(list, 'D:/m/a.mp3', match)[0]).toMatchObject({ match, matchState: 'found', selected: true });
    expect(applyMatch(list, 'b', null)[1]).toMatchObject({ match: null, matchState: 'none', selected: true });
  });

  it("ne touche pas une ligne qui n'attend plus de correspondance", () => {
    const list = [item({ matchState: 'skipped' })];
    expect(applyMatch(list, 'D:/m/a.mp3', { id: 1 })[0]).toBe(list[0]);
  });
});

describe('applyConversionResults', () => {
  it('rend la même liste quand aucune conversion n’a abouti', () => {
    const list = [item({ jobId: 'c1' }), item({ path: 'b' })];
    const byId = new Map([['c1', makeJob({ id: 'c1', status: 'RUNNING' })]]);
    expect(applyConversionResults(list, byId)).toBe(list);
  });

  it('reprend le fichier produit et les tags retenus', () => {
    const list = [item({ jobId: 'c1', selected: true, matchState: 'none' })];
    const result = { localPath: 'D:/m/a.flac', dbPath: 'm/a.flac', format: 'flac', bitrate: 0, title: '', artist: 'Z' };
    const byId = new Map([['c1', makeJob({ id: 'c1', status: 'SUCCEEDED', result })]]);
    expect(applyConversionResults(list, byId)[0]).toEqual({
      ...list[0],
      converted: true,
      selected: false,
      path: 'D:/m/a.flac',
      dbPath: 'm/a.flac',
      format: 'FLAC',
      bitrate: 0,
      title: 'A',
      artist: 'Z',
      album: '',
      matchState: 'found',
    });
  });

  it('garde les valeurs de la ligne sans résultat détaillé', () => {
    const list = [item({ jobId: 'c1', format: 'mp3' })];
    const byId = new Map([['c1', makeJob({ id: 'c1', status: 'SUCCEEDED' })]]);
    expect(applyConversionResults(list, byId)[0]).toMatchObject({ path: 'D:/m/a.mp3', format: 'MP3', converted: true });
  });
});

describe('visibleItems', () => {
  const target = { format: 'MP3', quality: '320 kbps' };
  const list = [
    item({ path: 'c', title: 'beta', artist: 'b', bitrate: 320, matchState: 'none' }),
    item({ path: 'a', title: 'Alpha', artist: 'C', bitrate: 128 }),
    item({ path: 'B', title: 'gamma', artist: 'a', format: 'FLAC', bitrate: 900 }),
  ];
  const paths = l => l.map(i => i.path);

  it('rend la liste telle quelle sans filtre ni tri', () => {
    expect(visibleItems(list, { hideTarget: false, target, key: null, asc: true })).toBe(list);
  });

  it('masque les fichiers déjà au format cible', () => {
    expect(paths(visibleItems(list, { hideTarget: true, target, key: null, asc: true }))).toEqual(['a', 'B']);
  });

  it('trie par colonne, dans les deux sens', () => {
    const sorted = (key, asc = true) => paths(visibleItems(list, { hideTarget: false, target, key, asc }));
    expect(sorted('title')).toEqual(['a', 'c', 'B']);
    expect(sorted('title', false)).toEqual(['B', 'c', 'a']);
    expect(sorted('artist')).toEqual(['B', 'c', 'a']);
    expect(sorted('path')).toEqual(['a', 'B', 'c']);
    expect(sorted('format')).toEqual(['B', 'a', 'c']);
    expect(sorted('match')).toEqual(['c', 'a', 'B']);
  });
});

describe('batchProgressOf', () => {
  it('fait la moyenne des tâches du lot', () => {
    expect(batchProgressOf([])).toBe(0);
    const batch = [
      makeJob({ status: 'SUCCEEDED' }),
      makeJob({ status: 'RUNNING', progress: 0.5 }),
      makeJob({ status: 'QUEUED' }),
    ];
    expect(batchProgressOf(batch)).toBe(0.5);
    expect(batchProgressOf([makeJob({ status: 'RUNNING', progress: 0.3333 })])).toBe(0.33);
  });
});
