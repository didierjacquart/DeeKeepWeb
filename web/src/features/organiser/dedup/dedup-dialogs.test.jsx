import { act, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { mockApi } from '../../../test/mock-api';
import { renderWithRouter } from '../../../test/render';
import { openFileDedupDialog } from './open-file-dedup-dialog';
import { openTrackDedupDialog } from './open-track-dedup-dialog';

/** Ouvre une boîte depuis un test et garde sa promesse. */
function open(opener) {
  let promise;
  act(() => {
    promise = opener();
  });
  return promise;
}

const TRACKS = [
  {
    key: 't1',
    title: 'Song',
    artist: 'Inna',
    album: 'Hot',
    files: [
      { path: 'D:/Music/Divers/song.mp3', bitrate: 128, fileSize: 3e6 },
      { path: 'D:/Music/Inna/song.mp3', bitrate: 320, fileSize: 8e6 },
    ],
  },
];

describe('openTrackDedupDialog', () => {
  it('supprime les fichiers cochés et rend leurs chemins', async () => {
    const deleted = { deleted: ['D:/Music/Divers/song.mp3'], failed: [], errors: [] };
    const server = mockApi({ DeleteOrganizeFiles: { deleteOrganizeFiles: deleted } });
    const { user } = renderWithRouter(<div />);
    const result = open(() => openTrackDedupDialog(TRACKS));

    const boxes = await screen.findAllByRole('checkbox');
    expect(boxes.map(b => b.checked)).toEqual([true, false]);
    expect(screen.getByText('Song')).toBeVisible();

    await user.click(screen.getByRole('button', { name: 'Supprimer les 1 fichier(s) cochés' }));
    const question = (await screen.findAllByRole('dialog')).at(-1);
    expect(question).toHaveTextContent('Supprimer 1 fichier(s) du disque ?');
    await user.click(within(question).getByRole('button', { name: 'Supprimer' }));

    await expect(result).resolves.toEqual(['D:/Music/Divers/song.mp3']);
    expect(server.mutations('DeleteOrganizeFiles')).toEqual([{ paths: ['D:/Music/Divers/song.mp3'] }]);
    expect(await screen.findByText('1 fichier(s) supprimé(s).')).toBeVisible();
  });

  it('rend undefined quand on ferme sans supprimer', async () => {
    mockApi();
    const { user } = renderWithRouter(<div />);
    const result = open(() => openTrackDedupDialog(TRACKS));
    await user.click(await screen.findByRole('button', { name: 'Tout décocher' }));
    expect(screen.getByRole('button', { name: 'Supprimer les fichiers cochés' })).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Fermer' }));
    await expect(result).resolves.toBeUndefined();
  });
});

const FOUND = {
  fileCount: 2,
  targetFormat: 'MP3',
  groups: [
    {
      number: 1,
      files: [
        { path: 'D:\\m\\a.mp3', dbPath: 'm/a.mp3', title: 'A', artist: 'X', format: 'MP3', bitrate: 320, keep: true },
        { path: 'D:\\m\\b.mp3', dbPath: 'm/b.mp3', title: 'A', artist: 'X', format: 'MP3', bitrate: 128, keep: false },
      ],
    },
  ],
};

describe('openFileDedupDialog', () => {
  it('supprime les copies cochées de la session et rend leurs chemins', async () => {
    const deleted = { deleted: ['m/b.mp3'], failed: [], errors: [] };
    const server = mockApi({ DeleteOrganizeFiles: { deleteOrganizeFiles: deleted } });
    const { user } = renderWithRouter(<div />);
    const result = open(() => openFileDedupDialog({ result: FOUND, sessionId: 's1' }));

    const boxes = await screen.findAllByRole('checkbox');
    expect(boxes.map(b => b.checked)).toEqual([false, true]);
    expect(screen.getByText('MP3 · 320 kbps')).toBeVisible();

    await user.click(screen.getByRole('button', { name: 'Supprimer les fichiers cochés' }));
    const question = (await screen.findAllByRole('dialog')).at(-1);
    await user.click(within(question).getByRole('button', { name: 'Supprimer' }));

    await expect(result).resolves.toEqual(['m/b.mp3']);
    expect(server.mutations('DeleteOrganizeFiles')).toEqual([{ paths: ['m/b.mp3'], sessionId: 's1' }]);
  });

  it('ne supprime rien si la question est refusée, et rend undefined à l’annulation', async () => {
    const server = mockApi();
    const { user } = renderWithRouter(<div />);
    const result = open(() => openFileDedupDialog({ result: FOUND, sessionId: 's1' }));

    await user.click(await screen.findByRole('button', { name: 'Supprimer les fichiers cochés' }));
    const question = (await screen.findAllByRole('dialog')).at(-1);
    await user.click(within(question).getByRole('button', { name: 'Non' }));
    await waitFor(() => expect(screen.getAllByRole('dialog')).toHaveLength(1));
    expect(server.mutations('DeleteOrganizeFiles')).toEqual([]);

    await user.click(screen.getByRole('button', { name: 'Annuler' }));
    await expect(result).resolves.toBeUndefined();
  });
});
