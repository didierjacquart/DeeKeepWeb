import { afterEach, describe, expect, it, vi } from 'vitest';
import { screen, within } from '@testing-library/react';
import { makeJob, renderWithRouter } from '../../test/render';
import { mockApi } from '../../test/mock-api';
import { auth } from '../../core/auth/auth';
import { openFsBrowser } from '../../shared/fs-browser/open-fs-browser';
import { SettingsPage } from './SettingsPage';

vi.mock('../../shared/fs-browser/open-fs-browser', () => ({ openFsBrowser: vi.fn() }));

const SETTINGS = {
  file_format: 'M4A',
  quality: '128 kbps (Standard)',
  youtube_browser: 'Firefox',
  cookies_file: '/srv/cookies.txt',
  org_artist: true,
  org_album: false,
  org_divers: true,
  org_divers_min: 5,
  download_dir: '/music',
};

const DB = {
  path: '/data/deekeep.db',
  sizeBytes: 2_500_000,
  tables: [
    { table: 'tracks', rows: 120, deleted: 3 },
    { table: 'history', rows: 40, deleted: 0 },
  ],
  historyCount: 40,
  coverBytes: 1_000_000,
  downloadDir: '/music',
  localDownloadDir: '/srv/music',
  presenceReady: true,
  presenceFiles: 118,
  desktopDataDir: '/srv/desktop',
};

function setup(responses = {}) {
  const api = mockApi({
    Settings: { settings: SETTINGS, databaseInfo: DB },
    UpdateSettings: vars => ({ updateSettings: { ...SETTINGS, ...vars.values } }),
    ...responses,
  });
  return { api, ...renderWithRouter(<SettingsPage />) };
}

function signIn() {
  auth.setState({ session: { authEnabled: true, authenticated: true, user: 'didier', needsSetup: false } });
}

afterEach(() => {
  auth.setState({ session: null });
  vi.unstubAllGlobals();
});

