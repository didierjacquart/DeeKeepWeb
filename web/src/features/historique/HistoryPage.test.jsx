import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { renderWithRouter } from '../../test/render';
import { mockApi } from '../../test/mock-api';
import { navBadges } from '../../layout/nav-badges';
import { player } from '../../core/player/player';
import { openSearchDialog } from '../musique/search-dialog/open-search-dialog';
import { HistoryPage } from './HistoryPage';

vi.mock('../musique/search-dialog/open-search-dialog', () => ({ openSearchDialog: vi.fn() }));

const HISTORY = {
  history: [
    {
      path: 'D:/m/b.mp3',
      title: 'Beta',
      artist: 'Zed',
      album: 'Z',
      coverSha1: 'sha-b',
      position: 1,
      onDisk: true,
      downloadedAt: 1,
    },
    {
      path: 'D:/m/a.mp3',
      title: 'Alpha',
      artist: 'Abba',
      album: 'A',
      coverSha1: null,
      position: 2,
      onDisk: false,
      downloadedAt: 2,
    },
  ],
  searchHistory: [
    { query: 'daft punk', position: 1 },
    { query: 'air', position: 2 },
  ],
};

function renderPage(responses = {}) {
  const server = mockApi({
    History: HISTORY,
    SearchCover: ({ query }) => ({ searchCover: { query, url: query === 'air' ? 'https://e/air.jpg' : null } }),
    ...responses,
  });
  const musique = { path: '/musique', element: <p>Page Musique</p> };
  return {
    server,
    ...renderWithRouter(<HistoryPage />, { route: '/historique', path: '/historique', extraRoutes: [musique] }),
  };
}

const searchRows = () =>
  screen.getAllByRole('button', { name: 'Relancer' }).map(b => b.closest('.dk-row').firstChild.textContent);
const titles = () =>
  screen.getAllByRole('button', { name: 'Écouter' }).map(b => b.closest('.dk-row').firstChild.firstChild.textContent);

