import { describe, expect, it } from 'vitest';
import { placeInViewport } from './menu/place-in-viewport';
import { tooltipPosition } from './tooltip/tooltip-position';
import { visibleRange } from './virtual-list/visible-range';

const viewport = { width: 1000, height: 800 };

describe('placeInViewport (menus)', () => {
  it('garde le point d’ancrage quand le menu tient', () => {
    expect(placeInViewport({ x: 100, y: 100 }, { width: 200, height: 300 }, viewport)).toEqual({ x: 100, y: 100 });
  });

  it('recule à gauche et remonte quand le menu déborderait', () => {
    expect(placeInViewport({ x: 900, y: 700 }, { width: 200, height: 300 }, viewport)).toEqual({ x: 792, y: 492 });
  });

  it('rabat un sous-menu à gauche de son entrée', () => {
    expect(placeInViewport({ x: 900, y: 100 }, { width: 200, height: 100 }, viewport, 700)).toEqual({ x: 500, y: 100 });
  });
});

describe('tooltipPosition', () => {
  const anchor = { left: 100, top: 100, width: 40, bottom: 120 };

  it('centre la bulle sous l’élément', () => {
    expect(tooltipPosition(anchor, { width: 100, height: 30 }, viewport)).toEqual({ left: 70, top: 128 });
  });

  it('passe au-dessus faute de place en bas, sans sortir de la fenêtre', () => {
    const low = { left: 980, top: 780, width: 20, bottom: 798 };
    expect(tooltipPosition(low, { width: 100, height: 30 }, viewport)).toEqual({ left: 892, top: 742 });
  });
});

describe('visibleRange (listes virtuelles)', () => {
  const base = { count: 1000, itemHeight: 40, height: 400, buffer: 80 };

  it('dessine la zone visible plus la marge', () => {
    expect(visibleRange({ ...base, scrollTop: 0 })).toEqual({ start: 0, end: 12 });
    expect(visibleRange({ ...base, scrollTop: 4000 })).toEqual({ start: 98, end: 112 });
  });

  it('ramène la position quand la liste a raccourci', () => {
    expect(visibleRange({ ...base, count: 20, scrollTop: 4000 })).toEqual({ start: 8, end: 20 });
  });

  it('ne dessine rien pour une liste vide', () => {
    expect(visibleRange({ ...base, count: 0, scrollTop: 0 })).toEqual({ start: 0, end: 0 });
  });
});
