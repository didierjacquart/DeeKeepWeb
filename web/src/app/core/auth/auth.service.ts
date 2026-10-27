import { Injectable, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';

export interface AuthState {
  authEnabled: boolean;
  authenticated: boolean;
  user: string | null;
  needsSetup: boolean;
}

/**
 * Session de l'utilisateur unique. Hors ligne, la dernière session connue est
 * présumée valide : l'application s'ouvre sur son cache plutôt que sur un
 * écran de connexion qui ne pourrait de toute façon rien vérifier.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  readonly state = signal<AuthState | null>(null);
  private readonly storageKey = 'dk-auth-last';

  async refresh(): Promise<AuthState> {
    try {
      const response = await fetch('/auth/me', { credentials: 'same-origin', cache: 'no-store' });
      if (!response.ok) throw new Error(String(response.status));
      const state = (await response.json()) as AuthState;
      this.state.set(state);
      localStorage.setItem(this.storageKey, JSON.stringify(state));
      return state;
    } catch {
      const cached = localStorage.getItem(this.storageKey);
      const state: AuthState = cached
        ? JSON.parse(cached)
        : { authEnabled: true, authenticated: false, user: null, needsSetup: false };
      this.state.set(state);
      return state;
    }
  }

  async login(password: string, user?: string): Promise<string | null> {
    return this.post('/auth/login', { user, password });
  }

  async setup(password: string, user?: string): Promise<string | null> {
    return this.post('/auth/setup', { user, password });
  }

  async changePassword(password: string): Promise<string | null> {
    return this.post('/auth/password', { password });
  }

  async logout(): Promise<void> {
    await fetch('/auth/logout', { method: 'POST', credentials: 'same-origin' }).catch(() => undefined);
    localStorage.removeItem(this.storageKey);
    await this.refresh();
  }

  private async post(url: string, body: unknown): Promise<string | null> {
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
      if (response.status === 401) return 'Mot de passe incorrect.';
      const text = await response.text();
      return text.replace(/^"|"$/g, '') || `Erreur ${response.status}`;
    } catch {
      return 'Serveur injoignable.';
    }
  }
}

export const authGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const state = auth.state() ?? (await auth.refresh());
  if (!state.authEnabled || state.authenticated) return true;
  return router.createUrlTree([state.needsSetup ? '/setup' : '/login']);
};
