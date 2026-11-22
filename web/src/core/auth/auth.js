import { redirect } from 'react-router';
import { Store } from '../../lib/store';
import { canSeePage, firstAllowedPath } from './permissions';

/**
 * Session du compte connecté (nom, rôle, pages ouvertes, droits d'action). Hors ligne, la dernière session connue est
 * présumée valide : l'application s'ouvre sur son cache plutôt que sur un
 * écran de connexion qui ne pourrait de toute façon rien vérifier.
 */
export class AuthService extends Store {
  storageKey = 'dk-auth-last';

  constructor() {
    super({ session: null });
  }

  async refresh() {
    try {
      const response = await fetch('/auth/me', { credentials: 'same-origin', cache: 'no-store' });
      if (!response.ok) throw new Error(String(response.status));
      const state = await response.json();
      this.setState({ session: state });
      localStorage.setItem(this.storageKey, JSON.stringify(state));
      return state;
    } catch {
      const cached = localStorage.getItem(this.storageKey);
      const state = cached
        ? JSON.parse(cached)
        : { authEnabled: true, authenticated: false, user: null, needsSetup: false, mailEnabled: false, pages: [] };
      this.setState({ session: state });
      return state;
    }
  }

  /** Connexion par adresse e-mail ; rend le message d'erreur, ou null. */
  async login(email, password) {
    return this.post('/auth/login', { user: email, password });
  }

  /** Premier compte (administrateur), à la toute première visite. */
  async setup(email, password) {
    return this.post('/auth/setup', { user: email, password });
  }

  /**
   * Envoie un code à usage unique (inscription ou mot de passe oublié) ; en redemander un annule le précédent.
   * @returns {Promise<{ error: string | null, resendAfter: number }>}
   */
  async requestCode(email) {
    const r = await this.call('/auth/code', { email });
    if (r.ok) return { error: null, resendAfter: r.data?.resendAfter ?? 30 };
    return { error: errorOf(r), resendAfter: r.data?.retryAfter ?? 0 };
  }

  /** Contrôle le code reçu (il reste valable pour l'étape du mot de passe) ; rend l'erreur, ou null. */
  async checkCode(email, code) {
    const r = await this.call('/auth/code/check', { email, code });
    return r.ok ? null : errorOf(r);
  }

  /**
   * Code et mot de passe : compte créé (en attente de validation) ou mot de passe remplacé.
   * @returns {Promise<{ error: string | null, status?: 'signedIn' | 'pending' | 'disabled', message?: string }>}
   */
  async complete(email, code, password) {
    const r = await this.call('/auth/complete', { email, code, password });
    if (!r.ok) return { error: errorOf(r) };
    if (r.data?.status === 'signedIn') await this.refresh();
    return { error: null, status: r.data?.status, message: r.data?.message };
  }

  async changePassword(password) {
    return this.post('/auth/password', { password });
  }

  async logout() {
    await fetch('/auth/logout', { method: 'POST', credentials: 'same-origin' }).catch(() => undefined);
    localStorage.removeItem(this.storageKey);
    await this.refresh();
  }

  /** Envoi qui ouvre ou change la session : rend le message d'erreur, ou null (session relue). */
  async post(url, body) {
    const r = await this.call(url, body);
    if (r.ok) {
      await this.refresh();
      return null;
    }
    if (r.status === 401) return 'Adresse e-mail ou mot de passe incorrect.';
    return errorOf(r);
  }

  /** @returns {Promise<{ ok: boolean, status: number, data: any }>} status 0 : serveur injoignable. */
  async call(url, body) {
    try {
      const response = await fetch(url, {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      const text = await response.text();
      let data = text;
      try {
        data = text ? JSON.parse(text) : null;
      } catch {
        // texte brut
      }
      return { ok: response.ok, status: response.status, data };
    } catch {
      return { ok: false, status: 0, data: null };
    }
  }
}

/** Message lisible d'une réponse refusée (texte du serveur, `{ error }`, ou code HTTP). */
function errorOf(r) {
  if (r.status === 0) return 'Serveur injoignable.';
  if (typeof r.data === 'string' && r.data.trim()) return r.data;
  if (r.data?.error) return r.data.error;
  if (r.data?.message) return r.data.message;
  return `Erreur ${r.status}`;
}

export const auth = new AuthService();

/** Garde des pages protégées : renvoie vers /login (ou /setup à la première visite). */
export async function requireSession() {
  const state = auth.state.session ?? (await auth.refresh());
  if (!state.authEnabled || state.authenticated) return null;
  throw redirect(state.needsSetup ? '/setup' : '/login');
}

/**
 * Garde d'une page : une adresse que le compte ne peut pas ouvrir renvoie vers sa
 * première page permise. Sans session, laisse `requireSession` (route parente) agir.
 * @param {string} key nom de la page (`organiser`, `administration`…)
 */
export function requirePage(key) {
  return async () => {
    const state = auth.state.session ?? (await auth.refresh());
    if (state.authEnabled && !state.authenticated) return null;
    if (!canSeePage(state, key)) throw redirect(firstAllowedPath(state));
    return null;
  };
}

/** Accueil : la première page ouverte au compte. */
export async function redirectHome() {
  const state = auth.state.session ?? (await auth.refresh());
  return redirect(firstAllowedPath(state));
}
