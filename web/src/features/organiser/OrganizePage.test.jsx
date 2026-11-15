import { act, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { jobs } from '../../core/jobs/jobs';
import { openFsBrowser } from '../../shared/fs-browser/open-fs-browser';
import { mockApi } from '../../test/mock-api';
import { makeJob, renderWithRouter } from '../../test/render';
import { LOOKUP_KEY } from './lookup-storage';
import { DEFAULT_OPTIONS } from './organize-options';
import { OrganizePage } from './OrganizePage';

vi.mock('../../shared/fs-browser/open-fs-browser', () => ({ openFsBrowser: vi.fn() }));

const NAME_TAGS = {
  organizeNameTags: {
    defaultPattern: '%titre%',
    presets: ['%titre%'],
    categories: [{ name: 'Morceau', items: [{ label: 'Album', token: '%album%' }] }],
  },
};
const stateOf = (fields = {}) => ({
  organizeState: { session: null, defaultRoot: '/music', targetFormat: 'MP3', options: DEFAULT_OPTIONS, ...fields },
});
const entry = (index, folder, name) => ({
  index,
  path: `/new/${folder}/${name}`,
  dbPath: `${folder}/${name}`,
  name,
  folder,
  title: name,
  titleTagged: true,
  artist: 'Inna',
  album: 'Hot',
});
const ENTRIES = [entry(0, 'A', 'one.mp3'), entry(1, 'A', 'two.mp3')];
const PLAN = {
  organizePlan: {
    sessionId: 's1',
    totalRows: 2,
    statusText: '2 fichiers analysés',
    filtersSummary: 'Artiste',
    canApply: true,
    options: DEFAULT_OPTIONS,
    stats: {},
    rows: [
      { index: 0, status: 'move', action: '→ Inna/one.mp3', defaultChecked: true, needsLookup: false },
      { index: 1, status: 'inplace', action: 'déjà en place', defaultChecked: false, needsLookup: false },
    ],
    albumFolders: [],
    preview: { text: 'one - Inna', warning: false, unknownTokens: [] },
  },
};
const session = {
  id: 's1',
  root: '/new',
  rootDb: '/new',
  jobId: 'job-a',
  state: 'running',
  total: 2,
  read: 0,
  message: null,
};
const analysisJob = makeJob({ id: 'job-a', type: 'organize.analyze', status: 'RUNNING' });

/** Serveur d'une session déjà analysée. */
const analyzed = (extra = {}) =>
  mockApi({
    OrganizeState: stateOf({ session: { ...session, state: 'done' } }),
    OrganizeNameTags: NAME_TAGS,
    OrganizeEntries: { organizeEntries: { total: 2, items: ENTRIES } },
    OrganizePlan: PLAN,
    OrganizeChecks: { organizeChecks: { checked: [], unchecked: [] } },
    ...extra,
  });

describe('OrganizePage', () => {
  it('affiche l’état vide sans analyse', async () => {
    mockApi({ OrganizeState: stateOf(), OrganizeNameTags: NAME_TAGS });
    renderWithRouter(<OrganizePage />);
    expect(await screen.findByText('Aucune analyse : choisissez un dossier et lancez « Analyser ».')).toBeVisible();
    expect(screen.getByText('Dossier analysé : /music')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Organiser' })).toBeDisabled();
  });

  it('choisit un dossier, lance l’analyse et remplit l’arbre au fil des lots', async () => {
    const server = mockApi({
      OrganizeState: stateOf(),
      OrganizeNameTags: NAME_TAGS,
      StartOrganizeAnalysis: { startOrganizeAnalysis: { session, job: analysisJob } },
    });
    openFsBrowser.mockResolvedValueOnce(['/new']);
    const { user } = renderWithRouter(<OrganizePage />);
    await screen.findByText('Dossier analysé : /music');

    await user.click(screen.getByRole('button', { name: 'Modifier le dossier' }));
    expect(openFsBrowser).toHaveBeenCalledWith({
      mode: 'folder',
      start: '/music',
      title: 'Choisir le dossier à organiser',
    });
    expect(await screen.findByText('Dossier analysé : /new')).toBeVisible();
    expect(screen.getByRole('status')).toHaveTextContent("Dossier modifié : relancez l'analyse.");

    await user.click(screen.getByRole('button', { name: 'Analyser' }));
    expect(server.mutations('StartOrganizeAnalysis')).toEqual([{ root: '/new' }]);
    expect(await screen.findByRole('button', { name: "Arrêter l'analyse" })).toBeVisible();

    const item = { jobId: 'job-a', type: 'organize.analyze', itemKey: '0', status: 'ok', message: null, data: ENTRIES };
    act(() => server.emit('JobItem', { jobItem: item }));
    expect(await screen.findByText('one.mp3')).toBeVisible();
    expect(screen.getByText('two.mp3')).toBeVisible();

    // Fin de l'analyse : la liste finale fait foi, le plan se calcule.
    server.respond('OrganizeState', stateOf({ session: { ...session, state: 'done' } }));
    server.respond('OrganizeEntries', { organizeEntries: { total: 2, items: ENTRIES } });
    server.respond('OrganizePlan', PLAN);
    server.respond('OrganizeChecks', { organizeChecks: { checked: [], unchecked: [] } });
    act(() => jobs.upsert({ ...analysisJob, status: 'SUCCEEDED', updatedAt: 2 }));
    expect(await screen.findByText('→ Inna/one.mp3')).toBeVisible();
    expect(screen.getByRole('status')).toHaveTextContent('2 fichiers analysés');
    expect(screen.getByRole('checkbox', { name: 'Cocher one.mp3' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Cocher two.mp3' })).not.toBeChecked();
    expect(screen.getByRole('button', { name: 'Organiser' })).toBeEnabled();
  });

  it('range les fichiers cochés après confirmation, puis rend compte', async () => {
    const applyJob = makeJob({ id: 'job-o', type: 'organize.apply', status: 'RUNNING' });
    const server = analyzed({ ApplyOrganize: { applyOrganize: applyJob } });
    const { user } = renderWithRouter(<OrganizePage />);
    await screen.findByText('→ Inna/one.mp3');

    await user.click(screen.getByRole('button', { name: 'Organiser' }));
    const dialog = await screen.findByRole('dialog');
    expect(dialog).toHaveTextContent('Déplacer 1 fichier(s) dans :');
    await user.click(within(dialog).getByRole('button', { name: 'Organiser' }));
    await waitFor(() => expect(server.mutations('ApplyOrganize')).toHaveLength(1));
    expect(server.mutations('ApplyOrganize')[0]).toMatchObject({ sessionId: 's1', indices: [0] });
    expect(await screen.findByRole('button', { name: 'Arrêter' })).toBeVisible();

    act(() => jobs.upsert({ ...applyJob, status: 'SUCCEEDED', updatedAt: 2, result: { summary: '1 fichier rangé.' } }));
    expect(await screen.findByText('1 fichier rangé.')).toBeVisible();
    expect(screen.getByRole('status')).toHaveTextContent("Rangement terminé : relancez l'analyse pour vérifier.");
    expect(screen.queryByText('one.mp3')).not.toBeInTheDocument();
  });

  it('propose de reprendre une revue des tags interrompue', async () => {
    localStorage.setItem(LOOKUP_KEY, JSON.stringify({ sessionId: 's1', job: analysisJob, files: [] }));
    analyzed();
    renderWithRouter(<OrganizePage />);
    expect(await screen.findByRole('button', { name: 'Reprendre la revue des tags' })).toBeVisible();
  });

  it('enregistre les options et recalcule le plan', async () => {
    const server = mockApi({
      OrganizeState: stateOf({ options: { ...DEFAULT_OPTIONS, filtersOpen: true } }),
      OrganizeNameTags: NAME_TAGS,
    });
    const { user } = renderWithRouter(<OrganizePage />);
    const artist = await screen.findByRole('checkbox', { name: 'Sous-dossier par Artiste' });
    expect(artist).toBeChecked();

    await user.click(artist);
    expect(artist).not.toBeChecked();
    await waitFor(() => expect(server.mutations('SaveOrganizeOptions')).toHaveLength(1));
    expect(server.mutations('SaveOrganizeOptions')[0].options).toMatchObject({ artist: false, diversMin: 3 });

    await user.click(screen.getByRole('button', { name: 'Filtres ▴' }));
    expect(screen.getByText('Filtres : voir le volet')).toBeVisible();
    expect(server.mutations('SaveOrganizeOptions')[1].options).toMatchObject({ filtersOpen: false });
  });

  it('insère une balise du menu à la position du curseur', async () => {
    const server = mockApi({
      OrganizeState: stateOf({ options: { ...DEFAULT_OPTIONS, filtersOpen: true, rename: true } }),
      OrganizeNameTags: NAME_TAGS,
      OrganizePreview: { organizePreview: { text: 'one - Hot - Inna', warning: false, unknownTokens: [] } },
    });
    const { user } = renderWithRouter(<OrganizePage />);
    const input = await screen.findByRole('combobox', { name: 'Motif de nommage' });
    expect(input).toHaveValue('%titre% - %artiste%');
    input.setSelectionRange(7, 7);

    await user.click(screen.getByRole('button', { name: 'Insérer une balise ▾' }));
    await user.click(screen.getByRole('menuitem', { name: 'Morceau' }));
    await user.click(screen.getByRole('menuitem', { name: /Album/ }));
    expect(input).toHaveValue('%titre%%album% - %artiste%');
    expect(input).toHaveFocus();
    expect(input.selectionStart).toBe(14);
    expect(await screen.findByText('one - Hot - Inna')).toBeVisible();
    expect(server.queries('OrganizePreview')[0]).toEqual({
      sessionId: null,
      pattern: '%titre%%album% - %artiste%',
      rename: true,
    });
  });
});
