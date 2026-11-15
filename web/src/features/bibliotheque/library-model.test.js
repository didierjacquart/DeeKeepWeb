import { describe, expect, it } from 'vitest';
import {
  MAX_SUGGESTIONS,
  basename,
  buildValueIndex,
  canonical,
  completion,
  completionFor,
  fieldValue,
  filterRows,
  nextSort,
  selectable,
  sortMark,
  sortRows,
  highlightParts,
  suggestionText,
  suggestionsFor,
  toRow,
} from './library-model';

const track = (path, fields = {}) => ({ path, onDisk: true, archiveState: 'none', ...fields });

describe('canonical', () => {
  it.each([
    ['Beyoncé & Jay-Z', 'beyonce and jay z'],
    ['A$AP Rocky', 'asap rocky'],
    ['Straße', 'strasse'],
    ['01 - Song (Copy)', 'song'],
    ['Track [2]', 'track'],
    ['  Hello,   World!  ', 'hello world'],
    [null, ''],
    [undefined, ''],
  ])('%s → %s', (input, expected) => {
    expect(canonical(input)).toBe(expected);
  });
});

describe('basename', () => {
  it('garde le dernier élément, quel que soit le séparateur', () => {
    expect(basename('/music/a/b.flac')).toBe('b.flac');
    expect(basename('C:\\music\\c.mp3')).toBe('c.mp3');
    expect(basename('seul.mp3')).toBe('seul.mp3');
  });
});

describe('toRow', () => {
  it('ne cherche que dans le titre, l’artiste et l’album, en forme canonique', () => {
    const r = toRow(
      track('/m/01 - Été.flac', {
        artist: 'Zaz',
        album: 'Recto',
        title: 'Été',
        genre: 'Pop',
        year: 2010,
        archiveState: 'stale',
      }),
    );
    expect(r.hay).toBe('zaz recto ete');
    expect(r.cloud).toBe(1);
  });

  it('prend le nom du fichier pour un titre sans tag, et l’artiste de l’album', () => {
    expect(toRow(track('/m/Inconnu - 03 Piste.mp3', { albumArtist: 'Divers' })).hay).toBe('divers inconnu 03 piste');
    expect(fieldValue(track('/m/x.flac'), 'title')).toBe('x');
    expect(fieldValue(track('/m/x.flac'), 'album')).toBe('');
  });

  it('classe la colonne NUAGE : ok, à renvoyer, absent', () => {
    expect(toRow(track('/a', { archiveState: 'ok' })).cloud).toBe(0);
    expect(toRow(track('/a', { archiveState: 'none' })).cloud).toBe(2);
  });
});

describe('selectable', () => {
  it('retient les titres présents, ou absents si demandé, ou archivés à jour', () => {
    expect(selectable(track('/a'), false)).toBe(true);
    expect(selectable(track('/a', { onDisk: false }), false)).toBe(false);
    expect(selectable(track('/a', { onDisk: false }), true)).toBe(true);
    expect(selectable(track('/a', { onDisk: false, archiveState: 'ok' }), false)).toBe(true);
  });
});

const ROWS = [
  track('/1', { artist: 'Daft Punk', album: 'Discovery', title: 'One More Time', year: 2001 }),
  track('/2', { artist: 'Daft Punk', album: 'Homework', title: 'Da Funk', year: 1997 }),
  track('/3', { artist: 'Justice', album: 'Cross', title: 'Daft Punk Is Playing', year: 2007 }),
  track('/4', { artist: 'Absent', album: 'Daft', title: 'Gone', onDisk: false }),
].map(toRow);

describe('buildValueIndex', () => {
  it('compte chaque valeur une fois par titre, sous sa nature la plus prioritaire', () => {
    const index = buildValueIndex(ROWS, false);
    const daft = index.find(v => v.key === 'daft punk');
    expect(daft).toMatchObject({ label: 'Artiste', value: 'Daft Punk', count: 2, priority: 0 });
    expect(index[0]).toBe(daft);
  });

  it('écarte les titres non sélectionnables, sauf case « absents »', () => {
    expect(buildValueIndex(ROWS, false).some(v => v.key === 'absent')).toBe(false);
    expect(buildValueIndex(ROWS, true).some(v => v.key === 'absent')).toBe(true);
  });
});

describe('suggestionsFor', () => {
  const index = buildValueIndex(ROWS, true);

  it("place d'abord les valeurs qui commencent par la frappe", () => {
    const list = suggestionsFor(index, 'daf');
    expect(list.map(s => s.key)).toEqual(['daft punk', 'daft', 'daft punk is playing']);
  });

  it('propose aussi les valeurs qui contiennent la frappe', () => {
    expect(suggestionsFor(index, 'work').map(s => s.value)).toEqual(['Homework']);
  });

  it('place les valeurs dont un mot commence par la frappe avant celles qui la contiennent', () => {
    const values = [
      { key: 'xpunkx', value: 'Xpunkx', label: 'Titre', count: 9 },
      { key: 'daft punk', value: 'Daft Punk', label: 'Artiste', count: 2 },
    ];
    expect(suggestionsFor(values, 'punk').map(s => s.value)).toEqual(['Daft Punk', 'Xpunkx']);
  });

  it('ne propose ni genre, ni année, ni format', () => {
    const rows = [track('/a', { artist: 'A', genre: 'Eurodance', year: 1993, format: 'MP3' })].map(toRow);
    expect(suggestionsFor(buildValueIndex(rows, false), 'euro')).toEqual([]);
    expect(suggestionsFor(buildValueIndex(rows, false), '1993')).toEqual([]);
  });

  it('rend au plus huit propositions, rien pour une frappe vide', () => {
    const many = Array.from({ length: 9 }, (_, i) => ({
      key: `abc ${i}`,
      value: `abc ${i}`,
      label: 'Titre',
      count: 1,
    }));
    expect(suggestionsFor(many, 'abc')).toHaveLength(MAX_SUGGESTIONS);
    expect(suggestionsFor(index, '  ')).toEqual([]);
  });
});

