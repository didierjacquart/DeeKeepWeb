import { describe, expect, it } from 'vitest';
import { statusLine } from './library-status';

const row = fields => ({ track: { onDisk: true, archiveState: 'none', ...fields } });

describe('statusLine', () => {
  it('résume affichés, disque, miroir et titres à renvoyer', () => {
    const rows = [
      row({ fileSize: 2_000_000, archiveState: 'ok', archiveBytes: 1_000_000, archiveSourceBytes: 2_000_000 }),
      row({ fileSize: 3_000_000, archiveState: 'stale' }),
      row({ onDisk: false }),
    ];
    expect(statusLine(rows, 2)).toBe(
      "2 titres affichés sur 3 · 2 sur le disque (5 Mo) · 1 en miroir (1 Mo, soit 50 % du poids d'origine) · 1 à renvoyer",
    );
  });

  it('signale l’absence de miroir', () => {
    expect(statusLine([row({ fileSize: null })], 1)).toBe(
      '1 titres affichés sur 1 · 1 sur le disque (0 o) · aucun titre en miroir',
    );
  });

  it('ne divise pas par zéro sans taille d’origine', () => {
    expect(statusLine([row({ archiveState: 'ok', archiveBytes: 0 })], 0)).toContain('soit 0 %');
  });
});
