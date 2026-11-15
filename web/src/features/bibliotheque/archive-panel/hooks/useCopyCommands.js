import { confirm } from '../../../../shared/dialog/confirm';
import { archiveActions } from '../archive-actions';
import { UNCONFIGURED_TEXT } from '../archive-model';

/**
 * Archiver, Restaurer, Arrêter : sur les titres cochés et affichés, sinon (après
 * confirmation) sur tous les titres affichés.
 *
 * @param {{
 *   chosen: string[], visible: string[], busy: boolean,
 *   setLine: (line: { text: string, error: boolean }) => void,
 *   setLastFailure: (text: string | null) => void,
 *   openPanel: () => void,
 * }} p
 */
export function useCopyCommands({ chosen, visible, busy, setLine, setLastFailure, openPanel }) {
  const archive = async () => {
    if (busy) return;
    let paths = chosen;
    if (!paths.length) {
      if (!visible.length) return;
      const ok = await confirm({
        title: 'Archiver',
        message: `Aucun titre coché. Envoyer les ${visible.length} titres affichés ?`,
        confirm: 'Oui',
        cancel: 'Non',
      });
      if (!ok) return;
      paths = visible;
    }
    setLastFailure(null);
    await archiveActions.archive(paths);
  };

  const restore = async () => {
    if (busy) return;
    const paths = chosen.length ? chosen : visible;
    if (!paths.length) return;
    if (!archiveActions.configured) {
      setLine({ text: UNCONFIGURED_TEXT, error: false });
      openPanel();
      return;
    }
    const plan = await archiveActions.plan(paths);
    if (!plan) return;
    if (plan.restorable && !chosen.length) {
      const ok = await confirm({
        title: 'Restaurer',
        message: `Aucun titre coché. Restaurer les ${plan.restorable} titres manquants parmi les ${paths.length} affichés ?`,
        confirm: 'Oui',
        cancel: 'Non',
      });
      if (!ok) return;
    }
    setLastFailure(null);
    await archiveActions.restore(paths, plan);
  };

  const stop = async () => {
    if (busy) setLine({ text: 'Arrêt demandé : le titre en cours est abandonné.', error: false });
    await archiveActions.stop();
  };

  return { archive, restore, stop };
}
