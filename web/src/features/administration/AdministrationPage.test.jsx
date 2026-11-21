import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import { renderWithRouter } from '../../test/render';
import { mockApi } from '../../test/mock-api';
import { auth } from '../../core/auth/auth';
import { AdministrationPage } from './AdministrationPage';
import { pagesSummary, rightsSummary, togglePage, formError, toBody, emptyForm } from './user-form';

const ADMIN = {
  id: 'a1',
  name: 'admin',
  role: 'admin',
  pages: [],
  canDownload: true,
  canDelete: true,
  disabled: false,
  createdAt: 1,
};
const GUEST = {
  id: 'u2',
  name: 'invite',
  role: 'user',
  pages: ['musique', 'qualite'],
  canDownload: true,
  canDelete: false,
  disabled: true,
  createdAt: 2,
};

function setup(responses = {}) {
  const server = mockApi({ AdminUsers: { users: [ADMIN, GUEST] }, ...responses });
  const view = renderWithRouter(<AdministrationPage />);
  return { server, ...view };
}

beforeEach(() => {
  auth.setState({ session: { authEnabled: true, authenticated: true, user: 'admin', role: 'admin', pages: [] } });
  vi.spyOn(auth, 'refresh').mockResolvedValue(auth.state.session);
});

afterEach(() => {
  vi.restoreAllMocks();
  auth.setState({ session: null });
});

describe('user-form', () => {
  it('résume pages et droits', () => {
    expect(pagesSummary(ADMIN)).toBe('Toutes');
    expect(pagesSummary(GUEST)).toBe('Musique, Qualité');
    expect(pagesSummary({ ...GUEST, pages: [] })).toBe('Aucune');
    expect(rightsSummary(GUEST)).toBe('Télécharger');
    expect(rightsSummary({ ...GUEST, canDownload: false })).toBe('Aucun');
  });

  it('coche les pages dans l’ordre de la navigation', () => {
    expect(togglePage(['qualite'], 'musique')).toEqual(['musique', 'qualite']);
    expect(togglePage(['musique', 'qualite'], 'musique')).toEqual(['qualite']);
  });

  it('vérifie le formulaire de création', () => {
    const form = { ...emptyForm(), name: ' bob ', password: 'motdepasse', confirm: 'motdepasse' };
    expect(formError({ ...form, name: '' }, true)).toBe("Le nom d'utilisateur est obligatoire.");
    expect(formError({ ...form, password: 'court', confirm: 'court' }, true)).toMatch(/8 caractères/);
    expect(formError({ ...form, confirm: 'autre' }, true)).toBe('Les deux saisies diffèrent.');
    expect(formError(form, true)).toBeNull();
    expect(toBody(form, true)).toMatchObject({ name: 'bob', password: 'motdepasse', role: 'user' });
    expect(toBody(form, false)).not.toHaveProperty('password');
  });
});

