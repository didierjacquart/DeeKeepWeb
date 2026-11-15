import { describe, expect, it } from 'vitest';
import {
  buildRows,
  eligibleRows,
  emptyTrack,
  globalProgress,
  presenceKey,
  progressLabel,
  toRow,
  unknownLiveKey,
} from './music-rows';

const track = (id, title = `T${id}`) => ({ ...emptyTrack(title), id, artist: 'A', duration: 200 });
const jobRow = job => toRow({ id: 'j', kind: 'job', job, track: track(1), seq: 0 });

describe('toRow', () => {
  it('ligne en attente : grise, sans progression', () => {
    const r = toRow({ id: 'p', kind: 'pending', job: null, track: track(1), seq: 0 });
    expect(r).toMatchObject({ status: 'En attente', color: 'muted', progress: 0, canCancel: false, failed: false });
  });

  it('tâche en file : annulable, tentative affichée après une erreur', () => {
    expect(jobRow({ status: 'QUEUED', attempts: 0 })).toMatchObject({ status: "En file d'attente", canCancel: true });
    expect(jobRow({ status: 'QUEUED', attempts: 2, error: 'HTTP 429' }).status).toBe('Nouvelle tentative (HTTP 429)');
  });

  it('tâche en cours : message, progression en pour cent', () => {
    expect(jobRow({ status: 'RUNNING', progress: 0.426, message: 'Conversion' })).toMatchObject({
      status: 'Conversion',
      color: 'info',
      progress: 43,
      busy: true,
      canCancel: true,
    });
    expect(jobRow({ status: 'RUNNING', progress: null, message: '' }).status).toBe('Initialisation...');
  });

  it('tâche réussie : fichier extrait, essais et vidéo source', () => {
    const r = jobRow({ status: 'SUCCEEDED', result: { attempts: 3, dbPath: 'a/b.mp3', videoTitle: 'Clip' } });
    expect(r).toMatchObject({
      status: 'Téléchargé ✓ (3 essais)',
      color: 'ok',
      progress: 100,
      succeeded: true,
      dbPath: 'a/b.mp3',
      tooltip: 'Vidéo : Clip',
    });
    expect(jobRow({ status: 'SUCCEEDED', result: null })).toMatchObject({ status: 'Téléchargé ✓', dbPath: null });
  });

  it('tâche en échec ou annulée : rouge, à relancer', () => {
    expect(jobRow({ status: 'FAILED', message: 'Introuvable', error: 'détail' })).toMatchObject({
      status: 'Introuvable',
      color: 'err',
      tooltip: 'détail',
      failed: true,
    });
    expect(jobRow({ status: 'FAILED', message: 'Terminé' }).status).toBe('Erreur');
    expect(jobRow({ status: 'CANCELLED' })).toMatchObject({ status: 'Annulé', color: 'err', failed: true });
  });
});

describe('buildRows', () => {
  const pending = [{ id: 'p1', track: track(10) }];
  const base = [
    { id: 'j2', createdAt: 2, status: 'QUEUED', payload: { track: track(2) } },
    { id: 'j1', createdAt: 1, status: 'SUCCEEDED', title: 'Sans charge', payload: null, result: { dbPath: 'x' } },
  ];

  it('lignes en attente puis tâches par date de création', () => {
    const rows = buildRows(base, pending, []);
    expect(rows.map(r => [r.id, r.kind, r.seq])).toEqual([
      ['p1', 'pending', 0],
      ['j1', 'job', 1],
      ['j2', 'job', 2],
    ]);
    expect(rows[1].track).toMatchObject({ id: 0, title: 'Sans charge' });
  });

  it("l'état vivant d'une tâche l'emporte, la piste vient de la requête", () => {
    const live = [{ id: 'j2', status: 'RUNNING', progress: 0.5, payload: null, result: null }];
    const row = buildRows(base, [], live).find(r => r.id === 'j2');
    expect(row).toMatchObject({ status: 'Initialisation...', progress: 50 });
    expect(row.track.id).toBe(2);
  });
});

describe('calculs de la file', () => {
  const rows = [
    { id: 'a', kind: 'pending', failed: false, progress: 0 },
    { id: 'b', kind: 'job', failed: true, progress: 0 },
    { id: 'c', kind: 'job', failed: false, progress: 100, succeeded: true },
  ];

  it('progression globale arrondie au pour cent inférieur', () => {
    expect(globalProgress([])).toBe(0);
    expect(globalProgress(rows)).toBe(0.33);
  });

  it('à extraire : en attente et en échec, ou seulement les cochées parmi elles', () => {
    expect(eligibleRows(rows, new Set()).map(r => r.id)).toEqual(['a', 'b']);
    expect(eligibleRows(rows, new Set(['b', 'c'])).map(r => r.id)).toEqual(['b']);
    expect(eligibleRows(rows, new Set(['c'])).map(r => r.id)).toEqual(['a', 'b']);
  });

  it('libellé de progression : durée, puis pourcentage une fois lancée', () => {
    expect(progressLabel({ track: { duration: 200 }, progress: 0 })).toBe('3:20');
    expect(progressLabel({ track: { duration: 200 }, progress: 42, busy: true })).toBe('3:20 - 42%');
    expect(progressLabel({ track: { duration: 200 }, progress: 100, succeeded: true })).toBe('3:20 - 100%');
  });

  it('clé de présence : identifiants et fichiers extraits', () => {
    expect(presenceKey(rows)).toBe('a|0,b|0,c|1');
  });

  it('tâches vivantes inconnues de la requête', () => {
    expect(unknownLiveKey([{ id: 'a' }], [{ id: 'a' }, { id: 'x' }, { id: 'y' }])).toBe('x,y');
    expect(unknownLiveKey([{ id: 'a' }], [{ id: 'a' }])).toBe('');
  });
});
