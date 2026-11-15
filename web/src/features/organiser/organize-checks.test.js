import { describe, expect, it } from 'vitest';
import {
  applyConfirmText,
  applyOperations,
  checksToMap,
  effectiveChecked,
  lookupIndicesOf,
  mergeEntries,
  withChecks,
} from './organize-checks';

describe('mergeEntries', () => {
  it('remplace les index connus, ajoute les autres et trie', () => {
    const list = [
      { index: 2, name: 'b' },
      { index: 0, name: 'a' },
    ];
    expect(
      mergeEntries(list, [
        { index: 1, name: 'x' },
        { index: 2, name: 'B' },
      ]),
    ).toEqual([
      { index: 0, name: 'a' },
      { index: 1, name: 'x' },
      { index: 2, name: 'B' },
    ]);
  });
});

describe('cases', () => {
  it('checksToMap relit les cases enregistrées', () => {
    expect([...checksToMap({ checked: [1, 2], unchecked: [3] })]).toEqual([
      [1, true],
      [2, true],
      [3, false],
    ]);
  });

  it('withChecks copie les cases en changeant les index donnés', () => {
    const m = new Map([[1, true]]);
    expect([...withChecks(m, [1, 2], false)]).toEqual([
      [1, false],
      [2, false],
    ]);
    expect(m.get(1)).toBe(true);
  });

  it('effectiveChecked : la main de l’utilisateur, sinon le plan, sinon coché', () => {
    const entries = [{ index: 0 }, { index: 1 }, { index: 2 }, { index: 3 }];
    const touched = new Map([
      [0, false],
      [1, true],
    ]);
    const rows = new Map([
      [1, { defaultChecked: false }],
      [2, { defaultChecked: false }],
    ]);
    expect([...effectiveChecked(entries, touched, rows)]).toEqual([1, 3]);
  });

  it('lookupIndicesOf garde les fichiers cochés à compléter', () => {
    const rows = [
      { index: 0, needsLookup: true },
      { index: 1, needsLookup: true },
      { index: 2, needsLookup: false },
    ];
    expect(lookupIndicesOf(rows, new Set([1, 2]))).toEqual([1]);
  });
});

describe('rangement', () => {
  const rows = [
    { index: 0, status: 'move' },
    { index: 1, status: 'duplicate' },
    { index: 2, status: 'inplace' },
    { index: 3, status: 'move' },
  ];

  it('applyOperations retient déplacements et doublons cochés', () => {
    expect(applyOperations(rows, new Set([0, 1, 2])).map(r => r.index)).toEqual([0, 1]);
  });

  it('applyConfirmText décrit le rangement', () => {
    expect(applyConfirmText(rows.slice(0, 2), '/music', true)).toBe(
      'Déplacer 1 fichier(s) dans :\n/music' +
        '\nSupprimer 1 doublon(s) du disque (corbeille de la bibliothèque), dont un autre exemplaire est conservé.' +
        '\nLes dossiers devenus vides seront supprimés.',
    );
    expect(applyConfirmText([rows[0]], '/music', false)).toBe('Déplacer 1 fichier(s) dans :\n/music');
  });
});
