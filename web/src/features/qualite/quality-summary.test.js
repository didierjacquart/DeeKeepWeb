import { describe, expect, it } from 'vitest';
import { makeJob } from '../../test/render';
import {
  qualityStats,
  redownloadMessage,
  redownloadPlan,
  redownloadSummary,
  scanProgress,
  statusLine,
} from './quality-summary';
import { defect, makeRow, makeThresholds } from './quality-fixtures';

const rowsOf = (...rows) => new Map(rows.map(r => [r.path, r]));

describe('qualityStats', () => {
  it('compte lus, douteux, illisibles et références attendues', () => {
    const rows = rowsOf(
      makeRow({ defects: [defect('hiss')] }),
      makeRow({ error: 'x' }),
      makeRow({ referenceState: 'pending' }),
      makeRow({ referenceState: 'network' }),
    );
    expect(qualityStats(rows)).toEqual({ count: 4, flagged: 1, errors: 1, waiting: 2 });
  });
});

describe('scanProgress', () => {
  it('avancement borné, indéterminé sans total, nul hors analyse', () => {
    expect(scanProgress(makeJob({ total: 10, done: 3, failed: 1 }))).toBeCloseTo(0.4);
    expect(scanProgress(makeJob({ total: 2, done: 3 }))).toBe(1);
    expect(scanProgress(makeJob({ total: 0 }))).toBeNull();
    expect(scanProgress(makeJob({ status: 'SUCCEEDED', total: 10 }))).toBeNull();
    expect(scanProgress(null)).toBeNull();
  });
});

describe('statusLine', () => {
  const empty = { count: 0, flagged: 0, errors: 0, waiting: 0 };

  it("pendant l'analyse", () => {
    expect(statusLine(makeJob({ status: 'QUEUED' }), empty, true, null)).toBe('Analyse en attente…');
    expect(statusLine(makeJob({ message: 'Lecture de a.flac' }), empty, true, null)).toBe('Lecture de a.flac');
    expect(statusLine(makeJob(), empty, true, null)).toBe('Analyse en cours…');
  });

  it('liste vide : chargement ou invitation', () => {
    expect(statusLine(null, empty, false, null)).toBe('Chargement…');
    expect(statusLine(null, empty, true, null)).toBe("Lancez l'analyse pour repérer les titres à la qualité médiocre.");
  });

  it('bilan de la dernière analyse', () => {
    const stats = { count: 12, flagged: 3, errors: 1, waiting: 2 };
    expect(statusLine(makeJob({ status: 'CANCELLED' }), stats, true, makeThresholds({ deep: false }))).toBe(
      'Analyse interrompue : 12 fichier(s) lus, 3 titre(s) à la qualité douteuse — 1 illisible(s) — durée du morceau en cours de recherche pour 2 titre(s) — craquements non mesurés (analyse rapide).',
    );
    expect(statusLine(null, { ...stats, errors: 0, waiting: 0 }, true, makeThresholds())).toBe(
      'Analyse terminée : 12 fichier(s) lus, 3 titre(s) à la qualité douteuse.',
    );
  });
});

describe('redownloadSummary', () => {
  it('rien sans retéléchargement actif', () => {
    expect(redownloadSummary([makeJob({ type: 'redownload', status: 'SUCCEEDED' })])).toBeNull();
  });

  it('avancement global du lot, titre en cours', () => {
    const group = { type: 'redownload', groupKey: 'g' };
    const summary = redownloadSummary([
      makeJob({ ...group, id: '1', status: 'SUCCEEDED' }),
      makeJob({ ...group, id: '2', status: 'RUNNING', progress: 0.5, title: 'Titre B', message: 'Extraction' }),
      makeJob({ ...group, id: '3', status: 'QUEUED' }),
    ]);
    expect(summary.value).toBeCloseTo(0.5);
    expect(summary.label).toBe('Retéléchargement 2 / 3 : Titre B — Extraction');
  });

  it('en attente tant que rien ne tourne', () => {
    const summary = redownloadSummary([makeJob({ type: 'redownload', status: 'QUEUED' })]);
    expect(summary).toEqual({ value: 0, label: 'Retéléchargement 1 / 1 : En attente…' });
  });
});

describe('redownloadPlan / redownloadMessage', () => {
  it('écarte les illisibles et les titres sans artiste ou titre', () => {
    const ok = makeRow({ path: 'a' });
    const untagged = makeRow({ path: 'b', artist: ' ' });
    const broken = makeRow({ path: 'c', error: 'x' });
    const other = makeRow({ path: 'd' });
    const plan = redownloadPlan([ok, untagged, broken, other], new Set(['a', 'b', 'c']));
    expect(plan).toEqual({ chosen: 2, targets: [ok], skipped: 1 });
  });

  it('mentionne les titres écartés', () => {
    expect(redownloadMessage(3, 0)).toMatch(/^3 titre\(s\) vont être réextraits/);
    expect(redownloadMessage(3, 0)).not.toContain('écartés');
    expect(redownloadMessage(3, 2)).toContain('\n\n2 titre(s) coché(s) sont écartés');
    expect(redownloadMessage(3, 2)).toMatch(/\n\nContinuer \?$/);
  });
});
