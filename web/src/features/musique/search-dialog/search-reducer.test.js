import { describe, expect, it } from 'vitest';
import { initialSearchState, searchReducer } from './search-reducer';

const t = (id, state = 'ABSENT') => ({ id, title: `T${id}`, presence: { state, files: [] } });
const result = (tracks, entities = [], extra = {}) => ({ tracks, entities, header: null, error: null, ...extra });
const search = { type: 'search', query: 'air', kind: 'ALL' };
const album = { type: 'browse', kind: 'ALBUM', id: 5 };

const reduce = (state, ...actions) => actions.reduce(searchReducer, state);

describe('searchReducer', () => {
  it('requête lancée puis résultat de pistes ; ✓ et ≈ décochés', () => {
    const start = { ...initialSearchState, picked: new Map([[2, t(2)]]) };
    const s = reduce(
      start,
      { type: 'started' },
      { type: 'loaded', request: search, crumb: '', data: result([t(1), t(2, 'POSSIBLE')]) },
    );
    expect(s).toMatchObject({ loading: false, mode: 'tracks', request: search, message: null, header: null });
    expect(s.tracks).toHaveLength(2);
    expect(s.picked.size).toBe(0);
  });

  it("une recherche qui trouve des entités les affiche ; l'erreur de Deezer vide la vue", () => {
    const entities = [{ id: 1, kind: 'ARTIST', name: 'Air' }];
    expect(
      reduce(initialSearchState, { type: 'loaded', request: search, crumb: '', data: result([t(1)], entities) }),
    ).toMatchObject({ mode: 'entities', entities, tracks: [] });
    expect(
      reduce(initialSearchState, {
        type: 'loaded',
        request: search,
        crumb: '',
        data: result([], [], { error: 'quota' }),
      }),
    ).toMatchObject({
      mode: 'idle',
      message: { title: 'Erreur de connexion', text: 'Erreur lors de la recherche : quota', error: true },
    });
  });

  it('aucun morceau : message ; en-tête gardé pour un album', () => {
    const header = { name: 'Moon Safari' };
    const s = reduce(initialSearchState, {
      type: 'loaded',
      request: album,
      crumb: 'Album : X',
      data: result([], [], { header }),
    });
    expect(s).toMatchObject({ mode: 'tracks', header, crumb: 'Album : X' });
    expect(s.message.title).toBe('Aucun résultat');
  });

  it('exploration puis « ← Retour » : la vue précédente revient', () => {
    const shown = reduce(
      initialSearchState,
      { type: 'loaded', request: search, crumb: '', data: result([t(1)]) },
      { type: 'filter', filter: 'x' },
    );
    const browsed = reduce(
      shown,
      { type: 'navigate' },
      { type: 'started' },
      { type: 'loaded', request: album, crumb: 'Album : A', data: result([t(7)]) },
    );
    expect(browsed.stack).toHaveLength(1);
    expect(browsed.filter).toBe('');
    const back = searchReducer(browsed, { type: 'back' });
    expect(back).toMatchObject({ mode: 'tracks', crumb: '', request: search, stack: [], loading: false });
    expect(back.tracks.map(x => x.id)).toEqual([1]);
    expect(searchReducer(back, { type: 'back' })).toBe(back);
  });

  it('nouvelle recherche : navigation, filtre et tri remis à zéro', () => {
    const s = { ...initialSearchState, stack: [{}], crumb: 'c', header: {}, filter: 'f', sortColumn: 'title' };
    expect(searchReducer(s, { type: 'search' })).toMatchObject({
      stack: [],
      crumb: '',
      header: null,
      filter: '',
      sortColumn: null,
    });
  });

  it('tri : second clic inverse ; popularité décroissante d’emblée', () => {
    const byTitle = searchReducer(initialSearchState, { type: 'sort', column: 'title' });
    expect(byTitle).toMatchObject({ sortColumn: 'title', sortAsc: true });
    expect(searchReducer(byTitle, { type: 'sort', column: 'title' }).sortAsc).toBe(false);
    expect(searchReducer(byTitle, { type: 'sort', column: 'rank' })).toMatchObject({
      sortColumn: 'rank',
      sortAsc: false,
    });
    const ents = searchReducer(initialSearchState, { type: 'sort-entities', column: 'name' });
    expect(ents.entitySort).toEqual({ column: 'name', asc: true });
    expect(searchReducer(ents, { type: 'sort-entities', column: 'name' }).entitySort.asc).toBe(false);
  });

  it('cocher, tout sélectionner, pastilles recalculées', () => {
    const s = reduce(
      initialSearchState,
      { type: 'loaded', request: search, crumb: '', data: result([t(1, 'PENDING'), t(2)]) },
      {
        type: 'select-all',
        visible: [t(1, 'PENDING'), t(2)],
        on: true,
      },
    );
    expect([...s.picked.keys()]).toEqual([1, 2]);
    const updated = searchReducer(s, { type: 'presence', tracks: [t(1, 'PRESENT'), t(2)] });
    expect([...updated.picked.keys()]).toEqual([2]);
    expect(searchReducer(updated, { type: 'toggle', track: t(2), on: false }).picked.size).toBe(0);
  });

  it('annulation et échec arrêtent le chargement', () => {
    const loading = searchReducer(initialSearchState, { type: 'started' });
    expect(searchReducer(loading, { type: 'stopped' }).loading).toBe(false);
    expect(searchReducer(loading, { type: 'failed', error: 'réseau' })).toMatchObject({
      loading: false,
      message: { text: 'Erreur lors de la recherche : réseau', error: true },
    });
  });
});
