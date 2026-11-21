/**
 * Droits du compte connecté, lus dans la session (`/auth/me`). Le serveur fait
 * foi (il refuse ce qui n'est pas permis) : ici on ne fait que masquer ce qui
 * serait refusé.
 *
 * Une session sans rôle (authentification désactivée, serveur antérieur aux
 * comptes multiples, tests) donne tous les droits.
 */

/** Pages ouvrables compte par compte, dans l'ordre de la navigation. */
export const PAGES = [
  { key: 'musique', label: 'Musique' },
  { key: 'bibliotheque', label: 'Bibliothèque' },
  { key: 'reencoder', label: 'Réencoder' },
  { key: 'organiser', label: 'Organiser' },
  { key: 'qualite', label: 'Qualité' },
  { key: 'diagnostic', label: 'Diagnostic' },
  { key: 'historique', label: 'Historique' },
  { key: 'parametres', label: 'Paramètres' },
];

export const PAGE_KEYS = PAGES.map(p => p.key);

/** Page réservée aux administrateurs (hors liste des pages ouvrables). */
export const ADMIN_PAGE = 'administration';

/** Adresse affichée quand aucune page n'est ouverte au compte. */
export const NO_ACCESS_PATH = '/aucun-acces';

const unrestricted = session => !session || !session.authEnabled || !session.role;

export function isAdmin(session) {
  return unrestricted(session) || session.role === 'admin';
}

/** Vrai si le compte peut ouvrir la page (`administration` : administrateurs seulement). */
export function canSeePage(session, key) {
  if (isAdmin(session)) return true;
  if (key === ADMIN_PAGE) return false;
  return (session.pages ?? []).includes(key);
}

/** Droit d'action : `download` (télécharger) ou `delete` (supprimer). */
export function can(session, capability) {
  if (isAdmin(session)) return true;
  return capability === 'download' ? !!session.canDownload : capability === 'delete' ? !!session.canDelete : false;
}

/** Première page ouverte au compte, où l'envoyer quand une adresse lui est refusée. */
export function firstAllowedPath(session) {
  const key = PAGE_KEYS.find(k => canSeePage(session, k)) ?? (isAdmin(session) ? ADMIN_PAGE : null);
  return key ? `/${key}` : NO_ACCESS_PATH;
}
