/**
 * Fenêtre de la file affichée : 10 titres avant, 150 après le titre courant.
 * @returns {{ track: import('./player').PlayerTrack, at: number }[]}
 */
export function queueWindow(queue, index) {
  const from = Math.max(0, index - 10);
  return queue.slice(from, from + 160).map((track, k) => ({ track, at: from + k }));
}
