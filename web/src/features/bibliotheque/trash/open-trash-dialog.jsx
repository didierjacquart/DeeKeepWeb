import { openDialog } from '../../../shared/dialog/dialog-stack';
import { TrashDialog } from './TrashDialog';

/** Ouvre la corbeille de la bibliothèque. */
export function openTrashDialog() {
  return openDialog(close => <TrashDialog close={close} />, {
    width: 'min(1000px, 96vw)',
    maxWidth: '96vw',
    autoFocus: false,
  });
}
