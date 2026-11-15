/** @typedef {{ path: string, label: string, icon: string }} NavItem */

/** @type {NavItem[]} */
export const MAIN_NAV = [
  { path: '/musique', label: 'Musique', icon: '♫' },
  { path: '/bibliotheque', label: 'Bibliothèque', icon: '▤' },
];

/** Groupe « Outils », repliable. @type {NavItem[]} */
export const TOOLS_NAV = [
  { path: '/reencoder', label: 'Réencoder', icon: '⇄' },
  { path: '/organiser', label: 'Organiser', icon: '⌂' },
  { path: '/qualite', label: 'Qualité', icon: '◎' },
  { path: '/diagnostic', label: 'Diagnostic', icon: '✚' },
];

/** @type {NavItem[]} */
export const BOTTOM_NAV = [
  { path: '/historique', label: 'Historique', icon: '↺' },
  { path: '/parametres', label: 'Paramètres', icon: '⚙' },
];

/** Titre de la page affichée (en-tête mobile). */
export function pageTitle(pathname) {
  return [...MAIN_NAV, ...TOOLS_NAV, ...BOTTOM_NAV].find(i => pathname.startsWith(i.path))?.label ?? 'DeeKeep';
}

/** Vrai si la page appartient au groupe « Outils ». */
export function isToolPage(pathname) {
  return TOOLS_NAV.some(t => pathname.startsWith(t.path));
}
