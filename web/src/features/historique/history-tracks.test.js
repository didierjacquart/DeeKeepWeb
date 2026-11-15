import { describe, expect, it } from 'vitest';
import { playerTrackOf, toPendingTracks } from './history-tracks';

describe('playerTrackOf', () => {
  it("construit la piste du lecteur d'une entrée", () => {
    expect(
      playerTrackOf({ path: 'Artiste/Album/01 Titre.mp3', title: 'Titre', artist: 'A', album: 'B', coverSha1: 'abc' }),
    ).toEqual({
      id: 'Artiste/Album/01 Titre.mp3',
      title: 'Titre',
      artist: 'A',
      album: 'B',
      src: '/media/track?path=Artiste%2FAlbum%2F01%20Titre.mp3',
      cover: '/covers/abc',
    });
  });

  it('prend le nom du fichier faute de titre', () => {
    expect(playerTrackOf({ path: 'D:\\m\\x.flac', title: '', coverSha1: null })).toMatchObject({
      title: 'x.flac',
      cover: null,
    });
  });
});

describe('toPendingTracks', () => {
  it('normalise les champs de la fenêtre de recherche', () => {
    expect(
      toPendingTracks([
        {
          id: '12',
          title: 'T',
          artist: 'A',
          album: '',
          duration: '1:02:05',
          artistId: 3,
          albumId: 'x',
          preview: 'https://p',
          cover: 'https://c',
          link: 'https://l',
          trackPosition: '4',
          diskNumber: null,
        },
      ]),
    ).toEqual([
      {
        id: 12,
        title: 'T',
        artist: 'A',
        album: null,
        duration: 3725,
        artistId: 3,
        albumId: null,
        previewUrl: 'https://p',
        coverSmall: 'https://c',
        coverBig: null,
        link: 'https://l',
        trackPosition: 4,
        diskNumber: null,
      },
    ]);
  });

  it('préfère previewUrl et coverSmall, accepte une durée numérique', () => {
    const [t] = toPendingTracks([
      { id: 5, duration: '185', previewUrl: 'p1', preview: 'p2', coverSmall: 's', coverUrl: 'u', cover: 'c' },
    ]);
    expect(t).toMatchObject({ title: '', artist: '', duration: 185, previewUrl: 'p1', coverSmall: 's' });
    expect(toPendingTracks([{ id: 6, coverUrl: 'u' }])[0]).toMatchObject({ duration: 0, coverSmall: 'u' });
  });

  it('écarte les pistes sans identifiant Deezer', () => {
    expect(toPendingTracks([{ id: null, title: 'x' }, { id: 'abc' }, { id: 0 }])).toEqual([]);
  });
});
