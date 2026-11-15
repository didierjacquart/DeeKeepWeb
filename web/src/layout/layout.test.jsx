import { act, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { renderWithRouter, makeJob } from '../test/render';
import { mockApi } from '../test/mock-api';
import { setMobile } from '../test/media';
import { jobs } from '../core/jobs/jobs';
import { connectivity } from '../core/offline/connectivity';
import { offlineQueue } from '../core/offline/offline-queue';
import { navBadges } from './nav-badges';
import { Shell } from './shell/Shell';
import { JobsPanel } from './jobs-panel/JobsPanel';
import { isToolPage, pageTitle } from './shell/nav-items';

/** La coquille seule, sur toutes les adresses : la navigation change l'URL sans charger de page. */
function renderShellWithPages(route) {
  mockApi({ Jobs: { jobs: [] } });
  return renderWithRouter(<Shell />, { route, path: '/*' });
}

describe('nav-items', () => {
  it('donne le titre de la page et reconnaît les outils', () => {
    expect(pageTitle('/qualite')).toBe('Qualité');
    expect(pageTitle('/inconnue')).toBe('DeeKeep');
    expect(isToolPage('/organiser')).toBe(true);
    expect(isToolPage('/musique')).toBe(false);
  });
});

describe('Shell', () => {
  it('affiche la navigation et marque la page courante', () => {
    renderShellWithPages('/qualite');
    const nav = screen.getByRole('navigation', { name: 'Navigation principale' });
    expect(within(nav).getByRole('link', { name: /Qualité/ })).toHaveClass('active');
    expect(within(nav).getByRole('link', { name: /Musique/ })).not.toHaveClass('active');
  });

  it('replie et déplie le groupe « Outils »', async () => {
    const { user } = renderShellWithPages('/musique');
    const toggle = screen.getByRole('button', { name: /Outils/ });
    await user.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('link', { name: /Réencoder/ })).not.toBeInTheDocument();
    await user.click(toggle);
    expect(screen.getByRole('link', { name: /Réencoder/ })).toBeInTheDocument();
  });

  it('montre le compteur de l’historique et l’état de la liaison', () => {
    renderShellWithPages('/musique');
    act(() => navBadges.setHistory(386));
    act(() => connectivity.connected());
    expect(screen.getByRole('link', { name: /Historique/ })).toHaveTextContent('386');
    expect(screen.getByRole('button', { name: /Serveur connecté/ })).toBeInTheDocument();
  });

  it('signale les tâches en cours et ouvre leur panneau', async () => {
    const { user } = renderShellWithPages('/musique');
    act(() => jobs.upsert(makeJob({ id: 'x', status: 'RUNNING', type: 'download' })));
    await user.click(screen.getByRole('button', { name: '1 tâche(s) en cours' }));
    expect(screen.getByRole('complementary', { name: 'Tâches de fond' })).toBeInTheDocument();
  });

  it('sur mobile, passe par un tiroir et un en-tête titré', async () => {
    setMobile(true);
    const { user } = renderShellWithPages('/organiser');
    expect(screen.getByRole('banner')).toHaveTextContent('Organiser');
    const sidebar = document.querySelector('.sidebar');
    expect(sidebar).not.toHaveClass('open');
    await user.click(screen.getByRole('button', { name: 'Menu' }));
    expect(sidebar).toHaveClass('open');
    await user.keyboard('{Escape}');
    expect(sidebar).not.toHaveClass('open');
  });

  it('referme le tiroir mobile en changeant de page', async () => {
    setMobile(true);
    const { user, router } = renderShellWithPages('/musique');
    await user.click(screen.getByRole('button', { name: 'Menu' }));
    await user.click(screen.getByRole('link', { name: /Paramètres/ }));
    expect(router.state.location.pathname).toBe('/parametres');
    expect(document.querySelector('.sidebar')).not.toHaveClass('open');
  });

  it('remonte en haut de page à chaque navigation, même quand scrollTo rend une promesse', async () => {
    // Navigateurs récents : les méthodes de défilement rendent une promesse.
    window.scrollTo.mockImplementation(() => Promise.resolve());
    const { user, unmount } = renderShellWithPages('/musique');
    await user.click(screen.getByRole('link', { name: /Paramètres/ }));
    expect(window.scrollTo).toHaveBeenLastCalledWith(0, 0);
    expect(() => unmount()).not.toThrow();
    window.scrollTo.mockReset();
  });

  it('démarre le suivi des tâches et le rejeu hors ligne dès l’ouverture', () => {
    const startJobs = vi.spyOn(jobs, 'start').mockImplementation(() => undefined);
    const startQueue = vi.spyOn(offlineQueue, 'start').mockImplementation(() => undefined);
    renderShellWithPages('/musique');
    expect(startJobs).toHaveBeenCalled();
    expect(startQueue).toHaveBeenCalled();
  });
});

describe('JobsPanel', () => {
  it('liste les tâches actives, puis toutes avec « Terminées »', async () => {
    const server = mockApi({ CancelJob: {}, RetryJob: {} });
    act(() => {
      jobs.upsert(
        makeJob({
          id: 'a',
          type: 'download',
          status: 'RUNNING',
          title: 'Daft Punk',
          total: 4,
          done: 1,
          progress: 0.25,
        }),
      );
      jobs.upsert(makeJob({ id: 'b', type: 'quality.scan', status: 'FAILED', error: 'ffmpeg absent' }));
    });
    const { user } = renderWithRouter(<JobsPanel onClose={() => undefined} />);
    expect(screen.getByText('Extraction')).toBeInTheDocument();
    expect(screen.getByRole('progressbar')).toHaveTextContent('1 / 4');
    expect(screen.queryByText('ffmpeg absent')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Terminées' }));
    expect(screen.getByText('ffmpeg absent')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Relancer' }));
    await user.click(screen.getByRole('button', { name: 'Arrêter' }));
    expect(server.mutations('RetryJob')).toEqual([{ service: 'library', id: 'b' }]);
    expect(server.mutations('CancelJob')).toEqual([{ service: 'library', id: 'a' }]);
  });

  it('annonce l’absence de tâche', () => {
    renderWithRouter(<JobsPanel onClose={() => undefined} />);
    expect(screen.getByText('Aucune tâche en cours.')).toBeInTheDocument();
  });
});
