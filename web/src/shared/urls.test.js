import { describe, expect, it } from 'vitest';
import { archiveMediaUrl, coverUrl, imageUrl, mediaUrl, previewUrl } from './urls';

describe('URL du serveur', () => {
  it('fait passer les images externes par le cache /img', () => {
    expect(imageUrl('https://cdn.deezer.com/a b.jpg')).toBe('/img?u=https%3A%2F%2Fcdn.deezer.com%2Fa%20b.jpg');
  });

  it('laisse telles quelles les images déjà servies par le serveur', () => {
    expect(imageUrl('/covers/abc')).toBe('/covers/abc');
    expect(imageUrl(null)).toBeNull();
    expect(imageUrl('')).toBeNull();
  });

  it('encode le chemin en base des flux audio', () => {
    expect(mediaUrl('D:\\Pop\\Titre & Co.mp3')).toBe('/media/track?path=D%3A%5CPop%5CTitre%20%26%20Co.mp3');
    expect(archiveMediaUrl('D:\\a.mp3')).toBe('/archive-media/track?path=D%3A%5Ca.mp3');
  });

  it('construit jaquettes et extraits', () => {
    expect(coverUrl('f00d')).toBe('/covers/f00d');
    expect(coverUrl(null)).toBeNull();
    expect(previewUrl(3135556)).toBe('/preview/3135556');
  });
});
