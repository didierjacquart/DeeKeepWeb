import { act, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { makeJob, renderWithRouter } from '../../test/render';
import { mockApi } from '../../test/mock-api';
import { setMobile } from '../../test/media';
import { jobs } from '../../core/jobs/jobs';
import { player } from '../../core/player/player';
import { QualityPage } from './QualityPage';
import { defect, makeRow, makeThresholds } from './quality-fixtures';

const noisy = makeRow({
  path: '/music/noisy.flac',
  title: 'Bruyant',
  artist: 'Groupe',
  score: 55,
  defects: [defect('hiss', 'Souffle'), defect('tail', 'Blanc de fin')],
  cells: { score: '55', defects: 'Souffle, blanc de fin', hiss: '-58 dB', blanks: 'Fin : 14 s', status: '' },
  tooltips: {
    defects: 'Souffle audible',
    hiss: 'Plancher de bruit relevé : -58 dB',
    blanks: 'Début : 0 s — Fin : 14 s',
  },
});
const gappy = makeRow({
  path: '/music/gappy.mp3',
  title: 'Troué',
  artist: 'Autre',
  score: 85,
  defects: [defect('gaps', 'Coupures')],
  cells: { score: '85', defects: 'Coupures', gaps: '4 · 51 %', status: '' },
  tooltips: { gaps: 'Passage le plus troué : 51 %' },
});
const clean = makeRow({ path: '/music/clean.flac', title: 'Impeccable' });

function setup(overrides = {}) {
  const api = mockApi({
    QualityEntries: {
      qualityEntries: {
        items: [noisy, gappy, clean],
        total: 3,
        flagged: 2,
        errors: 0,
        referencesPending: 0,
        folder: '/music',
        thresholds: makeThresholds(),
      },
    },
    UpdateQualityThresholds: v => ({ updateQualityThresholds: v.thresholds }),
    StartQualityScan: { startQualityScan: { id: 'scan-1' } },
    Reanalyze: { reanalyze: { id: 're-1' } },
    RedownloadQuality: { redownloadQuality: { requested: 1, skipped: 0, skippedPaths: [], jobs: [{ id: 'r1' }] } },
    ...overrides,
  });
  return { api, ...renderWithRouter(<QualityPage />) };
}

/** Ligne du tableau contenant le texte donné. */
const rowOf = text => screen.getByText(text).closest('.dk-row');

describe('QualityPage', () => {
  it('affiche les lignes du serveur, cellules de défauts et infobulles', async () => {
    setup();
    expect(await screen.findByText('Bruyant')).toBeInTheDocument();
    expect(screen.getByText('Troué')).toBeInTheDocument();
    // Les titres sans défaut ne s'affichent que sur demande.
    expect(screen.queryByText('Impeccable')).not.toBeInTheDocument();

    const row = within(rowOf('Bruyant'));
    expect(row.getByText('Souffle, blanc de fin')).toHaveAttribute('data-tip', 'Souffle audible');
    expect(row.getByText('-58 dB')).toHaveAttribute('data-tip', 'Plancher de bruit relevé : -58 dB');
    expect(row.getByText('-58 dB')).toHaveClass('bad');
    expect(row.getByText('Fin : 14 s')).toHaveClass('c-blanks', 'bad');
    expect(row.getByText('55')).toHaveClass('score', 'bad');
    expect(within(rowOf('Troué')).getByText('4 · 51 %')).toHaveAttribute('data-tip', 'Passage le plus troué : 51 %');

    expect(screen.getByText('/music', { selector: 'b' })).toBeInTheDocument();
    expect(
      screen.getByText('Analyse terminée : 3 fichier(s) lus, 2 titre(s) à la qualité douteuse.'),
    ).toBeInTheDocument();
  });

  it('filtre par défaut et montre les titres sains sur demande', async () => {
    const { user } = setup();
    await screen.findByText('Bruyant');
    await user.click(screen.getByRole('checkbox', { name: 'Coupures' }));
    expect(screen.queryByText('Troué')).not.toBeInTheDocument();
    await user.click(screen.getByRole('checkbox', { name: 'Titres sans défaut' }));
    expect(screen.getByText('Impeccable')).toBeInTheDocument();
  });

  it('trie par colonne au clic sur son en-tête', async () => {
    const { user } = setup();
    await screen.findByText('Bruyant');
    const titles = () => screen.getAllByText(/^(Bruyant|Troué)$/, { selector: 'b' }).map(b => b.textContent);
    expect(titles()).toEqual(['Bruyant', 'Troué']);
    expect(screen.getByRole('button', { name: 'Note' })).toHaveTextContent('Note ▲');
    await user.click(screen.getByRole('button', { name: 'Note' }));
    expect(titles()).toEqual(['Troué', 'Bruyant']);
    expect(screen.getByRole('button', { name: 'Note' })).toHaveTextContent('Note ▼');
  });

  it('enregistre un seuil une seule fois, au « change » natif et non à chaque frappe', async () => {
    const { user, api } = setup();
    await screen.findByText('Bruyant');
    await user.click(screen.getByRole('button', { name: /Seuils/ }));
    const input = screen.getByRole('spinbutton', { name: /Souffle au-dessus de/ });
    expect(input).toHaveValue(-80);

    await user.clear(input);
    await user.type(input, '-70');
    await new Promise(r => setTimeout(r, 500));
    expect(api.mutations('UpdateQualityThresholds')).toHaveLength(0);

    await user.tab();
    await waitFor(() => expect(api.mutations('UpdateQualityThresholds')).toHaveLength(1));
    expect(api.mutations('UpdateQualityThresholds')[0]).toEqual({ thresholds: makeThresholds({ hissDb: -70 }) });
  });

  it("lance l'analyse du dossier", async () => {
    const { user, api } = setup();
    await screen.findByText('Bruyant');
    await user.click(screen.getByRole('button', { name: 'Analyser' }));
    expect(api.mutations('StartQualityScan')).toEqual([{ folder: '/music', deep: true }]);
  });

  it('suit une analyse en cours : progression et Arrêter', async () => {
    const { user, api } = setup({ CancelJob: { cancelJob: null } });
    await screen.findByText('Bruyant');
    expect(screen.getByRole('button', { name: 'Arrêter' })).toBeDisabled();

    act(() => {
      jobs.upsert(
        makeJob({ id: 'scan-1', service: 'quality', type: 'quality.scan', total: 10, done: 4, message: 'Lecture…' }),
      );
    });
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '40');
    expect(screen.getByText('Lecture…')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Analyser' })).toBeDisabled();

    await user.click(screen.getByRole('button', { name: 'Arrêter' }));
    expect(api.mutations('CancelJob')).toEqual([{ service: 'quality', id: 'scan-1' }]);
  });

  it('menu de la ligne : Réanalyser', async () => {
    const { user, api } = setup();
    await screen.findByText('Bruyant');
    await user.pointer({ keys: '[MouseRight]', target: screen.getByText('Bruyant') });
    await user.click(screen.getByRole('menuitem', { name: 'Réanalyser' }));
    expect(api.mutations('Reanalyze')).toEqual([{ paths: ['/music/noisy.flac'] }]);
  });

  it('menu de la ligne : Écouter dans DeeKeep, la liste affichée devient la file', async () => {
    const play = vi.spyOn(player, 'play').mockImplementation(() => undefined);
    const { user } = setup();
    await screen.findByText('Bruyant');
    await user.click(within(rowOf('Troué')).getByRole('button', { name: "Plus d'actions" }));
    await user.click(screen.getByRole('menuitem', { name: 'Écouter dans DeeKeep' }));
    expect(play).toHaveBeenCalledTimes(1);
    const [track, queue] = play.mock.calls[0];
    expect(track).toMatchObject({ id: '/music/gappy.mp3', title: 'Troué', artist: 'Autre' });
    expect(queue.map(t => t.id)).toEqual(['/music/noisy.flac', '/music/gappy.mp3']);
  });

  it('retélécharge les titres cochés après confirmation', async () => {
    const { user, api } = setup();
    await screen.findByText('Bruyant');
    await user.click(screen.getByRole('checkbox', { name: 'Cocher Bruyant' }));
    await user.click(screen.getByRole('button', { name: 'Retélécharger la sélection (1)' }));
    await user.click(await screen.findByRole('button', { name: 'Continuer' }));
    await waitFor(() => expect(api.mutations('RedownloadQuality')).toEqual([{ paths: ['/music/noisy.flac'] }]));
    expect(screen.getByRole('checkbox', { name: 'Cocher Bruyant' })).not.toBeChecked();
  });

  it("ajoute les lignes reçues au fil de l'analyse", async () => {
    const { api } = setup();
    await screen.findByText('Bruyant');
    const late = makeRow({ path: '/music/late.flac', title: 'Tardif', score: 40, defects: [defect('clipping')] });
    act(() => api.emit('QualityUpdated', { qualityUpdated: { reason: 'entries', removed: [], entries: [late] } }));
    expect(screen.getByText('Tardif')).toBeInTheDocument();
  });

  it('mise en page compacte sur mobile : cartes sans en-têtes de colonnes', async () => {
    setMobile(true);
    const { container } = setup();
    await screen.findByText('Bruyant');
    expect(screen.queryByRole('button', { name: /^Note/ })).not.toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: "Plus d'actions" })).toHaveLength(2);
    // Cartes plus hautes que les lignes : deux lignes de 118 px.
    expect(container.querySelector('.dk-viewport > div')).toHaveStyle({ height: '236px' });
  });
});
