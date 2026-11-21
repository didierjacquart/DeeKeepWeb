import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderWithRouter } from '../../test/render';
import { mockApi } from '../../test/mock-api';
import { TrashSettingsCard } from './components/TrashSettingsCard';
import { parseRetention, retentionHelp } from './trash-retention';

describe('parseRetention', () => {
  it('accepte un nombre entier de jours, 0 compris', () => {
    expect(parseRetention('30')).toBe(30);
    expect(parseRetention(' 0 ')).toBe(0);
    expect(parseRetention('3650')).toBe(3650);
  });

  it('refuse le reste', () => {
    expect(parseRetention('')).toBeNull();
    expect(parseRetention('-1')).toBeNull();
    expect(parseRetention('2.5')).toBeNull();
    expect(parseRetention('4000')).toBeNull();
  });

  it('explique que 0 désactive l’effacement automatique', () => {
    expect(retentionHelp(0)).toMatch(/désactivé/);
    expect(retentionHelp(15)).toMatch(/15 jour\(s\)/);
  });
});

describe('TrashSettingsCard', () => {
  it('affiche la durée enregistrée et enregistre 0 pour désactiver la purge', async () => {
    const server = mockApi({
      TrashSettings: { trashSettings: { retentionDays: 30 } },
      SaveTrashSettings: v => ({ trashSettings: v }),
    });
    const { user } = renderWithRouter(<TrashSettingsCard />);
    const input = await screen.findByLabelText('Conserver les titres supprimés :');
    await waitFor(() => expect(input).toHaveValue(30));
    await user.clear(input);
    await user.type(input, '0{Enter}');
    await waitFor(() => expect(server.mutations('SaveTrashSettings')).toEqual([{ retentionDays: 0 }]));
    expect(await screen.findByText(/effacement automatique désactivé/i)).toBeInTheDocument();
  });
});
