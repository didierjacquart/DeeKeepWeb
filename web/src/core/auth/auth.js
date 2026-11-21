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
        : { authEnabled: true, authenticated: false, user: null, needsSetup: false, pages: [] };
      this.setState({ session: state });
      return state;
    }
  }

  async login(password, user) {
    return this.post('/auth/login', { user, password });
  }

  async setup(password, user) {
    return this.post('/auth/setup', { user, password });
  }

  async changePassword(password) {
    return this.post('/auth/password', { password });
  }

  async logout() {
    await fetch('/auth/logout', { method: 'POST', credentials: 'same-origin' }).catch(() => undefined);
    localStorage.removeItem(this.storageKey);
    await this.refresh();
  }

  async post(url, body) {
    try {
      const response = await fetch(url, {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      if (response.ok) {
        await this.refresh();
        return null;
      }
      if (response.status === 401) return "Nom d'utilisateur ou mot de passe incorrect.";
      const text = await response.text();
      return text.replace(/^"|"$/g, '') || `Erreur ${response.status}`;
    } catch {
      return 'Serveur injoignable.';
    }
  }
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
