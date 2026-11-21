import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderWithRouter } from '../../test/render';
import { mockApi } from '../../test/mock-api';
import { notify } from '../../core/ui/notify';
import { TrackUsersDialog } from './components/TrackUsersDialog';
import { UserFilter } from './components/UserFilter';
import { titleTip } from './track-info';
import { deleteTracks } from './trash/delete-actions';

describe('bibliothèques personnelles', () => {
  it("montre à l'administrateur les utilisateurs d'un titre dans l'infobulle", () => {
    expect(titleTip({ title: 'Waterloo', users: ['Alice', 'Bob'] })).toBe('Waterloo\nUtilisateurs : Alice, Bob');
    expect(titleTip({ title: 'SOS', users: [] })).toBe('SOS\nUtilisateurs : aucun utilisateur');
    expect(titleTip({ title: 'SOS' })).toBe('SOS');
  });

  it('propose de filtrer la bibliothèque par utilisateur', async () => {
    mockApi({
      UserCounts: {
        userCounts: [
          { id: 'a', name: 'Alice', tracks: 12 },
          { id: 'b', name: 'Bob', tracks: 3 },
        ],
      },
    });
    let chosen;
    const { user } = renderWithRouter(<UserFilter value={null} onChange={id => (chosen = id)} />);
    await user.selectOptions(await screen.findByRole('combobox'), 'Bob (3)');
    expect(chosen).toBe('b');
  });

  it("retire un utilisateur d'un titre sans toucher au fichier", async () => {
    const server = mockApi({
      TrackUsers: { trackUsers: [{ id: 'a', name: 'Alice', addedAt: 1795271000, origin: 'download' }] },
      UnlinkUser: { unlink: 1 },
    });
    const { user } = renderWithRouter(
      <TrackUsersDialog track={{ path: '/m/a.mp3', title: 'A' }} close={() => undefined} />,
    );
    expect(await screen.findByText('Alice')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Retirer' }));
    // Confirmation : le bouton « Retirer » de la boîte de dialogue, ouverte par-dessus.
    await waitFor(() => expect(screen.getAllByRole('button', { name: 'Retirer' })).toHaveLength(2));
    await user.click(screen.getAllByRole('button', { name: 'Retirer' }).at(-1));
    await waitFor(() => expect(server.mutations('UnlinkUser')).toEqual([{ userId: 'a', paths: ['/m/a.mp3'] }]));
  });

  it("dit qu'un titre partagé est seulement retiré de la bibliothèque de l'utilisateur", async () => {
    mockApi({ DeleteTracks: { deleteFiles: { deleted: [], failed: [], errors: [], unlinked: ['/a'] } } });
    const { user } = renderWithRouter(<div />);
    const done = deleteTracks([{ path: '/a', title: 'A', onDisk: true }]);
    await user.click(await screen.findByRole('button', { name: 'Mettre à la corbeille' }));
    expect(await done).toBe(true);
    expect(notify.state.toast?.message).toMatch(/retiré\(s\) de votre bibliothèque/);
  });
});
