import { describe, expect, it } from 'vitest';
import { makeJob } from '../../test/render';
import {
  defectsClass,
  hasDefect,
  mergeEntries,
  rowBusy,
  rowJob,
  rowStatus,
  scoreClass,
  toTrack,
} from './quality-model';
import { defect, makeRow } from './quality-fixtures';

describe('scoreClass', () => {
  it('classe la note : sain, douteux, mauvais', () => {
    expect(scoreClass(makeRow({ score: 100 }))).toBe('ok');
    expect(scoreClass(makeRow({ score: 80 }))).toBe('warn');
    expect(scoreClass(makeRow({ score: 75 }))).toBe('warn');
    expect(scoreClass(makeRow({ score: 74 }))).toBe('bad');
  });

  it("n'en donne aucune à un fichier illisible", () => {
    expect(scoreClass(makeRow({ score: 0, error: 'Illisible' }))).toBe('');
  });
});

describe('défauts', () => {
  const row = makeRow({ score: 60, defects: [defect('hiss'), defect('tail')] });

  it('hasDefect cherche par identifiant', () => {
    expect(hasDefect(row, 'hiss')).toBe(true);
    expect(hasDefect(row, 'crackle')).toBe(false);
  });

  it('defectsClass : illisible, selon la note, ou sain', () => {
    expect(defectsClass(makeRow({ error: 'x' }))).toBe('bad');
    expect(defectsClass(row)).toBe('bad');
    expect(defectsClass(makeRow({ score: 90, defects: [defect('gaps')] }))).toBe('warn');
    expect(defectsClass(makeRow())).toBe('ok');
  });
});

describe('toTrack', () => {
  it('construit le titre du lecteur à partir de la ligne', () => {
    const row = makeRow({ path: '/a b.mp3', title: '', cells: { context: 'MP3 · 128 kb/s' } });
    expect(toTrack(row)).toEqual({
      id: '/a b.mp3',
      title: '/a b.mp3',
      artist: 'Artiste',
      album: 'Album',
      src: '/media/track?path=%2Fa%20b.mp3',
      duration: 200,
      context: 'MP3 · 128 kb/s',
    });
  });
});

describe('rowJob / rowBusy', () => {
  const job = makeJob({ id: 'r1', type: 'redownload', status: 'RUNNING' });
  const byId = new Map([['r1', job]]);

  it('retrouve la tâche de retéléchargement', () => {
    expect(rowJob(makeRow({ redownloadJobId: 'r1' }), byId)).toBe(job);
    expect(rowJob(makeRow({ redownloadJobId: 'r2' }), byId)).toBeNull();
    expect(rowJob(makeRow(), byId)).toBeNull();
  });

  it('occupée pendant un retéléchargement actif ou une réanalyse', () => {
    expect(rowBusy(makeRow({ redownloadJobId: 'r1' }), byId)).toBe(true);
    expect(rowBusy(makeRow({ status: 'reanalyzing' }), byId)).toBe(true);
    const done = new Map([['r1', { ...job, status: 'SUCCEEDED' }]]);
    expect(rowBusy(makeRow({ redownloadJobId: 'r1' }), done)).toBe(false);
  });
});

describe('rowStatus', () => {
  const row = makeRow({ statusDetail: 'détail', cells: { status: 'Défaut persistant' }, tooltips: { status: 'tip' } });

  it("suit la tâche d'extraction", () => {
    expect(rowStatus(row, makeJob({ status: 'FAILED', error: 'Introuvable' }))).toEqual({
      text: 'Échec',
      tip: 'Introuvable',
      cls: 'bad',
    });
    expect(rowStatus(row, makeJob({ status: 'FAILED' })).tip).toBe('détail');
    expect(rowStatus(row, makeJob({ status: 'CANCELLED' })).tip).toBe('Retéléchargement annulé');
    expect(rowStatus(row, makeJob({ status: 'QUEUED' })).text).toBe('En attente…');
    expect(rowStatus(row, makeJob({ status: 'RUNNING', progress: 0.42, message: 'Extraction' })).text).toBe(
      'Extraction — 42%',
    );
    expect(rowStatus(row, makeJob({ status: 'RUNNING' })).text).toBe('Initialisation… — 0%');
    expect(rowStatus(row, makeJob({ status: 'SUCCEEDED' })).text).toBe('Réanalyse…');
  });

  it('rend ensuite le statut du serveur, coloré', () => {
    expect(rowStatus(row, null)).toEqual({ text: 'Défaut persistant', tip: 'tip', cls: 'warn' });
    expect(rowStatus(makeRow({ cells: { status: 'Défaut corrigé' } }), null).cls).toBe('ok');
    const replaced = makeRow({ status: 'replaced', cells: { status: 'Remplacé' } });
    expect(rowStatus(replaced, makeJob({ status: 'SUCCEEDED' }))).toEqual({ text: 'Remplacé', tip: null, cls: '' });
  });
});

describe('mergeEntries', () => {
  it('ajoute, remplace puis retire', () => {
    const a = makeRow({ path: 'a' });
    const b = makeRow({ path: 'b' });
    const rows = new Map([
      ['a', a],
      ['b', b],
    ]);
    const a2 = { ...a, score: 10 };
    const c = makeRow({ path: 'c' });
    const next = mergeEntries(rows, { entries: [a2, c], removed: ['b'] });
    expect([...next.keys()]).toEqual(['a', 'c']);
    expect(next.get('a')).toBe(a2);
    expect(rows.size).toBe(2);
  });
});
