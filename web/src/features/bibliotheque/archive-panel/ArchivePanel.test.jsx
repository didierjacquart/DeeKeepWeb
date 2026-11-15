import { act, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { makeJob, renderWithRouter } from '../../../test/render';
import { mockApi } from '../../../test/mock-api';
import { jobs } from '../../../core/jobs/jobs';
import { archiveActions } from './archive-actions';
import { ArchivePanel } from './ArchivePanel';

const CONFIG = { root: '/srv/archive.db', prefix: null, archiveFile: '/srv/archive.db', configured: true };
const USAGE = {
  objectCount: 4,
  storedBytes: 2_000_000,
  archiveFileBytes: 2_000_000,
  diskFreeBytes: 8_000_000,
  diskTotalBytes: 20_000_000,
  tracksArchived: 4,
  tracksStale: 1,
  tracksNotArchived: 5,
  bytesToArchive: 2_000_000,
  tracksOnDisk: 10,
  bytesOnDisk: 5_000_000,
  sameVolume: false,
  configured: true,
  archiveFile: '/srv/archive.db',
  error: null,
};
const PLAN = { requested: 2, onDisk: 2, restorable: 1, restorableElsewhere: 0, configured: true };
const copyJob = fields => makeJob({ id: 'copy-1', service: 'storage', type: 'archive.upload', ...fields });

function setup({ selected = [], visible = ['/m/a.flac', '/m/b.flac'], config = CONFIG } = {}) {
  const server = mockApi({
    ArchiveConfig: { archiveConfig: config },
    ArchiveUsage: { archiveUsage: USAGE },
    ArchivePlan: { archivePlan: PLAN },
    ArchiveTracks: { archiveTracks: copyJob() },
    RestoreTracks: { restoreTracks: copyJob({ type: 'archive.restore' }) },
    MeasureArchive: { measureArchive: makeJob({ id: 'measure-1', service: 'storage', type: 'archive.usage' }) },
  });
  const view = renderWithRouter(<ArchivePanel selected={selected} visible={visible} />);
  return { server, ...view };
}

/** Bouton principal, actif une fois la configuration lue. */
const archiveButton = async () => {
  const button = screen.getByRole('button', { name: 'Archiver' });
  await waitFor(() => expect(button).toBeEnabled());
  return button;
};

describe('ArchivePanel', () => {
  it('déplié, montre le fichier configuré et la jauge', async () => {
    const { user } = setup();
    await user.click(screen.getByRole('button', { name: '▸ Archive', expanded: false }));
    const field = screen.getByRole('textbox', { name: "Fichier d'archive" });
    await waitFor(() => expect(field).toHaveValue('/srv/archive.db'));
    expect(screen.getByText('Fichier : /srv/archive.db')).toBeInTheDocument();
    expect(await screen.findByText(/Tous reviendraient octet pour octet\.$/)).toBeInTheDocument();
  });

  it('sans titre coché, archive les titres affichés après confirmation', async () => {
    const { user, server } = setup();
    await user.click(await archiveButton());
    expect(await screen.findByText('Aucun titre coché. Envoyer les 2 titres affichés ?')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Oui' }));
    await waitFor(() => expect(server.mutations('ArchiveTracks')).toEqual([{ paths: ['/m/a.flac', '/m/b.flac'] }]));
    expect(jobs.get('copy-1')).not.toBeNull();
  });

  it('« Non » annule l’archivage', async () => {
    const { user, server } = setup();
    await user.click(await archiveButton());
    await user.click(await screen.findByRole('button', { name: 'Non' }));
    expect(server.mutations('ArchiveTracks')).toEqual([]);
  });

  it('archive directement les titres cochés et affichés', async () => {
    const { user, server } = setup({ selected: ['/m/b.flac', '/m/zz.flac'] });
    await user.click(await archiveButton());
    await waitFor(() => expect(server.mutations('ArchiveTracks')).toEqual([{ paths: ['/m/b.flac'] }]));
    expect(screen.queryByRole('button', { name: 'Oui' })).not.toBeInTheDocument();
  });

  it('restaure les titres manquants après confirmation', async () => {
    const { user, server } = setup();
    const restore = screen.getByRole('button', { name: 'Restaurer' });
    await waitFor(() => expect(restore).toBeEnabled());
    await user.click(restore);
    await user.click(await screen.findByRole('button', { name: 'Oui' }));
    await waitFor(() => expect(server.mutations('RestoreTracks')).toEqual([{ paths: ['/m/a.flac', '/m/b.flac'] }]));
  });

  it('sans archive configurée, les copies sont désactivées', async () => {
    const { server } = setup({ config: { ...CONFIG, configured: false } });
    await waitFor(() => expect(server.queries('ArchiveConfig')).toHaveLength(1));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Archiver' })).toBeDisabled());
    expect(screen.getByRole('button', { name: 'Restaurer' })).toBeDisabled();
  });

  it('une archive demandée sans configuration déplie le volet', async () => {
    setup({ config: { ...CONFIG, configured: false } });
    expect(screen.queryByRole('textbox', { name: "Fichier d'archive" })).not.toBeInTheDocument();
    await act(() => archiveActions.archive(['/m/a.flac']));
    expect(screen.getByRole('button', { name: '▾ Archive', expanded: true })).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: "Fichier d'archive" })).toBeInTheDocument();
  });

  it('suit la copie en cours puis affiche son bilan', async () => {
    setup();
    act(() => jobs.upsert(copyJob({ message: 'Envoi de 3 / 10', total: 10, done: 3, progress: 0.3 })));
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuetext', '3 / 10');
    expect(screen.getByText('Envoi de 3 / 10')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Arrêter' })).toBeEnabled();

    act(() => jobs.upsert(copyJob({ status: 'SUCCEEDED', message: '10 titres envoyés.', updatedAt: 2 })));
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
    expect(screen.getByText('10 titres envoyés.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Arrêter' })).toBeDisabled();
  });

  it('mesure l’occupation, puis affiche le bilan et relit la jauge', async () => {
    const { user, server } = setup();
    await user.click(screen.getByRole('button', { name: '▸ Archive', expanded: false }));
    await waitFor(() => expect(server.queries('ArchiveUsage')).toHaveLength(1));
    const measure = screen.getByRole('button', { name: "Mesurer l'occupation" });
    await user.click(measure);
    await waitFor(() => expect(measure).toBeDisabled());
    expect(screen.getByText("Mesure de l'archive…")).toBeInTheDocument();

    const done = makeJob({ id: 'measure-1', service: 'storage', type: 'archive.usage', status: 'SUCCEEDED' });
    act(() => jobs.upsert({ ...done, message: '4 objets mesurés.', updatedAt: 2 }));
    expect(screen.getByText('4 objets mesurés.')).toBeInTheDocument();
    expect(measure).toBeEnabled();
    await waitFor(() => expect(server.queries('ArchiveUsage')).toHaveLength(2));
  });

  it('enregistre le chemin saisi', async () => {
    const { user, server } = setup();
    server.respond('UpdateArchiveConfig', { updateArchiveConfig: { ...CONFIG, root: '/mnt/b.db' } });
    await user.click(screen.getByRole('button', { name: '▸ Archive', expanded: false }));
    const field = screen.getByRole('textbox', { name: "Fichier d'archive" });
    await waitFor(() => expect(field).toHaveValue('/srv/archive.db'));
    await user.clear(field);
    await user.type(field, '/mnt/b.db');
    await user.click(screen.getByRole('button', { name: 'Enregistrer' }));
    expect(await screen.findByText('Archive enregistrée.')).toBeInTheDocument();
    expect(server.mutations('UpdateArchiveConfig')).toEqual([{ root: '/mnt/b.db', prefix: null }]);
  });
});
