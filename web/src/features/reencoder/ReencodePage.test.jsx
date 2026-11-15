import { act, screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { makeJob, renderWithRouter } from '../../test/render';
import { mockApi } from '../../test/mock-api';
import { jobs } from '../../core/jobs/jobs';
import { openFsBrowser } from '../../shared/fs-browser/open-fs-browser';
import { reencodeStore } from './reencode-store';
import { ReencodePage } from './ReencodePage';

vi.mock('../../shared/fs-browser/open-fs-browser', () => ({ openFsBrowser: vi.fn() }));

const item = (n, fields = {}) => ({
  path: `D:/m/${n}.mp3`,
  dbPath: `m/${n}.mp3`,
  title: `Titre ${n}`,
  artist: 'X',
  album: '',
  bitrate: 128,
  format: 'MP3',
  matchState: 'none',
  match: null,
  selected: false,
  jobId: null,
  converted: false,
  ...fields,
});

const EXPORT_OPTIONS = { exportOptions: { current: { format: 'MP3', quality: '320 kbps (HQ)' } } };

function renderPage(responses = {}) {
  const server = mockApi({ ExportOptions: EXPORT_OPTIONS, ...responses });
  return { server, ...renderWithRouter(<ReencodePage />) };
}

const rowNames = () => screen.getAllByRole('checkbox').map(c => c.getAttribute('aria-label'));

beforeEach(() => {
  reencodeStore.setState({ items: [], scanJobIds: [], batchIds: [], matching: 0 });
});

describe('ReencodePage', () => {
  it('affiche la liste conservée et la cible des Paramètres', async () => {
    reencodeStore.updateItems(() => [item(1), item(2, { format: 'MP3', bitrate: 320 })]);
    renderPage({ ExportOptions: { exportOptions: { current: { format: 'FLAC', quality: 'Sans perte' } } } });
    expect(await screen.findByText(/cible : FLAC · Sans perte/)).toBeInTheDocument();
    expect(screen.getByText(/2 fichier\(s\) affiché\(s\) sur 2/)).toBeInTheDocument();
    expect(rowNames()).toEqual(['Cocher Titre 1', 'Cocher Titre 2']);
    expect(screen.getAllByText('✗ Introuvable')).toHaveLength(2);
  });

  it('invite à ajouter des fichiers quand la liste est vide', () => {
    renderPage();
    expect(screen.getByText(/^Aucun fichier\. Ajoutez des fichiers/)).toBeInTheDocument();
  });

  it('ajoute des fichiers choisis sur le serveur et suit l’analyse', async () => {
    openFsBrowser.mockResolvedValue(['D:/m/a.mp3', 'D:/m/b.mp3']);
    const { server, user } = renderPage({ ScanLocalFiles: { scanLocalFiles: { id: 'scan-p1' } } });

    await user.click(screen.getByRole('button', { name: 'Ajouter des fichiers' }));
    expect(openFsBrowser).toHaveBeenCalledWith({ mode: 'files', audioOnly: true });
    expect(server.mutations('ScanLocalFiles')).toEqual([{ paths: ['D:/m/a.mp3', 'D:/m/b.mp3'] }]);

    act(() => jobs.upsert(makeJob({ id: 'scan-p1', type: 'files.scan', message: '1 / 2 fichier(s) analysé(s)' })));
    expect(screen.getByText('1 / 2 fichier(s) analysé(s)')).toBeInTheDocument();
    expect(screen.getByRole('progressbar')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Ajouter des fichiers' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Ajouter un dossier' })).toBeDisabled();

    act(() => jobs.upsert(makeJob({ id: 'scan-p1', type: 'files.scan', status: 'SUCCEEDED', updatedAt: 2 })));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText('Aucun fichier audio trouvé.')).toBeInTheDocument();
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
  });

  it('ajoute un dossier entier', async () => {
    openFsBrowser.mockResolvedValue(undefined);
    const { server, user } = renderPage();
    await user.click(screen.getByRole('button', { name: 'Ajouter un dossier' }));
    expect(openFsBrowser).toHaveBeenCalledWith({ mode: 'folder', audioOnly: true });
    expect(server.mutations('ScanLocalFiles')).toEqual([]);
  });

  it('convertit les lignes cochées et montre la progression', async () => {
    reencodeStore.updateItems(() => [item(1, { selected: true, match: { id: 42 } }), item(2), item(3)]);
    const { server, user } = renderPage({ EnqueueConversions: { enqueueConversions: [{ id: 'conv-p1' }] } });

    await user.click(screen.getByRole('checkbox', { name: 'Cocher Titre 3' }));
    await user.click(screen.getByRole('button', { name: 'Convertir' }));
    const [vars] = server.mutations('EnqueueConversions');
    expect(vars.items).toEqual([
      { localPath: 'D:/m/1.mp3', deezerId: 42 },
      { localPath: 'D:/m/3.mp3', deezerId: null },
    ]);

    act(() => jobs.upsert(makeJob({ id: 'conv-p1', type: 'convert', progress: 0.5, message: 'Extraction' })));
    expect(screen.getAllByText('Extraction — 50%').length).toBeGreaterThan(0);
    expect(screen.getByText('Progression globale de la conversion :')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Convertir' })).toBeDisabled();
  });

  it('demande une sélection avant de convertir', async () => {
    reencodeStore.updateItems(() => [item(1)]);
    const { server, user } = renderPage();
    await user.click(screen.getByRole('button', { name: 'Convertir' }));
    expect(await screen.findByText('Sélectionnez au moins un fichier à réencoder (visible).')).toBeInTheDocument();
    expect(server.mutations('EnqueueConversions')).toEqual([]);
  });

  it('coche, décoche et retire les lignes visibles', async () => {
    reencodeStore.updateItems(() => [item(1), item(2)]);
    const { user } = renderPage();
    await user.click(screen.getByRole('button', { name: 'Tout cocher' }));
    expect(screen.getAllByRole('checkbox').every(c => c.checked)).toBe(true);
    await user.click(screen.getByRole('button', { name: 'Tout décocher' }));
    expect(screen.getAllByRole('checkbox').some(c => c.checked)).toBe(false);
    await user.click(screen.getByRole('checkbox', { name: 'Cocher Titre 2' }));
    await user.click(screen.getByRole('button', { name: 'Retirer la sélection' }));
    expect(rowNames()).toEqual(['Cocher Titre 1']);
  });

  it('trie par colonne et masque les fichiers déjà au format cible', async () => {
    reencodeStore.updateItems(() => [item('b'), item('a'), item('c', { bitrate: 320 })]);
    const { user } = renderPage();
    await screen.findByText(/cible : MP3 · 320 kbps \(HQ\)/);

    await user.click(screen.getByRole('button', { name: 'Titre' }));
    expect(screen.getByRole('button', { name: 'Titre' })).toHaveTextContent('Titre ▲');
    expect(rowNames()).toEqual(['Cocher Titre a', 'Cocher Titre b', 'Cocher Titre c']);
    await user.click(screen.getByRole('button', { name: 'Titre' }));
    expect(rowNames()).toEqual(['Cocher Titre c', 'Cocher Titre b', 'Cocher Titre a']);

    await user.click(screen.getByRole('button', { name: 'Masquer ceux déjà au format cible' }));
    expect(rowNames()).toEqual(['Cocher Titre b', 'Cocher Titre a']);
    expect(screen.getByRole('button', { name: 'Afficher tous les fichiers' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('purge les fichiers convertis', async () => {
    reencodeStore.updateItems(() => [item(1, { converted: true }), item(2)]);
    const { user } = renderPage();
    await user.click(screen.getByRole('button', { name: 'Purger les convertis' }));
    expect(rowNames()).toEqual(['Cocher Titre 2']);
    expect(screen.getByText('1 fichier(s) converti(s) retiré(s) de la liste.')).toBeInTheDocument();
  });
});
