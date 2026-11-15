import { describe, expect, it } from 'vitest';
import { baseName, bestCopyIndex, dirName, fileQuality, humanSize, inArtistFolder, pathKey } from './organize-rules';

const MB = 1024 * 1024;

describe('inArtistFolder', () => {
  it('reconnaît un dossier parent au nom de l’artiste, casse et ponctuation ignorées', () => {
    expect(inArtistFolder('D:\\Music\\AC-DC\\x.mp3', 'AC/DC')).toBe(true);
    expect(inArtistFolder('Music/inna/Hot/x.mp3', 'Inna')).toBe(true);
  });

  it('ignore le nom du fichier lui-même et un artiste vide', () => {
    expect(inArtistFolder('Music/Inna', 'Inna')).toBe(false);
    expect(inArtistFolder('Music/x.mp3', '')).toBe(false);
  });
});

describe('bestCopyIndex', () => {
  it('rend -1 pour une liste vide', () => {
    expect(bestCopyIndex([])).toBe(-1);
  });

  it('préfère le meilleur débit', () => {
    const items = [
      { path: 'a/x.mp3', bitrate: 128, fileSize: 5 * MB },
      { path: 'b/x.mp3', bitrate: 320, fileSize: 5 * MB },
    ];
    expect(bestCopyIndex(items)).toBe(1);
  });

  it('écarte un exemplaire plus petit de plus de 3 %, même au format cible', () => {
    const items = [
      { path: 'a/x.mp3', bitrate: 320, fileSize: 5 * MB },
      { path: 'b/x.flac', bitrate: 320, fileSize: 4 * MB },
    ];
    expect(bestCopyIndex(items, 'FLAC')).toBe(0);
  });

  it('préfère ensuite le format cible des Paramètres', () => {
    const items = [
      { path: 'a/x.mp3', bitrate: 320, fileSize: 5 * MB },
      { path: 'b/x.flac', bitrate: 320, fileSize: 5 * MB },
    ];
    expect(bestCopyIndex(items, '.flac')).toBe(1);
  });

  it('préfère le dossier de l’artiste, puis hors « Divers », puis le moins enfoui', () => {
    expect(
      bestCopyIndex([
        { path: 'Pop/x.mp3', artist: 'Inna' },
        { path: 'Music/Inna/x.mp3', artist: 'Inna' },
      ]),
    ).toBe(1);
    expect(bestCopyIndex([{ path: 'Divers/x.mp3' }, { path: 'Pop/x.mp3' }])).toBe(1);
    expect(bestCopyIndex([{ path: 'a/b/c/x.mp3' }, { path: 'a/x.mp3' }])).toBe(1);
  });

  it('garde le premier en cas d’égalité totale', () => {
    expect(bestCopyIndex([{ path: 'a/x.mp3' }, { path: 'a/x.mp3' }])).toBe(0);
  });
});

describe('humanSize', () => {
  it('écrit en unités binaires avec une virgule décimale', () => {
    expect(humanSize(500)).toBe('500 o');
    expect(humanSize(1536)).toBe('1,5 Ko');
    expect(humanSize(8.4 * MB)).toBe('8,4 Mo');
    expect(humanSize(null)).toBe('0 o');
    expect(humanSize(5 * 1024 * 1024 * MB)).toBe('5120,0 Go');
  });
});

describe('fileQuality', () => {
  it('assemble format, débit et taille', () => {
    expect(fileQuality('a/x.mp3', null, 320, 8.4 * MB)).toBe('MP3 · 320 kbps · 8,4 Mo');
    expect(fileQuality('a/x', '.flac', 0, 1024)).toBe('FLAC · 1,0 Ko');
    expect(fileQuality('a/noext', null, null, 10)).toBe('? · 10 o');
  });

  it('signale un fichier introuvable quand la taille est inconnue', () => {
    expect(fileQuality('a/x.mp3', null, 320, null)).toBe('fichier introuvable');
  });
});

describe('chemins', () => {
  it('pathKey ignore la casse et les séparateurs', () => {
    expect(pathKey('C:/Music/A.mp3')).toBe('c:\\music\\a.mp3');
    expect(pathKey('c:\\music\\a.MP3')).toBe(pathKey('C:/Music/A.mp3'));
    expect(pathKey(null)).toBe('');
  });

  it('baseName et dirName acceptent les deux séparateurs', () => {
    expect(baseName('a\\b/c.mp3')).toBe('c.mp3');
    expect(baseName(null)).toBe('');
    expect(dirName('a/b\\c.mp3')).toBe('a/b');
    expect(dirName('c.mp3')).toBe('');
  });
});
