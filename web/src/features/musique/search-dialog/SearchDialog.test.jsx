import { act, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { renderWithRouter } from '../../../test/render';
import { mockApi } from '../../../test/mock-api';
import { connectivity } from '../../../core/offline/connectivity';
import { openTrackDedupDialog } from '../../organiser/dedup/open-track-dedup-dialog';
import { openSearchDialog } from './open-search-dialog';

vi.mock('../../organiser/dedup/open-track-dedup-dialog', () => ({ openTrackDedupDialog: vi.fn() }));

const track = (id, title, state = 'ABSENT', extra = {}) => ({
  id,
  title,
  artist: 'Air',
  artistId: 5,
  album: 'Moon Safari',
  albumId: 50,
  duration: 200,
  rank: 500000,
  previewUrl: 'https://p/1.mp3',
  coverSmall: null,
  coverBig: null,
  link: null,
  explicit: false,
  trackPosition: 1,
  diskNumber: 1,
  presence: { key: String(id), state, reason: null, files: [] },
  ...extra,
});
const result = (tracks, entities = [], header = null) => ({
  query: 'air',
  kind: 'ALL',
  error: null,
  header,
  entities,
  tracks,
});
const TRACKS = [track(1, 'Sexy Boy'), track(2, 'Kelly Watch the Stars'), track(3, 'La Femme d’argent', 'PRESENT')];

async function open(responses = {}, initialQuery) {
  const server = mockApi({
    SearchHistory: { searchHistory: [] },
    RememberSearch: {},
    CatalogSearch: { search: result(TRACKS) },
    ...responses,
  });
  const view = renderWithRouter(<p>Page</p>);
  let promise;
  act(() => {
    promise = openSearchDialog(initialQuery);
  });
  const dialog = screen.getByRole('dialog');
  return { server, dialog, promise, ...view };
}

async function searchFor(user, dialog, text) {
  await user.type(within(dialog).getByLabelText(/Rechercher par morceau/), text);
  await user.click(within(dialog).getByRole('button', { name: 'Rechercher' }));
}

beforeEach(() => connectivity.setState({ socketConnected: true }));
afterEach(() => {
  connectivity.setState({ socketConnected: false });
  vi.useRealTimers();
});

describe('SearchDialog', () => {
  it('une recherche affiche les titres trouvés', async () => {
    const { server, user, dialog } = await open();
    await user.selectOptions(within(dialog).getByRole('combobox', { name: 'Type de recherche' }), 'ARTIST');
    await searchFor(user, dialog, 'air');
    expect(await within(dialog).findByText('Sexy Boy')).toBeInTheDocument();
    expect(within(dialog).getByText('Kelly Watch the Stars')).toBeInTheDocument();
    expect(server.queries('CatalogSearch')).toEqual([{ query: 'air', kind: 'ARTIST' }]);
    expect(server.mutations('RememberSearch')).toEqual([{ query: 'air' }]);
  });

  it('les titres cochés sont rendus par « Ajouter la sélection »', async () => {
    const { user, dialog, promise } = await open();
    await searchFor(user, dialog, 'air');
    await user.click(await within(dialog).findByRole('checkbox', { name: 'Sélectionner Sexy Boy' }));
    await user.click(within(dialog).getByRole('checkbox', { name: 'Sélectionner Kelly Watch the Stars' }));
    await user.click(within(dialog).getByRole('button', { name: 'Ajouter la sélection (2)' }));
    const { presence: _p1, ...sexyBoy } = TRACKS[0];
    const { presence: _p2, ...kelly } = TRACKS[1];
    await expect(promise).resolves.toEqual({ tracks: [sexyBoy, kelly] });
  });

  it('« Annuler » ferme sans rien rendre', async () => {
    const { user, dialog, promise } = await open();
    expect(within(dialog).getByRole('button', { name: 'Ajouter la sélection' })).toBeDisabled();
    await user.click(within(dialog).getByRole('button', { name: 'Annuler' }));
    await expect(promise).resolves.toBeUndefined();
  });

  it('« Annuler » interrompt d’abord une recherche en cours', async () => {
    const { user, dialog, promise } = await open({ CatalogSearch: () => new Promise(() => {}) });
    await searchFor(user, dialog, 'air');
    expect(within(dialog).getByRole('button', { name: 'Recherche...' })).toBeDisabled();
    await user.click(within(dialog).getByRole('button', { name: 'Annuler' }));
    expect(within(dialog).getByRole('button', { name: 'Rechercher' })).toBeEnabled();
    await user.click(within(dialog).getByRole('button', { name: 'Annuler' }));
    await expect(promise).resolves.toBeUndefined();
  });

  it('déjà sur le disque : case verrouillée, ignorée par « Tout sélectionner »', async () => {
    const { user, dialog } = await open();
    await searchFor(user, dialog, 'air');
    expect(await within(dialog).findByRole('checkbox', { name: 'Sélectionner La Femme d’argent' })).toBeDisabled();
    expect(within(dialog).getByText('✓ 1 déjà sur le disque')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('checkbox', { name: 'Tout sélectionner' }));
    expect(within(dialog).getByRole('button', { name: 'Ajouter la sélection (2)' })).toBeEnabled();
  });

  it('une requête initiale est lancée et mémorisée dès l’ouverture', async () => {
    const { server, dialog } = await open({}, '  air ');
    expect(await within(dialog).findByText('Sexy Boy')).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/Rechercher par morceau/)).toHaveValue('air');
    expect(server.queries('CatalogSearch')).toEqual([{ query: 'air', kind: 'ALL' }]);
    expect(server.mutations('RememberSearch')).toEqual([{ query: 'air' }]);
  });

  it('explore un artiste puis revient aux résultats', async () => {
    const artist = { kind: 'ARTIST', id: 5, name: 'Air', subtitle: '1 M de fans', picture: null };
    const { server, user, dialog } = await open({
      CatalogSearch: { search: result(TRACKS, [artist]) },
      CatalogBrowse: { browse: result([track(7, 'Playground Love')]) },
    });
    await searchFor(user, dialog, 'air');
    await user.click(await within(dialog).findByRole('button', { name: 'Voir le top 50' }));
    expect(await within(dialog).findByText('Playground Love')).toBeInTheDocument();
    expect(within(dialog).getByText('Artiste : Air — top 50')).toBeInTheDocument();
    expect(server.queries('CatalogBrowse')).toEqual([{ kind: 'ARTIST', id: 5 }]);
    await user.click(within(dialog).getByRole('button', { name: '← Retour' }));
    expect(within(dialog).getByRole('button', { name: 'Voir le top 50' })).toBeInTheDocument();
    expect(within(dialog).queryByText('Playground Love')).not.toBeInTheDocument();
  });

  it('dernières recherches : relancer et retirer', async () => {
    const { server, user, dialog } = await open({
      SearchHistory: { searchHistory: [{ query: 'daft punk', position: 0 }] },
      SearchCover: ({ query }) => ({ searchCover: { query, url: null } }),
      DeleteSearch: {},
    });
    await waitFor(() => expect(server.queries('SearchHistory')).toHaveLength(1));
    await user.click(within(dialog).getByLabelText(/Rechercher par morceau/));
    expect(await within(dialog).findByText('Dernières recherches')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Retirer cette recherche' }));
    expect(server.mutations('DeleteSearch')).toEqual([{ query: 'daft punk' }]);
  });

  it('dernières recherches : un clic relance la recherche', async () => {
    const { server, user, dialog } = await open({
      SearchHistory: { searchHistory: [{ query: 'daft punk', position: 0 }] },
      SearchCover: ({ query }) => ({ searchCover: { query, url: null } }),
    });
    await waitFor(() => expect(server.queries('SearchHistory')).toHaveLength(1));
    await user.click(within(dialog).getByLabelText(/Rechercher par morceau/));
    await user.click(await within(dialog).findByRole('button', { name: /daft punk/ }));
    await waitFor(() => expect(server.queries('CatalogSearch')).toEqual([{ query: 'daft punk', kind: 'ALL' }]));
  });

  it('« Dédoublonner… » ouvre l’écran des doublons puis recalcule les pastilles', async () => {
    const file = { path: 'D:/m/sexy.mp3', reason: 'Même titre', bitrate: 320, fileSize: 1000, ext: 'mp3' };
    const possible = track(1, 'Sexy Boy', 'POSSIBLE', {
      presence: { key: '1', state: 'POSSIBLE', reason: 'Même titre', files: [file] },
    });
    vi.mocked(openTrackDedupDialog).mockResolvedValueOnce(['D:/m/sexy.mp3']);
    const { server, user, dialog } = await open({ CatalogSearch: { search: result([possible]) } });
    await searchFor(user, dialog, 'air');
    await user.click(await within(dialog).findByRole('button', { name: 'Dédoublonner…' }));
    expect(openTrackDedupDialog).toHaveBeenCalledWith([
      { key: '1', title: 'Sexy Boy', artist: 'Air', album: 'Moon Safari', files: [file] },
    ]);
    expect(await screen.findByText('Déduplication : 1 fichier(s) supprimé(s).')).toBeInTheDocument();
    await waitFor(() => expect(server.queries('CatalogSearch')).toHaveLength(2));
  });

  it("pastilles « ⏳ » : recalculées quand l'index de présence est prêt", async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    let state = 'PENDING';
    const { server, dialog } = await open(
      { CatalogSearch: () => ({ search: result([track(1, 'Sexy Boy', state)]) }) },
      'air',
    );
    // Pas de findBy* ici : les utilitaires asynchrones de Testing Library attendent un setTimeout réel.
    await act(() => vi.advanceTimersByTimeAsync(0));
    expect(within(dialog).getByRole('img', { name: 'Vérification en cours' })).toBeInTheDocument();
    expect(server.queries('CatalogSearch')).toHaveLength(1);
    state = 'ABSENT';
    await act(() => vi.advanceTimersByTimeAsync(3000));
    expect(server.queries('CatalogSearch')).toHaveLength(2);
    expect(within(dialog).getByRole('img', { name: 'Absent' })).toBeInTheDocument();
  });
});
