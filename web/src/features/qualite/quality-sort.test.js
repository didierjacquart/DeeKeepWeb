import { describe, expect, it } from 'vitest';
import { ALL_SHOWN } from './quality-model';
import { compareKeys, filterAndSort, keepRow, nextSort, sortKey, sortMark, visibleDefects } from './quality-sort';
import { defect, makeRow } from './quality-fixtures';

describe('sortKey', () => {
  it('note : les illisibles en premier, puis le titre', () => {
    expect(sortKey(makeRow({ score: 80, title: 'Abc' }), 'score')).toEqual([80, 'abc']);
    expect(sortKey(makeRow({ error: 'x', score: 80 }), 'score')[0]).toBe(-1);
  });

  it('lit les nombres des cellules calculées par le serveur', () => {
    const row = makeRow({
      cells: { hiss: '-62 dB', crackle: '0.004 %', clipping: '0.3 %', gaps: '3 · 45 %', status: 'OK' },
    });
    expect(sortKey(row, 'hiss')).toEqual([-62]);
    expect(sortKey(row, 'crackle')).toEqual([0.004]);
    expect(sortKey(row, 'clipping')).toEqual([0.3]);
    expect(sortKey(row, 'gaps')).toEqual([45]);
    expect(sortKey(row, 'status')).toEqual(['ok']);
  });

  it('valeurs absentes ou non mesurées', () => {
    const row = makeRow({ cells: { crackle: 'non mesuré' }, referenceDuration: null });
    expect(sortKey(row, 'hiss')).toEqual([-Infinity]);
    expect(sortKey(row, 'crackle')).toEqual([-1]);
    expect(sortKey(row, 'clipping')).toEqual([0]);
    expect(sortKey(row, 'duration')).toEqual([-1]);
    expect(sortKey(row, 'blanks')).toEqual([0]);
  });

  it("durée : écart à la référence ; blancs : le plus long d'après l'infobulle", () => {
    const row = makeRow({ duration: 190, referenceDuration: 200, tooltips: { blanks: 'Début : 1.5 s — Fin : 12 s' } });
    expect(sortKey(row, 'duration')).toEqual([10]);
    expect(sortKey(row, 'blanks')).toEqual([12]);
  });

  it('artiste puis titre, format par débit, défauts par nombre', () => {
    const row = makeRow({ artist: 'Zed', title: 'Air', bitrate: 320, format: 'MP3', score: 50 });
    row.defects = [defect('hiss'), defect('gaps')];
    expect(sortKey(row, 'artist')).toEqual(['zed', 'air']);
    expect(sortKey(row, 'format')).toEqual([320, 'mp3']);
    expect(sortKey(row, 'defects')).toEqual([2, 50]);
    expect(sortKey(makeRow({ path: '/A/b' }), 'path')).toEqual(['/a/b']);
  });
});

describe('compareKeys', () => {
  it('compare élément par élément', () => {
    expect(compareKeys([1, 'b'], [1, 'a'])).toBe(1);
    expect(compareKeys([0, 'z'], [1, 'a'])).toBe(-1);
    expect(compareKeys([1], [1, 'a'])).toBe(0);
  });
});

describe('filtres', () => {
  it('visibleDefects : défauts cochés, sans les profonds en analyse rapide', () => {
    expect([...visibleDefects(ALL_SHOWN, true)]).toContain('crackle');
    expect([...visibleDefects(ALL_SHOWN, false)]).not.toContain('crackle');
    expect([...visibleDefects({ ...ALL_SHOWN, hiss: false }, true)]).not.toContain('hiss');
  });

  it('keepRow : un défaut visible, illisible toujours, sains sur demande', () => {
    const visible = new Set(['hiss']);
    expect(keepRow(makeRow({ defects: [defect('hiss')] }), visible, false)).toBe(true);
    expect(keepRow(makeRow({ defects: [defect('gaps')] }), visible, false)).toBe(false);
    expect(keepRow(makeRow({ error: 'x' }), new Set(), false)).toBe(true);
    expect(keepRow(makeRow(), visible, false)).toBe(false);
    expect(keepRow(makeRow(), visible, true)).toBe(true);
  });

  it("filterAndSort : filtre, trie, et départage par ordre d'arrivée", () => {
    const a = makeRow({ title: 'B', score: 50, defects: [defect('hiss')] });
    const b = makeRow({ title: 'A', score: 50, defects: [defect('hiss')] });
    const c = makeRow({ title: 'C', score: 90, defects: [defect('gaps')] });
    const clean = makeRow();
    const visible = visibleDefects(ALL_SHOWN, true);
    const titles = list => list.map(r => r.title);
    expect(titles(filterAndSort([c, a, b, clean], visible, false, { key: 'score', asc: true }))).toEqual([
      'A',
      'B',
      'C',
    ]);
    expect(titles(filterAndSort([c, a, b], visible, false, { key: 'score', asc: false }))).toEqual(['C', 'B', 'A']);
    expect(titles(filterAndSort([a, b], visible, false, { key: 'defects', asc: true }))).toEqual(['B', 'A']);
  });
});

describe('tri par en-tête', () => {
  it('nextSort : inverse le sens sur la même colonne, croissant sur une autre', () => {
    expect(nextSort({ key: 'score', asc: true }, 'score')).toEqual({ key: 'score', asc: false });
    expect(nextSort({ key: 'score', asc: false }, 'title')).toEqual({ key: 'title', asc: true });
    const cur = { key: 'score', asc: true };
    expect(nextSort(cur, null)).toBe(cur);
  });

  it('sortMark : flèche de la colonne triée', () => {
    expect(sortMark({ key: 'title', asc: true }, 'title')).toBe(' ▲');
    expect(sortMark({ key: 'title', asc: false }, 'title')).toBe(' ▼');
    expect(sortMark({ key: 'title', asc: true }, 'artist')).toBe('');
    expect(sortMark({ key: 'title', asc: true }, null)).toBe('');
  });
});
