import { currentTrack, player } from '../../core/player/player';
import { imageUrl, mediaUrl } from '../../shared/urls';
import { deezerPreview, previewId } from './deezer-preview';

/** Fichier extrait → piste du lecteur complet. */
export function playerTrack(r) {
  return {
    id: r.dbPath,
    title: r.track.title,
    artist: r.track.artist,
    album: r.track.album,
    src: mediaUrl(r.dbPath),
    cover: imageUrl(r.track.coverSmall),
    duration: r.track.duration,
  };
}

export function canListen(r) {
  return !!r.dbPath || !!r.track.previewUrl || !!r.track.id;
}

/** Le fichier extrait (lecteur complet) ou l'extrait de la ligne est en cours de lecture. */
export function isPlaying(r, playerState) {
  const id = r.dbPath ?? previewId(r.track);
  return r.dbPath
    ? currentTrack(playerState)?.id === id && playerState.playing
    : playerState.preview?.id === id && playerState.previewPlaying;
}

export function listenTip(r) {
  if (r.dbPath) return 'Écouter le fichier extrait';
  if (r.track.previewUrl || r.track.id) return "Écouter l'extrait de 30 secondes (le titre n'est pas encore extrait)";
  return "Titre pas encore extrait, et Deezer n'en donne aucun extrait";
}

/**
 * Fichier extrait → lecteur complet (la liste des fichiers extraits devient la file) ; sinon extrait de 30 s.
 * @param {object} r ligne écoutée
 * @param {object[]} shown lignes affichées, dans l'ordre
 * @param {boolean} [fromButton] bouton ▶ (un double-clic n'écoute l'extrait que si Deezer en annonce un)
 */
export function listenRow(r, shown, fromButton = false) {
  if (r.dbPath) {
    const queue = shown.filter(x => x.dbPath).map(x => playerTrack(x));
    player.play(playerTrack(r), queue);
  } else if (r.track.id && (fromButton || r.track.previewUrl)) {
    player.togglePreview(deezerPreview(r.track));
  }
}
