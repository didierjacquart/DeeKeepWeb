import { useState } from 'react';
import { DialogActions, DialogContent, DialogTitle } from '../../../shared/dialog/DialogParts';
import { passwordError } from '../user-form';

/**
 * Nouveau mot de passe d'un compte, choisi par un administrateur.
 * @param {{ name: string, onSubmit: (password: string) => Promise<string | null>, close: (r?: boolean) => void }} props
 */
export function PasswordDialog({ name, onSubmit, close }) {
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const submit = async e => {
    e.preventDefault();
    const invalid = passwordError(password, confirm);
    if (invalid) {
      setError(invalid);
      return;
    }
    setBusy(true);
    const failure = await onSubmit(password);
    setBusy(false);
    if (failure) setError(failure);
    else close(true);
  };

  return (
    <form className="dk-user-dialog" onSubmit={submit}>
      <DialogTitle>Mot de passe de « {name} »</DialogTitle>
      <DialogContent className="content">
        <label className="field">
          <span>Nouveau mot de passe</span>
          <input
            className="dk-input"
            type="password"
            autoComplete="new-password"
            value={password}
            onChange={e => setPassword(e.target.value)}
            data-autofocus
          />
        </label>
        <label className="field">
          <span>Confirmer le mot de passe</span>
          <input
            className="dk-input"
            type="password"
            autoComplete="new-password"
            value={confirm}
            onChange={e => setConfirm(e.target.value)}
          />
        </label>
        {error && (
          <p className="dk-error-text" role="alert">
            {error}
          </p>
        )}
      </DialogContent>
      <DialogActions>
        <button type="button" className="control-btn" onClick={() => close()}>
          Annuler
        </button>
        <button type="submit" className="primary-btn" disabled={busy}>
          Changer
        </button>
      </DialogActions>
    </form>
  );
}
