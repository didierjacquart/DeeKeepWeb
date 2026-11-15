import { describe, expect, it } from 'vitest';
import { qualitiesFor, qualityFor, readBool, readNum, readStr } from './settings-values';

describe('qualitiesFor', () => {
  it('propose le sans-perte pour FLAC et WAV', () => {
    expect(qualitiesFor('FLAC')).toEqual(['Lossless (Qualité Source)']);
    expect(qualitiesFor('WAV')).toEqual(['Lossless (Qualité Source)']);
  });

  it('propose les débits pour MP3, M4A et tout autre format', () => {
    expect(qualitiesFor('MP3')).toEqual(['320 kbps (HQ)', '128 kbps (Standard)']);
    expect(qualitiesFor('M4A')).toEqual(['320 kbps (HQ)', '128 kbps (Standard)']);
    expect(qualitiesFor(undefined)).toEqual(['320 kbps (HQ)', '128 kbps (Standard)']);
  });
});

describe('qualityFor', () => {
  it('garde la qualité si le format la propose', () => {
    expect(qualityFor('MP3', '128 kbps (Standard)')).toBe('128 kbps (Standard)');
    expect(qualityFor('FLAC', 'Lossless (Qualité Source)')).toBe('Lossless (Qualité Source)');
  });

  it('prend la première qualité du format sinon', () => {
    expect(qualityFor('FLAC', '320 kbps (HQ)')).toBe('Lossless (Qualité Source)');
    expect(qualityFor('M4A', 'Lossless (Qualité Source)')).toBe('320 kbps (HQ)');
    expect(qualityFor('MP3', '')).toBe('320 kbps (HQ)');
  });
});

describe('lecture typée des réglages', () => {
  const settings = { s: 'texte', b: true, n: 4, wrong: 12 };

  it('rend la valeur du bon type', () => {
    expect(readStr(settings, 's')).toBe('texte');
    expect(readBool(settings, 'b')).toBe(true);
    expect(readNum(settings, 'n', 3)).toBe(4);
  });

  it('rend la valeur par défaut si la clé manque ou a un autre type', () => {
    expect(readStr(settings, 'wrong')).toBe('');
    expect(readStr(settings, 'absent', 'MP3')).toBe('MP3');
    expect(readBool(settings, 's')).toBe(false);
    expect(readBool(settings, 'absent', true)).toBe(true);
    expect(readNum(settings, 's', 3)).toBe(3);
  });
});
