import { api } from '../../../core/api/api';
import { notify } from '../../../core/ui/notify';
import { confirm } from '../../../shared/dialog/confirm';
import { DELETE_TRACKS } from './trash-api';

/** Message de confirmation : un titre nommé, ou un nombre. */
export function deleteMessage(tracks) {
  const what = tracks.length === 1 ? `« ${tracks[0].title || tracks[0].path} »` : `ces ${tracks.length} titres`;
  return (
    `Supprimer ${what} de votre bibliothèque ?\n` +
    "Si d'autres utilisateurs l'ont aussi, il est seulement retiré de la vôtre. Sinon, le fichier part dans la " +
    'corbeille, de la bibliothèque et du disque USB. ' +
    'Il reste récupérable depuis le bouton « Corbeille », le temps réglé dans les Paramètres.'
  );
}

/**
 * Supprime des titres de la bibliothèque après confirmation. Seuls les titres présents sur le
 * disque sont concernés ; la liste se met à jour par l'événement « tracks.removed ».
 * @param {object[]} tracks titres de la bibliothèque (`path`, `title`, `onDisk`)
 * @returns {Promise<boolean>} vrai si quelque chose a été supprimé
 */
export async function deleteTracks(tracks) {
  const onDisk = tracks.filter(t => t.onDisk);
  if (onDisk.length === 0) {
    notify.info("Aucun de ces titres n'est sur le disque : il n'y a rien à supprimer.");
    return false;
  }
  const ok = await confirm({
    title: onDisk.length === 1 ? 'Supprimer le titre' : 'Supprimer les titres',
    message: deleteMessage(onDisk),
    confirm: 'Mettre à la corbeille',
    danger: true,
  });
  if (!ok) return false;
  const r = await api.mutate(
    DELETE_TRACKS,
    { paths: onDisk.map(t => t.path) },
    { label: `Supprimer ${onDisk.length} titre(s)`, queueable: false },
  );
  if (r.error) {
    notify.error(r.error);
    return false;
  }
  const result = r.data?.deleteFiles;
  const deleted = result?.deleted?.length ?? 0;
  const unlinked = result?.unlinked?.length ?? 0;
  if (result?.failed?.length)
    notify.error(`Échec pour ${result.failed.length} fichier(s) : ${result.errors.join(' ; ')}`);
  if (deleted) notify.success(`${deleted} titre(s) mis à la corbeille.`);
  if (unlinked)
    notify.info(
      `${unlinked} titre(s) retiré(s) de votre bibliothèque : d'autres utilisateurs l'ont, le fichier est conservé.`,
    );
  return deleted + unlinked > 0;
}
