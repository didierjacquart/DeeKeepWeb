import { PAGES, PAGE_KEYS } from '../../core/auth/permissions';

/** Longueur minimale d'un mot de passe (vérifiée aussi par le serveur). */
export const MIN_PASSWORD = 8;

/** Forme d'une adresse e-mail (même règle que le serveur). */
const EMAIL = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;

/**
 * @typedef {object} UserForm
 * @property {string} name adresse e-mail
 * @property {string} password
 * @property {string} confirm
 * @property {'admin' | 'user'} role
 * @property {string[]} pages
 * @property {boolean} canDownload
 * @property {boolean} canDelete
 * @property {boolean} disabled
 */

/** Formulaire d'un nouveau compte : utilisateur, Musique et Bibliothèque, aucun droit d'action. */
export function emptyForm() {
  return {
    name: '',
    password: '',
    confirm: '',
    role: 'user',
    pages: ['musique', 'bibliotheque'],
    canDownload: false,
    canDelete: false,
    disabled: false,
  };
}

/** @param {import('./admin-api').AdminUser} user */
export function formFromUser(user) {
  return {
    name: user.name,
    password: '',
    confirm: '',
    role: user.role,
    pages: [...user.pages],
    canDownload: user.canDownload,
    canDelete: user.canDelete,
    disabled: user.disabled,
  };
}

/** Coche ou décoche une page, en gardant l'ordre de la navigation. */
export function togglePage(pages, key) {
  const set = new Set(pages);
  if (set.has(key)) set.delete(key);
  else set.add(key);
  return PAGE_KEYS.filter(k => set.has(k));
}

/** Message d'erreur du mot de passe saisi, ou null. */
export function passwordError(password, confirm) {
  if (password.length < MIN_PASSWORD) return `Le mot de passe doit compter au moins ${MIN_PASSWORD} caractères.`;
  if (password !== confirm) return 'Les deux saisies diffèrent.';
  return null;
}

/**
 * Message d'erreur du formulaire, ou null s'il peut partir.
 * @param {UserForm} form
 * @param {boolean} creating
 */
export function formError(form, creating) {
  if (creating) {
    const email = form.name.trim();
    if (!email) return "L'adresse e-mail est obligatoire.";
    if (!EMAIL.test(email)) return 'Adresse e-mail invalide.';
    const pwd = passwordError(form.password, form.confirm);
    if (pwd) return pwd;
  }
  return null;
}

/** Corps envoyé au serveur : droits seuls, plus nom et mot de passe à la création. */
export function toBody(form, creating) {
  const rights = {
    role: form.role,
    pages: form.pages,
    canDownload: form.canDownload,
    canDelete: form.canDelete,
    disabled: form.disabled,
  };
  return creating ? { name: form.name.trim(), password: form.password, ...rights } : rights;
}

/** Pages d'un compte en clair (« Toutes » pour un administrateur). */
export function pagesSummary(user) {
  if (user.role === 'admin') return 'Toutes';
  const labels = PAGES.filter(p => user.pages.includes(p.key)).map(p => p.label);
  return labels.length ? labels.join(', ') : 'Aucune';
}

/** Droits d'action d'un compte en clair. */
export function rightsSummary(user) {
  if (user.role === 'admin') return 'Tous';
  const rights = [user.canDownload && 'Télécharger', user.canDelete && 'Supprimer'].filter(Boolean);
  return rights.length ? rights.join(', ') : 'Aucun';
}

export const roleLabel = role => (role === 'admin' ? 'Administrateur' : 'Utilisateur');
