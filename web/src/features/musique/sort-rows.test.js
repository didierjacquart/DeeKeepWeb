import { describe, expect, it } from 'vitest';
import { sortArrow, sortRows } from './sort-rows';

const row = (seq, track, status = 'En attente') => ({ seq, status, track: { album: null, ...track } });
const rows = [
  row(0, { id: 3, title: 'beta', artist: 'Zed', duration: 100, album: 'B' }, 'Téléchargé ✓'),
  row(1, { id: 1, title: 'Alpha', artist: 'abba', duration: 300 }),
  row(2, { id: 0, title: 'gamma', artist: 'Abba', duration: 0, album: 'a' }, 'Erreur'),
];
const order = list => list.map(r => r.seq);

describe('sortRows', () => {
  it("sans colonne : l'ordre d'origine", () => {
    expect(sortRows(rows, null, true)).toBe(rows);
  });

  it('par texte sans casse, à égalité par ordre d’arrivée', () => {
    expect(order(sortRows(rows, 'title', true))).toEqual([1, 0, 2]);
    expect(order(sortRows(rows, 'artist', true))).toEqual([1, 2, 0]);
    expect(order(sortRows(rows, 'artist', false))).toEqual([0, 1, 2]);
    expect(order(sortRows(rows, 'album', true))).toEqual([1, 2, 0]);
    expect(order(sortRows(rows, 'status', true))).toEqual([1, 2, 0]);
  });

  it('par nombre', () => {
    expect(order(sortRows(rows, 'id', true))).toEqual([2, 1, 0]);
    expect(order(sortRows(rows, 'duration', false))).toEqual([1, 0, 2]);
  });

  it('flèche de la colonne triée', () => {
    expect(sortArrow('title', 'title', true)).toBe(' ▲');
    expect(sortArrow('title', 'title', false)).toBe(' ▼');
    expect(sortArrow('album', 'title', true)).toBe('');
  });
});
