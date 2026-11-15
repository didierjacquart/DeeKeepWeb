import { describe, expect, it } from 'vitest';
import { THRESHOLD_FIELDS_UI, withThreshold } from './quality-thresholds';
import { makeThresholds } from './quality-fixtures';

const field = key => THRESHOLD_FIELDS_UI.find(f => f.key === key);

describe('withThreshold', () => {
  it('remplace le seuil saisi, les autres sont conservés', () => {
    const current = makeThresholds();
    expect(withThreshold(current, field('hissDb'), '-70')).toEqual({ ...current, hissDb: -70 });
  });

  it('ramène la valeur dans ses bornes', () => {
    expect(withThreshold(makeThresholds(), field('hissDb'), '-10').hissDb).toBe(-30);
    expect(withThreshold(makeThresholds(), field('headSeconds'), '0').headSeconds).toBe(0.5);
  });

  it('ignore une saisie qui n’est pas un nombre, ou des seuils inconnus', () => {
    expect(withThreshold(makeThresholds(), field('hissDb'), '')).toBeNull();
    expect(withThreshold(makeThresholds(), field('hissDb'), 'abc')).toBeNull();
    expect(withThreshold(null, field('hissDb'), '-70')).toBeNull();
  });

  it('chaque champ a des bornes cohérentes et une infobulle', () => {
    for (const f of THRESHOLD_FIELDS_UI) {
      expect(f.min).toBeLessThan(f.max);
      expect(f.tip).toBeTruthy();
    }
  });
});
