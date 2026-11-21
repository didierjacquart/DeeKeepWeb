import { openDialog } from '../../../shared/dialog/dialog-stack';
import { PasswordDialog } from './PasswordDialog';
import { UserDialog } from './UserDialog';
import './user-dialog.scss';

/**
 * Création (sans `user`) ou modification d'un compte.
 * @returns {Promise<boolean | undefined>} vrai si enregistré.
 */
export function openUserDialog({ user, onSubmit }) {
  return openDialog(close => <UserDialog user={user} onSubmit={onSubmit} close={close} />, {
    width: 'min(520px, 94vw)',
    autoFocus: true,
  });
}

/** Changement du mot de passe d'un compte. */
export function openPasswordDialog({ name, onSubmit }) {
  return openDialog(close => <PasswordDialog name={name} onSubmit={onSubmit} close={close} />, {
    width: 'min(440px, 94vw)',
    autoFocus: true,
  });
}
