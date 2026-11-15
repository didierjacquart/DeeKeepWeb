/**
 * Jaquettes à essayer pour un titre, dans l'ordre : celle intégrée au fichier (marquée de
 * l'icône DeeKeep quand le titre est passé par l'application, comme dans le lecteur de
 * l'application desktop), puis celle connue de la base ou de Deezer.
 * @param {{ src?: string, cover?: string | null } | null} track
 * @returns {string[]}
 */
export function coverSources(track) {
  if (!track) return [];
  const list = [];
  if (track.src?.startsWith('/media/track?')) list.push('/media/cover?' + track.src.slice('/media/track?'.length));
  if (track.cover && !list.includes(track.cover)) list.push(track.cover);
  return list;
}
