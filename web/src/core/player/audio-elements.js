/**
 * Élément de lecture principal, relié aux rappels du lecteur.
 * @param {number} gain volume appliqué (carré du volume perçu)
 * @param {{
 *   onPlay: () => void, onPause: () => void, onTime: (s: number) => void, onDuration: (s: number) => void,
 *   onBuffering: (b: boolean) => void, onEnded: () => void, onError: () => void,
 * }} on
 */
export function createPlaybackAudio(gain, on) {
  const audio = new Audio();
  audio.preload = 'auto';
  audio.volume = gain;
  audio.addEventListener('play', on.onPlay);
  audio.addEventListener('pause', on.onPause);
  audio.addEventListener('timeupdate', () => on.onTime(audio.currentTime));
  audio.addEventListener('durationchange', () => Number.isFinite(audio.duration) && on.onDuration(audio.duration));
  audio.addEventListener('waiting', () => on.onBuffering(true));
  audio.addEventListener('playing', () => on.onBuffering(false));
  audio.addEventListener('ended', on.onEnded);
  audio.addEventListener('error', on.onError);
  return audio;
}

/**
 * Élément des extraits de 30 s : indépendant du lecteur (« un seul son à la fois » est géré par le lecteur).
 * @param {{ onPlay: () => void, onStop: () => void }} on fin ou erreur de l'extrait = arrêt
 */
export function createPreviewAudio(on) {
  const audio = new Audio();
  audio.addEventListener('play', on.onPlay);
  audio.addEventListener('ended', on.onStop);
  audio.addEventListener('error', on.onStop);
  return audio;
}

/**
 * Message d'échec de lecture : fichier introuvable, archive injoignable, ou titre absent
 * de l'appareil hors ligne.
 * @param {boolean} online
 * @param {string} [src] flux demandé (un titre lu depuis l'archive a son propre message)
 */
export function playbackErrorMessage(online, src) {
  if (!online) return "Hors ligne : ce titre n'est pas disponible sur l'appareil.";
  if (src?.startsWith('/archive-media/'))
    return "Lecture impossible : titre absent du disque et archive injoignable (emplacement de l'archive à vérifier).";
  return 'Lecture impossible : fichier introuvable ou illisible.';
}
