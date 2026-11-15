import { describe, expect, it } from 'vitest';
import {
  archiveGauge,
  archiveTip,
  baseName,
  chosenPaths,
  failureLine,
  nothingToRestore,
  panelJobLine,
  restoreTip,
  selectConfigured,
  workLine,
} from './archive-model';

const USAGE = {
  tracksOnDisk: 10,
  bytesOnDisk: 5_000_000,
  diskFreeBytes: 8_000_000,
  bytesToArchive: 2_000_000,
  archiveFileBytes: 2_000_000,
  objectCount: 4,
  tracksArchived: 4,
  tracksStale: 1,
  tracksNotArchived: 5,
  sameVolume: false,
  error: null,
};

describe('sélection et bulles', () => {
  it('garde les cochés affichés, dans l’ordre d’affichage', () => {
    expect(chosenPaths(['/c', '/a', '/z'], ['/a', '/b', '/c'])).toEqual(['/a', '/c']);
  });

  it('lit la configuration', () => {
    expect(selectConfigured({ config: null })).toBe(false);
    expect(selectConfigured({ config: { configured: true } })).toBe(true);
  });

  it('explique les boutons sans archive configurée', () => {
    expect(archiveTip(false)).toBe(restoreTip(false));
    expect(archiveTip(true)).toContain('aucun réencodage');
    expect(restoreTip(true)).toContain("Rien n'est écrasé");
  });

  it('extrait le nom de fichier', () => {
    expect(baseName('/a/b/c.flac')).toBe('c.flac');
  });
});

describe('nothingToRestore', () => {
  it('signale les titres restaurables masqués par le filtre', () => {
    expect(nothingToRestore({ restorableElsewhere: 3 })).toMatch(
      /^Aucun des titres affichés .* mais 3 le sont ailleurs/,
    );
    expect(nothingToRestore({ restorableElsewhere: 0 })).toBe(
      "Rien à restaurer : tous les titres que l'archive détient sont déjà sur le disque.",
    );
  });
});

describe('archiveGauge', () => {
  it('rien tant que la jauge n’est pas mesurée', () => {
    expect(archiveGauge(null, '/x')).toBeNull();
  });

  it('demande le fichier quand le champ est vide', () => {
    const g = archiveGauge(USAGE, '  ');
    expect(g).toEqual({
      text: "Indiquez le fichier d'archive : les 10 titres présents sur le disque y entreraient tels quels, soit 5 Mo.",
      warn: true,
      bar: null,
      detail: '',
    });
  });

  it('compare la place libre à ce qui reste à copier', () => {
    const g = archiveGauge(USAGE, '/srv/a.db');
    expect(g.warn).toBe(false);
    expect(g.text).toBe(
      "Les 10 titres présents sur le disque entreraient tels quels dans l'archive, soit 5 Mo — pour 8 Mo de libre sur son volume. Tous reviendraient octet pour octet.",
    );
    expect(g.bar).toEqual({ used: 20, todo: 20 });
    expect(g.detail).toBe(
      'Archive : 2 Mo (4 objets) · 4 titres archivés · 1 à renvoyer · 5 non archivés — reste 2 Mo à archiver.',
    );
  });

  it('alerte si la place manque ou si le volume est partagé', () => {
    const g = archiveGauge({ ...USAGE, diskFreeBytes: 1_000_000, sameVolume: true, error: 'Disque lent.' }, '/a');
    expect(g.warn).toBe(true);
    expect(g.text).toContain('La place ne suffit pas.');
    expect(g.text).toContain('même volume');
    expect(g.text.endsWith('Disque lent.')).toBe(true);
  });

  it('pas de barre sans aucune place connue', () => {
    expect(archiveGauge({ ...USAGE, archiveFileBytes: 0, diskFreeBytes: 0 }, '/a').bar).toBeNull();
  });
});

describe('workLine', () => {
  const job = fields => ({ id: 'j', status: 'RUNNING', message: null, result: null, error: null, ...fields });

  it('montre le message de la tâche en cours', () => {
    expect(workLine(job({ message: '3 / 10' }), undefined)).toEqual({ text: '3 / 10', error: false });
    expect(workLine(job(), 'RUNNING')).toBeNull();
  });

  it('montre le bilan une seule fois', () => {
    const done = job({ status: 'SUCCEEDED', message: '10 envoyés', result: { sent: 10 } });
    expect(workLine(done, 'RUNNING')).toEqual({ text: '10 envoyés', error: false });
    expect(workLine(done, 'SUCCEEDED')).toBeNull();
  });

  it('marque en erreur une copie entièrement en échec', () => {
    expect(workLine(job({ status: 'SUCCEEDED', result: { failed: 2 } }), undefined)).toEqual({
      text: 'Terminé.',
      error: true,
    });
  });

  it('décrit arrêt et échec', () => {
    expect(workLine(job({ status: 'CANCELLED', message: 'Arrêtée' }), 'RUNNING')).toEqual({
      text: 'Arrêtée.',
      error: false,
    });
    expect(workLine(job({ status: 'CANCELLED', message: '3 envoyés' }), 'RUNNING').text).toBe('Arrêtée — 3 envoyés');
    expect(workLine(job({ status: 'FAILED', error: 'Disque plein' }), 'RUNNING')).toEqual({
      text: 'Disque plein',
      error: true,
    });
    expect(workLine(job({ status: 'FAILED' }), 'RUNNING').text).toBe('Échec.');
  });
});

describe('panelJobLine / failureLine', () => {
  it('résume une tâche du volet', () => {
    expect(panelJobLine({ status: 'SUCCEEDED', message: null })).toEqual({ text: 'Terminé.', error: false });
    expect(panelJobLine({ status: 'CANCELLED' })).toEqual({ text: 'Arrêtée.', error: false });
    expect(panelJobLine({ status: 'FAILED', error: null, message: 'Illisible' })).toEqual({
      text: 'Illisible',
      error: true,
    });
  });

  it('nomme le dernier titre en échec', () => {
    expect(failureLine({ status: 'failed', data: { path: '/m/a.flac' }, message: 'Absent' })).toBe('a.flac — Absent');
    expect(failureLine({ status: 'failed', itemKey: '/m/b.flac', data: null })).toBe('b.flac — ');
    expect(failureLine({ status: 'done' })).toBeNull();
    expect(failureLine(undefined)).toBeNull();
  });
});
