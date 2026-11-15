import { afterEach, describe, expect, it, vi } from 'vitest';
import { act, screen } from '@testing-library/react';
import { makeJob, renderWithRouter } from '../../test/render';
import { mockApi } from '../../test/mock-api';
import { DiagnosticsPage } from './DiagnosticsPage';

function info(service, tools = []) {
  return {
    service,
    version: '1.4.0',
    startedAt: Math.floor(Date.now() / 1000) - (3 * 3600 + 12 * 60),
    workingSetBytes: 150_000_000,
    managedHeapBytes: 40_000_000,
    activeJobs: 0,
    outboxPending: 0,
    links: [],
    tools,
  };
}

function service(name, fields = {}) {
  return { service: name, reachable: true, error: null, eventsConnected: true, info: info(name), ...fields };
}

const SERVICES = [
  service('catalog'),
  service('library', {
    info: { ...info('library'), links: [{ producer: 'downloader', connected: true, since: null, lastError: null }] },
  }),
  service('downloader', {
    info: info('downloader', [
      { name: 'yt-dlp', available: true, version: '2025.10.1', path: '/usr/bin/yt-dlp', detail: null },
      { name: 'ffmpeg', available: false, version: null, path: null, detail: null },
    ]),
  }),
  service('quality'),
  service('storage'),
];

function setup(services = SERVICES, responses = {}) {
  const api = mockApi({ Services: { services, settings: { download_dir: '/music' } }, ...responses });
  return { api, ...renderWithRouter(<DiagnosticsPage />) };
}

afterEach(() => {
  vi.useRealTimers();
});

describe('DiagnosticsPage', () => {
  it('affiche les services, les outils et le dossier du serveur', async () => {
    setup();

    expect(await screen.findByText('5 / 5 services joignables')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Catalogue (Deezer, MusicBrainz, AcoustID)' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Archive' })).toBeInTheDocument();
    expect(screen.getAllByText('1.4.0 · démarré il y a 3 h 12 min')).toHaveLength(5);
    expect(screen.getByText('événements ← downloader : connecté')).toBeInTheDocument();
    expect(screen.getByText('yt-dlp')).toBeInTheDocument();
    expect(screen.getByText('2025.10.1')).toBeInTheDocument();
    expect(screen.getByText('Non détecté')).toBeInTheDocument();
    expect(screen.getByText('/music')).toBeInTheDocument();
  });

  it('signale un service injoignable', async () => {
    setup([...SERVICES.slice(0, 4), service('storage', { reachable: false, info: null, error: 'Connexion refusée' })]);

    expect(await screen.findByText('4 / 5 services joignables')).toBeInTheDocument();
    expect(screen.getByText('Injoignable')).toBeInTheDocument();
    expect(screen.getByText('Connexion refusée')).toBeInTheDocument();
  });

  it("montre l'erreur quand le serveur ne répond pas", async () => {
    setup(SERVICES, { Services: new Error('Serveur injoignable') });

    expect(await screen.findByText('Serveur injoignable')).toBeInTheDocument();
    expect(screen.getByText('Aucun service.')).toBeInTheDocument();
  });

  it('se rafraîchit toutes les 5 s tant que la page est affichée', async () => {
    vi.useFakeTimers();
    const { api } = setup();
    await act(() => vi.advanceTimersByTimeAsync(0));
    expect(screen.getByText('5 / 5 services joignables')).toBeInTheDocument();

    api.respond('Services', {
      services: [...SERVICES.slice(0, 4), service('storage', { reachable: false, info: null, error: 'Coupé' })],
      settings: { download_dir: '/music' },
    });
    await act(() => vi.advanceTimersByTimeAsync(4999));
    expect(api.queries('Services')).toHaveLength(0);

    await act(() => vi.advanceTimersByTimeAsync(1));
    expect(api.queries('Services')).toHaveLength(1);
    expect(screen.getByText('4 / 5 services joignables')).toBeInTheDocument();

    await act(() => vi.advanceTimersByTimeAsync(5000));
    expect(api.queries('Services')).toHaveLength(2);
  });

  it('« Actualiser » recharge les requêtes affichées', async () => {
    const { api, user } = setup();
    await screen.findByText('5 / 5 services joignables');

    await user.click(screen.getByRole('button', { name: 'Actualiser' }));

    expect(api.refetchActive).toHaveBeenCalled();
  });

  it('« Mettre à jour yt-dlp » lance la tâche et la suit', async () => {
    const job = makeJob({ id: 'upd-1', service: 'downloader', type: 'tools.update', message: 'Téléchargement…' });
    const { api, user } = setup(SERVICES, { UpdateYtDlp: { updateYtDlp: job } });
    await screen.findByText('5 / 5 services joignables');

    await user.click(screen.getByRole('button', { name: 'Mettre à jour yt-dlp' }));

    expect(api.mutations('UpdateYtDlp')).toHaveLength(1);
    expect(await screen.findByRole('button', { name: 'Mise à jour…' })).toBeDisabled();
    expect(screen.getByText('yt-dlp : Téléchargement…')).toBeInTheDocument();
  });

  it("décrit l'état de cet appareil", async () => {
    setup();
    await screen.findByText('5 / 5 services joignables');

    expect(screen.getByText("0 depuis l'ouverture")).toBeInTheDocument();
    expect(screen.getByText("0 pour l'écoute hors ligne")).toBeInTheDocument();
  });
});
