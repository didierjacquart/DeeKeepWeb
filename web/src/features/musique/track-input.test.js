import { describe, expect, it } from 'vitest';
import { toInput } from './track-input';

describe('toInput', () => {
  it('piste complète de la recherche', () => {
    expect(
      toInput({
        id: 3135556,
        title: 'Harder',
        artist: 'Daft Punk',
        album: 'Discovery',
        duration: 224,
        artistId: 27,
        albumId: 302127,
        previewUrl: 'https://p/1.mp3',
        coverSmall: 'https://c/s.jpg',
        coverBig: 'https://c/b.jpg',
        link: 'https://deezer/t/1',
        trackPosition: 4,
        diskNumber: 1,
      }),
    ).toEqual({
      id: 3135556,
      title: 'Harder',
      artist: 'Daft Punk',
      album: 'Discovery',
      duration: 224,
      artistId: 27,
      albumId: 302127,
      previewUrl: 'https://p/1.mp3',
      coverSmall: 'https://c/s.jpg',
      coverBig: 'https://c/b.jpg',
      link: 'https://deezer/t/1',
      trackPosition: 4,
      diskNumber: 1,
    });
  });

  it('valeurs absentes ou vides : nulles, sauf identifiant, titre, artiste et durée', () => {
    expect(toInput({})).toEqual({
      id: 0,
      title: '',
      artist: '',
      album: null,
      duration: 0,
      artistId: null,
      albumId: null,
      previewUrl: null,
      coverSmall: null,
      coverBig: null,
      link: null,
      trackPosition: null,
      diskNumber: null,
    });
    expect(toInput({ album: '', artistId: 'abc' })).toMatchObject({ album: null, artistId: null });
  });

  it('nombres en texte convertis', () => {
    expect(toInput({ id: '42', duration: '185', diskNumber: '2' })).toMatchObject({
      id: 42,
      duration: 185,
      diskNumber: 2,
    });
  });

  it('durées « m:ss » et « h:mm:ss »', () => {
    expect(toInput({ duration: '3:25' }).duration).toBe(205);
    expect(toInput({ duration: '1:02:03' }).duration).toBe(3723);
  });

  it('variantes de noms de champs (extrait, pochette)', () => {
    expect(toInput({ preview: 'p.mp3', coverUrl: 'c.jpg' })).toMatchObject({
      previewUrl: 'p.mp3',
      coverSmall: 'c.jpg',
    });
    expect(toInput({ cover: 'k.jpg' }).coverSmall).toBe('k.jpg');
  });
});
