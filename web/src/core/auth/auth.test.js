import { afterEach, describe, expect, it, vi } from 'vitest';
import { AuthService, auth, redirectHome, requirePage, requireSession } from './auth';

const session = (fields = {}) => ({ authEnabled: true, authenticated: true, user: null, needsSetup: false, ...fields });
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status }));

afterEach(() => {
  vi.unstubAllGlobals();
  auth.setState({ session: null });
});

describe('AuthService', () => {
  it('lit la session du serveur et la mémorise', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => reply(200, session())),
    );
    const service = new AuthService();
    await expect(service.refresh()).resolves.toEqual(session());
    expect(JSON.parse(localStorage.getItem('dk-auth-last'))).toEqual(session());
  });

  it('hors ligne, reprend la dernière session connue', async () => {
    localStorage.setItem('dk-auth-last', JSON.stringify(session({ user: 'moi' })));
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.reject(new TypeError('Failed to fetch'))),
    );
    await expect(new AuthService().refresh()).resolves.toEqual(session({ user: 'moi' }));
  });

  it('hors ligne sans souvenir, demande la connexion', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.reject(new TypeError('Failed to fetch'))),
    );
    await expect(new AuthService().refresh()).resolves.toMatchObject({ authenticated: false });
  });

  it('traduit les refus de connexion en messages', async () => {
    const service = new AuthService();
    vi.stubGlobal(
      'fetch',
      vi.fn(() => reply(401, {})),
    );
    await expect(service.login('moi@example.com', 'faux')).resolves.toBe('Adresse e-mail ou mot de passe incorrect.');
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(new Response('"Trop court"', { status: 400 }))),
    );
    await expect(service.setup('moi@example.com', '123')).resolves.toBe('Trop court');
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.reject(new TypeError('Failed to fetch'))),
    );
    await expect(service.login('moi@example.com', 'x')).resolves.toBe('Serveur injoignable.');
  });

  it('rend null et rafraîchit la session quand la connexion réussit', async () => {
    const fetch = vi.fn(url => (url === '/auth/me' ? reply(200, session()) : reply(200, {})));
    vi.stubGlobal('fetch', fetch);
    const service = new AuthService();
    await expect(service.login('moi@example.com', 'bon')).resolves.toBeNull();
    expect(fetch.mock.calls.map(c => c[0])).toEqual(['/auth/login', '/auth/me']);
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ user: 'moi@example.com', password: 'bon' });
    expect(service.state.session).toEqual(session());
  });

  it('demande un code et rend le délai de renvoi, ou le refus du serveur', async () => {
    const service = new AuthService();
    const fetch = vi.fn(() => reply(200, { resendAfter: 30, expiresIn: 900 }));
    vi.stubGlobal('fetch', fetch);
    await expect(service.requestCode('moi@example.com')).resolves.toEqual({ error: null, resendAfter: 30 });
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ email: 'moi@example.com' });

    vi.stubGlobal(
      'fetch',
      vi.fn(() => reply(429, { error: 'Patientez 12 s avant de redemander un code.', retryAfter: 12 })),
    );
    await expect(service.requestCode('moi@example.com')).resolves.toEqual({
      error: 'Patientez 12 s avant de redemander un code.',
      resendAfter: 12,
    });
  });

  it('valide le code puis termine : session relue seulement si connecté', async () => {
    const service = new AuthService();
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(new Response('"Code incorrect."', { status: 400 }))),
    );
    await expect(service.checkCode('moi@example.com', '000000')).resolves.toBe('Code incorrect.');

    const fetch = vi.fn(url => (url === '/auth/me' ? reply(200, session()) : reply(200, { status: 'signedIn' })));
    vi.stubGlobal('fetch', fetch);
    await expect(service.complete('moi@example.com', '123456', 'motdepasse')).resolves.toMatchObject({
      error: null,
      status: 'signedIn',
    });
    expect(fetch.mock.calls.map(c => c[0])).toEqual(['/auth/complete', '/auth/me']);

    const pending = vi.fn(() => reply(200, { status: 'pending', message: 'En attente' }));
    vi.stubGlobal('fetch', pending);
    await expect(service.complete('x@example.com', '123456', 'motdepasse')).resolves.toMatchObject({ status: 'pending' });
    expect(pending.mock.calls.map(c => c[0])).toEqual(['/auth/complete']);
  });
});

describe('requireSession', () => {
  it('laisse passer une session ouverte ou une application sans mot de passe', async () => {
    auth.setState({ session: session() });
    await expect(requireSession()).resolves.toBeNull();
    auth.setState({ session: session({ authEnabled: false, authenticated: false }) });
    await expect(requireSession()).resolves.toBeNull();
  });

  it('renvoie vers /login, ou /setup à la première visite', async () => {
    auth.setState({ session: session({ authenticated: false }) });
    const toLogin = await requireSession().catch(r => r);
    expect(toLogin.status).toBe(302);
    expect(toLogin.headers.get('Location')).toBe('/login');
    auth.setState({ session: session({ authenticated: false, needsSetup: true }) });
    const toSetup = await requireSession().catch(r => r);
    expect(toSetup.headers.get('Location')).toBe('/setup');
  });
});

describe('requirePage', () => {
  const limited = session({ role: 'user', pages: ['bibliotheque', 'qualite'], canDownload: false, canDelete: false });

  it('laisse passer une page ouverte au compte', async () => {
    auth.setState({ session: limited });
    await expect(requirePage('qualite')()).resolves.toBeNull();
  });

  it('renvoie une page refusée vers la première page permise', async () => {
    auth.setState({ session: limited });
    const r = await requirePage('organiser')().catch(x => x);
    expect(r.headers.get('Location')).toBe('/bibliotheque');
    const admin = await requirePage('administration')().catch(x => x);
    expect(admin.headers.get('Location')).toBe('/bibliotheque');
  });

  it('sans aucune page, renvoie vers la page d’accès refusé', async () => {
    auth.setState({ session: session({ role: 'user', pages: [] }) });
    const r = await requirePage('musique')().catch(x => x);
    expect(r.headers.get('Location')).toBe('/aucun-acces');
  });

  it('ouvre tout à un administrateur et choisit l’accueil', async () => {
    auth.setState({ session: session({ role: 'admin', pages: [] }) });
    await expect(requirePage('administration')()).resolves.toBeNull();
    expect((await redirectHome()).headers.get('Location')).toBe('/musique');
    auth.setState({ session: limited });
    expect((await redirectHome()).headers.get('Location')).toBe('/bibliotheque');
  });
});