describe('HistoryPage', () => {
  it('affiche les recherches puis les téléchargements du serveur', async () => {
    const { user } = renderPage();
    expect(screen.getByText('Chargement…')).toBeInTheDocument();
    expect(await screen.findByText('daft punk')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Recherches.*\(2\)/ })).toHaveAttribute('aria-selected', 'true');
    expect(searchRows()).toEqual(['daft punk', 'air']);

    await user.click(screen.getByRole('tab', { name: /Téléchargements.*\(2\)/ }));
    expect(titles()).toEqual(['Alpha', 'Beta']);
    const [alpha, beta] = screen.getAllByRole('button', { name: 'Écouter' });
    expect(alpha).toBeDisabled();
    expect(beta).toBeEnabled();
  });

  it('renseigne le compteur de navigation', async () => {
    renderPage();
    await waitFor(() => expect(navBadges.state.history).toBe(4));
  });

  it('montre la pochette du premier résultat de chaque recherche', async () => {
    const { server } = renderPage();
    await screen.findByText('daft punk');
    await waitFor(() => expect(server.queries('SearchCover')).toHaveLength(2));
    await waitFor(() => expect(document.querySelector('img[src="/img?u=https%3A%2F%2Fe%2Fair.jpg"]')).not.toBeNull());
    expect(screen.getByText('♪')).toBeInTheDocument();
  });

  it('trie les recherches et les téléchargements', async () => {
    const { user } = renderPage();
    await screen.findByText('daft punk');
    await user.click(screen.getByRole('button', { name: 'Recherche' }));
    expect(searchRows()).toEqual(['air', 'daft punk']);
    expect(screen.getByRole('button', { name: 'Recherche' })).toHaveTextContent('Recherche ▴');
    await user.click(screen.getByRole('button', { name: 'Recherche' }));
    expect(searchRows()).toEqual(['daft punk', 'air']);
    expect(screen.getByRole('button', { name: 'Recherche' })).toHaveTextContent('Recherche ▾');
    expect(screen.getByRole('button', { name: 'Recherche' })).toHaveClass('sorted');

    await user.click(screen.getByRole('tab', { name: /Téléchargements/ }));
    await user.click(screen.getByRole('button', { name: 'Artiste' }));
    expect(titles()).toEqual(['Alpha', 'Beta']);
    await user.click(screen.getByRole('button', { name: 'Artiste' }));
    expect(titles()).toEqual(['Beta', 'Alpha']);
    expect(screen.getByRole('button', { name: 'Artiste' })).toHaveTextContent('Artiste ▾');
  });

  it('vide les recherches après confirmation', async () => {
    const { server, user } = renderPage({ ClearSearches: { clearSearches: [] } });
    await screen.findByText('daft punk');
    await user.click(screen.getByRole('button', { name: 'Vider les recherches' }));
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText("Retirer toutes les recherches de l'historique ?")).toBeInTheDocument();
    expect(server.mutations('ClearSearches')).toEqual([]);
    await user.click(within(dialog).getByRole('button', { name: 'Vider' }));
    expect(server.mutations('ClearSearches')).toEqual([{}]);
    expect(server.mutate.mock.calls.at(-1)[2]).toEqual({ label: 'Vider les recherches', refetch: ['History'] });
  });

  it("n'envoie rien si la confirmation est annulée", async () => {
    const { server, user } = renderPage();
    await screen.findByText('daft punk');
    await user.click(screen.getByRole('button', { name: 'Vider les recherches' }));
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Annuler' }));
    expect(server.mutations('ClearSearches')).toEqual([]);
  });

  it('supprime une recherche', async () => {
    const { server, user } = renderPage({ DeleteSearch: { deleteSearch: [] } });
    await screen.findByText('daft punk');
    await user.click(screen.getAllByRole('button', { name: 'Supprimer cette recherche' })[1]);
    expect(server.mutations('DeleteSearch')).toEqual([{ query: 'air' }]);
  });

  it('relance une recherche depuis la page Musique et ajoute les pistes choisies', async () => {
    openSearchDialog.mockResolvedValue({
      tracks: [
        { id: '12', title: 'One More Time', artist: 'Daft Punk', duration: '5:20', preview: 'https://p' },
        { id: null, title: 'sans identifiant' },
      ],
    });
    const { server, user, router } = renderPage({ AddPending: { addPendingDownloads: [{ id: 'p1' }] } });
    await screen.findByText('daft punk');
    await user.click(screen.getAllByRole('button', { name: 'Relancer' })[0]);

    await waitFor(() => expect(server.mutations('AddPending')).toHaveLength(1));
    expect(router.state.location.pathname).toBe('/musique');
    expect(screen.getByText('Page Musique')).toBeInTheDocument();
    expect(openSearchDialog).toHaveBeenCalledWith('daft punk');
    expect(server.mutations('AddPending')[0].tracks).toEqual([
      {
        id: 12,
        title: 'One More Time',
        artist: 'Daft Punk',
        album: null,
        duration: 320,
        artistId: null,
        albumId: null,
        previewUrl: 'https://p',
        coverSmall: null,
        coverBig: null,
        link: null,
        trackPosition: null,
        diskNumber: null,
      },
    ]);
  });

  it("n'ajoute rien si la recherche est fermée sans choix", async () => {
    openSearchDialog.mockResolvedValue(undefined);
    const { server, user, router } = renderPage();
    await screen.findByText('daft punk');
    await user.click(screen.getAllByRole('button', { name: 'Relancer' })[1]);
    await waitFor(() => expect(openSearchDialog).toHaveBeenCalledWith('air'));
    expect(router.state.location.pathname).toBe('/musique');
    expect(server.mutations('AddPending')).toEqual([]);
  });

  it('agrandit la jaquette d’un téléchargement', async () => {
    const { user } = renderPage();
    await screen.findByText('daft punk');
    await user.click(screen.getByRole('tab', { name: /Téléchargements/ }));
    await user.click(screen.getByRole('button', { name: 'Agrandir la jaquette' }));
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByRole('heading', { name: 'Beta — Zed' })).toBeInTheDocument();
    expect(within(dialog).getByRole('img', { name: 'Jaquette' })).toHaveAttribute('src', '/covers/sha-b');
    await user.click(within(dialog).getByRole('button', { name: 'Fermer' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('écoute un téléchargement présent sur le disque', async () => {
    const play = vi.spyOn(player, 'play').mockImplementation(() => undefined);
    const { user } = renderPage();
    await screen.findByText('daft punk');
    await user.click(screen.getByRole('tab', { name: /Téléchargements/ }));
    await user.click(screen.getAllByRole('button', { name: 'Écouter' })[1]);
    expect(play).toHaveBeenCalledWith(expect.objectContaining({ id: 'D:/m/b.mp3', title: 'Beta' }), [
      expect.objectContaining({ id: 'D:/m/b.mp3' }),
    ]);
  });

  it('purge les fichiers introuvables', async () => {
    const { server, user } = renderPage({
      PurgeMissingHistory: { purgeMissingHistory: { removed: 1, message: '1 entrée retirée.' } },
    });
    await screen.findByText('daft punk');
    await user.click(screen.getByRole('tab', { name: /Téléchargements/ }));
    await user.click(screen.getByRole('button', { name: 'Purger les fichiers introuvables' }));
    expect(server.mutations('PurgeMissingHistory')).toEqual([{}]);
    expect(await screen.findByText('1 entrée retirée.')).toBeInTheDocument();
  });
});
