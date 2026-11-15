import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { renderWithRouter } from '../../test/render';
import { mockApi } from '../../test/mock-api';
import { player } from '../../core/player/player';
import { LibraryPage } from './LibraryPage';

const track = (path, fields) => ({
  path,
  title: null,
  artist: null,
  album: null,
  albumArtist: null,
  genre: null,
  year: null,
  duration: null,
  bitrate: null,
  codec: null,
  ext: null,
  fileSize: 1_000_000,
  coverSha1: null,
  inHistory: false,
  onDisk: true,
  archiveState: 'none',
  archiveBytes: null,
  archiveSourceBytes: null,
  format: 'FLAC',
  ...fields,
});

const TRACKS = [
  track('/m/a.flac', { title: 'One More Time', artist: 'Daft Punk', album: 'Discovery' }),
  track('/m/b.flac', { title: 'Da Funk', artist: 'Daft Punk', album: 'Homework' }),
  track('/m/c.flac', { title: 'Genesis', artist: 'Justice', album: 'Cross' }),
  track('/m/gone.flac', { title: 'Ghost', artist: 'Nobody', onDisk: false, archiveState: 'ok', archiveBytes: 1 }),
  track('/m/lost.flac', { title: 'Lost', artist: 'Nobody', onDisk: false }),
];

function setup() {
  const server = mockApi({ LibraryTracks: { tracks: { total: 5, totalOnDisk: 3, totalMissing: 2, items: TRACKS } } });
  const view = renderWithRouter(<LibraryPage />);
  return { server, ...view };
}

/** Titres des lignes affichées, dans l'ordre (nom accessible des cases). */
const shownTitles = () =>
  screen.queryAllByRole('checkbox', { name: /^Cocher / }).map(c => c.getAttribute('aria-label').slice(7));

