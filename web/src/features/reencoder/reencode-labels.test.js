import { describe, expect, it } from 'vitest';
import { makeJob } from '../../test/render';
import { coverOf, emptyText, formatText, matchOf, playerTrackOf, statusOf } from './reencode-labels';

describe('formatText', () => {
  it('affiche le format et le débit connus', () => {
    expect(formatText({ format: 'MP3', bitrate: 320 })).toBe('MP3 · 320 kbps');
    expect(formatText({ format: 'FLAC', bitrate: 0 })).toBe('FLAC');
    expect(formatText({ format: '', bitrate: 320 })).toBe('?');
  });
});

describe('coverOf', () => {
  it('passe la jaquette Deezer par le cache du serveur', () => {
    expect(coverOf({ match: { coverSmall: 'https://e/s.jpg', coverBig: 'https://e/b.jpg' } })).toBe(
      '/img?u=https%3A%2F%2Fe%2Fs.jpg',
    );
    expect(coverOf({ match: { coverSmall: null, coverBig: '/covers/x' } })).toBe('/covers/x');
    expect(coverOf({ match: null })).toBeNull();
  });
});

describe('matchOf', () => {
  it('décrit chaque état de correspondance', () => {
    expect(matchOf({ matchState: 'found', match: { artist: 'X', title: 'A' } })).toEqual({
      text: '✓ Trouvé',
      cls: 'ok',
      tip: 'X - A',
    });
    expect(matchOf({ matchState: 'skipped' }).tip).toBe('Correspondance non recherchée (trop de fichiers)');
    expect(matchOf({ matchState: 'none' }).text).toBe('✗ Introuvable');
    expect(matchOf({ matchState: 'pending' }).text).toBe('⏳');
  });
});

describe('statusOf', () => {
  it('suit la tâche de conversion', () => {
    expect(statusOf({ converted: true }, null).text).toBe('Réencodé ✓');
    expect(statusOf({}, makeJob({ status: 'SUCCEEDED' })).text).toBe('Réencodé ✓');
    expect(statusOf({}, makeJob({ status: 'QUEUED' })).text).toBe('En attente...');
    expect(statusOf({}, makeJob({ status: 'RUNNING', progress: 0.42, message: 'Extraction' }))).toEqual({
      text: 'Extraction — 42%',
      cls: 'info',
      tip: '',
    });
    expect(statusOf({}, makeJob({ status: 'RUNNING' })).text).toBe('Initialisation... — 0%');
    expect(statusOf({}, makeJob({ status: 'FAILED', error: 'Boum' }))).toEqual({
      text: 'Erreur',
      cls: 'err',
      tip: 'Boum',
    });
    expect(statusOf({}, makeJob({ status: 'CANCELLED' })).text).toBe('Annulé');
    expect(statusOf({}, null).text).toBe('En attente');
  });
});

describe('playerTrackOf', () => {
  it('construit la piste du lecteur', () => {
    const track = playerTrackOf({
      dbPath: 'm/a b.mp3',
      path: 'D:/m/a b.mp3',
      title: '',
      artist: 'X',
      album: 'Y',
      format: 'MP3',
      bitrate: 128,
      match: null,
    });
    expect(track).toEqual({
      id: 'm/a b.mp3',
      title: 'D:/m/a b.mp3',
      artist: 'X',
      album: 'Y',
      src: '/media/track?path=m%2Fa%20b.mp3',
      cover: null,
      context: 'MP3 · 128 kbps  ·  D:/m/a b.mp3',
    });
  });
});

describe('emptyText', () => {
  it("montre l'analyse en cours, sinon la raison de la liste vide", () => {
    expect(emptyText('3 / 10', 0)).toBe('3 / 10');
    expect(emptyText(null, 4)).toBe('Tous les fichiers sont déjà au format cible.');
    expect(emptyText(null, 0)).toMatch(/^Aucun fichier\./);
  });
});
