import { describe, expect, it } from 'vitest';
import {
  allVisibleChecked,
  mergePresence,
  pickVisible,
  pickedTracks,
  sortedEntities,
  togglePick,
  uncheckChanged,
  uncheckDuplicates,
  visibleTracks,
} from './track-list';

const t = (id, state, extra = {}) => ({
  id,
  title: `Titre ${id}`,
  artist: '',
  duration: 0,
  presence: { state, files: [] },
  ...extra,
});
const ids = list => list.map(x => x.id);
const view = { filter: '', duplicatesOnly: false, sortColumn: null, sortAsc: true };

describe('visibleTracks', () => {
  const tracks = [
    t(1, 'ABSENT', { title: 'Harder', artist: 'daft', duration: 200, rank: 10 }),
    t(2, 'PRESENT', { title: 'One more time', artist: 'Air', duration: 100, rank: 30 }),
    t(3, 'POSSIBLE', { title: 'Better', artist: 'bob', duration: 300 }),
  ];

  it('filtre sur le titre, sans casse', () => {
    expect(ids(visibleTracks(tracks, { ...view, filter: '  HAR ' }))).toEqual([1]);
  });

  it('doublons seulement', () => {
    expect(ids(visibleTracks(tracks, { ...view, duplicatesOnly: true }))).toEqual([2, 3]);
  });

  it('tri par texte, durée ou popularité', () => {
    expect(ids(visibleTracks(tracks, { ...view, sortColumn: 'artist' }))).toEqual([2, 3, 1]);
    expect(ids(visibleTracks(tracks, { ...view, sortColumn: 'duration', sortAsc: false }))).toEqual([3, 1, 2]);
    expect(ids(visibleTracks(tracks, { ...view, sortColumn: 'rank', sortAsc: false }))).toEqual([2, 1, 3]);
  });
});

describe('sortedEntities', () => {
  const entities = [{ name: 'b', subtitle: 'Z' }, { name: 'A' }, { name: 'c', subtitle: 'y' }];
  it('par colonne, sans casse, valeurs absentes en tête', () => {
    expect(sortedEntities(entities, null)).toBe(entities);
    expect(sortedEntities(entities, { column: 'name', asc: true }).map(e => e.name)).toEqual(['A', 'b', 'c']);
    expect(sortedEntities(entities, { column: 'subtitle', asc: false }).map(e => e.name)).toEqual(['b', 'c', 'A']);
  });
});

describe('sélection', () => {
  const absent = t(1, 'ABSENT');
  const present = t(2, 'PRESENT');
  const possible = t(3, 'POSSIBLE');

  it('une piste déjà sur le disque ne se coche pas', () => {
    expect([...togglePick(new Map(), absent, true).keys()]).toEqual([1]);
    expect(togglePick(new Map(), present, true).size).toBe(0);
    expect(togglePick(new Map([[1, absent]]), absent, false).size).toBe(0);
  });

  it('« Tout sélectionner » ignore ✓ et ≈ ; décocher retire toutes les lignes visibles', () => {
    const picked = pickVisible(new Map(), [absent, present, possible], true);
    expect([...picked.keys()]).toEqual([1]);
    expect(pickVisible(new Map([[3, possible]]), [absent, possible], false).size).toBe(0);
  });

  it('« Tout sélectionner » coché quand toutes les lignes cochables le sont', () => {
    expect(allVisibleChecked([absent, present], new Map([[1, absent]]))).toBe(true);
    expect(allVisibleChecked([present], new Map())).toBe(false);
    expect(allVisibleChecked([absent], new Map())).toBe(false);
  });

  it('pastilles reçues : ✓ et ≈ décochés', () => {
    const picked = new Map([
      [1, absent],
      [3, possible],
    ]);
    expect([...uncheckDuplicates(picked, [absent, possible]).keys()]).toEqual([1]);
  });

  it('pastilles recalculées : seules les lignes qui changent sont décochées', () => {
    const before = [t(1, 'PENDING'), t(3, 'POSSIBLE')];
    const updated = mergePresence(before, [{ id: 1, presence: { state: 'POSSIBLE', files: [] } }]);
    expect(updated.map(x => x.presence.state)).toEqual(['POSSIBLE', 'POSSIBLE']);
    const picked = new Map([
      [1, before[0]],
      [3, before[1]],
    ]);
    expect([...uncheckChanged(picked, before, updated).keys()]).toEqual([3]);
  });

  it('pistes rendues sans leur pastille, identifiant nul écarté', () => {
    const picked = new Map([
      [1, absent],
      [0, t(0, 'ABSENT')],
    ]);
    expect(pickedTracks(picked)).toEqual([{ id: 1, title: 'Titre 1', artist: '', duration: 0 }]);
  });
});
