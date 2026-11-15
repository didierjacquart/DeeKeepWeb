import { act, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { makeJob, renderWithRouter } from '../../test/render';
import { mockApi } from '../../test/mock-api';
import { jobs } from '../../core/jobs/jobs';
import { connectivity } from '../../core/offline/connectivity';
import { MusicPage } from './MusicPage';

const deezer = (id, title, extra = {}) => ({
  id,
  title,
  artist: 'Daft Punk',
  artistId: 27,
  album: 'Discovery',
  albumId: 302127,
  duration: 224,
  previewUrl: null,
  coverSmall: null,
  coverBig: null,
  link: null,
  trackPosition: null,
  diskNumber: null,
  ...extra,
});

const downloadJob = fields => makeJob({ service: 'downloader', type: 'download', payload: null, ...fields });

const QUEUE = {
  DownloadJobs: {
    downloadJobs: [
      downloadJob({
        id: 'j-ok',
        status: 'SUCCEEDED',
        createdAt: 1,
        payload: { track: deezer(2, 'Digital Love') },
        result: { dbPath: 'D:/m/digital.mp3' },
      }),
      downloadJob({
        id: 'j-ko',
        status: 'FAILED',
        message: 'Introuvable',
        createdAt: 2,
        payload: { track: deezer(3, 'Aerodynamic') },
      }),
    ],
  },
  PendingDownloads: { pendingDownloads: [{ id: 'p-1', addedAt: 1, track: deezer(1, 'Harder') }] },
  Presence: ({ items }) => ({
    presence: items.map(i => ({ key: i.key, state: 'ABSENT', reason: null, files: [] })),
  }),
};

function renderPage(responses = {}) {
  const server = mockApi({ ...QUEUE, ...responses });
  return { server, ...renderWithRouter(<MusicPage />) };
}

const rowOf = title => screen.getByText(title, { selector: '.t' }).closest('.dk-row');

beforeEach(() => connectivity.setState({ socketConnected: true }));
afterEach(() => connectivity.setState({ socketConnected: false }));

describe('MusicPage', () => {
  it('affiche la file du serveur : lignes en attente puis tâches', async () => {
    renderPage();
    expect(await screen.findByText('Harder', { selector: '.t' })).toBeInTheDocument();
    const titles = [...document.querySelectorAll('.dk-row .t')].map(e => e.textContent);
    expect(titles).toEqual(['Harder', 'Digital Love', 'Aerodynamic']);
    expect(within(rowOf('Harder')).getAllByText('En attente')).not.toHaveLength(0);
    expect(within(rowOf('Digital Love')).getAllByText('Téléchargé ✓')).not.toHaveLength(0);
    expect(within(rowOf('Aerodynamic')).getAllByText('Introuvable')).not.toHaveLength(0);
    expect(screen.getByText('fichiers téléchargés').previousElementSibling).toHaveTextContent('1');
    expect(screen.getByText('fichiers abandonnés').previousElementSibling).toHaveTextContent('1');
    await waitFor(() => expect(within(rowOf('Harder')).getByRole('img', { name: 'Absent' })).toBeInTheDocument());
  });

  it('liste vide : invitation à rechercher', async () => {
    renderPage({ DownloadJobs: { downloadJobs: [] }, PendingDownloads: { pendingDownloads: [] } });
    expect(await screen.findByText("Aucun titre. « Rechercher » permet d'en ajouter à la liste.")).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Extraire' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Réessayer' })).toBeDisabled();
  });

  it('« Tout cocher » puis « Extraire » : lignes en attente mises en file, échecs relancés', async () => {
    const { server, user } = renderPage();
    await screen.findByText('Harder', { selector: '.t' });
    await user.click(screen.getByRole('button', { name: 'Tout cocher' }));
    expect(screen.getByRole('checkbox', { name: 'Cocher Harder' })).toBeChecked();
    await user.click(screen.getByRole('button', { name: 'Extraire' }));

    await waitFor(() => expect(server.mutations('Enqueue')).toHaveLength(1));
    const [vars] = server.mutations('Enqueue');
    expect(vars.pendingIds).toEqual(['p-1']);
    expect(vars.tracks).toEqual([
      expect.objectContaining({ id: 1, title: 'Harder', duration: 224, album: 'Discovery' }),
    ]);
    expect(vars.batchId).toMatch(/^[0-9a-f]{32}$/);
    await waitFor(() => expect(server.mutations('RetryDownloads')).toEqual([{ ids: ['j-ko'] }]));
    await waitFor(() => expect(screen.getByRole('checkbox', { name: 'Cocher Harder' })).not.toBeChecked());
  });

  it('« Extraire » avec une ligne cochée : seulement elle', async () => {
    const { server, user } = renderPage();
    await user.click(await screen.findByRole('checkbox', { name: 'Cocher Aerodynamic' }));
    await user.click(screen.getByRole('button', { name: 'Extraire' }));
    await waitFor(() => expect(server.mutations('RetryDownloads')).toEqual([{ ids: ['j-ko'] }]));
    expect(server.mutations('Enqueue')).toHaveLength(0);
  });

  it('« Retirer la sélection » : lignes en attente et tâches retirées', async () => {
    const { server, user } = renderPage();
    const remove = screen.getByRole('button', { name: 'Retirer la sélection' });
    expect(remove).toBeDisabled();
    await user.click(await screen.findByRole('checkbox', { name: 'Cocher Harder' }));
    await user.click(screen.getByRole('checkbox', { name: 'Cocher Digital Love' }));
    await user.click(remove);
    expect(await screen.findByText('2 élément(s) retiré(s) de votre liste.')).toBeInTheDocument();
    expect(server.mutations('RemovePending')).toEqual([{ ids: ['p-1'] }]);
    expect(server.mutations('RemoveJobs')).toEqual([{ ids: ['j-ok'] }]);
  });

  it('« Purger les téléchargés » : purge serveur, compteur conservé', async () => {
    const { server, user } = renderPage({ Purge: { purgeDownloads: 1 } });
    await screen.findByText('Digital Love', { selector: '.t' });
    server.respond('DownloadJobs', { downloadJobs: QUEUE.DownloadJobs.downloadJobs.slice(1) });
    await user.click(screen.getByRole('button', { name: 'Purger les téléchargés' }));
    expect(await screen.findByText('1 téléchargement(s) réussi(s) retiré(s) de la liste.')).toBeInTheDocument();
    expect(server.mutations('Purge')).toEqual([{}]);
    await waitFor(() => expect(screen.queryByText('Digital Love', { selector: '.t' })).not.toBeInTheDocument());
    expect(screen.getByText('fichiers téléchargés').previousElementSibling).toHaveTextContent('1');
  });

  it('rien à purger : message sans appel au serveur', async () => {
    const { server, user } = renderPage({ DownloadJobs: { downloadJobs: [] } });
    await screen.findByText('Harder', { selector: '.t' });
    await user.click(screen.getByRole('button', { name: 'Purger les téléchargés' }));
    expect(await screen.findByText('Aucun téléchargement réussi à purger.')).toBeInTheDocument();
    expect(server.mutations('Purge')).toHaveLength(0);
  });

  it("suit la progression d'une tâche en cours", async () => {
    renderPage({
      DownloadJobs: {
        downloadJobs: [downloadJob({ id: 'j-run', status: 'QUEUED', payload: { track: deezer(4, 'Voyager') } })],
      },
    });
    await screen.findByText('Voyager', { selector: '.t' });
    expect(within(rowOf('Voyager')).getAllByText("En file d'attente")).not.toHaveLength(0);

    act(() =>
      jobs.upsert(
        downloadJob({ id: 'j-run', status: 'RUNNING', progress: 0.42, message: 'Téléchargement', updatedAt: 5 }),
      ),
    );
    const row = rowOf('Voyager');
    expect(within(row).getAllByText('Téléchargement')).not.toHaveLength(0);
    expect(within(row).getByRole('progressbar')).toHaveAttribute('aria-valuetext', '3:44 - 42%');
    expect(within(row).getByRole('button', { name: 'Annuler' })).toBeInTheDocument();
  });

  it('trie par colonne', async () => {
    const { user } = renderPage();
    await screen.findByText('Harder', { selector: '.t' });
    await user.click(screen.getByRole('button', { name: 'Titre' }));
    const titles = () => [...document.querySelectorAll('.dk-row .t')].map(e => e.textContent);
    expect(titles()).toEqual(['Aerodynamic', 'Digital Love', 'Harder']);
    expect(screen.getByRole('button', { name: 'Titre' })).toHaveTextContent('Titre ▲');
    await user.click(screen.getByRole('button', { name: 'Titre' }));
    expect(titles()).toEqual(['Harder', 'Digital Love', 'Aerodynamic']);
  });

  it('« Rechercher » : les titres choisis rejoignent la liste, les doublons sont signalés', async () => {
    const found = [
      {
        ...deezer(1, 'Harder'),
        rank: 1,
        explicit: false,
        presence: { key: '1', state: 'ABSENT', reason: null, files: [] },
      },
      {
        ...deezer(9, 'Veridis Quo'),
        rank: 1,
        explicit: false,
        presence: { key: '9', state: 'ABSENT', reason: null, files: [] },
      },
    ];
    const { server, user } = renderPage({
      SearchHistory: { searchHistory: [] },
      RememberSearch: {},
      CatalogSearch: { search: { query: 'daft', kind: 'ALL', error: null, header: null, entities: [], tracks: found } },
      AddPending: { addPendingDownloads: [{ id: 'p-9' }] },
    });
    await screen.findByText('Harder', { selector: '.t' });
    await user.click(screen.getByRole('button', { name: 'Rechercher' }));
    const dialog = await screen.findByRole('dialog');
    await user.type(within(dialog).getByLabelText(/Rechercher par morceau/), 'daft{Enter}');
    await user.click(await within(dialog).findByRole('checkbox', { name: 'Tout sélectionner' }));
    await user.click(within(dialog).getByRole('button', { name: 'Ajouter la sélection (2)' }));

    await waitFor(() => expect(server.mutations('AddPending')).toHaveLength(1));
    expect(server.mutations('AddPending')[0].tracks).toEqual([
      expect.objectContaining({ id: 9, title: 'Veridis Quo' }),
    ]);
    expect(
      await screen.findByText("1 titre(s) déjà dans la liste n'ont pas été ajoutés une seconde fois."),
    ).toBeInTheDocument();
  });
});
