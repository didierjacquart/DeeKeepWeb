import { openDialog } from '../../../shared/dialog/dialog-stack';
import { FileDedupDialog } from './FileDedupDialog';

/**
 * Ouvre l'écran fichier/fichier ; rend les chemins supprimés (ou undefined si fermé sans suppression).
 * @param {{ result: any, sessionId: string }} data résultat de la tâche organize.duplicates et session d'analyse.
 * @returns {Promise<string[] | undefined>}
 */
export function openFileDedupDialog(data) {
  return openDialog(close => <FileDedupDialog {...data} close={close} />, {
    width: 'min(1200px, 96vw)',
    maxWidth: '96vw',
    autoFocus: false,
  });
}
