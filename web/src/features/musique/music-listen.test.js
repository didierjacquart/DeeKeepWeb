import { describe, expect, it } from 'vitest';
import { canListen, isPlaying, listenTip, playerTrack } from './music-listen';
import { deezerPreview } from './deezer-preview';

const extracted = { dbPath: 'D:/m/a.mp3', track: { id: 7, title: 'A', artist: 'X', album: 'Y', duration: 90 } };
const withPreview = { dbPath: null, track: { id: 7, previewUrl: 'p', title: 'A' } };
const nothing = { dbPath: null, track: { id: 0, previewUrl: null, title: 'A' } };

describe('écoute des lignes', () => {
  it('écoutable si extrait, ou si Deezer peut donner un extrait', () => {
    expect(canListen(extracted)).toBe(true);
    expect(canListen(withPreview)).toBe(true);
    expect(canListen(nothing)).toBe(false);
  });

  it('infobulles', () => {
    expect(listenTip(extracted)).toBe('Écouter le fichier extrait');
    expect(listenTip(withPreview)).toBe("Écouter l'extrait de 30 secondes (le titre n'est pas encore extrait)");
    expect(listenTip(nothing)).toBe("Titre pas encore extrait, et Deezer n'en donne aucun extrait");
  });

  it('piste du lecteur pour un fichier extrait', () => {
    expect(playerTrack(extracted)).toEqual({
      id: 'D:/m/a.mp3',
      title: 'A',
      artist: 'X',
      album: 'Y',
      src: '/media/track?path=D%3A%2Fm%2Fa.mp3',
      cover: null,
      duration: 90,
    });
  });

  it('extrait Deezer de 30 s', () => {
    expect(deezerPreview({ id: 7, title: 'A', artist: 'X', album: null, coverSmall: 'https://c/s.jpg' })).toEqual({
      id: 'deezer:7',
      title: 'A',
      artist: 'X',
      album: null,
      src: '/preview/7',
      cover: '/img?u=https%3A%2F%2Fc%2Fs.jpg',
      duration: 30,
    });
  });

  it('en lecture : lecteur complet pour un fichier, extrait sinon', () => {
    const idle = { queue: [], index: 0, playing: false, preview: null, previewPlaying: false };
    expect(isPlaying(extracted, { ...idle, queue: [{ id: 'D:/m/a.mp3' }], playing: true })).toBe(true);
    expect(isPlaying(extracted, { ...idle, queue: [{ id: 'D:/m/a.mp3' }] })).toBe(false);
    expect(isPlaying(withPreview, { ...idle, preview: { id: 'deezer:7' }, previewPlaying: true })).toBe(true);
    expect(isPlaying(withPreview, { ...idle, preview: { id: 'deezer:8' }, previewPlaying: true })).toBe(false);
  });
});
