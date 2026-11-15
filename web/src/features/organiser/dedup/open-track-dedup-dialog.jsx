import { openDialog } from '../../../shared/dialog/dialog-stack';
import { TrackDedupDialog } from './TrackDedupDialog';

/**
 * Ouvre l'écran « Doublons de la bibliothèque » et supprime (corbeille) les
 * fichiers cochés. Renvoie les chemins effectivement supprimés, ou undefined
 * si l'écran est fermé sans suppression.
 * @returns {Promise<string[] | undefined>}
 */
export function openTrackDedupDialog(items) {
  return openDialog(close => <TrackDedupDialog items={items} close={close} />, {
    width: 'min(1200px, 96vw)',
    maxWidth: '96vw',
    autoFocus: false,
  });
}
