import { screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { auth } from '../core/auth/auth';
import { renderRoutes, renderWithRouter } from '../test/render';
import { mockApi } from '../test/mock-api';
import { LoginPage } from '../features/login/LoginPage';
import { RouteError } from './RouteError';
import { routes } from './routes';

const session = fields => ({ authEnabled: true, authenticated: false, user: null, needsSetup: false, ...fields });

afterEach(() => auth.setState({ session: null }));

describe('garde des pages', () => {
  it('renvoie vers la connexion sans session', async () => {
    auth.setState({ session: session() });
    const { router } = renderRoutes(routes, '/bibliotheque');
    expect(await screen.findByRole('button', { name: 'Se connecter' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/login');
  });

  it('demande de choisir un mot de passe à la première visite', async () => {
    auth.setState({ session: session({ needsSetup: true }) });
    const { router } = renderRoutes(routes, '/');
    expect(await screen.findByRole('button', { name: 'Enregistrer' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/setup');
  });

  it('renvoie une adresse inconnue vers l’accueil', async () => {
    auth.setState({ session: session() });
    const { router } = renderRoutes(routes, '/nimporte');
    await screen.findByRole('button', { name: 'Se connecter' });
    expect(router.state.location.pathname).toBe('/login');
  });
});

describe('LoginPage', () => {
  it('se connecte et rejoint l’accueil', async () => {
    mockApi({});
    const login = vi.spyOn(auth, 'login').mockResolvedValue(null);
    const { user, router } = renderWithRouter(<LoginPage />, {
      route: '/login',
      path: '/login',
      extraRoutes: [{ path: '/', element: <p>Accueil</p> }],
    });
    expect(screen.getByLabelText('Mot de passe')).toHaveFocus();
    await user.type(screen.getByLabelText('Mot de passe'), 'secret');
    await user.click(screen.getByRole('button', { name: 'Se connecter' }));
    expect(login).toHaveBeenCalledWith('secret');
    expect(router.state.location.pathname).toBe('/');
  });

  it('affiche le refus du serveur', async () => {
    vi.spyOn(auth, 'login').mockResolvedValue('Mot de passe incorrect.');
    const { user } = renderWithRouter(<LoginPage />, { route: '/login', path: '/login' });
    await user.type(screen.getByLabelText('Mot de passe'), 'faux');
    await user.keyboard('{Enter}');
    expect(await screen.findByRole('alert')).toHaveTextContent('Mot de passe incorrect.');
  });

  it('à la première visite, vérifie que les deux saisies concordent', async () => {
    const setup = vi.spyOn(auth, 'setup').mockResolvedValue(null);
    const { user } = renderWithRouter(<LoginPage />, { route: '/setup', path: '/setup' });
    await user.type(screen.getByLabelText('Nouveau mot de passe'), 'motdepasse');
    await user.type(screen.getByLabelText('Confirmer le mot de passe'), 'autre');
    await user.click(screen.getByRole('button', { name: 'Enregistrer' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Les deux saisies diffèrent.');
    expect(setup).not.toHaveBeenCalled();
  });
});

describe('RouteError', () => {
  it('remplace une page en erreur par un message et un bouton Recharger', async () => {
    const Broken = () => {
      throw new Error('Données inattendues');
    };
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    renderRoutes([{ path: '/x', element: <Broken />, errorElement: <RouteError /> }], '/x');
    expect(await screen.findByRole('heading', { name: 'Cette page a rencontré une erreur' })).toBeInTheDocument();
    expect(screen.getByText('Données inattendues')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Recharger' })).toBeInTheDocument();
  });
});
