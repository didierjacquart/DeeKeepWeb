import { player } from '../../core/player/player';
import { notify } from '../../core/ui/notify';
import { reanalyzeTrack } from './quality-api';
import { toTrack } from './quality-model';

/** Écoute dans le lecteur de DeeKeep : la liste affichée devient la file. */
export function playRow(row, list) {
  const queue = list.map(toTrack);
  player.play(toTrack(row), queue);
}

export async function copyPath(row) {
  try {
    await navigator.clipboard.writeText(row.path);
    notify.success('Chemin copié.');
  } catch {
    notify.error('Copie impossible : ' + row.path);
  }
}

export async function reanalyze(row) {
  const r = await reanalyzeTrack(row.path);
  if (r.error) notify.error(r.error);
}
