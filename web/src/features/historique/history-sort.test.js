import { describe, expect, it } from 'vitest';
import {
  downloadSortMark,
  nextDownloadSort,
  nextSearchSort,
  searchSortMark,
  sortDownloads,
  sortSearches,
} from './history-sort';

describe('recherches', () => {
  const searches = [
    { query: 'Éa', position: 2 },
    { query: 'abba', position: 3 },
    { query: 'eb', position: 1 },
  ];
  const queries = l => l.map(s => s.query);

  it("suivent l'ordre de l'historique sans tri", () => {
    expect(queries(sortSearches(searches, null))).toEqual(['eb', 'Éa', 'abba']);
  });

  it('se trient par requête, accents et casse ignorés', () => {
    expect(queries(sortSearches(searches, 'asc'))).toEqual(['abba', 'Éa', 'eb']);
    expect(queries(sortSearches(searches, 'desc'))).toEqual(['eb', 'Éa', 'abba']);
  });

  it('alternent le sens à chaque clic', () => {
    expect(nextSearchSort(null)).toBe('asc');
    expect(nextSearchSort('asc')).toBe('desc');
    expect(nextSearchSort('desc')).toBe('asc');
    expect([null, 'asc', 'desc'].map(searchSortMark)).toEqual(['', ' ▴', ' ▾']);
  });
});

describe('téléchargements', () => {
  const history = [
    { path: 'D:/b.mp3', title: 'beta', artist: null, position: 1 },
    { path: 'D:/c.mp3', title: 'Alpha', artist: 'Zed', position: 3 },
    { path: 'D:/a.mp3', title: 'gamma', artist: 'abba', position: 2 },
  ];
  const paths = l => l.map(e => e.path);

  it('montrent les plus récents en premier sans tri', () => {
    expect(paths(sortDownloads(history, null))).toEqual(['D:/c.mp3', 'D:/a.mp3', 'D:/b.mp3']);
  });

  it('se trient par colonne ; une valeur absente passe en premier', () => {
    expect(paths(sortDownloads(history, { key: 'title', desc: false }))).toEqual(['D:/c.mp3', 'D:/b.mp3', 'D:/a.mp3']);
    expect(paths(sortDownloads(history, { key: 'title', desc: true }))).toEqual(['D:/a.mp3', 'D:/b.mp3', 'D:/c.mp3']);
    expect(paths(sortDownloads(history, { key: 'artist', desc: false }))).toEqual(['D:/b.mp3', 'D:/a.mp3', 'D:/c.mp3']);
    expect(paths(sortDownloads(history, { key: 'path', desc: false }))).toEqual(['D:/a.mp3', 'D:/b.mp3', 'D:/c.mp3']);
  });

  it('alternent le sens sur la même colonne et repartent en croissant sur une autre', () => {
    expect(nextDownloadSort(null, 'title')).toEqual({ key: 'title', desc: false });
    expect(nextDownloadSort({ key: 'title', desc: false }, 'title')).toEqual({ key: 'title', desc: true });
    expect(nextDownloadSort({ key: 'title', desc: true }, 'album')).toEqual({ key: 'album', desc: false });
  });

  it('marquent la colonne triée', () => {
    expect(downloadSortMark(null, 'title')).toBe('');
    expect(downloadSortMark({ key: 'title', desc: false }, 'title')).toBe(' ▴');
    expect(downloadSortMark({ key: 'title', desc: true }, 'title')).toBe(' ▾');
    expect(downloadSortMark({ key: 'title', desc: true }, 'album')).toBe('');
  });
});
