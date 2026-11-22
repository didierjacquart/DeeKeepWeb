import { useEffect, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router';
import { cx } from '../../lib/cx';
import { useStore } from '../../lib/store';
import { auth } from '../../core/auth/auth';
import { AppLogo } from '../../layout/AppLogo';
import './login-page.scss';

const MIN_PASSWORD = 8;

/**
 * Connexion par adresse e-mail, ou inscription. Inscription et mot de passe oublié suivent
 * le même parcours : adresse → code à usage unique reçu par e-mail → deux saisies du mot de
 * passe. « Renvoyer un code » annule le précédent. Un compte inscrit attend la validation
 * d'un administrateur. À la toute première visite (route /setup), création directe du compte
 * administrateur (adresse et mot de passe, sans code).
 */
export function LoginPage() {
  const navigate = useNavigate();
  const setup = useLocation().pathname === '/setup';
  const session = useStore(auth, s => s.session);
  const mailEnabled = !!session?.mailEnabled;

  /** @type {['login' | 'signup' | 'reset', Function]} */
  const [mode, setMode] = useState('login');
  /** @type {['email' | 'code' | 'password' | 'done', Function]} */
  const [step, setStep] = useState('email');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [code, setCode] = useState('');
  const [error, setError] = useState(null);
  const [notice, setNotice] = useState(null);
  const [busy, setBusy] = useState(false);
  const [resendAt, setResendAt] = useState(0);
  const [now, setNow] = useState(() => Date.now());
  const firstInput = useRef(null);

  // État du serveur relu à l'ouverture : il dit si l'envoi d'e-mails est possible (il a pu être configuré depuis).
  useEffect(() => {
    void auth.refresh();
  }, []);

  // Chaque écran place le curseur dans son premier champ.
  useEffect(() => {
    firstInput.current?.focus();
  }, [mode, step]);

  // Compte à rebours du renvoi de code.
  useEffect(() => {
    if (resendAt <= Date.now()) return undefined;
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [resendAt]);
  const resendIn = Math.max(0, Math.ceil((resendAt - now) / 1000));

  const go = (nextMode, nextStep = 'email') => {
    setMode(nextMode);
    setStep(nextStep);
    setError(null);
    setNotice(null);
    setPassword('');
    setConfirm('');
    setCode('');
  };

  const run = async action => {
    setError(null);
    setBusy(true);
    try {
      await action();
    } finally {
      setBusy(false);
    }
  };

  const login = e => {
    e.preventDefault();
    void run(async () => {
      const failure = setup ? await auth.setup(email.trim(), password) : await auth.login(email.trim(), password);
      if (failure) setError(failure);
      else navigate('/');
    });
  };

  const submitSetup = e => {
    if (password !== confirm) {
      e.preventDefault();
      setError('Les deux saisies diffèrent.');
      return;
    }
    login(e);
  };

  const sendCode = async () => {
    const r = await auth.requestCode(email.trim());
    if (r.resendAfter) {
      setResendAt(Date.now() + r.resendAfter * 1000);
      setNow(Date.now());
    }
    if (r.error) {
      setError(r.error);
      return false;
    }
    setCode('');
    setNotice(`Code envoyé à ${email.trim()}. Il est valable 15 minutes.`);
    return true;
  };

  const requestCode = e => {
    e.preventDefault();
    void run(async () => {
      if (await sendCode()) setStep('code');
    });
  };

  const resend = () =>
    void run(async () => {
      if (await sendCode()) firstInput.current?.focus();
    });

  const checkCode = e => {
    e.preventDefault();
    void run(async () => {
      const failure = await auth.checkCode(email.trim(), code);
      if (failure) setError(failure);
      else {
        setNotice(null);
        setStep('password');
      }
    });
  };

  const savePassword = e => {
    e.preventDefault();
    if (password.length < MIN_PASSWORD) {
      setError(`Le mot de passe doit compter au moins ${MIN_PASSWORD} caractères.`);
      return;
    }
    if (password !== confirm) {
      setError('Les deux saisies diffèrent.');
      return;
    }
    void run(async () => {
      const r = await auth.complete(email.trim(), code, password);
      if (r.error) {
        setError(r.error);
        // Code expiré ou épuisé : retour à sa saisie pour en redemander un.
        if (/code/i.test(r.error)) setStep('code');
      } else if (r.status === 'signedIn') navigate('/');
      else {
        setNotice(r.message ?? 'Mot de passe enregistré.');
        setStep('done');
      }
    });
  };

  const errorText = error && (
    <p className="dk-error-text" role="alert">
      {error}
    </p>
  );

  if (setup) {
    return (
      <div className="dk-login">
        <form className="box dk-card" onSubmit={submitSetup}>
          <AppLogo />
          <p className="dk-help">
            Première visite : créez le compte administrateur qui protégera DeeKeep (mot de passe de 8 caractères au
            moins).
          </p>
          <EmailInput inputRef={firstInput} value={email} onChange={setEmail} />
          <PasswordInput value={password} onChange={setPassword} label="Nouveau mot de passe" autoComplete="new-password" />
          <PasswordInput value={confirm} onChange={setConfirm} label="Confirmer le mot de passe" autoComplete="new-password" />
          {errorText}
          <button className="primary-btn" type="submit" disabled={busy}>
            Enregistrer
          </button>
        </form>
      </div>
    );
  }

  const tabs = (
    <div className="tabs" role="tablist">
      <button type="button" role="tab" aria-selected={mode === 'login'} className={cx(mode === 'login' && 'active')} onClick={() => go('login')}>
        Connexion
      </button>
      <button
        type="button"
        role="tab"
        aria-selected={mode !== 'login'}
        className={cx(mode !== 'login' && 'active')}
        onClick={() => go('signup')}
      >
        Inscription
      </button>
    </div>
  );

  let body;
  if (mode === 'login') {
    body = (
      <form className="steps" onSubmit={login}>
        <EmailInput inputRef={firstInput} value={email} onChange={setEmail} />
        <PasswordInput value={password} onChange={setPassword} label="Mot de passe" autoComplete="current-password" />
        {errorText}
        <button className="primary-btn" type="submit" disabled={busy}>
          Se connecter
        </button>
        <button type="button" className="link-btn" onClick={() => go('reset')}>
          Mot de passe oublié ?
        </button>
      </form>
    );
  } else if (!mailEnabled) {
    body = (
      <div className="steps">
        <p className="dk-help">
          L'envoi d'e-mails n'est pas configuré sur ce serveur : {mode === 'reset' ? 'pour un mot de passe oublié' : "pour créer un compte"},
          adressez-vous à l'administrateur.
        </p>
        <button type="button" className="link-btn" onClick={() => go('login')}>
          Retour à la connexion
        </button>
      </div>
    );
  } else if (step === 'email') {
    body = (
      <form className="steps" onSubmit={requestCode}>
        <p className="dk-help">
          {mode === 'reset'
            ? 'Mot de passe oublié : saisissez votre adresse, un code vous y sera envoyé pour en choisir un nouveau.'
            : 'Saisissez votre adresse e-mail : un code vous y sera envoyé pour la confirmer.'}
        </p>
        <EmailInput inputRef={firstInput} value={email} onChange={setEmail} />
        {errorText}
        <button className="primary-btn" type="submit" disabled={busy}>
          Recevoir un code
        </button>
        {mode === 'reset' && (
          <button type="button" className="link-btn" onClick={() => go('login')}>
            Retour à la connexion
          </button>
        )}
      </form>
    );
  } else if (step === 'code') {
    body = (
      <form className="steps" onSubmit={checkCode}>
        {notice && <p className="dk-help">{notice}</p>}
        <input
          ref={firstInput}
          className="dk-input code"
          type="text"
          name="code"
          inputMode="numeric"
          autoComplete="one-time-code"
          placeholder="Code à 6 chiffres"
          aria-label="Code reçu par e-mail"
          maxLength={9}
          value={code}
          onChange={e => setCode(e.target.value)}
          required
        />
        {errorText}
        <button className="primary-btn" type="submit" disabled={busy}>
          Valider le code
        </button>
        <div className="row-links">
          <button type="button" className="link-btn" onClick={resend} disabled={busy || resendIn > 0}>
            {resendIn > 0 ? `Renvoyer un code (${resendIn} s)` : 'Renvoyer un code'}
          </button>
          <button type="button" className="link-btn" onClick={() => go(mode)}>
            Changer d'adresse
          </button>
        </div>
      </form>
    );
  } else if (step === 'password') {
    body = (
      <form className="steps" onSubmit={savePassword}>
        <p className="dk-help">
          Adresse confirmée : <strong>{email.trim()}</strong>. Choisissez votre mot de passe (8 caractères au moins).
        </p>
        <input type="email" name="username" autoComplete="username" value={email.trim()} readOnly hidden />
        <PasswordInput
          inputRef={firstInput}
          value={password}
          onChange={setPassword}
          label="Nouveau mot de passe"
          autoComplete="new-password"
        />
        <PasswordInput value={confirm} onChange={setConfirm} label="Confirmer le mot de passe" autoComplete="new-password" />
        {errorText}
        <button className="primary-btn" type="submit" disabled={busy}>
          Enregistrer le mot de passe
        </button>
      </form>
    );
  } else {
    body = (
      <div className="steps">
        <p className="dk-help" role="status">
          {notice}
        </p>
        <button type="button" ref={firstInput} className="primary-btn" onClick={() => go('login')}>
          Retour à la connexion
        </button>
      </div>
    );
  }

  return (
    <div className="dk-login">
      <div className="box dk-card">
        <AppLogo />
        {tabs}
        {body}
      </div>
    </div>
  );
}

function EmailInput({ inputRef, value, onChange }) {
  return (
    <input
      ref={inputRef}
      className="dk-input"
      type="email"
      name="username"
      autoComplete="username"
      autoCapitalize="none"
      placeholder="Adresse e-mail"
      aria-label="Adresse e-mail"
      value={value}
      onChange={e => onChange(e.target.value)}
      required
    />
  );
}

function PasswordInput({ inputRef, value, onChange, label, autoComplete }) {
  return (
    <input
      ref={inputRef}
      className="dk-input"
      type="password"
      name={autoComplete === 'new-password' ? 'new-password' : 'password'}
      autoComplete={autoComplete}
      placeholder={label}
      aria-label={label}
      value={value}
      onChange={e => onChange(e.target.value)}
      required
    />
  );
}
