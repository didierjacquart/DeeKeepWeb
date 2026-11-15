/** Commandes de l'écran verrouillé et du casque (Media Session), si le navigateur les gère. */
const supported = () => typeof navigator !== 'undefined' && 'mediaSession' in navigator;

/** Titre, artiste, album et jaquette affichés par le système. */
export function showOnLockScreen(track) {
  if (!supported()) return;
  navigator.mediaSession.metadata = new MediaMetadata({
    title: track.title,
    artist: track.artist ?? '',
    album: track.album ?? '',
    artwork: track.cover ? [{ src: track.cover, sizes: '500x500' }] : [],
  });
}

/**
 * Relie les touches multimédia au lecteur.
 * @param {{ play: () => void, pause: () => void, previous: () => void, next: () => void, seekBy: (s: number) => void, seek: (s: number) => void }} player
 */
export function bindMediaKeys(player) {
  if (!supported()) return;
  const ms = navigator.mediaSession;
  ms.setActionHandler('play', () => player.play());
  ms.setActionHandler('pause', () => player.pause());
  ms.setActionHandler('previoustrack', () => player.previous());
  ms.setActionHandler('nexttrack', () => player.next());
  ms.setActionHandler('seekbackward', () => player.seekBy(-10));
  ms.setActionHandler('seekforward', () => player.seekBy(10));
  ms.setActionHandler('seekto', d => d.seekTime != null && player.seek(d.seekTime));
}
