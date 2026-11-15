import { notify } from '../../core/ui/notify';
import { player } from '../../core/player/player';
import { offlineAudio } from '../../core/player/offline-audio';
import { confirm } from '../../shared/dialog/confirm';
import { mediaUrl } from '../../shared/urls';
import { archiveActions } from './archive-panel/archive-actions';
import { asPlayerTrack, playable } from './track-info';

/**
 * La liste affichée devient la file ; les lignes sans source sont sautées. La fenêtre du
 * lecteur s'ouvre d'office (on peut la refermer, la lecture continue dans la barre).
 */
export function playTrack(t, displayed) {
  if (!playable(t)) {
    notify.info("Ce titre n'est ni sur le disque ni dans l'archive : il n'y a rien à écouter.");
    return;
  }
  player.play(
    asPlayerTrack(t),
    displayed
      .map(r => r.track)
      .filter(x => playable(x))
      .map(x => asPlayerTrack(x)),
    { open: true },
  );
}

/**
 * Écoute d'un album ou d'un artiste entier, depuis son premier titre.
 * @param {object[]} tracks Titres du groupe, déjà écoutables et dans l'ordre de lecture.
 * @param {string} what Ce qui est écouté (« l’album « X » »), pour le message s'il est vide.
 */
export function playGroup(tracks, what) {
  if (!tracks.length) {
    notify.info(`Rien à écouter pour ${what} : aucun titre sur le disque ni dans l'archive.`);
    return;
  }
  const queue = tracks.map(x => asPlayerTrack(x));
  player.play(queue[0], queue, { open: true });
}

export async function togglePin(t) {
  const src = mediaUrl(t.path);
  if (offlineAudio.isPinned(src)) {
    await offlineAudio.unpin(src);
    notify.info('Titre retiré de l’écoute hors ligne.');
  } else {
    const ok = await offlineAudio.pin(src);
    if (ok) notify.success('Titre épinglé : il restera écoutable sans connexion.');
    else notify.error('Épinglage impossible (titre absent du disque ou stockage indisponible).');
  }
}

export async function copyPath(t) {
  try {
    await navigator.clipboard.writeText(t.path);
    notify.info('Chemin copié.');
  } catch {
    notify.error('Copie impossible dans ce navigateur.');
  }
}

/** « Retirer de l'archive » demande confirmation (l'original ne le faisait pas : défaut D5). */
export async function removeFromArchive(t) {
  const ok = await confirm({
    title: "Retirer de l'archive",
    message: `Retirer « ${t.title || t.path} » de l'archive ?\nLe fichier du disque n'est pas touché.`,
    confirm: 'Retirer',
    danger: true,
  });
  if (ok) await archiveActions.remove([t.path]);
}
