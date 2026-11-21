import { api } from '../../../core/api/api';
import { useWatch } from '../../../core/api/useWatch';
import { auth } from '../../../core/auth/auth';
import { CREATE_USER, DELETE_USER, RESET_PASSWORD, UPDATE_USER, USERS } from '../admin-api';

/** Ces actions n'ont pas de sens différé : jamais mises en file hors ligne. */
const now = { queueable: false, refetch: ['AdminUsers'] };

/**
 * Comptes et actions de l'administration. Chaque action rend le message d'erreur
 * du serveur (ou null) pour l'afficher dans la boîte qui l'a demandée.
 */
export function useAdminUsers() {
  const state = useWatch(USERS);
  /** @type {import('../admin-api').AdminUser[]} */
  const users = state.data?.users ?? [];

  const create = async body =>
    (await api.mutate(CREATE_USER, body, { ...now, label: 'Créer un compte' })).error ?? null;

  const update = async (id, body) => {
    const r = await api.mutate(UPDATE_USER, { id, ...body }, { ...now, label: 'Modifier un compte' });
    // Ses propres droits ont pu changer : la navigation suit.
    if (!r.error) void auth.refresh();
    return r.error ?? null;
  };

  const resetPassword = async (id, password) =>
    (await api.mutate(RESET_PASSWORD, { id, password }, { ...now, label: 'Changer un mot de passe' })).error ?? null;

  const remove = async id =>
    (await api.mutate(DELETE_USER, { id }, { ...now, label: 'Supprimer un compte' })).error ?? null;

  return {
    users,
    loading: state.loading && !state.data,
    error: state.error,
    stale: state.stale,
    create,
    update,
    resetPassword,
    remove,
  };
}