describe('AdministrationPage', () => {
  it('liste les comptes avec rôle, pages, droits et état', async () => {
    setup();
    const table = screen.getByRole('table', { name: 'Comptes' });
    const row = (await within(table).findByText('invite')).closest('[role="row"]');
    expect(row).toHaveTextContent('Utilisateur');
    expect(row).toHaveTextContent('Musique, Qualité');
    expect(row).toHaveTextContent('Désactivé');
    const me = within(table).getByText('admin').closest('[role="row"]');
    expect(me).toHaveTextContent('vous');
    expect(within(me).getByRole('button', { name: 'Supprimer admin' })).toBeDisabled();
  });

  it('crée un compte avec ses pages et ses droits', async () => {
    const { user, server } = setup({ CreateUser: { createUser: { ...GUEST, id: 'n3', name: 'bob' } } });
    await screen.findByText('invite');
    await user.click(screen.getByRole('button', { name: 'Nouveau compte' }));
    const dialog = screen.getByRole('dialog');
    await user.type(within(dialog).getByLabelText("Nom d'utilisateur"), 'bob');
    await user.type(within(dialog).getByLabelText('Mot de passe'), 'motdepasse');
    await user.type(within(dialog).getByLabelText('Confirmer le mot de passe'), 'motdepasse');
    await user.click(within(dialog).getByLabelText('Qualité'));
    await user.click(within(dialog).getByLabelText('Bibliothèque'));
    await user.click(within(dialog).getByLabelText(/^Télécharger/));
    await user.click(within(dialog).getByRole('button', { name: 'Créer' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(server.mutations('CreateUser')).toEqual([
      {
        name: 'bob',
        password: 'motdepasse',
        role: 'user',
        pages: ['musique', 'qualite'],
        canDownload: true,
        canDelete: false,
        disabled: false,
      },
    ]);
    expect(screen.getByText('Compte créé.')).toBeInTheDocument();
  });

  it('refuse un mot de passe trop court sans appeler le serveur', async () => {
    const { user, server } = setup();
    await screen.findByText('invite');
    await user.click(screen.getByRole('button', { name: 'Nouveau compte' }));
    const dialog = screen.getByRole('dialog');
    await user.type(within(dialog).getByLabelText("Nom d'utilisateur"), 'bob');
    await user.type(within(dialog).getByLabelText('Mot de passe'), 'court');
    await user.type(within(dialog).getByLabelText('Confirmer le mot de passe'), 'court');
    await user.click(within(dialog).getByRole('button', { name: 'Créer' }));
    expect(within(dialog).getByRole('alert')).toHaveTextContent('8 caractères');
    expect(server.mutations('CreateUser')).toEqual([]);
  });

  it('modifie les droits et garde la boîte ouverte sur un refus du serveur', async () => {
    const { user, server } = setup({ UpdateUser: new Error('Il faut garder au moins un administrateur actif.') });
    await screen.findByText('invite');
    await user.click(screen.getByRole('button', { name: 'Modifier admin' }));
    const dialog = screen.getByRole('dialog');
    await user.selectOptions(within(dialog).getByRole('combobox'), 'user');
    await user.click(within(dialog).getByRole('button', { name: 'Enregistrer' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'Il faut garder au moins un administrateur actif.',
    );
    expect(server.mutations('UpdateUser')[0]).toMatchObject({ id: 'a1', role: 'user' });
  });

  it('réactive un compte', async () => {
    const { user, server } = setup({ UpdateUser: { updateUser: { ...GUEST, disabled: false } } });
    await screen.findByText('invite');
    await user.click(screen.getByRole('button', { name: 'Modifier invite' }));
    const dialog = screen.getByRole('dialog');
    await user.click(within(dialog).getByLabelText(/Compte désactivé/));
    await user.click(within(dialog).getByRole('button', { name: 'Enregistrer' }));
    await waitFor(() => expect(server.mutations('UpdateUser')).toHaveLength(1));
    expect(server.mutations('UpdateUser')[0]).toEqual({
      id: 'u2',
      role: 'user',
      pages: ['musique', 'qualite'],
      canDownload: true,
      canDelete: false,
      disabled: false,
    });
  });

  it('change le mot de passe d’un compte', async () => {
    const { user, server } = setup({ ResetPassword: { resetPassword: { ok: true } } });
    await screen.findByText('invite');
    await user.click(screen.getByRole('button', { name: 'Mot de passe de invite' }));
    const dialog = screen.getByRole('dialog');
    await user.type(within(dialog).getByLabelText('Nouveau mot de passe'), 'nouveau-mdp');
    await user.type(within(dialog).getByLabelText('Confirmer le mot de passe'), 'nouveau-mdp');
    await user.click(within(dialog).getByRole('button', { name: 'Changer' }));
    await waitFor(() => expect(server.mutations('ResetPassword')).toEqual([{ id: 'u2', password: 'nouveau-mdp' }]));
  });

  it('supprime un compte après confirmation', async () => {
    const { user, server } = setup({ DeleteUser: { deleteUser: { ok: true } } });
    await screen.findByText('invite');
    await user.click(screen.getByRole('button', { name: 'Supprimer invite' }));
    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveTextContent('Supprimer le compte « invite » ?');
    await user.click(within(dialog).getByRole('button', { name: 'Supprimer' }));
    await waitFor(() => expect(server.mutations('DeleteUser')).toEqual([{ id: 'u2' }]));
  });
});
