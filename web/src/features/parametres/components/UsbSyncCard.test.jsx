import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { makeJob, renderWithRouter } from '../../../test/render';
import { mockApi } from '../../../test/mock-api';
import { UsbSyncCard } from './UsbSyncCard';

const status = fields => ({
  status: {
    enabled: true,
    usbRoot: '/mnt/deekeep-usb',
    libraryRoot: '/mnt/deekeep-music',
    usbPresent: true,
    libraryPresent: true,
    usbFreeBytes: 800 * 2 ** 30,
    usbTotalBytes: 953 * 2 ** 30,
    pendingDeletions: 0,
    running: null,
    last: null,
    problem: null,
    ...fields,
  },
});

describe('UsbSyncCard', () => {
  it('montre le disque branché, la place libre et le dernier bilan', async () => {
    mockApi({
      UsbSyncStatus: status({
        last: { at: 1795271000, files: 5203, message: '3 copié(s) vers le disque.', failed: 0, bytes: 12_000_000 },
      }),
    });
    renderWithRouter(<UsbSyncCard />);
    expect(await screen.findByText('Disque branché')).toBeInTheDocument();
    expect(screen.getByText(/libres sur/)).toBeInTheDocument();
    expect(screen.getByText(/3 copié\(s\) vers le disque\./)).toBeInTheDocument();
  });

  it('lance une synchronisation à la demande', async () => {
    const server = mockApi({
      UsbSyncStatus: status(),
      UsbSyncRun: { run: makeJob({ id: 'sync-1', type: 'usbsync.run', status: 'QUEUED' }) },
    });
    const { user } = renderWithRouter(<UsbSyncCard />);
    await user.click(await screen.findByRole('button', { name: 'Synchroniser maintenant' }));
    await waitFor(() => expect(server.mutations('UsbSyncRun')).toEqual([{}]));
  });

  it("désactive la synchronisation quand le disque n'est pas branché", async () => {
    mockApi({ UsbSyncStatus: status({ usbPresent: false, problem: 'Disque USB absent.' }) });
    renderWithRouter(<UsbSyncCard />);
    expect(await screen.findByText('Disque absent')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Synchroniser maintenant' })).toBeDisabled();
    expect(screen.getByText('Disque USB absent.')).toBeInTheDocument();
  });
});