describe('LibraryPage', () => {
  it('affiche les titres présents ou archivés, triés par artiste, et la ligne d’état', async () => {
    setup();
    expect(await screen.findByRole('checkbox', { name: 'Cocher One More Time' })).toBeInTheDocument();
    expect(shownTitles()).toEqual(['One More Time', 'Da Funk', 'Genesis', 'Ghost']);
    expect(screen.getByText(/^4 titres affichés sur 5 · 3 sur le disque/)).toBeInTheDocument();
  });

  it('montre les titres absents du disque sur demande', async () => {
    const { user } = setup();
    await screen.findByRole('checkbox', { name: 'Cocher Ghost' });
    await user.click(screen.getByRole('checkbox', { name: 'Titres absents du disque' }));
    expect(screen.getByRole('checkbox', { name: 'Cocher Lost' })).toBeInTheDocument();
  });

  it('filtre au fil de la frappe (tous les mots, ordre libre)', async () => {
    const { user } = setup();
    await screen.findByRole('checkbox', { name: 'Cocher Genesis' });
    await user.type(screen.getByRole('combobox', { name: 'Rechercher' }), 'homework daft');
    await waitFor(() => expect(shownTitles()).toEqual(['Da Funk']));
    await user.click(screen.getByRole('button', { name: 'Effacer la recherche' }));
    expect(shownTitles()).toHaveLength(4);
  });

  it('propose des valeurs, choisies au clavier', async () => {
    const { user } = setup();
    await screen.findByRole('checkbox', { name: 'Cocher Genesis' });
    const input = screen.getByRole('combobox', { name: 'Rechercher' });
    await user.type(input, 'daf');
    const list = screen.getByRole('listbox');
    expect(within(list).getByRole('option', { name: 'Artiste · Daft Punk (2 titres)' })).toBeInTheDocument();
    expect(input).toHaveAttribute('aria-expanded', 'true');

    await user.keyboard('{ArrowDown}');
    expect(within(list).getAllByRole('option')[0]).toHaveAttribute('aria-selected', 'true');
    await user.keyboard('{Enter}');

    expect(input).toHaveValue('Daft Punk');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(shownTitles()).toEqual(['One More Time', 'Da Funk']);
  });

  it('complète la frappe avec la meilleure proposition, acceptée par Tab', async () => {
    const { user } = setup();
    await screen.findByRole('checkbox', { name: 'Cocher Genesis' });
    const input = screen.getByRole('combobox', { name: 'Rechercher' });
    await user.type(input, 'ju');
    expect(within(screen.getByRole('listbox')).getByRole('option', { name: /Artiste · Justice/ })).toBeInTheDocument();
    expect(document.querySelector('.predict .rest')).toHaveTextContent('stice');

    await user.keyboard('{Tab}');
    expect(input).toHaveValue('Justice');
    expect(input).toHaveFocus();
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(document.querySelector('.predict')).toBeNull();
    expect(shownTitles()).toEqual(['Genesis']);
    expect(screen.getByRole('button', { name: '▶ Écouter l’artiste « Justice »' })).toBeInTheDocument();
  });

  it('ne cherche pas dans le format', async () => {
    const { user } = setup();
    await screen.findByRole('checkbox', { name: 'Cocher Genesis' });
    await user.type(screen.getByRole('combobox', { name: 'Rechercher' }), 'flac{Enter}');
    expect(shownTitles()).toEqual([]);
  });

  it('trie par un clic sur l’en-tête, puis en sens inverse', async () => {
    const { user } = setup();
    await screen.findByRole('checkbox', { name: 'Cocher Genesis' });
    await user.click(screen.getByRole('button', { name: 'Titre' }));
    expect(shownTitles()).toEqual(['Da Funk', 'Genesis', 'Ghost', 'One More Time']);
    expect(screen.getByRole('columnheader', { name: 'Titre' })).toHaveAttribute('aria-sort', 'ascending');
    await user.click(screen.getByRole('button', { name: 'Titre' }));
    expect(shownTitles()).toEqual(['One More Time', 'Ghost', 'Genesis', 'Da Funk']);
  });

  it('coche une ligne, tout, puis rien', async () => {
    const { user } = setup();
    const one = await screen.findByRole('checkbox', { name: 'Cocher Genesis' });
    await user.click(one);
    expect(one).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Cocher Ghost' })).not.toBeChecked();

    await user.click(screen.getByRole('button', { name: 'Tout cocher' }));
    for (const box of screen.getAllByRole('checkbox', { name: /^Cocher / })) expect(box).toBeChecked();
    await user.click(screen.getByRole('button', { name: 'Tout décocher' }));
    for (const box of screen.getAllByRole('checkbox', { name: /^Cocher / })) expect(box).not.toBeChecked();
  });

  it('adapte le menu contextuel à l’état du titre', async () => {
    setup();
    fireEvent.contextMenu(await screen.findByRole('checkbox', { name: 'Cocher Ghost' }));
    const item = name => screen.getByRole('menuitem', { name });
    expect(item('Écouter dans le lecteur')).toBeEnabled();
    expect(item('Épingler pour l’écoute hors ligne')).toBeDisabled();
    expect(item('Archiver ce titre')).toBeDisabled();
    expect(item("Restaurer ce titre depuis l'archive")).toBeEnabled();
    expect(item("Retirer de l'archive")).toBeEnabled();
    expect(item('Copier le chemin')).toBeEnabled();

    fireEvent.keyDown(document, { key: 'Escape' });
    fireEvent.contextMenu(screen.getByRole('checkbox', { name: 'Cocher Genesis' }));
    expect(item('Archiver ce titre')).toBeEnabled();
    expect(item("Restaurer ce titre depuis l'archive")).toBeDisabled();
    expect(item("Retirer de l'archive")).toBeDisabled();
  });

  it('« Écouter dans le lecteur » joue le titre, la liste affichée devenant la file', async () => {
    const play = vi.spyOn(player, 'play').mockImplementation(() => undefined);
    const { user } = setup();
    fireEvent.contextMenu(await screen.findByRole('checkbox', { name: 'Cocher Ghost' }));
    await user.click(screen.getByRole('menuitem', { name: 'Écouter dans le lecteur' }));

    expect(play).toHaveBeenCalledTimes(1);
    const [current, queue] = play.mock.calls[0];
    expect(current).toMatchObject({
      id: '/m/gone.flac',
      title: 'Ghost',
      src: expect.stringContaining('/archive-media/'),
    });
    expect(queue.map(t => t.id)).toEqual(['/m/a.flac', '/m/b.flac', '/m/c.flac', '/m/gone.flac']);
    expect(play.mock.calls[0][2]).toEqual({ open: true });
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
  });

  it('ouvre la fenêtre du lecteur à l’écoute', async () => {
    vi.spyOn(window.HTMLMediaElement.prototype, 'play').mockResolvedValue(undefined);
    const { user } = setup();
    await screen.findByRole('checkbox', { name: 'Cocher Genesis' });
    player.setExpanded(false);
    await user.click(screen.getAllByRole('button', { name: 'Écouter' })[2]);
    expect(player.state.expanded).toBe(true);
    player.close();
  });

  it('écoute l’album ou l’artiste d’un titre depuis son menu', async () => {
    const play = vi.spyOn(player, 'play').mockImplementation(() => undefined);
    const { user } = setup();
    fireEvent.contextMenu(await screen.findByRole('checkbox', { name: 'Cocher Da Funk' }));
    await user.click(screen.getByRole('menuitem', { name: 'Écouter l’album « Homework » (1 titre)' }));
    expect(play.mock.calls[0][1].map(t => t.id)).toEqual(['/m/b.flac']);

    fireEvent.contextMenu(screen.getByRole('checkbox', { name: 'Cocher Da Funk' }));
    await user.click(screen.getByRole('menuitem', { name: 'Écouter l’artiste « Daft Punk » (2 titres)' }));
    const [first, queue, options] = play.mock.calls[1];
    expect(first.id).toBe('/m/a.flac');
    expect(queue.map(t => t.id)).toEqual(['/m/a.flac', '/m/b.flac']);
    expect(options).toEqual({ open: true });

    // Ni sur le disque ni dans l'archive : rien à écouter.
    await user.click(screen.getByRole('checkbox', { name: 'Titres absents du disque' }));
    fireEvent.contextMenu(screen.getByRole('checkbox', { name: 'Cocher Lost' }));
    expect(screen.getByRole('menuitem', { name: 'Écouter l’artiste « Nobody » (1 titre)' })).toBeEnabled();
  });

  it('le ▶ des colonnes Artiste et Album écoute l’artiste ou l’album entier', async () => {
    const play = vi.spyOn(player, 'play').mockImplementation(() => undefined);
    const { user } = setup();
    await user.click(await screen.findByRole('button', { name: 'Écouter l’album « Discovery »' }));
    expect(play.mock.calls[0][1].map(t => t.id)).toEqual(['/m/a.flac']);
    await user.click(screen.getAllByRole('button', { name: 'Écouter l’artiste « Daft Punk »' })[1]);
    expect(play.mock.calls[1][1].map(t => t.id)).toEqual(['/m/a.flac', '/m/b.flac']);
    expect(play.mock.calls[1][2]).toEqual({ open: true });
  });

  it('écoute l’artiste choisi dans les propositions de la recherche', async () => {
    const play = vi.spyOn(player, 'play').mockImplementation(() => undefined);
    const { user } = setup();
    await screen.findByRole('checkbox', { name: 'Cocher Genesis' });
    expect(screen.queryByRole('button', { name: /^▶ Écouter l’/ })).not.toBeInTheDocument();
    await user.type(screen.getByRole('combobox', { name: 'Rechercher' }), 'daf');
    await user.click(within(screen.getByRole('listbox')).getByRole('option', { name: /Artiste · Daft Punk/ }));

    await user.click(screen.getByRole('button', { name: '▶ Écouter l’artiste « Daft Punk »' }));
    expect(play.mock.calls[0][1].map(t => t.id)).toEqual(['/m/a.flac', '/m/b.flac']);

    // Retoucher le champ oublie le choix.
    await user.type(screen.getByRole('combobox', { name: 'Rechercher' }), 'x');
    expect(screen.queryByRole('button', { name: /^▶ Écouter l’/ })).not.toBeInTheDocument();
  });

  it('le bouton ▶ d’une ligne joue aussi le titre', async () => {
    const play = vi.spyOn(player, 'play').mockImplementation(() => undefined);
    const { user } = setup();
    await screen.findByRole('checkbox', { name: 'Cocher Genesis' });
    await user.click(screen.getAllByRole('button', { name: 'Écouter' })[2]);
    expect(play.mock.calls[0][0].id).toBe('/m/c.flac');
  });

  it('« Actualiser » relance les requêtes affichées', async () => {
    const { user, server } = setup();
    await screen.findByRole('checkbox', { name: 'Cocher Genesis' });
    await user.click(screen.getByRole('button', { name: 'Actualiser' }));
    expect(server.refetchActive).toHaveBeenCalled();
  });

  it('garde la dernière liste reçue si une réponse arrive sans données', async () => {
    const { server } = setup();
    await screen.findByRole('checkbox', { name: 'Cocher Genesis' });
    server.pushWatch('LibraryTracks', { data: null, stale: true });
    expect(await screen.findByText(/Hors ligne : liste servie par le cache local/)).toBeInTheDocument();
    expect(shownTitles()).toHaveLength(4);
  });
});
