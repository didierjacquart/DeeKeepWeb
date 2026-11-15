import { describe, expect, it } from 'vitest';
import {
  applyResultText,
  emptyText,
  isRunning,
  isTerminal,
  longLookupMessage,
  progressOf,
  statusText,
} from './organize-labels';
import { DEFAULT_OPTIONS, displayedOptions, optionsInput } from './organize-options';

describe('statusText', () => {
  const base = { applying: false, scanning: false, message: '', plan: null };

  it('suit le rangement, puis l’analyse, puis le message ou le plan', () => {
    expect(statusText({ ...base, applying: true, applyJob: { message: 'Fichier 3' } })).toBe('Fichier 3');
    expect(statusText({ ...base, applying: true, applyJob: null })).toBe('Rangement…');
    expect(statusText({ ...base, scanning: true, job: null, session: { message: 'Lecture' } })).toBe('Lecture');
    expect(statusText({ ...base, scanning: true })).toBe('Analyse en cours…');
    expect(statusText({ ...base, message: 'Bonjour', plan: { statusText: 'Plan' } })).toBe('Bonjour');
    expect(statusText({ ...base, plan: { statusText: 'Plan' } })).toBe('Plan');
    expect(statusText(base)).toBe('');
  });
});

describe('états de tâche', () => {
  it('isTerminal et isRunning', () => {
    expect(['SUCCEEDED', 'CANCELLED', 'FAILED'].every(isTerminal)).toBe(true);
    expect(isTerminal('RUNNING')).toBe(false);
    expect(isRunning({ status: 'QUEUED' })).toBe(true);
    expect(isRunning(null)).toBe(false);
  });

  it('progressOf est indéterminé sans total', () => {
    expect(progressOf(null)).toBeNull();
    expect(progressOf({ total: 0, progress: 0.5 })).toBeNull();
    expect(progressOf({ total: 4, progress: 0.5 })).toBe(0.5);
  });
});

describe('textes', () => {
  it('emptyText', () => {
    expect(emptyText(false, false)).toBe('Chargement…');
    expect(emptyText(true, true)).toBe('Analyse en cours…');
    expect(emptyText(true, false)).toBe('Aucune analyse : choisissez un dossier et lancez « Analyser ».');
  });

  it('applyResultText', () => {
    expect(applyResultText({ status: 'FAILED', error: 'disque plein' })).toBe('Le rangement a échoué : disque plein');
    expect(applyResultText({ status: 'CANCELLED', result: { summary: '2 fichiers.' } })).toBe(
      'Rangement arrêté entre deux fichiers. 2 fichiers.',
    );
    expect(applyResultText({ status: 'SUCCEEDED', result: null })).toBe('');
  });

  it('longLookupMessage estime la durée en minutes (au moins une)', () => {
    expect(longLookupMessage(100)).toContain("100 fichiers cochés n'ont pas tous leurs tags.");
    expect(longLookupMessage(100)).toContain("de l'ordre de 2 minute(s)");
    expect(longLookupMessage(10)).toContain("de l'ordre de 1 minute(s)");
  });
});

describe('options', () => {
  it('optionsInput borne le seuil « Divers » entre 2 et 10', () => {
    expect(optionsInput({ ...DEFAULT_OPTIONS, diversMin: 0 }).diversMin).toBe(3);
    expect(optionsInput({ ...DEFAULT_OPTIONS, diversMin: 1 }).diversMin).toBe(2);
    expect(optionsInput({ ...DEFAULT_OPTIONS, diversMin: 4.6 }).diversMin).toBe(5);
    expect(optionsInput({ ...DEFAULT_OPTIONS, diversMin: 15 }).diversMin).toBe(10);
  });

  it('displayedOptions replie le volet sur téléphone sans toucher au réglage ailleurs', () => {
    const saved = { ...DEFAULT_OPTIONS, filtersOpen: true };
    expect(displayedOptions(saved, true).filtersOpen).toBe(false);
    expect(displayedOptions(saved, false).filtersOpen).toBe(true);
  });
});