describe('SettingsPage', () => {
  it('affiche les réglages et la base du serveur', async () => {
    setup();

    expect(await screen.findByText('Cookies YouTube : /srv/cookies.txt')).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: /Format de fichier/ })).toHaveValue('M4A');
    expect(screen.getByRole('combobox', { name: /Qualité d'enregistrement/ })).toHaveValue('128 kbps (Standard)');
    expect(screen.getByRole('combobox', { name: /Compte YouTube/ })).toHaveValue('Firefox');
    expect(screen.getByRole('checkbox', { name: 'Créer un sous-dossier par Artiste' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Créer un sous-dossier par Album' })).not.toBeChecked();
    expect(screen.getByRole('combobox', { name: 'Seuil de titres' })).toHaveValue('5');
    expect(screen.getByRole('combobox', { name: 'Seuil de titres' })).toBeEnabled();
    expect(screen.getByText('(sur le serveur : /srv/music)', { exact: false })).toBeInTheDocument();
    expect(screen.getByText('/data/deekeep.db')).toBeInTheDocument();
    expect(screen.getByText('tracks : 120 (+3 supprimées)')).toBeInTheDocument();
    expect(screen.getByText("L'authentification est désactivée sur ce serveur.")).toBeInTheDocument();
  });

  it('changer de format reconstruit les qualités et enregistre format et qualité', async () => {
    const { api, user } = setup();
    const format = await screen.findByRole('combobox', { name: /Format de fichier/ });
    await screen.findByText('Cookies YouTube : /srv/cookies.txt');

    await user.selectOptions(format, 'FLAC');

    const quality = screen.getByRole('combobox', { name: /Qualité d'enregistrement/ });
    expect(
      within(quality)
        .getAllByRole('option')
        .map(o => o.textContent),
    ).toEqual(['Lossless (Qualité Source)']);
    expect(quality).toHaveValue('Lossless (Qualité Source)');
    expect(api.mutations('UpdateSettings')).toEqual([
      { values: { file_format: 'FLAC', quality: 'Lossless (Qualité Source)' } },
    ]);
  });

  it('retirer le fichier de cookies enregistre un chemin vide', async () => {
    const { api, user } = setup();
    await screen.findByText('Cookies YouTube : /srv/cookies.txt');

    await user.click(screen.getByRole('button', { name: 'Retirer' }));

    expect(api.mutations('UpdateSettings')).toEqual([{ values: { cookies_file: '' } }]);
    expect(await screen.findByText('Cookies YouTube : aucun fichier')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retirer' })).toBeDisabled();
  });

  it('le dossier choisi sur le serveur est enregistré', async () => {
    vi.mocked(openFsBrowser).mockResolvedValueOnce(['/srv/new-music']);
    const { api, user } = setup();
    await screen.findByText('/data/deekeep.db');

    await user.click(screen.getByRole('button', { name: 'Modifier le dossier' }));

    expect(openFsBrowser).toHaveBeenCalledWith({ start: '/srv/music', mode: 'folder', title: "Dossier d'exportation" });
    expect(await screen.findByText('Musique enregistrée dans : /srv/new-music')).toBeInTheDocument();
    expect(api.mutations('UpdateSettings')).toEqual([{ values: { download_dir: '/srv/new-music' } }]);
  });

  it("n'enregistre rien si le choix du dossier est annulé", async () => {
    vi.mocked(openFsBrowser).mockResolvedValueOnce(undefined);
    const { api, user } = setup();
    await screen.findByText('/data/deekeep.db');

    await user.click(screen.getByRole('button', { name: 'Modifier le dossier' }));

    expect(openFsBrowser).toHaveBeenCalled();
    expect(api.mutations('UpdateSettings')).toEqual([]);
  });

  it('relire la bibliothèque lance la tâche et en montre la progression', async () => {
    const job = makeJob({ id: 'scan-1', type: 'library.scan', message: 'Lecture des fichiers…' });
    const { api, user } = setup({ RescanLibrary: { rescanLibrary: job } });
    await screen.findByText('/data/deekeep.db');

    await user.click(screen.getByRole('button', { name: 'Relire la bibliothèque' }));

    expect(api.mutations('RescanLibrary')).toHaveLength(1);
    expect(await screen.findByRole('progressbar')).toHaveAttribute('aria-valuetext', 'Lecture des fichiers…');
    expect(screen.getByRole('button', { name: 'Arrêter' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Relire la bibliothèque' })).toBeDisabled();
  });

  it('valide puis change le mot de passe', async () => {
    signIn();
    const changePassword = vi.spyOn(auth, 'changePassword').mockResolvedValue(null);
    const { user } = setup();

    expect(screen.getByText('didier')).toBeInTheDocument();
    const input = screen.getByLabelText('Nouveau mot de passe');
    const button = screen.getByRole('button', { name: 'Changer le mot de passe' });

    await user.type(input, 'abc');
    expect(button).toBeDisabled();
    await user.type(input, 'def');
    expect(button).toBeEnabled();

    await user.click(button);

    expect(changePassword).toHaveBeenCalledWith('abcdef');
    expect(await screen.findByText('Mot de passe modifié.')).toBeInTheDocument();
    expect(input).toHaveValue('');
  });

  it("montre l'erreur du serveur et garde la saisie", async () => {
    signIn();
    vi.spyOn(auth, 'changePassword').mockResolvedValue('Serveur injoignable.');
    const { user } = setup();
    const input = screen.getByLabelText('Nouveau mot de passe');

    await user.type(input, 'secret1');
    await user.click(screen.getByRole('button', { name: 'Changer le mot de passe' }));

    expect(await screen.findByText('Serveur injoignable.')).toBeInTheDocument();
    expect(input).toHaveValue('secret1');
  });

  it('se déconnecte et revient à la page de connexion', async () => {
    signIn();
    const logout = vi.spyOn(auth, 'logout').mockResolvedValue(undefined);
    const assign = vi.fn();
    vi.stubGlobal('location', { ...window.location, assign });
    const { user } = setup();

    await user.click(screen.getByRole('button', { name: 'Se déconnecter' }));

    expect(logout).toHaveBeenCalled();
    expect(assign).toHaveBeenCalledWith('/login');
  });
});
