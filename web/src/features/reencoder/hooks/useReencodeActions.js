import { player } from '../../../core/player/player';
import { notify } from '../../../core/ui/notify';
import { openFsBrowser } from '../../../shared/fs-browser/open-fs-browser';
import { reencodeStore } from '../reencode-store';
import { playerTrackOf } from '../reencode-labels';

const errorText = e => (e instanceof Error ? e.message : String(e));

/**
 * Actions de la page Réencoder. La liste est refusée pendant les conversions ;
 * « cocher » et « Convertir » ne portent que sur les lignes visibles.
 * @param {{ visible: object[], busy: boolean, scanning: boolean }} context
 */
export function useReencodeActions({ visible, busy, scanning }) {
  const toggle = i => {
    reencodeStore.updateItems(l => l.map(x => (x.path === i.path ? { ...x, selected: !x.selected } : x)));
  };

  /** « Tout cocher / décocher » : seulement les lignes visibles. */
  const selectAll = on => {
    const shown = new Set(visible.map(i => i.path));
    reencodeStore.updateItems(l => l.map(x => (shown.has(x.path) ? { ...x, selected: on } : x)));
  };

  const add = async mode => {
    if (busy) {
      await reencodeStore.info('Conversion en cours', "Attendez la fin des conversions avant d'ajouter des fichiers.");
      return;
    }
    if (scanning) {
      await reencodeStore.info('Analyse en cours', 'Une analyse de fichiers est déjà en cours.');
      return;
    }
    const paths = await openFsBrowser({ mode, audioOnly: true });
    if (!paths?.length) return;
    try {
      await reencodeStore.scan(paths);
    } catch (e) {
      notify.error(errorText(e));
    }
  };

  const removeSelection = async () => {
    if (busy) {
      await reencodeStore.info('Conversion en cours', 'Attendez la fin des conversions avant de retirer des fichiers.');
      return;
    }
    reencodeStore.updateItems(l => l.filter(i => !i.selected));
  };

  const purge = async () => {
    if (busy) {
      await reencodeStore.info('Conversion en cours', 'Attendez la fin des conversions avant de purger.');
      return;
    }
    const n = reencodeStore.state.items.filter(i => i.converted).length;
    reencodeStore.updateItems(l => l.filter(i => !i.converted));
    notify.info(n ? `${n} fichier(s) converti(s) retiré(s) de la liste.` : 'Aucun fichier converti à purger.');
  };

  const convert = async () => {
    if (busy) {
      await reencodeStore.info('Conversion en cours', 'Des conversions sont déjà en cours.');
      return;
    }
    const chosen = visible.filter(i => i.selected);
    if (!chosen.length) {
      await reencodeStore.info('Aucune sélection', 'Sélectionnez au moins un fichier à réencoder (visible).');
      return;
    }
    try {
      await reencodeStore.convert(chosen);
    } catch (e) {
      notify.error(errorText(e));
    }
  };

  /** La liste affichée devient la file du lecteur, départ sur la ligne choisie. */
  const listen = i => player.play(playerTrackOf(i), visible.map(playerTrackOf));

  return { toggle, selectAll, add, removeSelection, purge, convert, listen };
}
