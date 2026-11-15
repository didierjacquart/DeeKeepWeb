import { useEffect, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router';
import { auth } from '../../core/auth/auth';
import { AppLogo } from '../../layout/AppLogo';

/** Connexion, ou définition du mot de passe à la toute première visite (route /setup). */
export function LoginPage() {
  const navigate = useNavigate();
  const setup = useLocation().pathname === '/setup';
  const passwordInput = useRef(null);
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  // Seul champ utile de la page : le curseur y est placé d'emblée.
  useEffect(() => {
    passwordInput.current?.focus();
  }, []);

  const submit = async e => {
    e.preventDefault();
    setError(null);
    if (setup && password !== confirm) {
      setError('Les deux saisies diffèrent.');
      return;
    }
    setBusy(true);
    const failure = setup ? await auth.setup(password) : await auth.login(password);
    setBusy(false);
    if (failure) setError(failure);
    else navigate('/');
  };

  return (
    <div className="dk-login">
      <form className="box dk-card" onSubmit={submit}>
        <AppLogo />
        {setup && (
          <p className="dk-help">
            Première visite : choisissez le mot de passe qui protégera DeeKeep (8 caractères au moins).
          </p>
        )}
        <input
          ref={passwordInput}
          className="dk-input"
          type="password"
          name="password"
          autoComplete={setup ? 'new-password' : 'current-password'}
          placeholder={setup ? 'Nouveau mot de passe' : 'Mot de passe'}
          aria-label={setup ? 'Nouveau mot de passe' : 'Mot de passe'}
          value={password}
          onChange={e => setPassword(e.target.value)}
          required
        />
        {setup && (
          <input
            className="dk-input"
            type="password"
            name="confirm"
            autoComplete="new-password"
            placeholder="Confirmer"
            aria-label="Confirmer le mot de passe"
            value={confirm}
            onChange={e => setConfirm(e.target.value)}
            required
          />
        )}
        {error && (
          <p className="dk-error-text" role="alert">
            {error}
          </p>
        )}
        <button className="primary-btn" type="submit" disabled={busy}>
          {setup ? 'Enregistrer' : 'Se connecter'}
        </button>
      </form>
    </div>
  );
}
