import { describe, expect, it } from 'vitest';
import { EQ_BANDS, EQ_BAND_LABELS, bandLevels, levelRatio } from './eq-bands';
import { queueWindow } from './queue-window';

describe('égaliseur', () => {
  it('nomme les bandes comme l’original', () => {
    expect(EQ_BAND_LABELS).toEqual(['31', '62', '125', '250', '500', '1k', '2k', '4k', '8k', '16k']);
  });

  it('additionne la puissance des raies de chaque bande', () => {
    // 4096 raies de 5,38 Hz (44 100 Hz / 8192), toutes à -60 dB sauf autour de 1 kHz.
    const hzPerBin = 44100 / 8192;
    const bins = new Float32Array(4096).fill(-200);
    const k = Math.round(1000 / hzPerBin);
    bins[k] = -20;
    const levels = bandLevels(bins, hzPerBin, new Float32Array(EQ_BANDS.length));
    const loud = EQ_BANDS.indexOf(1000);
    expect(levels[loud]).toBeCloseTo(-20, 1);
    for (let i = 0; i < EQ_BANDS.length; i++) if (i !== loud) expect(levels[i]).toBeLessThan(-150);
  });

  it('rend -120 dB pour une bande sans aucune énergie', () => {
    const bins = new Float32Array(4096).fill(Number.NEGATIVE_INFINITY);
    const levels = bandLevels(bins, 5.38, new Float32Array(EQ_BANDS.length));
    expect([...levels].every(v => v === -120)).toBe(true);
  });

  it('borne la hauteur des barres de -80 dB à -10 dB', () => {
    expect(levelRatio(-120)).toBe(0);
    expect(levelRatio(-80)).toBe(0);
    expect(levelRatio(-45)).toBeCloseTo(0.5);
    expect(levelRatio(0)).toBe(1);
  });
});

describe('queueWindow', () => {
  const queue = Array.from({ length: 400 }, (_, i) => ({ id: `t${i}` }));

  it('montre 10 titres avant et 150 après le titre courant', () => {
    const shown = queueWindow(queue, 100);
    expect(shown).toHaveLength(160);
    expect(shown[0]).toEqual({ track: queue[90], at: 90 });
    expect(shown.at(-1).at).toBe(249);
  });

  it('commence au début de la file pour les premiers titres', () => {
    expect(queueWindow(queue, 3)[0].at).toBe(0);
  });
});
