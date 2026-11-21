import { openDialog } from '../../../shared/dialog/dialog-stack';
import { TrackUsersDialog } from './TrackUsersDialog';

/** Administrateur : utilisateurs d'un titre. */
export function openTrackUsersDialog(track) {
  return openDialog(close => <TrackUsersDialog track={track} close={close} />, {
    width: 'min(560px, 96vw)',
    autoFocus: false,
  });
}
