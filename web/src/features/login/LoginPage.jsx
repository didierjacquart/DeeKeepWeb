import { useEffect, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router';
import { auth } from '../../core/auth/auth';
import { AppLogo } from '../../layout/AppLogo';

/**
 * Connexion, ou création du premier compte (administrateur) à la toute première
 * visite (route /setup). Nom laissé vide : compte « deekeep » (installations
 * antérieures aux comptes multiples).
 */
export function LoginPage() {
  const navigate = useNavigate();
  const setup = useLocation().pathname === '/setup';
  const nameInput = useRef(null);
  const [name, setName] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  // Premier champ de la page : le curseur y est placé d'emblée.
  useEffect(() => {
    nameInput.current?.focus();
  }, []);

  const submit = async e => {
    e.preventDefault();
    setError(null);
    if (setup && password !== confirm) {
      setError('Les deux saisies diffèrent.');
      return;
    }
    setBusy(true);
    const user = name.trim() || undefined;
    const failure = setup ? await auth.setup(password, user) : await auth.login(password, user);
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
            Première visite : créez le compte administrateur qui protégera DeeKeep (mot de passe de 8 caractères au
            moins).
          </p>
        )}
        <input
          ref={nameInput}
          className="dk-input"
          type="text"
          name="username"
          autoComplete="username"
          autoCapitalize="none"
          placeholder="Nom d'utilisateur (deekeep par défaut)"
          aria-label="Nom d'utilisateur"
          value={name}
          onChange={e => setName(e.target.value)}
        />
        <input
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
