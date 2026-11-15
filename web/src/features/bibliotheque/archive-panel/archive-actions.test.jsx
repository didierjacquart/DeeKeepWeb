import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { makeJob, renderWithRouter } from '../../../test/render';
import { mockApi } from '../../../test/mock-api';
import { jobs } from '../../../core/jobs/jobs';
import { notify } from '../../../core/ui/notify';
import { ArchiveActions } from './archive-actions';
import { UNCONFIGURED_TEXT } from './archive-model';

const CONFIGURED = { archiveConfig: { root: '/a.db', prefix: 'p', archiveFile: '/a.db', configured: true } };
const plan = fields => ({
  archivePlan: { requested: 1, onDisk: 1, restorable: 0, restorableElsewhere: 0, configured: true, ...fields },
});
const message = () => notify.state.toast?.message;

describe('ArchiveActions', () => {
  it("n'interroge pas le serveur à la création", () => {
    const server = mockApi();
    const actions = new ArchiveActions();
    expect(server.query).not.toHaveBeenCalled();
    expect(actions.configured).toBe(false);
  });

  it('sans archive configurée, prévient et demande le volet', async () => {
    mockApi({ ArchiveConfig: { archiveConfig: { configured: false } } });
    const actions = new ArchiveActions();
    await actions.archive(['/m/a.flac']);
    expect(message()).toBe(UNCONFIGURED_TEXT);
    expect(actions.state.openPanelRequest).toBe(1);
  });

  it('archive les titres présents et suit la tâche', async () => {
    const server = mockApi({
      ArchiveConfig: CONFIGURED,
      ArchivePlan: plan({ onDisk: 2 }),
      ArchiveTracks: { archiveTracks: makeJob({ id: 'up', type: 'archive.upload' }) },
    });
    const actions = new ArchiveActions();
    await actions.archive(['/a', '/b']);
    expect(server.mutations('ArchiveTracks')).toEqual([{ paths: ['/a', '/b'] }]);
    expect(jobs.get('up')).not.toBeNull();
    expect(message()).toBe('Envoi de 2 titres en cours…');
    expect(actions.busy).toBe(true);
  });

  it("n'archive rien quand aucun titre n'est sur le disque", async () => {
    const server = mockApi({ ArchiveConfig: CONFIGURED, ArchivePlan: plan({ onDisk: 0 }) });
    await new ArchiveActions().archive(['/a']);
    expect(server.mutations('ArchiveTracks')).toEqual([]);
    expect(message()).toBe("Aucun des titres retenus n'est présent sur le disque : il n'y a rien à archiver.");
  });

  it('explique pourquoi rien n’est à restaurer', async () => {
    const server = mockApi({ ArchiveConfig: CONFIGURED, ArchivePlan: plan({ restorableElsewhere: 2 }) });
    await new ArchiveActions().restore(['/a']);
    expect(server.mutations('RestoreTracks')).toEqual([]);
    expect(message()).toMatch(/mais 2 le sont ailleurs/);
  });

  it('retire de l’archive après confirmation', async () => {
    const server = mockApi({ RemoveFromArchive: { removeFromArchive: 2 } });
    const { user } = renderWithRouter(<p>page</p>);
    const done = new ArchiveActions().remove(['/a', '/b']);
    expect(await screen.findByText(/^Retirer ces 2 titres de l'archive \?/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Retirer' }));
    await done;
    expect(server.mutations('RemoveFromArchive')).toEqual([{ paths: ['/a', '/b'] }]);
    await waitFor(() => expect(message()).toBe("2 titres retirés de l'archive."));
  });

  it('enregistre la configuration en gardant le préfixe', async () => {
    const server = mockApi({
      ArchiveConfig: CONFIGURED,
      UpdateArchiveConfig: { updateArchiveConfig: { ...CONFIGURED.archiveConfig, root: '/b.db' } },
    });
    const actions = new ArchiveActions();
    await actions.loadConfig();
    const saved = await actions.saveConfig('/b.db');
    expect(server.mutations('UpdateArchiveConfig')).toEqual([{ root: '/b.db', prefix: 'p' }]);
    expect(saved.root).toBe('/b.db');
    expect(actions.state.config.root).toBe('/b.db');
  });

  it('« Arrêter » annule toutes les tâches de l’archive en cours', async () => {
    const server = mockApi({ CancelJob: { cancelJob: null } });
    jobs.upsert(makeJob({ id: 'u', type: 'archive.upload' }));
    jobs.upsert(makeJob({ id: 'c', type: 'archive.check' }));
    jobs.upsert(makeJob({ id: 's', type: 'library.scan' }));
    await new ArchiveActions().stop();
    expect(server.mutations('CancelJob').map(v => v.id)).toEqual(['u', 'c']);
  });
});
