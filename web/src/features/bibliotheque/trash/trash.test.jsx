import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderWithRouter } from '../../../test/render';
import { mockApi } from '../../../test/mock-api';
import { deleteMessage, deleteTracks } from './delete-actions';
import { TrashDialog } from './TrashDialog';
import { daysLeft } from './trash-model';

const NOW = Date.UTC(2026, 10, 21, 12);
const entry = (id, fields = {}) => ({
  id,
  dbPath: `D:\\Abba\\${id}.mp3`,
  name: `${id}.mp3`,
  folder: 'Abba',
  deletedAt: NOW / 1000 - 86400,
  expiresAt: NOW / 1000 + 29 * 86400,
  size: 4_000_000,
  ...fields,
});

describe('daysLeft', () => {
  it('compte les jours restants avant effacement', () => {
    expect(daysLeft(NOW / 1000 + 29 * 86400, NOW)).toBe('29 jours');
    expect(daysLeft(NOW / 1000 + 3600, NOW)).toBe('1 jour');
    expect(daysLeft(NOW / 1000 - 10, NOW)).toBe('effacé sous peu');
    expect(daysLeft(null, NOW)).toBe('conservé');
  });
});

describe('deleteTracks', () => {
  it('nomme un titre seul et prévient de la corbeille et du disque USB', () => {
    expect(deleteMessage([{ title: 'Waterloo' }])).toMatch(/« Waterloo ».*disque USB.*Corbeille/s);
    expect(deleteMessage([{}, {}])).toMatch(/ces 2 titres/);
  });

  it('après confirmation, met à la corbeille les seuls titres présents sur le disque', async () => {
    const server = mockApi({ DeleteTracks: { deleteFiles: { deleted: ['/a'], failed: [], errors: [] } } });
    const { user } = renderWithRouter(<div />);
    const done = deleteTracks([
      { path: '/a', title: 'A', onDisk: true },
      { path: '/b', title: 'B', onDisk: false },
    ]);
    await user.click(await screen.findByRole('button', { name: 'Mettre à la corbeille' }));
    expect(await done).toBe(true);
    expect(server.mutations('DeleteTracks')).toEqual([{ paths: ['/a'] }]);
  });

  it("n'envoie rien si l'utilisateur renonce", async () => {
    const server = mockApi();
    const { user } = renderWithRouter(<div />);
    const done = deleteTracks([{ path: '/a', title: 'A', onDisk: true }]);
    await user.click(await screen.findByRole('button', { name: 'Annuler' }));
    expect(await done).toBe(false);
    expect(server.mutations('DeleteTracks')).toEqual([]);
  });
});

describe('TrashDialog', () => {
  it('liste la corbeille et restaure les fichiers cochés', async () => {
    const server = mockApi({
      Trash: { trash: [entry('Waterloo'), entry('SOS')] },
      TrashRestore: { restore: { done: 1, errors: [] } },
    });
    const { user } = renderWithRouter(<TrashDialog close={() => undefined} />);
    expect(await screen.findByText('Waterloo.mp3')).toBeInTheDocument();
    expect(screen.getByText('2 fichier(s), 8 Mo')).toBeInTheDocument();
    await user.click(screen.getByRole('checkbox', { name: 'SOS.mp3' }));
    await user.click(screen.getByRole('button', { name: 'Restaurer (1)' }));
    await waitFor(() => expect(server.mutations('TrashRestore')).toEqual([{ ids: ['SOS'] }]));
  });

  it('dit quand la corbeille est vide', async () => {
    mockApi({ Trash: { trash: [] } });
    renderWithRouter(<TrashDialog close={() => undefined} />);
    expect(await screen.findByText('La corbeille est vide.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Vider la corbeille' })).toBeDisabled();
  });
});
