import { fireEvent, render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { coverSources } from './cover-sources';
import { createMeter, LED_SEGMENTS, segmentColor, stepMeter } from './led-meter';
import { clampWindowPosition } from './window-position';
import { Cover } from './components/Cover';

describe('coverSources', () => {
  it('essaie d’abord la jaquette intégrée au fichier, puis celle de la base', () => {
    expect(coverSources({ src: '/media/track?path=D%3A%5Ca.mp3', cover: '/covers/abc' })).toEqual([
      '/media/cover?path=D%3A%5Ca.mp3',
      '/covers/abc',
    ]);
  });

  it('se contente de la jaquette connue pour une archive ou un extrait', () => {
    expect(coverSources({ src: '/archive-media/track?path=a', cover: '/covers/abc' })).toEqual(['/covers/abc']);
    expect(coverSources({ src: '/preview/1', cover: null })).toEqual([]);
    expect(coverSources(null)).toEqual([]);
  });
});

describe('Cover', () => {
  it('passe à la source suivante puis à la croche quand une image manque', () => {
    const { container } = render(<Cover sources={['/a', '/b']} className="c" />);
    fireEvent.error(container.querySelector('img'));
    expect(container.querySelector('img')).toHaveAttribute('src', '/b');
    fireEvent.error(container.querySelector('img'));
    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('.c.placeholder')).toHaveTextContent('♪');
  });
});

describe('égaliseur à diodes', () => {
  it('va du vert au rouge doux en montant', () => {
    expect(segmentColor(0)).toEqual([0x22, 0xd3, 0x66]);
    expect(segmentColor(LED_SEGMENTS - 1)).toEqual([0xff, 0x8a, 0x80]);
  });

  it('monte d’un coup, redescend amorti, garde la crête puis s’éteint', () => {
    const m = createMeter(1);
    stepMeter(m, [0.8], 0.016);
    expect(m.levels[0]).toBeCloseTo(0.8);
    stepMeter(m, [0], 0.1);
    expect(m.levels[0]).toBeGreaterThan(0.6);
    expect(m.peaks[0]).toBeCloseTo(0.8);
    let lit = true;
    for (let i = 0; i < 100 && lit; i++) lit = stepMeter(m, null, 0.1);
    expect(lit).toBe(false);
  });
});

describe('position de la fenêtre', () => {
  const viewport = { width: 1200, height: 800 };
  const size = { width: 560, height: 420 };

  it('reste atteignable quand on la pousse hors de l’écran', () => {
    expect(clampWindowPosition({ x: 5000, y: -50 }, size, viewport)).toEqual({ x: 1120, y: 0 });
    expect(clampWindowPosition({ x: -5000, y: 5000 }, size, viewport)).toEqual({ x: -480, y: 752 });
  });
});
