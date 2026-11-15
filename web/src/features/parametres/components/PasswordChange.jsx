import { useState } from 'react';
import { auth } from '../../../core/auth/auth';

/** Changement du mot de passe (6 caractères au moins), avec le message du serveur. */
export function PasswordChange() {
  const [newPassword, setNewPassword] = useState('');
  const [passwordMessage, setPasswordMessage] = useState(null);

  const changePassword = async () => {
    const error = await auth.changePassword(newPassword);
    setPasswordMessage(error ?? 'Mot de passe modifié.');
    if (!error) setNewPassword('');
  };

  return (
    <>
      <div className="row">
        <input
          className="dk-input"
          type="password"
          autoComplete="new-password"
          placeholder="Nouveau mot de passe"
          aria-label="Nouveau mot de passe"
          value={newPassword}
          onChange={e => setNewPassword(e.target.value)}
        />
        <button className="accent-btn" disabled={newPassword.length < 6} onClick={() => void changePassword()}>
          Changer le mot de passe
        </button>
      </div>
      {passwordMessage && <p className="dk-help">{passwordMessage}</p>}
    </>
  );
}
