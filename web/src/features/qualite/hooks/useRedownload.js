import { notify } from '../../../core/ui/notify';
import { confirm } from '../../../shared/dialog/confirm';
import { redownloadQuality } from '../quality-api';
import { redownloadMessage, redownloadPlan } from '../quality-summary';

/** « Retélécharger la sélection » : réextrait les titres cochés, qui sont ensuite réanalysés par le serveur. */
export function useRedownload({ list, selection, downloading }) {
  return async () => {
    if (downloading) {
      notify.info('Des retéléchargements sont déjà en cours.');
      return;
    }
    const { chosen, targets, skipped } = redownloadPlan(list, selection.selected);
    if (chosen === 0) {
      notify.info('Cochez au moins un titre à retélécharger.');
      return;
    }
    if (targets.length === 0) {
      notify.error(
        "Les titres cochés n'ont ni artiste ni titre dans leurs tags : la recherche n'aurait rien à chercher, et le fichier trouvé remplacerait l'existant à l'aveugle. Renseignez d'abord les tags — l'écran « Organiser » sait les retrouver à partir du signal.",
      );
      return;
    }
    const ok = await confirm({
      title: 'Retélécharger',
      message: redownloadMessage(targets.length, skipped),
      confirm: 'Continuer',
      cancel: 'Non',
    });
    if (!ok) return;
    const paths = targets.map(t => t.path);
    const r = await redownloadQuality(paths);
    if (r.error) notify.error(r.error);
    else selection.remove(paths);
  };
}
