import { imageUrl, previewUrl } from '../../shared/urls';

/** Identifiant de lecteur de l'extrait Deezer d'une piste. */
export function previewId(track) {
  return `deezer:${track.id}`;
}

/** Extrait Deezer de 30 s d'une piste, pour `player.togglePreview`. */
export function deezerPreview(track) {
  return {
    id: previewId(track),
    title: track.title,
    artist: track.artist,
    album: track.album,
    src: previewUrl(track.id),
    cover: imageUrl(track.coverSmall),
    duration: 30,
  };
}
