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

  it('renvoie une page refusée au compte vers sa première page permise', async () => {
    mockApi({ Jobs: { jobs: [] }, QualityEntries: new Error('hors test') });
    auth.setState({ session: session({ authenticated: true, user: 'invite', role: 'user', pages: ['qualite'] }) });
    const { router } = renderRoutes(routes, '/organiser');
    await vi.waitFor(() => expect(router.state.location.pathname).toBe('/qualite'));
  });

  it('renvoie une adresse inconnue vers l’accueil', async () => {
    auth.setState({ session: session() });
    const { router } = renderRoutes(routes, '/nimporte');
    await screen.findByRole('button', { name: 'Se connecter' });
    expect(router.state.location.pathname).toBe('/login');
  });
});

describe('LoginPage', () => {
  const open = (route = '/login', mailEnabled = true) => {
    auth.setState({ session: session({ mailEnabled }) });
    // La page relit l'état du serveur à l'ouverture.
    vi.spyOn(auth, 'refresh').mockImplementation(async () => auth.state.session);
    return renderWithRouter(<LoginPage />, {
      route,
      path: route,
      extraRoutes: [{ path: '/', element: <p>Accueil</p> }],
    });
  };

  it('se connecte avec son adresse e-mail et rejoint l’accueil', async () => {
    const login = vi.spyOn(auth, 'login').mockResolvedValue(null);
    const { user, router } = open();
    expect(screen.getByRole('tab', { name: 'Connexion' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByLabelText('Adresse e-mail')).toHaveFocus();
    await user.type(screen.getByLabelText('Adresse e-mail'), ' moi@example.com ');
    await user.type(screen.getByLabelText('Mot de passe'), 'secret');
    await user.click(screen.getByRole('button', { name: 'Se connecter' }));
    expect(login).toHaveBeenCalledWith('moi@example.com', 'secret');
    expect(router.state.location.pathname).toBe('/');
  });

  it('affiche le refus du serveur', async () => {
    vi.spyOn(auth, 'login').mockResolvedValue('Adresse e-mail ou mot de passe incorrect.');
    const { user } = open();
    await user.type(screen.getByLabelText('Adresse e-mail'), 'moi@example.com');
    await user.type(screen.getByLabelText('Mot de passe'), 'faux');
    await user.keyboard('{Enter}');
    expect(await screen.findByRole('alert')).toHaveTextContent('Adresse e-mail ou mot de passe incorrect.');
  });

  it('inscription : adresse, code reçu, puis deux saisies du mot de passe', async () => {
    const requestCode = vi.spyOn(auth, 'requestCode').mockResolvedValue({ error: null, resendAfter: 30 });
    const checkCode = vi.spyOn(auth, 'checkCode').mockResolvedValue(null);
    const complete = vi
      .spyOn(auth, 'complete')
      .mockResolvedValue({ error: null, status: 'pending', message: 'Compte en attente de validation par un administrateur.' });
    const { user } = open();
    await user.click(screen.getByRole('tab', { name: 'Inscription' }));
    await user.type(screen.getByLabelText('Adresse e-mail'), 'nouveau@example.com');
    await user.click(screen.getByRole('button', { name: 'Recevoir un code' }));
    expect(requestCode).toHaveBeenCalledWith('nouveau@example.com');

    // L'application bascule sur la saisie du code, renvoi bloqué le temps du délai.
    const codeInput = await screen.findByLabelText('Code reçu par e-mail');
    expect(codeInput).toHaveFocus();
    expect(screen.getByText(/Code envoyé à nouveau@example.com/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Renvoyer un code \(\d+ s\)/ })).toBeDisabled();
    await user.type(codeInput, '123456');
    await user.click(screen.getByRole('button', { name: 'Valider le code' }));
    expect(checkCode).toHaveBeenCalledWith('nouveau@example.com', '123456');

    const first = await screen.findByLabelText('Nouveau mot de passe');
    await user.type(first, 'motdepasse');
    await user.type(screen.getByLabelText('Confirmer le mot de passe'), 'autre-mdp');
    await user.click(screen.getByRole('button', { name: 'Enregistrer le mot de passe' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Les deux saisies diffèrent.');
    expect(complete).not.toHaveBeenCalled();

    await user.clear(screen.getByLabelText('Confirmer le mot de passe'));
    await user.type(screen.getByLabelText('Confirmer le mot de passe'), 'motdepasse');
    await user.click(screen.getByRole('button', { name: 'Enregistrer le mot de passe' }));
    expect(complete).toHaveBeenCalledWith('nouveau@example.com', '123456', 'motdepasse');
    expect(await screen.findByRole('status')).toHaveTextContent('Compte en attente de validation');
  });

  it('un code faux reste sur sa saisie ; renvoyer un code est possible après le délai', async () => {
    const requestCode = vi.spyOn(auth, 'requestCode').mockResolvedValue({ error: null, resendAfter: 0 });
    vi.spyOn(auth, 'checkCode').mockResolvedValue('Code incorrect.');
    const { user } = open();
    await user.click(screen.getByRole('tab', { name: 'Inscription' }));
    await user.type(screen.getByLabelText('Adresse e-mail'), 'nouveau@example.com');
    await user.click(screen.getByRole('button', { name: 'Recevoir un code' }));
    await user.type(await screen.findByLabelText('Code reçu par e-mail'), '000000');
    await user.click(screen.getByRole('button', { name: 'Valider le code' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Code incorrect.');
    expect(screen.getByLabelText('Code reçu par e-mail')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Renvoyer un code' }));
    expect(requestCode).toHaveBeenCalledTimes(2);
  });

  it('mot de passe oublié : même parcours, puis connexion directe', async () => {
    vi.spyOn(auth, 'requestCode').mockResolvedValue({ error: null, resendAfter: 30 });
    vi.spyOn(auth, 'checkCode').mockResolvedValue(null);
    vi.spyOn(auth, 'complete').mockResolvedValue({ error: null, status: 'signedIn' });
    const { user, router } = open();
    await user.click(screen.getByRole('button', { name: 'Mot de passe oublié ?' }));
    expect(screen.getByText(/Mot de passe oublié : saisissez votre adresse/)).toBeInTheDocument();
    await user.type(screen.getByLabelText('Adresse e-mail'), 'moi@example.com');
    await user.click(screen.getByRole('button', { name: 'Recevoir un code' }));
    await user.type(await screen.findByLabelText('Code reçu par e-mail'), '654321');
    await user.click(screen.getByRole('button', { name: 'Valider le code' }));
    await user.type(await screen.findByLabelText('Nouveau mot de passe'), 'nouveau-mdp');
    await user.type(screen.getByLabelText('Confirmer le mot de passe'), 'nouveau-mdp');
    await user.click(screen.getByRole('button', { name: 'Enregistrer le mot de passe' }));
    await vi.waitFor(() => expect(router.state.location.pathname).toBe('/'));
  });

  it('sans envoi d’e-mails configuré, l’inscription renvoie vers l’administrateur', async () => {
    const requestCode = vi.spyOn(auth, 'requestCode');
    const { user } = open('/login', false);
    await user.click(screen.getByRole('tab', { name: 'Inscription' }));
    expect(screen.getByText(/L'envoi d'e-mails n'est pas configuré/)).toBeInTheDocument();
    expect(requestCode).not.toHaveBeenCalled();
  });

  it('à la première visite, vérifie que les deux saisies concordent', async () => {
    const setup = vi.spyOn(auth, 'setup').mockResolvedValue(null);
    const { user } = open('/setup');
    await user.type(screen.getByLabelText('Adresse e-mail'), 'admin@example.com');
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