describe('saisie prédictive', () => {
  it('prolonge la frappe, sans tenir compte de la casse ni des accents', () => {
    expect(completion('Daft Punk', 'daf')).toBe('t Punk');
    expect(completion('Été indien', 'ete')).toBe(' indien');
    expect(completion('Daft Punk', 'Daft Punk')).toBeNull();
    expect(completion('Daft Punk', 'punk')).toBeNull();
    expect(completion('Daft Punk', ' daf')).toBeNull();
    expect(completion('Daft Punk', '')).toBeNull();
  });

  it('suit la proposition active, sinon la première qui prolonge la frappe', () => {
    const list = [
      { value: 'Homework', label: 'Album' },
      { value: 'Daft Punk', label: 'Artiste' },
      { value: 'Daft Punk Is Playing', label: 'Titre' },
    ];
    expect(completionFor(list, 'daf')).toEqual({ suggestion: list[1], rest: 't Punk' });
    expect(completionFor(list, 'daf', 2)).toEqual({ suggestion: list[2], rest: 't Punk Is Playing' });
    expect(completionFor(list, 'daf', 0)).toBeNull();
    expect(completionFor([], 'daf')).toBeNull();
  });

  it('découpe la valeur autour de la frappe pour la surligner', () => {
    expect(highlightParts('Daft Punk', 'punk')).toEqual(['Daft ', 'Punk', '']);
    expect(highlightParts('Beyoncé', 'ONCE')).toEqual(['Bey', 'oncé', '']);
    expect(highlightParts('Daft Punk', 'zzz')).toEqual(['Daft Punk', '', '']);
    expect(highlightParts('Daft Punk', ' ')).toEqual(['Daft Punk', '', '']);
  });
});

describe('suggestionText', () => {
  it('accorde le nombre de titres', () => {
    expect(suggestionText({ label: 'Artiste', value: 'Daft Punk', count: 2 })).toBe('Artiste · Daft Punk   (2 titres)');
    expect(suggestionText({ label: 'Album', value: 'Cross', count: 1 })).toBe('Album · Cross   (1 titre)');
  });
});

describe('filterRows', () => {
  it('garde les titres qui contiennent tous les mots, dans n’importe quel ordre', () => {
    expect(filterRows(ROWS, 'punk daft', false).map(r => r.track.path)).toEqual(['/1', '/2', '/3']);
    expect(filterRows(ROWS, 'homework DA', false).map(r => r.track.path)).toEqual(['/2']);
    expect(filterRows(ROWS, '', false)).toHaveLength(3);
    expect(filterRows(ROWS, '', true)).toHaveLength(4);
  });
});

describe('sortRows', () => {
  const rows = [
    toRow(track('/b', { title: 'Beta', duration: 30, archiveState: 'none' })),
    toRow(track('/a', { title: 'alpha', duration: 300, archiveState: 'ok' })),
    toRow(track('/c', { title: '', duration: null, archiveState: 'stale' })),
  ];
  const order = (column, desc) => sortRows(rows, column, desc).map(r => r.track.path);

  it('trie le texte en forme canonique, vides en fin', () => {
    expect(order('title', false)).toEqual(['/a', '/b', '/c']);
    expect(order('title', true)).toEqual(['/c', '/b', '/a']);
  });

  it('trie durée et NUAGE comme des nombres', () => {
    expect(order('duration', false)).toEqual(['/c', '/b', '/a']);
    expect(order('cloud', false)).toEqual(['/a', '/c', '/b']);
  });

  it('trie le chemin complet et ne modifie pas la liste reçue', () => {
    expect(order('path', true)).toEqual(['/c', '/b', '/a']);
    expect(rows.map(r => r.track.path)).toEqual(['/b', '/a', '/c']);
  });
});

describe('nextSort / sortMark', () => {
  it('inverse le sens sur la même colonne, repart ascendant sur une autre', () => {
    const s = { column: 'artist', desc: false };
    expect(nextSort(s, 'artist')).toEqual({ column: 'artist', desc: true });
    expect(nextSort({ column: 'artist', desc: true }, 'title')).toEqual({ column: 'title', desc: false });
    expect(nextSort(s, null)).toBe(s);
  });

  it('marque la colonne triée', () => {
    expect(sortMark({ column: 'title', desc: false }, 'title')).toBe(' ▴');
    expect(sortMark({ column: 'title', desc: true }, 'title')).toBe(' ▾');
    expect(sortMark({ column: 'title', desc: true }, 'artist')).toBe('');
  });
});
