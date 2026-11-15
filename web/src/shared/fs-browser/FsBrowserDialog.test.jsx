import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { DialogHost } from '../dialog/DialogHost';
import { mockApi } from '../../test/mock-api';
import { openFsBrowser } from './open-fs-browser';

const entry = (name, dir, fields = {}) => ({
  name,
  localPath: `${dir}\\${name}`,
  isDirectory: false,
  size: 4_200_000,
  mtime: 1,
  isAudio: true,
  ...fields,
});

const LISTINGS = {
  'D:\\Musique': {
    localPath: 'D:\\Musique',
    parent: null,
    roots: ['D:\\Musique', 'E:\\Archive'],
    entries: [entry('Pop', 'D:\\Musique', { isDirectory: true, size: null }), entry('a.mp3', 'D:\\Musique')],
  },
  'D:\\Musique\\Pop': {
    localPath: 'D:\\Musique\\Pop',
    parent: 'D:\\Musique',
    roots: ['D:\\Musique'],
    entries: [],
  },
};

function setup(options) {
  const server = mockApi({ BrowseFolder: ({ path }) => ({ browseFolder: LISTINGS[path || 'D:\\Musique'] }) });
  const user = userEvent.setup();
  render(<DialogHost />);
  let result;
  act(() => {
    result = openFsBrowser(options);
  });
  return { server, user, result };
}

describe('navigateur de dossiers du serveur', () => {
  it('choisit le dossier affiché, après y être descendu', async () => {
    const { user, result } = setup({ mode: 'folder', title: 'Choisir le dossier à organiser' });
    expect(screen.getByRole('heading', { name: 'Choisir le dossier à organiser' })).toBeInTheDocument();
    await user.click(await screen.findByRole('button', { name: /Pop/ }));
    expect(await screen.findByText('Dossier vide')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Choisir ce dossier' }));
    await expect(result).resolves.toEqual(['D:\\Musique\\Pop']);
  });

  it('remonte au parent et saute d’une racine à l’autre', async () => {
    const { user, server } = setup({ mode: 'folder', start: 'D:\\Musique\\Pop' });
    await user.click(await screen.findByRole('button', { name: 'Dossier parent' }));
    expect(await screen.findByText('a.mp3')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'E:\\Archive' }));
    expect(server.queries('BrowseFolder').map(v => v.path)).toEqual(['D:\\Musique\\Pop', 'D:\\Musique', 'E:\\Archive']);
  });

  it('ouvre un chemin saisi, et garde le dossier affiché si le serveur le refuse', async () => {
    const server = mockApi({
      BrowseFolder: ({ path }) => {
        if (path === 'Z:\\absent') throw new Error('Dossier hors des racines autorisées.');
        return { browseFolder: LISTINGS[path || 'D:\\Musique'] };
      },
    });
    const user = userEvent.setup();
    render(<DialogHost />);
    act(() => void openFsBrowser({ mode: 'folder' }));
    const field = await screen.findByRole('textbox', { name: 'Chemin du dossier' });
    await user.clear(field);
    await user.type(field, 'Z:\\absent{Enter}');
    expect(await screen.findByText('Dossier hors des racines autorisées.')).toBeInTheDocument();
    expect(screen.getByText('a.mp3')).toBeInTheDocument();
    const again = screen.getByRole('textbox', { name: 'Chemin du dossier' });
    await user.clear(again);
    await user.type(again, 'D:\\Musique\\Pop{Enter}');
    expect(await screen.findByText('Dossier vide')).toBeInTheDocument();
    expect(server.queries('BrowseFolder').map(v => v.path)).toEqual(['', 'Z:\\absent', 'D:\\Musique\\Pop']);
  });

  it('choisit un seul fichier filtré par extension en mode « file »', async () => {
    mockApi({
      BrowseFolder: () => ({
        browseFolder: {
          localPath: 'D:\\Sauvegarde',
          parent: null,
          roots: ['D:\\Sauvegarde'],
          entries: [
            entry('Ancien', 'D:\\Sauvegarde', { isDirectory: true, size: null, isAudio: false }),
            entry('deekeep-archive.db', 'D:\\Sauvegarde', { isAudio: false }),
            entry('notes.txt', 'D:\\Sauvegarde', { isAudio: false }),
          ],
        },
      }),
    });
    const user = userEvent.setup();
    render(<DialogHost />);
    let result;
    act(() => {
      result = openFsBrowser({ mode: 'file', extensions: ['.db'], allowFolder: true });
    });
    await user.click(await screen.findByRole('button', { name: /deekeep-archive\.db/ }));
    expect(screen.queryByText('notes.txt')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Choisir ce dossier' })).toBeEnabled();
    await user.click(screen.getByRole('button', { name: 'Choisir ce fichier' }));
    await expect(result).resolves.toEqual(['D:\\Sauvegarde\\deekeep-archive.db']);
  });

  it('coche plusieurs fichiers en mode « files »', async () => {
    const { user, result, server } = setup({ mode: 'files', audioOnly: true });
    await user.click(await screen.findByRole('checkbox', { name: 'a.mp3' }));
    await user.click(screen.getByRole('checkbox', { name: 'Pop' }));
    expect(screen.getByText('4,2 Mo')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Ajouter (2)' }));
    await expect(result).resolves.toEqual(['D:\\Musique\\a.mp3', 'D:\\Musique\\Pop']);
    expect(server.queries('BrowseFolder')[0]).toEqual({ path: '', audioOnly: true });
  });

  it('affiche l’erreur du serveur et s’annule', async () => {
    mockApi({ BrowseFolder: new Error('Dossier non autorisé') });
    const user = userEvent.setup();
    render(<DialogHost />);
    let result;
    act(() => {
      result = openFsBrowser({ mode: 'folder' });
    });
    expect(await screen.findByText('Dossier non autorisé')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Annuler' }));
    await expect(result).resolves.toBeUndefined();
  });
});
